using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using Amazon.S3;
using Amazon.S3.Model;
using Amazon.S3.Transfer;
using EstateKit.Documents.Core.Interfaces;
using EstateKit.Documents.Infrastructure.Configuration;
using EstateKit.Documents.Infrastructure.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Caching.Memory;
using Polly;
using Polly.CircuitBreaker;

namespace EstateKit.Documents.Infrastructure.Services
{
    /// <summary>
    /// Implements secure document storage operations using AWS S3 with server-side encryption,
    /// enhanced error handling, retry policies, and comprehensive monitoring support.
    /// </summary>
    public class AwsS3StorageService : IStorageService, IDisposable
    {
        private readonly IAmazonS3 _s3Client;
        private readonly AwsConfiguration _awsConfig;
        private readonly DocumentEncryptionService _encryptionService;
        private readonly ILogger<AwsS3StorageService> _logger;
        private readonly IMemoryCache _cache;
        private readonly AsyncCircuitBreaker _circuitBreaker;
        private readonly TransferUtility _transferUtility;
        private readonly ConcurrentDictionary<string, SemaphoreSlim> _uploadLocks;
        private bool _disposed;

        private const int MaxRetries = 3;
        private const int MaxConcurrentUploads = 5;
        private const int CacheExpirationMinutes = 30;
        private const int BufferSize = 81920; // 80KB buffer

        /// <summary>
        /// Initializes a new instance of AwsS3StorageService with required dependencies.
        /// </summary>
        public AwsS3StorageService(
            IAmazonS3 s3Client,
            AwsConfiguration awsConfig,
            DocumentEncryptionService encryptionService,
            ILogger<AwsS3StorageService> logger,
            IMemoryCache cache)
        {
            _s3Client = s3Client ?? throw new ArgumentNullException(nameof(s3Client));
            _awsConfig = awsConfig ?? throw new ArgumentNullException(nameof(awsConfig));
            _encryptionService = encryptionService ?? throw new ArgumentNullException(nameof(encryptionService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _cache = cache ?? throw new ArgumentNullException(nameof(cache));

            _transferUtility = new TransferUtility(_s3Client);
            _uploadLocks = new ConcurrentDictionary<string, SemaphoreSlim>();

            // Configure circuit breaker for fault tolerance
            _circuitBreaker = Policy<object>
                .Handle<AmazonS3Exception>()
                .CircuitBreakerAsync(
                    exceptionsAllowedBeforeBreaking: 5,
                    durationOfBreak: TimeSpan.FromSeconds(30),
                    onBreak: (ex, duration) => LogCircuitBreakerStateChange(true, ex),
                    onReset: () => LogCircuitBreakerStateChange(false, null)
                );

            _logger.LogInformation("AwsS3StorageService initialized with bucket: {BucketName}", _awsConfig.S3BucketName);
        }

        /// <inheritdoc/>
        public async Task<string> UploadDocumentAsync(Stream documentStream, string documentPath, string contentType)
        {
            if (documentStream == null) throw new ArgumentNullException(nameof(documentStream));
            if (string.IsNullOrEmpty(documentPath)) throw new ArgumentException("Document path cannot be empty", nameof(documentPath));
            
            await EnsureNotDisposed();
            var uploadLock = _uploadLocks.GetOrAdd(documentPath, _ => new SemaphoreSlim(1, 1));

            try
            {
                await uploadLock.WaitAsync();

                using var memoryStream = new MemoryStream();
                await documentStream.CopyToAsync(memoryStream, BufferSize);
                var documentBytes = memoryStream.ToArray();

                // Encrypt document content
                var (encryptedContent, encryptedDataKey) = await _encryptionService.EncryptDocument(documentBytes);

                var putRequest = new PutObjectRequest
                {
                    BucketName = _awsConfig.S3BucketName,
                    Key = documentPath,
                    InputStream = new MemoryStream(encryptedContent),
                    ContentType = contentType,
                    ServerSideEncryptionMethod = _awsConfig.EnableServerSideEncryption ? 
                        ServerSideEncryptionMethod.AES256 : null,
                    Metadata = new Dictionary<string, string>
                    {
                        { "x-amz-key-id", Convert.ToBase64String(encryptedDataKey) },
                        { "x-amz-content-sha256", CalculateChecksum(encryptedContent) },
                        { "x-amz-timestamp", DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString() }
                    }
                };

                await ExecuteWithRetry(() => _s3Client.PutObjectAsync(putRequest));

                _logger.LogInformation(
                    "Document uploaded successfully. Path: {DocumentPath}, Size: {Size} bytes",
                    documentPath, encryptedContent.Length);

                if (_awsConfig.EnableCrossRegionReplication)
                {
                    await TriggerCrossRegionReplication(documentPath, encryptedContent);
                }

                return documentPath;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to upload document: {DocumentPath}", documentPath);
                throw new InvalidOperationException("Document upload failed", ex);
            }
            finally
            {
                uploadLock.Release();
            }
        }

        /// <inheritdoc/>
        public async Task<Stream> DownloadDocumentAsync(string documentPath)
        {
            if (string.IsNullOrEmpty(documentPath)) throw new ArgumentException("Document path cannot be empty", nameof(documentPath));
            await EnsureNotDisposed();

            try
            {
                var getRequest = new GetObjectRequest
                {
                    BucketName = _awsConfig.S3BucketName,
                    Key = documentPath
                };

                var response = await ExecuteWithRetry(() => _s3Client.GetObjectAsync(getRequest));

                using var memoryStream = new MemoryStream();
                await response.ResponseStream.CopyToAsync(memoryStream);
                var encryptedContent = memoryStream.ToArray();

                // Get encrypted data key from metadata
                var encryptedDataKey = Convert.FromBase64String(response.Metadata["x-amz-key-id"]);

                // Verify checksum
                var storedChecksum = response.Metadata["x-amz-content-sha256"];
                var calculatedChecksum = CalculateChecksum(encryptedContent);
                if (storedChecksum != calculatedChecksum)
                {
                    throw new InvalidOperationException("Document integrity check failed");
                }

                var decryptedContent = await _encryptionService.DecryptDocument(encryptedContent, encryptedDataKey);

                _logger.LogInformation(
                    "Document downloaded successfully. Path: {DocumentPath}, Size: {Size} bytes",
                    documentPath, decryptedContent.Length);

                return new MemoryStream(decryptedContent);
            }
            catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                throw new FileNotFoundException($"Document not found: {documentPath}", ex);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to download document: {DocumentPath}", documentPath);
                throw new InvalidOperationException("Document download failed", ex);
            }
        }

        /// <inheritdoc/>
        public async Task<bool> DeleteDocumentAsync(string documentPath)
        {
            if (string.IsNullOrEmpty(documentPath)) throw new ArgumentException("Document path cannot be empty", nameof(documentPath));
            await EnsureNotDisposed();

            try
            {
                var deleteRequest = new DeleteObjectRequest
                {
                    BucketName = _awsConfig.S3BucketName,
                    Key = documentPath
                };

                await ExecuteWithRetry(() => _s3Client.DeleteObjectAsync(deleteRequest));

                _logger.LogInformation("Document deleted successfully. Path: {DocumentPath}", documentPath);
                _cache.Remove(documentPath);

                return true;
            }
            catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to delete document: {DocumentPath}", documentPath);
                throw new InvalidOperationException("Document deletion failed", ex);
            }
        }

