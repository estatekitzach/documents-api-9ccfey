using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Net;
using System.Text.Json;

namespace EstateKit.Documents.Api.Filters
{
    /// <summary>
    /// Global exception filter that handles all unhandled exceptions in the API,
    /// providing consistent error responses, security-aware error handling, and comprehensive logging.
    /// </summary>
    public class ApiExceptionFilter : IExceptionFilter
    {
        private readonly ILogger<ApiExceptionFilter> _logger;
        private readonly IHostEnvironment _environment;

        /// <summary>
        /// Initializes a new instance of the ApiExceptionFilter with required dependencies.
        /// </summary>
        /// <param name="logger">Logger for CloudWatch integration</param>
        /// <param name="environment">Host environment information</param>
        /// <exception cref="ArgumentNullException">Thrown when logger or environment is null</exception>
        public ApiExceptionFilter(ILogger<ApiExceptionFilter> logger, IHostEnvironment environment)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _environment = environment ?? throw new ArgumentNullException(nameof(environment));
        }

        /// <summary>
        /// Handles unhandled exceptions globally across the API.
        /// </summary>
        /// <param name="context">The exception context containing exception details</param>
        public void OnException(ExceptionContext context)
        {
            var correlationId = Guid.NewGuid().ToString();
            var path = context.HttpContext.Request.Path;
            var exception = context.Exception;

            // Log the exception with correlation ID for tracing
            _logger.LogError(
                exception,
                "Unhandled exception occurred. CorrelationId: {CorrelationId}, Path: {Path}, Message: {Message}",
                correlationId,
                path,
                exception.Message
            );

            var statusCode = GetStatusCode(exception);

            var errorResponse = new
            {
                CorrelationId = correlationId,
                Message = GetSanitizedErrorMessage(exception),
                StatusCode = statusCode,
                Path = path.ToString(),
                Timestamp = DateTimeOffset.UtcNow,
                // Only include stack trace in development
                StackTrace = _environment.IsDevelopment() ? exception.StackTrace : null
            };

            context.Result = new ObjectResult(errorResponse)
            {
                StatusCode = statusCode
            };

            // Set response headers for better error tracking
            context.HttpContext.Response.Headers.Add("X-Correlation-ID", correlationId);
            context.HttpContext.Response.Headers.Add("X-Error-Time", DateTimeOffset.UtcNow.ToString("o"));

            context.ExceptionHandled = true;

            _logger.LogInformation(
                "Exception handled. CorrelationId: {CorrelationId}, StatusCode: {StatusCode}",
                correlationId,
                statusCode
            );
        }

        /// <summary>
        /// Determines the appropriate HTTP status code based on exception type.
        /// </summary>
        /// <param name="exception">The exception to evaluate</param>
        /// <returns>Appropriate HTTP status code</returns>
        private static int GetStatusCode(Exception exception)
        {
            return exception switch
            {
                ValidationException _ => (int)HttpStatusCode.BadRequest,
                UnauthorizedAccessException _ => (int)HttpStatusCode.Unauthorized,
                AuthenticationException _ => (int)HttpStatusCode.Unauthorized,
                AuthorizationException _ => (int)HttpStatusCode.Forbidden,
                NotFoundException _ => (int)HttpStatusCode.NotFound,
                RateLimitExceededException _ => (int)HttpStatusCode.TooManyRequests,
                ServiceUnavailableException _ => (int)HttpStatusCode.ServiceUnavailable,
                OperationCanceledException _ => (int)HttpStatusCode.ServiceUnavailable,
                _ => (int)HttpStatusCode.InternalServerError
            };
        }

        /// <summary>
        /// Returns a sanitized error message safe for client consumption.
        /// </summary>
        /// <param name="exception">The exception to sanitize</param>
        /// <returns>Sanitized error message</returns>
        private string GetSanitizedErrorMessage(Exception exception)
        {
            // In production, return generic messages for security
            if (!_environment.IsDevelopment())
            {
                return exception switch
                {
                    ValidationException _ => "The request data is invalid.",
                    UnauthorizedAccessException _ => "Authentication required.",
                    AuthenticationException _ => "Authentication failed.",
                    AuthorizationException _ => "Access denied.",
                    NotFoundException _ => "The requested resource was not found.",
                    RateLimitExceededException _ => "Too many requests. Please try again later.",
                    ServiceUnavailableException _ => "Service temporarily unavailable.",
                    OperationCanceledException _ => "The operation was cancelled.",
                    _ => "An unexpected error occurred. Please try again later."
                };
            }

            // In development, return actual exception message
            return exception.Message;
        }
    }

    // Custom exception types referenced in the filter
    public class ValidationException : Exception
    {
        public ValidationException(string message) : base(message) { }
    }

    public class AuthenticationException : Exception
    {
        public AuthenticationException(string message) : base(message) { }
    }

    public class AuthorizationException : Exception
    {
        public AuthorizationException(string message) : base(message) { }
    }

    public class NotFoundException : Exception
    {
        public NotFoundException(string message) : base(message) { }
    }

    public class RateLimitExceededException : Exception
    {
        public RateLimitExceededException(string message) : base(message) { }
    }

    public class ServiceUnavailableException : Exception
    {
        public ServiceUnavailableException(string message) : base(message) { }
    }
}