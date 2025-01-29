using System;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using EstateKit.Documents.Core.Entities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis; // v2.6.122

namespace EstateKit.Documents.Infrastructure.Cache
{
    /// <summary>
    /// Manages Redis caching operations with resilience, compression, and telemetry for the EstateKit Documents API.
    /// Implements high-performance caching with connection pooling and automatic retry policies.
    /// </summary>
    public sealed class RedisCacheManager : IDisposable
    {
        private readonly IConnectionMultiplexer _connection;
        private readonly IDatabase _cache;
        private readonly ILogger<RedisCacheManager> _logger;
        private readonly JsonSerializerOptions _jsonOptions;
        private const int CompressionThreshold = 1024 * 100; // 100KB
        private readonly TimeSpan _defaultTimeout = TimeSpan.FromSeconds(3);
        private bool _disposed;

        public RedisCacheManager(
            IOptions<RedisConfiguration> config,
            ILogger<RedisCacheManager> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            
            var options = config?.Value ?? throw new ArgumentNullException(nameof(config));
            
            var redisConfig = ConfigurationOptions.Parse(options.ConnectionString);
            redisConfig.AbortOnConnectFail = false;
            redisConfig.ConnectRetry = 3;
            redisConfig.ConnectTimeout = 5000;
            
            _connection = ConnectionMultiplexer.Connect(redisConfig);
            _connection.ConnectionFailed += (_, e) => 
                _logger.LogError("Redis connection failed: {ErrorMessage}", e.Exception?.Message);
            
            _cache = _connection.GetDatabase();
            
            _jsonOptions = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = false
            };
        }

        /// <summary>
        /// Caches document metadata with configurable expiration and compression
        /// </summary>
        public async Task<bool> SetDocumentMetadataAsync(
            string key,
            DocumentMetadata metadata,
            TimeSpan expiration,
            CancellationToken cancellationToken = default)
        {
            try
            {
                if (string.IsNullOrEmpty(key))
                    throw new ArgumentNullException(nameof(key));
                if (metadata == null)
                    throw new ArgumentNullException(nameof(metadata));

                var startTime = DateTime.UtcNow;
                var jsonData = JsonSerializer.Serialize(metadata, _jsonOptions);
                var dataBytes = Encoding.UTF8.GetBytes(jsonData);

                if (dataBytes.Length > CompressionThreshold)
                {
                    dataBytes = CompressData(dataBytes);
                    key = $"compressed:{key}";
                }

                var result = await _cache.StringSetAsync(
                    key,
                    dataBytes,
                    expiration,
                    flags: CommandFlags.FireAndForget
                );

                var duration = DateTime.UtcNow - startTime;
                _logger.LogInformation(
                    "Cache set for key {Key}, size: {Size}KB, duration: {Duration}ms",
                    key,
                    dataBytes.Length / 1024,
                    duration.TotalMilliseconds
                );

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error setting cache for key {Key}", key);
                return false;
            }
        }