        /// <inheritdoc/>
        public async Task<bool> DocumentExistsAsync(string documentPath)
        {
            if (string.IsNullOrEmpty(documentPath)) throw new ArgumentException("Document path cannot be empty", nameof(documentPath));
            await EnsureNotDisposed();

            try
            {
                var request = new GetObjectMetadataRequest
                {
                    BucketName = _awsConfig.S3BucketName,
                    Key = documentPath
                };

                await ExecuteWithRetry(() => _s3Client.GetObjectMetadataAsync(request));
                return true;
            }
            catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to check document existence: {DocumentPath}", documentPath);
                throw new InvalidOperationException("Document existence check failed", ex);
            }
        }

        /// <inheritdoc/>
        public async Task<IDictionary<string, string>> GetDocumentMetadataAsync(string documentPath)
        {
            if (string.IsNullOrEmpty(documentPath)) throw new ArgumentException("Document path cannot be empty", nameof(documentPath));
            await EnsureNotDisposed();

            try
            {
                var cacheKey = $"metadata_{documentPath}";
                if (_cache.TryGetValue(cacheKey, out IDictionary<string, string> cachedMetadata))
                {
                    return cachedMetadata;
                }

                var request = new GetObjectMetadataRequest
                {
                    BucketName = _awsConfig.S3BucketName,
                    Key = documentPath
                };

                var response = await ExecuteWithRetry(() => _s3Client.GetObjectMetadataAsync(request));

                var metadata = new Dictionary<string, string>
                {
                    { "ContentType", response.Headers.ContentType },
                    { "LastModified", response.LastModified.ToString("O") },
                    { "Size", response.ContentLength.ToString() }
                };

                _cache.Set(cacheKey, metadata, TimeSpan.FromMinutes(CacheExpirationMinutes));

                return metadata;
            }
            catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                throw new FileNotFoundException($"Document not found: {documentPath}", ex);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get document metadata: {DocumentPath}", documentPath);
                throw new InvalidOperationException("Failed to retrieve document metadata", ex);
            }
        }

        private async Task<T> ExecuteWithRetry<T>(Func<Task<T>> operation)
        {
            return await Policy
                .Handle<AmazonS3Exception>()
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

        private async Task TriggerCrossRegionReplication(string documentPath, byte[] content)
        {
            try
            {
                var replicaRequest = new PutObjectRequest
                {
                    BucketName = _awsConfig.S3ReplicaBucketName,
                    Key = documentPath,
                    InputStream = new MemoryStream(content),
                    ServerSideEncryptionMethod = ServerSideEncryptionMethod.AES256
                };

                await ExecuteWithRetry(() => _s3Client.PutObjectAsync(replicaRequest));

                _logger.LogInformation(
                    "Document replicated successfully. Path: {DocumentPath}",
                    documentPath);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to replicate document: {DocumentPath}", documentPath);
                // Don't throw - replication failure shouldn't affect primary upload
            }
        }

        private string CalculateChecksum(byte[] content)
        {
            using var sha256 = SHA256.Create();
            var hash = sha256.ComputeHash(content);
            return Convert.ToBase64String(hash);
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
                throw new ObjectDisposedException(nameof(AwsS3StorageService));
            }
        }

        public void Dispose()
        {
            if (_disposed) return;

            _transferUtility?.Dispose();
            foreach (var uploadLock in _uploadLocks.Values)
            {
                uploadLock.Dispose();
            }
            _disposed = true;

            GC.SuppressFinalize(this);
        }
    }
}