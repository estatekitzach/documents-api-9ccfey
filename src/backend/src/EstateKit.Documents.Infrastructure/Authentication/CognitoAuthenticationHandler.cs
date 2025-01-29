using Amazon.CognitoIdentityProvider;
using Amazon.CognitoIdentityProvider.Model;
using EstateKit.Documents.Infrastructure.Configuration;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;

namespace EstateKit.Documents.Infrastructure.Authentication
{
    /// <summary>
    /// Handles AWS Cognito authentication with enhanced security features including token validation,
    /// rate limiting, and security monitoring for the EstateKit Documents API.
    /// </summary>
    public class CognitoAuthenticationHandler
    {
        private readonly AmazonCognitoIdentityProviderClient _cognitoClient;
        private readonly ILogger<CognitoAuthenticationHandler> _logger;
        private readonly AwsConfiguration _awsConfig;
        private readonly JwtSecurityTokenHandler _tokenHandler;
        private readonly IMemoryCache _tokenCache;
        private readonly TokenValidationParameters _validationParameters;
        private const int TOKEN_CACHE_MINUTES = 15;
        private const int MAX_FAILED_ATTEMPTS = 5;
        private const string TOKEN_BLACKLIST_PREFIX = "blacklist_";
        private const string RATE_LIMIT_PREFIX = "ratelimit_";

        public CognitoAuthenticationHandler(
            AwsConfiguration awsConfig,
            ILogger<CognitoAuthenticationHandler> logger,
            IMemoryCache tokenCache)
        {
            _awsConfig = awsConfig ?? throw new ArgumentNullException(nameof(awsConfig));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _tokenCache = tokenCache ?? throw new ArgumentNullException(nameof(tokenCache));

            // Initialize Cognito client with regional endpoint
            _cognitoClient = new AmazonCognitoIdentityProviderClient(
                new Amazon.Runtime.AWSCredentials(), 
                Amazon.RegionEndpoint.GetBySystemName(_awsConfig.Region));

            _tokenHandler = new JwtSecurityTokenHandler();

            // Configure token validation parameters with enhanced security
            _validationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                ClockSkew = TimeSpan.FromMinutes(5),
                RequireExpirationTime = true,
                RequireSignedTokens = true,
                ValidIssuer = $"https://cognito-idp.{_awsConfig.Region}.amazonaws.com/{_awsConfig.CognitoUserPoolId}",
                ValidAudience = _awsConfig.CognitoAppClientId
            };

            _logger.LogInformation("CognitoAuthenticationHandler initialized with enhanced security features");
        }

        /// <summary>
        /// Validates JWT token with enhanced security checks
        /// </summary>
        public async Task<TokenValidationResult> ValidateTokenAsync(string token)
        {
            try
            {
                if (string.IsNullOrEmpty(token))
                {
                    _logger.LogWarning("Token validation failed: Empty token");
                    return new TokenValidationResult { IsValid = false, Error = "Invalid token" };
                }

                // Check token blacklist
                if (await IsTokenBlacklistedAsync(token))
                {
                    _logger.LogWarning("Token validation failed: Blacklisted token");
                    return new TokenValidationResult { IsValid = false, Error = "Token is blacklisted" };
                }

                // Validate token format
                if (!_tokenHandler.CanReadToken(token))
                {
                    _logger.LogWarning("Token validation failed: Invalid format");
                    return new TokenValidationResult { IsValid = false, Error = "Invalid token format" };
                }

                // Parse and validate token
                var jwtToken = _tokenHandler.ReadJwtToken(token);

                // Additional security checks
                if (!ValidateTokenClaims(jwtToken))
                {
                    _logger.LogWarning("Token validation failed: Invalid claims");
                    return new TokenValidationResult { IsValid = false, Error = "Invalid token claims" };
                }

                // Verify token with Cognito
                var verifyRequest = new GetUserRequest { AccessToken = token };
                await _cognitoClient.GetUserAsync(verifyRequest);

                _logger.LogInformation("Token successfully validated");
                return new TokenValidationResult { IsValid = true };
            }
            catch (NotAuthorizedException ex)
            {
                _logger.LogWarning(ex, "Token validation failed: Not authorized");
                return new TokenValidationResult { IsValid = false, Error = "Token not authorized" };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Token validation failed with unexpected error");
                return new TokenValidationResult { IsValid = false, Error = "Validation error" };
            }
        }

