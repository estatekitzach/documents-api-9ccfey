using Amazon.CognitoIdentityProvider;
using Amazon.KeyManagementService;
using Amazon.S3;
using Amazon.Textract;
using EstateKit.Documents.Infrastructure.Configuration;
using EstateKit.Documents.Infrastructure.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Polly;
using Polly.CircuitBreaker;
using Polly.Extensions.Http;
using StackExchange.Redis;
using System;
using System.Net.Http;

namespace EstateKit.Documents.Api.Extensions
{
    /// <summary>
    /// Provides extension methods for configuring EstateKit Documents API services
    /// with enhanced security, monitoring, and AWS integration.
    /// </summary>
    public static class ServiceCollectionExtensions
    {
        /// <summary>
        /// Configures all required services for the EstateKit Documents API with comprehensive
        /// security, monitoring, and performance optimizations.
        /// </summary>
        public static IServiceCollection AddEstateKitServices(
            this IServiceCollection services,
            IConfiguration configuration)
        {
            if (services == null) throw new ArgumentNullException(nameof(services));
            if (configuration == null) throw new ArgumentNullException(nameof(configuration));

            // Configure AWS services with enhanced security
            services.AddAwsServices(configuration);

            // Configure storage services with encryption
            services.AddStorageServices(configuration);

            // Configure document analysis services
            services.AddDocumentServices(configuration);

            // Configure Redis caching with security
            services.AddRedisCache(configuration);

            // Configure health checks
            services.AddHealthChecks()
                .AddS3(name: "s3_storage")
                .AddTextract(name: "textract_analysis")
                .AddRedis(name: "redis_cache")
                .AddCognito(name: "cognito_auth");

            // Configure retry policies
            services.AddResiliencePolicies();

            return services;
        }

        private static IServiceCollection AddAwsServices(
            this IServiceCollection services,
            IConfiguration configuration)
        {
            // Register and validate AWS configuration
            services.AddSingleton(provider =>
            {
                var awsConfig = new AwsConfiguration(configuration);
                if (!awsConfig.Validate())
                {
                    throw new InvalidOperationException("AWS configuration validation failed");
                }
                return awsConfig;
            });

            // Configure AWS service clients with security options
            services.AddAWSService<IAmazonS3>(new AWSOptions
            {
                DefaultClientConfig = 
                {
                    MaxErrorRetry = 3,
                    UseHttp3 = true,
                    HttpClientCacheSize = 100
                }
            });

            services.AddAWSService<IAmazonTextract>(new AWSOptions
            {
                DefaultClientConfig =
                {
                    MaxErrorRetry = 3,
                    UseHttp3 = true
                }
            });

            services.AddAWSService<IAmazonCognitoIdentityProvider>();
            services.AddAWSService<IAmazonKeyManagementService>();

            return services;
        }

        private static IServiceCollection AddStorageServices(
            this IServiceCollection services,
            IConfiguration configuration)
        {
            // Register and validate storage configuration
            services.AddSingleton(provider =>
            {
                var storageConfig = new StorageConfiguration(configuration);
                if (!storageConfig.Validate())
                {
                    throw new InvalidOperationException("Storage configuration validation failed");
                }
                return storageConfig;
            });

            // Configure document repository with caching
            services.AddScoped<IDocumentRepository, DocumentRepository>();
            services.AddScoped<IStorageService, StorageService>();

            return services;
        }

        private static IServiceCollection AddDocumentServices(
            this IServiceCollection services,
            IConfiguration configuration)
        {
            // Configure document service with monitoring
            services.AddScoped<IDocumentService, DocumentService>();
            services.AddScoped<IDocumentAnalysisService, DocumentAnalysisService>();

            // Configure document processing options
            services.Configure<DocumentProcessingOptions>(configuration.GetSection("DocumentProcessing"));

            return services;
        }

        private static IServiceCollection AddRedisCache(
            this IServiceCollection services,
            IConfiguration configuration)
        {
            var redisConnection = configuration.GetConnectionString("Redis");
            
            services.AddStackExchangeRedisCache(options =>
            {
                options.Configuration = redisConnection;
                options.InstanceName = "EstateKit_";
                options.ConfigurationOptions = new ConfigurationOptions
                {
                    AbortOnConnectFail = false,
                    ConnectTimeout = 5000,
                    SyncTimeout = 5000,
                    ConnectRetry = 3,
                    Ssl = true,
                    Password = configuration["Redis:Password"]
                };
            });

            return services;
        }

        private static IServiceCollection AddResiliencePolicies(
            this IServiceCollection services)
        {
            // Configure circuit breaker policy
            services.AddHttpClient("default")
                .AddPolicyHandler(GetCircuitBreakerPolicy())
                .AddPolicyHandler(GetRetryPolicy());

            return services;
        }

        private static IAsyncPolicy<HttpResponseMessage> GetCircuitBreakerPolicy()
        {
            return HttpPolicyExtensions
                .HandleTransientHttpError()
                .CircuitBreakerAsync(
                    handledEventsAllowedBeforeBreaking: 5,
                    durationOfBreak: TimeSpan.FromSeconds(30)
                );
        }

        private static IAsyncPolicy<HttpResponseMessage> GetRetryPolicy()
        {
            return HttpPolicyExtensions
                .HandleTransientHttpError()
                .WaitAndRetryAsync(3, retryAttempt => 
                    TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)));
        }
    }

    /// <summary>
    /// Health check extension methods for AWS services
    /// </summary>
    internal static class HealthCheckExtensions
    {
        public static IHealthChecksBuilder AddS3(
            this IHealthChecksBuilder builder,
            string name = "s3")
        {
            return builder.AddCheck<S3HealthCheck>(
                name,
                HealthStatus.Unhealthy,
                new[] { "storage" }
            );
        }

        public static IHealthChecksBuilder AddTextract(
            this IHealthChecksBuilder builder,
            string name = "textract")
        {
            return builder.AddCheck<TextractHealthCheck>(
                name,
                HealthStatus.Unhealthy,
                new[] { "analysis" }
            );
        }

        public static IHealthChecksBuilder AddCognito(
            this IHealthChecksBuilder builder,
            string name = "cognito")
        {
            return builder.AddCheck<CognitoHealthCheck>(
                name,
                HealthStatus.Unhealthy,
                new[] { "auth" }
            );
        }
    }
}