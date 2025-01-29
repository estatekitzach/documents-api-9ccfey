using System;
using System.Threading;
using System.Threading.Tasks;
using Amazon.KeyManagementService;
using Amazon.KeyManagementService.Model;
using EstateKit.Documents.Infrastructure.Configuration;
using Microsoft.Extensions.Logging;
using System.Security.Cryptography;
using System.Collections.Concurrent;
using Polly;
using Polly.CircuitBreaker;

namespace EstateKit.Documents.Infrastructure.Security
{
    /// <summary>
    /// Service responsible for AWS KMS operations including key generation, encryption,
    /// and decryption with enhanced security features and compliance controls.
    /// </summary>
    public class KeyManagementService : IDisposable
    {
        private readonly IAmazonKeyManagementService _kmsClient;
        private readonly ILogger<KeyManagementService> _logger;
        private readonly string _kmsKeyId;
        private readonly AmazonKeyManagementServiceConfig _kmsConfig;
        private readonly AsyncCircuitBreaker _circuitBreaker;
        private readonly ConcurrentDictionary<string, SemaphoreSlim> _rateLimiters;
        private bool _disposed;

        private const int MaxRetries = 3;
        private const int MaxConcurrentOperations = 10;
        private const int MaxDataSize = 4096; // KMS maximum size for direct encryption

        /// <summary>
        /// Initializes a new instance of the KeyManagementService with secure configuration.
        /// </summary>
        /// <param name="awsConfig">AWS configuration containing KMS settings</param>
        /// <param name="logger">Logger for secure audit logging</param>
        /// <exception cref="ArgumentNullException">Thrown when required parameters are null</exception>
        /// <exception cref="ArgumentException">Thrown when configuration is invalid</exception>
        public KeyManagementService(AwsConfiguration awsConfig, ILogger<KeyManagementService> logger)
        {
            if (awsConfig == null) throw new ArgumentNullException(nameof(awsConfig));
            if (logger == null) throw new ArgumentNullException(nameof(logger));

            _logger = logger;
            _kmsKeyId = awsConfig.KmsKeyId;
            _rateLimiters = new ConcurrentDictionary<string, SemaphoreSlim>();

            // Configure KMS client with security and performance settings
            _kmsConfig = new AmazonKeyManagementServiceConfig
            {
                RegionEndpoint = Amazon.RegionEndpoint.GetBySystemName(awsConfig.Region),
                MaxErrorRetry = MaxRetries,
                Timeout = TimeSpan.FromSeconds(10),
                ThrottleRetries = true,
                UseHttp2 = true
            };

            // Initialize KMS client with configuration
            _kmsClient = new AmazonKeyManagementServiceClient(_kmsConfig);

            // Configure circuit breaker for fault tolerance
            _circuitBreaker = Policy<object>
                .Handle<AmazonKMSException>()
                .CircuitBreakerAsync(
                    exceptionsAllowedBeforeBreaking: 5,
                    durationOfBreak: TimeSpan.FromSeconds(30),
                    onBreak: (ex, duration) => LogCircuitBreakerStateChange(true, ex),
                    onReset: () => LogCircuitBreakerStateChange(false, null)
                );

            _logger.LogInformation("KeyManagementService initialized with KMS key ID: {KeyIdPrefix}*****", 
                _kmsKeyId.Substring(0, 8));
        }

        /// <summary>
        /// Generates a new data key for envelope encryption with enhanced security.
        /// </summary>
        /// <returns>Tuple containing plaintext and encrypted data key</returns>
        /// <exception cref="InvalidOperationException">Thrown when KMS operations fail</exception>
        public async Task<(byte[] plaintextKey, byte[] encryptedKey)> GenerateDataKey()
        {
            await EnsureNotDisposed();
            await EnsureRateLimit("GenerateDataKey");

            try
            {
                var request = new GenerateDataKeyRequest
                {
                    KeyId = _kmsKeyId,
                    KeySpec = DataKeySpec.AES_256,
                    EncryptionContext = new Dictionary<string, string>
                    {
                        { "Purpose", "EstateKitDocumentEncryption" },
                        { "Timestamp", DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString() }
                    }
                };

                var response = await ExecuteWithRetry(() => _kmsClient.GenerateDataKeyAsync(request));

                return (response.Plaintext.ToArray(), response.CiphertextBlob.ToArray());
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to generate data key");
                throw new InvalidOperationException("Failed to generate data key", ex);
            }
        }