        /// <summary>
        /// Processes authentication with security monitoring
        /// </summary>
        public async Task<AuthenticationResult> HandleAuthenticateAsync(string token)
        {
            try
            {
                // Check rate limiting
                if (await IsRateLimitExceededAsync(token))
                {
                    _logger.LogWarning("Authentication blocked: Rate limit exceeded");
                    return new AuthenticationResult { Success = false, Error = "Rate limit exceeded" };
                }

                // Validate token
                var validationResult = await ValidateTokenAsync(token);
                if (!validationResult.IsValid)
                {
                    await IncrementFailedAttemptsAsync(token);
                    return new AuthenticationResult { Success = false, Error = validationResult.Error };
                }

                // Get user claims
                var jwtToken = _tokenHandler.ReadJwtToken(token);
                var username = jwtToken.Claims.FirstOrDefault(c => c.Type == "username")?.Value;
                
                if (string.IsNullOrEmpty(username))
                {
                    _logger.LogWarning("Authentication failed: Username claim missing");
                    return new AuthenticationResult { Success = false, Error = "Invalid token claims" };
                }

                var claims = await GetUserClaimsAsync(username);
                
                _logger.LogInformation("Authentication successful for user: {Username}", username);
                return new AuthenticationResult 
                { 
                    Success = true,
                    Claims = claims,
                    Username = username
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Authentication failed with unexpected error");
                return new AuthenticationResult { Success = false, Error = "Authentication error" };
            }
        }

        /// <summary>
        /// Retrieves and validates user claims
        /// </summary>
        public async Task<IEnumerable<Claim>> GetUserClaimsAsync(string username)
        {
            try
            {
                var getUserRequest = new AdminGetUserRequest
                {
                    UserPoolId = _awsConfig.CognitoUserPoolId,
                    Username = username
                };

                var userResponse = await _cognitoClient.AdminGetUserAsync(getUserRequest);
                
                var claims = new List<Claim>
                {
                    new Claim(ClaimTypes.Name, username),
                    new Claim(ClaimTypes.NameIdentifier, username)
                };

                // Add user attributes as claims
                foreach (var attribute in userResponse.UserAttributes)
                {
                    claims.Add(new Claim(attribute.Name, attribute.Value));
                }

                _logger.LogInformation("Successfully retrieved claims for user: {Username}", username);
                return claims;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to retrieve user claims for: {Username}", username);
                throw;
            }
        }

        private bool ValidateTokenClaims(JwtSecurityToken token)
        {
            if (token == null) return false;

            // Validate token lifetime
            if (token.ValidTo < DateTime.UtcNow || token.ValidFrom > DateTime.UtcNow)
                return false;

            // Validate required claims
            var requiredClaims = new[] { "sub", "aud", "iss", "exp", "iat" };
            if (!requiredClaims.All(claim => token.Claims.Any(c => c.Type == claim)))
                return false;

            // Validate issuer and audience
            if (token.Issuer != _validationParameters.ValidIssuer ||
                !token.Audiences.Contains(_validationParameters.ValidAudience))
                return false;

            return true;
        }

        private async Task<bool> IsTokenBlacklistedAsync(string token)
        {
            var tokenId = _tokenHandler.ReadJwtToken(token).Id;
            return await _tokenCache.GetOrCreateAsync(
                $"{TOKEN_BLACKLIST_PREFIX}{tokenId}",
                entry => Task.FromResult(false));
        }

        private async Task<bool> IsRateLimitExceededAsync(string token)
        {
            var tokenId = _tokenHandler.ReadJwtToken(token).Id;
            var attempts = await _tokenCache.GetOrCreateAsync(
                $"{RATE_LIMIT_PREFIX}{tokenId}",
                entry =>
                {
                    entry.SetAbsoluteExpiration(TimeSpan.FromMinutes(15));
                    return Task.FromResult(0);
                });

            return attempts >= MAX_FAILED_ATTEMPTS;
        }

        private async Task IncrementFailedAttemptsAsync(string token)
        {
            var tokenId = _tokenHandler.ReadJwtToken(token).Id;
            var key = $"{RATE_LIMIT_PREFIX}{tokenId}";
            
            var attempts = await _tokenCache.GetOrCreateAsync(key, entry =>
            {
                entry.SetAbsoluteExpiration(TimeSpan.FromMinutes(15));
                return Task.FromResult(0);
            });

            _tokenCache.Set(key, attempts + 1, TimeSpan.FromMinutes(15));

            if (attempts + 1 >= MAX_FAILED_ATTEMPTS)
            {
                _logger.LogWarning("Rate limit exceeded for token ID: {TokenId}", tokenId);
            }
        }
    }

    public class TokenValidationResult
    {
        public bool IsValid { get; set; }
        public string Error { get; set; }
    }

    public class AuthenticationResult
    {
        public bool Success { get; set; }
        public string Error { get; set; }
        public string Username { get; set; }
        public IEnumerable<Claim> Claims { get; set; }
    }
}