        /// <summary>
        /// Retrieves cached document metadata with decompression support
        /// </summary>
        public async Task<DocumentMetadata> GetDocumentMetadataAsync(
            string key,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var startTime = DateTime.UtcNow;
                var isCompressed = key.StartsWith("compressed:");
                
                var data = await _cache.StringGetAsync(key);
                if (!data.HasValue)
                {
                    _logger.LogDebug("Cache miss for key {Key}", key);
                    return null;
                }

                var dataBytes = (byte[])data;
                if (isCompressed)
                {
                    dataBytes = DecompressData(dataBytes);
                }

                var jsonData = Encoding.UTF8.GetString(dataBytes);
                var metadata = JsonSerializer.Deserialize<DocumentMetadata>(jsonData, _jsonOptions);

                var duration = DateTime.UtcNow - startTime;
                _logger.LogInformation(
                    "Cache hit for key {Key}, size: {Size}KB, duration: {Duration}ms",
                    key,
                    dataBytes.Length / 1024,
                    duration.TotalMilliseconds
                );

                return metadata;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving cache for key {Key}", key);
                return null;
            }
        }

        /// <summary>
        /// Caches document analysis results with automatic compression
        /// </summary>
        public async Task<bool> SetAnalysisResultAsync(
            string analysisId,
            DocumentAnalysis analysis,
            TimeSpan expiration,
            CancellationToken cancellationToken = default)
        {
            try
            {
                if (string.IsNullOrEmpty(analysisId))
                    throw new ArgumentNullException(nameof(analysisId));
                if (analysis == null)
                    throw new ArgumentNullException(nameof(analysis));

                var startTime = DateTime.UtcNow;
                var key = $"analysis:{analysisId}";
                var jsonData = JsonSerializer.Serialize(analysis, _jsonOptions);
                var dataBytes = Encoding.UTF8.GetBytes(jsonData);

                // Always compress analysis results due to their typically large size
                dataBytes = CompressData(dataBytes);
                key = $"compressed:{key}";

                var result = await _cache.StringSetAsync(
                    key,
                    dataBytes,
                    expiration,
                    flags: CommandFlags.FireAndForget
                );

                var duration = DateTime.UtcNow - startTime;
                _logger.LogInformation(
                    "Analysis cache set for key {Key}, size: {Size}KB, duration: {Duration}ms",
                    key,
                    dataBytes.Length / 1024,
                    duration.TotalMilliseconds
                );

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error setting analysis cache for ID {AnalysisId}", analysisId);
                return false;
            }
        }

        /// <summary>
        /// Retrieves cached document analysis results with decompression
        /// </summary>
        public async Task<DocumentAnalysis> GetAnalysisResultAsync(
            string analysisId,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var key = $"compressed:analysis:{analysisId}";
                var startTime = DateTime.UtcNow;

                var data = await _cache.StringGetAsync(key);
                if (!data.HasValue)
                {
                    _logger.LogDebug("Analysis cache miss for key {Key}", key);
                    return null;
                }

                var dataBytes = DecompressData((byte[])data);
                var jsonData = Encoding.UTF8.GetString(dataBytes);
                var analysis = JsonSerializer.Deserialize<DocumentAnalysis>(jsonData, _jsonOptions);

                var duration = DateTime.UtcNow - startTime;
                _logger.LogInformation(
                    "Analysis cache hit for key {Key}, size: {Size}KB, duration: {Duration}ms",
                    key,
                    dataBytes.Length / 1024,
                    duration.TotalMilliseconds
                );

                return analysis;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving analysis cache for ID {AnalysisId}", analysisId);
                return null;
            }
        }

        /// <summary>
        /// Removes an item from cache with telemetry
        /// </summary>
        public async Task<bool> RemoveCacheAsync(
            string key,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var startTime = DateTime.UtcNow;
                var result = await _cache.KeyDeleteAsync(key);

                var duration = DateTime.UtcNow - startTime;
                _logger.LogInformation(
                    "Cache removal for key {Key}, duration: {Duration}ms",
                    key,
                    duration.TotalMilliseconds
                );

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error removing cache for key {Key}", key);
                return false;
            }
        }

        private static byte[] CompressData(byte[] data)
        {
            using var outputStream = new MemoryStream();
            using (var gzip = new GZipStream(outputStream, CompressionLevel.Optimal))
            {
                gzip.Write(data, 0, data.Length);
            }
            return outputStream.ToArray();
        }

        private static byte[] DecompressData(byte[] compressedData)
        {
            using var inputStream = new MemoryStream(compressedData);
            using var outputStream = new MemoryStream();
            using (var gzip = new GZipStream(inputStream, CompressionMode.Decompress))
            {
                gzip.CopyTo(outputStream);
            }
            return outputStream.ToArray();
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _connection?.Dispose();
            _disposed = true;
        }
    }

    public class RedisConfiguration
    {
        public string ConnectionString { get; set; }
    }
}