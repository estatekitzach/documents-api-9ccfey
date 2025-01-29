using EstateKit.Documents.Infrastructure.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Amazon.SecurityToken;
using System.Net;

namespace EstateKit.Documents.Api.Middleware
{
    /// <summary>
    /// Enhanced middleware component that handles OAuth 2.0 authentication using AWS Cognito
    /// with comprehensive security monitoring and compliance logging.
    /// </summary>
    public class AuthenticationMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<AuthenticationMiddleware> _logger;
        private readonly CognitoAuthenticationHandler _authHandler;
        private readonly IConfiguration _configuration;
        private const string BEARER_PREFIX = "Bearer ";
        private const int MIN_TOKEN_LENGTH = 20;
        private const int MAX_TOKEN_LENGTH = 2048;
        private static readonly Regex TokenFormatRegex = new(@"^[A-Za-z0-9-_=]+\.[A-Za-z0-9-_=]+\.[A-Za-z0-9-_.+/=]*$", RegexOptions.Compiled);
        private readonly Dictionary<string, int> _clientRateLimits = new();
        private const int RATE_LIMIT_WINDOW_MINUTES = 15;
        private const int MAX_REQUESTS_PER_WINDOW = 1000;

        /// <summary>
        /// Initializes a new instance of the AuthenticationMiddleware
        /// </summary>
        public AuthenticationMiddleware(
            RequestDelegate next,
            ILogger<AuthenticationMiddleware> logger,
            CognitoAuthenticationHandler authHandler,
            IConfiguration configuration)
        {
            _next = next ?? throw new ArgumentNullException(nameof(next));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _authHandler = authHandler ?? throw new ArgumentNullException(nameof(authHandler));
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));

            _logger.LogInformation("AuthenticationMiddleware initialized with enhanced security features");
        }

        /// <summary>
        /// Processes authentication with enhanced security checks for incoming HTTP requests
        /// </summary>
        public async Task InvokeAsync(HttpContext context)
        {
            try
            {
                // Check rate limits
                var clientIp = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
                if (!await CheckRateLimitAsync(clientIp))
                {
                    _logger.LogWarning("Rate limit exceeded for IP: {ClientIp}", clientIp);
                    context.Response.StatusCode = (int)HttpStatusCode.TooManyRequests;
                    await context.Response.WriteAsJsonAsync(new { error = "Rate limit exceeded" });
                    return;
                }

                // Skip authentication for OPTIONS requests (CORS preflight)
                if (context.Request.Method == HttpMethods.Options)
                {
                    await _next(context);
                    return;
                }

                // Extract bearer token
                string token = ExtractBearerToken(context.Request);
                if (string.IsNullOrEmpty(token))
                {
                    await HandleAuthenticationFailure(context, HttpStatusCode.Unauthorized, "No bearer token provided");
                    return;
                }

                // Validate token format
                if (!ValidateTokenFormat(token))
                {
                    await HandleAuthenticationFailure(context, HttpStatusCode.Unauthorized, "Invalid token format");
                    return;
                }

                // Authenticate user
                var authResult = await _authHandler.HandleAuthenticateAsync(token);
                if (!authResult.Success)
                {
                    await HandleAuthenticationFailure(context, HttpStatusCode.Unauthorized, authResult.Error);
                    return;
                }

                // Set user claims in context
                var identity = new ClaimsIdentity(authResult.Claims, "Bearer");
                context.User = new ClaimsPrincipal(identity);

                // Log successful authentication
                await LogSecurityEvent("authentication_success", new Dictionary<string, object>
                {
                    { "username", authResult.Username },
                    { "ip_address", clientIp },
                    { "request_path", context.Request.Path }
                });

                await _next(context);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Authentication error occurred");
                await HandleAuthenticationFailure(context, HttpStatusCode.InternalServerError, "Authentication error");
            }
        }

        /// <summary>
        /// Extracts and performs preliminary validation of the bearer token
        /// </summary>
        private string ExtractBearerToken(HttpRequest request)
        {
            string authHeader = request.Headers["Authorization"].ToString();
            if (string.IsNullOrEmpty(authHeader) || !authHeader.StartsWith(BEARER_PREFIX))
            {
                return null;
            }

            string token = authHeader.Substring(BEARER_PREFIX.Length).Trim();
            return ValidateTokenFormat(token) ? token : null;
        }

        /// <summary>
        /// Validates the basic format and structure of the token
        /// </summary>
        private bool ValidateTokenFormat(string token)
        {
            if (string.IsNullOrEmpty(token) || 
                token.Length < MIN_TOKEN_LENGTH || 
                token.Length > MAX_TOKEN_LENGTH)
            {
                return false;
            }

            return TokenFormatRegex.IsMatch(token);
        }

        /// <summary>
        /// Implements rate limiting per client IP
        /// </summary>
        private async Task<bool> CheckRateLimitAsync(string clientIp)
        {
            lock (_clientRateLimits)
            {
                if (!_clientRateLimits.ContainsKey(clientIp))
                {
                    _clientRateLimits[clientIp] = 1;
                    return true;
                }

                if (_clientRateLimits[clientIp] >= MAX_REQUESTS_PER_WINDOW)
                {
                    return false;
                }

                _clientRateLimits[clientIp]++;
                return true;
            }
        }

        /// <summary>
        /// Handles authentication failures with proper response and logging
        /// </summary>
        private async Task HandleAuthenticationFailure(HttpContext context, HttpStatusCode statusCode, string error)
        {
            context.Response.StatusCode = (int)statusCode;
            await context.Response.WriteAsJsonAsync(new { error });

            await LogSecurityEvent("authentication_failure", new Dictionary<string, object>
            {
                { "error", error },
                { "ip_address", context.Connection.RemoteIpAddress?.ToString() },
                { "request_path", context.Request.Path }
            });
        }

        /// <summary>
        /// Logs detailed security events for audit and monitoring
        /// </summary>
        private async Task LogSecurityEvent(string eventType, Dictionary<string, object> eventData)
        {
            try
            {
                eventData["event_type"] = eventType;
                eventData["timestamp"] = DateTime.UtcNow;
                eventData["environment"] = _configuration["Environment"];

                _logger.LogInformation(
                    "Security Event: {EventType} - Details: {@EventData}",
                    eventType,
                    eventData
                );

                // Additional security monitoring could be implemented here
                // such as sending events to CloudWatch or other monitoring services
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to log security event: {EventType}", eventType);
            }
        }
    }
}