        /// <summary>
        /// Encrypts data directly using AWS KMS with security validation.
        /// </summary>
        /// <param name="data">Data to encrypt</param>
        /// <returns>Encrypted data bytes</returns>
        /// <exception cref="ArgumentNullException">Thrown when data is null</exception>
        /// <exception cref="ArgumentException">Thrown when data exceeds size limits</exception>
        /// <exception cref="InvalidOperationException">Thrown when encryption fails</exception>
        public async Task<byte[]> EncryptData(byte[] data)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            if (data.Length > MaxDataSize) throw new ArgumentException($"Data exceeds maximum size of {MaxDataSize} bytes");

            await EnsureNotDisposed();
            await EnsureRateLimit("EncryptData");

            try
            {
                var request = new EncryptRequest
                {
                    KeyId = _kmsKeyId,
                    Plaintext = new MemoryStream(data),
                    EncryptionContext = new Dictionary<string, string>
                    {
                        { "Purpose", "EstateKitDirectEncryption" },
                        { "Timestamp", DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString() }
                    }
                };

                var response = await ExecuteWithRetry(() => _kmsClient.EncryptAsync(request));
                return response.CiphertextBlob.ToArray();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to encrypt data");
                throw new InvalidOperationException("Failed to encrypt data", ex);
            }
        }

        /// <summary>
        /// Decrypts data using AWS KMS with integrity verification.
        /// </summary>
        /// <param name="encryptedData">Encrypted data to decrypt</param>
        /// <returns>Decrypted data bytes</returns>
        /// <exception cref="ArgumentNullException">Thrown when encryptedData is null</exception>
        /// <exception cref="InvalidOperationException">Thrown when decryption fails</exception>
        public async Task<byte[]> DecryptData(byte[] encryptedData)
        {
            if (encryptedData == null) throw new ArgumentNullException(nameof(encryptedData));

            await EnsureNotDisposed();
            await EnsureRateLimit("DecryptData");

            try
            {
                var request = new DecryptRequest
                {
                    CiphertextBlob = new MemoryStream(encryptedData),
                    EncryptionContext = new Dictionary<string, string>
                    {
                        { "Purpose", "EstateKitDirectEncryption" },
                        { "Timestamp", DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString() }
                    }
                };

                var response = await ExecuteWithRetry(() => _kmsClient.DecryptAsync(request));
                return response.Plaintext.ToArray();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to decrypt data");
                throw new InvalidOperationException("Failed to decrypt data", ex);
            }
        }

        private async Task<T> ExecuteWithRetry<T>(Func<Task<T>> operation)
        {
            return await Policy
                .Handle<AmazonKMSException>()
                .WaitAndRetryAsync(
                    MaxRetries,
                    retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)),
                    (ex, timeSpan, retryCount, context) =>
                    {
                        _logger.LogWarning(ex, "Retry attempt {RetryCount} after {Delay}ms", 
                            retryCount, timeSpan.TotalMilliseconds);
                    })
                .ExecuteAsync(async () =>
                {
                    var result = await _circuitBreaker.ExecuteAsync(async () => await operation());
                    return (T)result;
                });
        }

        private async Task EnsureRateLimit(string operationType)
        {
            var rateLimiter = _rateLimiters.GetOrAdd(operationType, 
                _ => new SemaphoreSlim(MaxConcurrentOperations));

            if (!await rateLimiter.WaitAsync(TimeSpan.FromSeconds(5)))
            {
                throw new InvalidOperationException($"Rate limit exceeded for operation: {operationType}");
            }

            try
            {
                await Task.Delay(100); // Minimum delay between operations
            }
            finally
            {
                rateLimiter.Release();
            }
        }

        private void LogCircuitBreakerStateChange(bool isOpen, Exception exception)
        {
            if (isOpen)
            {
                _logger.LogError(exception, "Circuit breaker opened due to consecutive failures");
            }
            else
            {
                _logger.LogInformation("Circuit breaker reset and closed");
            }
        }

        private async Task EnsureNotDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(KeyManagementService));
            }
        }

        public void Dispose()
        {
            if (_disposed) return;

            _kmsClient?.Dispose();
            foreach (var rateLimiter in _rateLimiters.Values)
            {
                rateLimiter.Dispose();
            }
            _disposed = true;

            GC.SuppressFinalize(this);
        }
    }
}