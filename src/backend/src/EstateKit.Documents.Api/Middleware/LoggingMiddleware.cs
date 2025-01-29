using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace EstateKit.Documents.Api.Middleware
{
    /// <summary>
    /// Middleware component that provides comprehensive request/response logging with CloudWatch integration,
    /// performance monitoring, and error tracking for the EstateKit Documents API.
    /// </summary>
    public class LoggingMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<LoggingMiddleware> _logger;
        private const int ResponseTimeThresholdMs = 3000; // 3 seconds threshold from spec

        /// <summary>
        /// Initializes a new instance of the LoggingMiddleware class.
        /// </summary>
        /// <param name="next">The next middleware delegate in the pipeline</param>
        /// <param name="logger">Logger instance for CloudWatch integration</param>
        public LoggingMiddleware(RequestDelegate next, ILogger<LoggingMiddleware> logger)
        {
            _next = next ?? throw new ArgumentNullException(nameof(next));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Processes HTTP requests while providing comprehensive logging, performance monitoring, and error tracking.
        /// </summary>
        /// <param name="context">The HTTP context for the current request</param>
        /// <returns>A task representing the completion of request processing</returns>
        public async Task InvokeAsync(HttpContext context)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));

            var stopwatch = Stopwatch.StartNew();
            var correlationId = Guid.NewGuid().ToString();
            context.TraceIdentifier = correlationId;

            try
            {
                // Log request details
                _logger.LogInformation(
                    "Request starting - CorrelationId: {CorrelationId}, Method: {Method}, Path: {Path}, ContentLength: {ContentLength}",
                    correlationId,
                    context.Request.Method,
                    context.Request.Path,
                    context.Request.ContentLength);

                // Enable request body buffering for logging
                context.Request.EnableBuffering();

                // Capture and buffer the response
                var originalBodyStream = context.Response.Body;
                using var responseBody = new MemoryStream();
                context.Response.Body = responseBody;

                try
                {
                    await _next(context);
                }
                finally
                {
                    stopwatch.Stop();
                    var elapsedMs = stopwatch.ElapsedMilliseconds;

                    // Performance threshold check
                    if (elapsedMs > ResponseTimeThresholdMs)
                    {
                        _logger.LogWarning(
                            "Request exceeded performance threshold - CorrelationId: {CorrelationId}, Duration: {Duration}ms, Threshold: {Threshold}ms",
                            correlationId,
                            elapsedMs,
                            ResponseTimeThresholdMs);
                    }

                    // Log response details
                    responseBody.Seek(0, SeekOrigin.Begin);
                    var responseBodyText = await new StreamReader(responseBody).ReadToEndAsync();

                    _logger.LogInformation(
                        "Request completed - CorrelationId: {CorrelationId}, StatusCode: {StatusCode}, Duration: {Duration}ms, ResponseLength: {ResponseLength}",
                        correlationId,
                        context.Response.StatusCode,
                        elapsedMs,
                        responseBody.Length);

                    // Copy the buffered response to the original stream
                    responseBody.Seek(0, SeekOrigin.Begin);
                    await responseBody.CopyToAsync(originalBodyStream);
                }
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                
                _logger.LogError(
                    ex,
                    "Request failed - CorrelationId: {CorrelationId}, Method: {Method}, Path: {Path}, Duration: {Duration}ms",
                    correlationId,
                    context.Request.Method,
                    context.Request.Path,
                    stopwatch.ElapsedMilliseconds);

                // Rethrow to allow error handling middleware to process
                throw;
            }
            finally
            {
                // Log request metrics to CloudWatch
                _logger.LogInformation(
                    "Request metrics - CorrelationId: {CorrelationId}, Method: {Method}, Path: {Path}, StatusCode: {StatusCode}, Duration: {Duration}ms",
                    correlationId,
                    context.Request.Method,
                    context.Request.Path,
                    context.Response.StatusCode,
                    stopwatch.ElapsedMilliseconds);
            }
        }
    }
}