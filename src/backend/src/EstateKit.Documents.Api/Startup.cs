using EstateKit.Documents.Api.Extensions;
using EstateKit.Documents.Api.Middleware;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System;
using System.IO.Compression;
using System.Threading.RateLimiting;

namespace EstateKit.Documents.Api
{
    /// <summary>
    /// Configures the EstateKit Documents API services and request pipeline with comprehensive
    /// security, monitoring, and performance features.
    /// </summary>
    public class Startup
    {
        private readonly IConfiguration _configuration;
        private readonly IWebHostEnvironment _environment;

        public Startup(IConfiguration configuration, IWebHostEnvironment environment)
        {
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            _environment = environment ?? throw new ArgumentNullException(nameof(environment));
        }

        /// <summary>
        /// Configures application services with enhanced security and monitoring
        /// </summary>
        public void ConfigureServices(IServiceCollection services)
        {
            // Configure MVC with security features
            services.AddControllers(options =>
            {
                options.RequireHttpsPermanent = true;
                options.EnableEndpointRouting = true;
            })
            .AddJsonOptions(options =>
            {
                options.JsonSerializerOptions.WriteIndented = false;
                options.JsonSerializerOptions.PropertyNamingPolicy = null;
            });

            // Configure API versioning
            services.AddApiVersioning(options =>
            {
                options.DefaultApiVersion = new ApiVersion(1, 0);
                options.AssumeDefaultVersionWhenUnspecified = true;
                options.ReportApiVersions = true;
            });

            // Configure rate limiting (1000 requests/minute per client)
            services.AddRateLimiter(options =>
            {
                options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
                    RateLimitPartition.GetFixedWindowLimiter(
                        partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                        factory: partition => new FixedWindowRateLimiterOptions
                        {
                            AutoReplenishment = true,
                            PermitLimit = 1000,
                            Window = TimeSpan.FromMinutes(1)
                        }));
                options.OnRejected = async (context, token) =>
                {
                    context.HttpContext.Response.StatusCode = 429;
                    await context.HttpContext.Response.WriteAsJsonAsync(new { error = "Too many requests" });
                };
            });

            // Configure response compression
            services.AddResponseCompression(options =>
            {
                options.EnableForHttps = true;
                options.Providers.Add<BrotliCompressionProvider>();
                options.Providers.Add<GzipCompressionProvider>();
            });

            services.Configure<BrotliCompressionProviderOptions>(options =>
            {
                options.Level = CompressionLevel.Fastest;
            });

            // Configure CORS with secure defaults
            services.AddCors(options =>
            {
                options.AddDefaultPolicy(builder =>
                {
                    builder
                        .WithOrigins(_configuration.GetSection("AllowedOrigins").Get<string[]>() ?? Array.Empty<string>())
                        .WithMethods("GET", "POST", "DELETE")
                        .WithHeaders("Authorization", "Content-Type")
                        .SetPreflightMaxAge(TimeSpan.FromMinutes(10));
                });
            });

            // Configure OpenAPI documentation
            services.AddEndpointsApiExplorer();
            services.AddSwaggerGen(options =>
            {
                options.SwaggerDoc("v1", new()
                {
                    Title = "EstateKit Documents API",
                    Version = "v1",
                    Description = "Secure document management API for EstateKit"
                });
                options.AddSecurityDefinition("Bearer", new()
                {
                    Type = "apiKey",
                    Name = "Authorization",
                    In = "header",
                    Description = "JWT Authorization header using the Bearer scheme"
                });
            });

            // Configure health checks
            services.AddHealthChecks()
                .AddCheck("api_health", () => Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Healthy())
                .AddS3(name: "s3_storage")
                .AddTextract(name: "textract_analysis")
                .AddCognito(name: "cognito_auth");

            // Add EstateKit core services
            services.AddEstateKitServices(_configuration);

            // Add AWS services with security configuration
            services.AddAwsServices(_configuration);
        }

        /// <summary>
        /// Configures the HTTP request pipeline with security and monitoring
        /// </summary>
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            // Configure error handling
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
                app.UseSwagger();
                app.UseSwaggerUI();
            }
            else
            {
                app.UseExceptionHandler("/error");
                app.UseHsts();
            }

            // Enable response compression
            app.UseResponseCompression();

            // Enable security headers
            app.Use(async (context, next) =>
            {
                context.Response.Headers.Add("X-Content-Type-Options", "nosniff");
                context.Response.Headers.Add("X-Frame-Options", "DENY");
                context.Response.Headers.Add("X-XSS-Protection", "1; mode=block");
                context.Response.Headers.Add("Referrer-Policy", "strict-origin-when-cross-origin");
                context.Response.Headers.Add("Permissions-Policy", "accelerometer=(), camera=(), geolocation=(), gyroscope=(), magnetometer=(), microphone=(), payment=(), usb=()");
                await next();
            });

            // Enable HTTPS redirection
            app.UseHttpsRedirection();

            // Enable CORS
            app.UseCors();

            // Configure request logging
            app.UseMiddleware<LoggingMiddleware>();

            // Configure authentication
            app.UseMiddleware<AuthenticationMiddleware>();

            // Enable routing
            app.UseRouting();

            // Enable authorization
            app.UseAuthorization();

            // Enable rate limiting
            app.UseRateLimiter();

            // Map endpoints
            app.UseEndpoints(endpoints =>
            {
                endpoints.MapControllers();
                endpoints.MapHealthChecks("/health");
                endpoints.MapHealthChecks("/health/ready", new()
                {
                    Predicate = check => check.Tags.Contains("ready")
                });
                endpoints.MapHealthChecks("/health/live", new()
                {
                    Predicate = _ => false
                });
            });
        }
    }
}