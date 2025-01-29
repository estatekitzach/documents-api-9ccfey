using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Events;
using Serilog.Sinks.CloudWatch;
using Amazon.CloudWatch;
using System;
using System.Threading.Tasks;

namespace EstateKit.Documents.Api
{
    /// <summary>
    /// Entry point for the EstateKit Documents API application with comprehensive
    /// security, monitoring, and AWS service integration.
    /// </summary>
    public class Program
    {
        /// <summary>
        /// Application entry point with enhanced error handling and monitoring
        /// </summary>
        public static async Task Main(string[] args)
        {
            // Configure initial bootstrap logger
            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Information()
                .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
                .Enrich.FromLogContext()
                .WriteTo.Console()
                .CreateBootstrapLogger();

            try
            {
                Log.Information("Starting EstateKit Documents API");

                // Build and run the host
                var host = CreateHostBuilder(args).Build();

                // Configure graceful shutdown
                AppDomain.CurrentDomain.UnhandledException += (sender, e) =>
                {
                    Log.Fatal(e.ExceptionObject as Exception, "Unhandled application error");
                };

                await host.RunAsync();
            }
            catch (Exception ex)
            {
                Log.Fatal(ex, "Application startup failed");
                throw;
            }
            finally
            {
                await Log.CloseAndFlushAsync();
            }
        }

        /// <summary>
        /// Configures the web host with comprehensive security and monitoring
        /// </summary>
        private static IHostBuilder CreateHostBuilder(string[] args) =>
            Host.CreateDefaultBuilder(args)
                .UseSerilog((context, services, configuration) =>
                {
                    configuration
                        .MinimumLevel.Information()
                        .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
                        .MinimumLevel.Override("System", LogEventLevel.Warning)
                        .Enrich.FromLogContext()
                        .Enrich.WithEnvironmentName()
                        .Enrich.WithMachineName()
                        .WriteTo.Console()
                        .WriteTo.CloudWatch(new CloudWatchSinkOptions
                        {
                            LogGroupName = context.Configuration["AWS:CloudWatch:LogGroup"] ?? "/estatekit/documents/api",
                            TextFormatter = new Serilog.Formatting.Json.JsonFormatter(),
                            LogStreamNameProvider = new DefaultLogStreamProvider(),
                            RetryAttempts = 3,
                            BatchSizeLimit = 100,
                            QueueSizeLimit = 10000,
                            Period = TimeSpan.FromSeconds(10)
                        });
                })
                .ConfigureWebHostDefaults(webBuilder =>
                {
                    webBuilder
                        .UseKestrel(options =>
                        {
                            // Configure Kestrel with security settings
                            options.AddServerHeader = false;
                            options.Limits.MaxRequestBodySize = 104857600; // 100MB limit
                            options.Limits.MaxConcurrentConnections = 100;
                            options.Limits.MaxConcurrentUpgradedConnections = 100;
                            options.Limits.RequestHeadersTimeout = TimeSpan.FromSeconds(30);
                        })
                        .ConfigureAppConfiguration((hostingContext, config) =>
                        {
                            var env = hostingContext.HostingEnvironment;

                            config
                                .SetBasePath(env.ContentRootPath)
                                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                                .AddJsonFile($"appsettings.{env.EnvironmentName}.json", optional: true, reloadOnChange: true)
                                .AddEnvironmentVariables()
                                .AddCommandLine(args);

                            if (env.IsDevelopment())
                            {
                                config.AddUserSecrets<Program>();
                            }
                        })
                        .ConfigureLogging((hostingContext, logging) =>
                        {
                            logging.ClearProviders();
                            logging.AddSerilog();
                        })
                        .UseStartup<Startup>();
                })
                .UseDefaultServiceProvider((context, options) =>
                {
                    options.ValidateScopes = context.HostingEnvironment.IsDevelopment();
                    options.ValidateOnBuild = true;
                });
    }
}