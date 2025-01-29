using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.CircuitBreaker;
using EstateKit.Documents.Core.Interfaces;
using EstateKit.Documents.Core.Entities;
using EstateKit.Documents.Infrastructure.Cache;

namespace EstateKit.Documents.Infrastructure.Services
{
    /// <summary>
    /// Enterprise-grade document analysis service implementing AWS Textract integration
    /// with enhanced caching, resilience, and performance monitoring capabilities.
    /// </summary>
    public class DocumentAnalysisService : IDocumentAnalysisService
    {
        private readonly ILogger<DocumentAnalysisService> _logger;
        private readonly RedisCacheManager _cacheManager;
        private readonly IDocumentAnalysisService _textractService;
        private readonly AsyncCircuitBreakerPolicy _circuitBreaker;
        private readonly IAsyncPolicy _retryPolicy;
        private const int MaxRetries = 3;
        private const int BreakDuration = 30;
        private const double MinConfidenceScore = 0.98;
        private const int ProcessingTimeout = 3000;

        public DocumentAnalysisService(
            ILogger<DocumentAnalysisService> logger,
            RedisCacheManager cacheManager,
            IDocumentAnalysisService textractService)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _cacheManager = cacheManager ?? throw new ArgumentNullException(nameof(cacheManager));
            _textractService = textractService ?? throw new ArgumentNullException(nameof(textractService));

            // Configure circuit breaker for AWS Textract service
            _circuitBreaker = Policy
                .Handle<Exception>()
                .CircuitBreakerAsync(
                    exceptionsAllowedBeforeBreaking: 5,
                    durationOfBreak: TimeSpan.FromSeconds(BreakDuration),
                    onBreak: (ex, duration) =>
                    {
                        _logger.LogError(ex, 
                            "Circuit breaker opened for {Duration} seconds due to: {Error}",
                            BreakDuration, ex.Message);
                    },
                    onReset: () =>
                    {
                        _logger.LogInformation("Circuit breaker reset, resuming normal operations");
                    });

            // Configure retry policy with exponential backoff
            _retryPolicy = Policy
                .Handle<Exception>()
                .WaitAndRetryAsync(
                    retryCount: MaxRetries,
                    sleepDurationProvider: retryAttempt => 
                        TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)),
                    onRetry: (exception, timeSpan, retryCount, context) =>
                    {
                        _logger.LogWarning(exception,
                            "Retry {RetryCount} of {MaxRetries} after {Delay}ms. Error: {Error}",
                            retryCount, MaxRetries, timeSpan.TotalMilliseconds, exception.Message);
                    });
        }

        /// <inheritdoc/>
        public async Task<DocumentAnalysis> AnalyzeDocumentAsync(
            string documentId,
            Stream documentStream,
            AnalysisOptions options = null,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(documentId))
                throw new ArgumentNullException(nameof(documentId));
            if (documentStream == null || !documentStream.CanRead)
                throw new ArgumentException("Invalid document stream", nameof(documentStream));

            var correlationId = Guid.NewGuid().ToString();
            using var scope = _logger.BeginScope(
                new { CorrelationId = correlationId, DocumentId = documentId });

            try
            {
                _logger.LogInformation(
                    "Starting document analysis. CorrelationId: {CorrelationId}", 
                    correlationId);

                // Check cache first
                var cachedAnalysis = await _cacheManager.GetAnalysisResultAsync(
                    documentId, 
                    cancellationToken);

                if (cachedAnalysis != null)
                {
                    _logger.LogInformation(
                        "Retrieved analysis from cache. CorrelationId: {CorrelationId}",
                        correlationId);
                    return cachedAnalysis;
                }

                // Configure analysis options
                options ??= new AnalysisOptions
                {
                    MinimumConfidence = MinConfidenceScore,
                    ProcessingTimeout = ProcessingTimeout,
                    ExtractTables = true,
                    ExtractForms = true,
                    MaxRetryAttempts = MaxRetries
                };

                // Execute analysis with resilience policies
                var analysis = await _circuitBreaker.WrapAsync(_retryPolicy)
                    .ExecuteAsync(async () =>
                    {
                        var startTime = DateTime.UtcNow;
                        
                        var result = await _textractService.AnalyzeDocumentAsync(
                            documentId,
                            documentStream,
                            options,
                            cancellationToken);

                        var duration = DateTime.UtcNow - startTime;
                        
                        // Validate analysis results
                        if (result.ConfidenceScore < MinConfidenceScore)
                        {
                            _logger.LogWarning(
                                "Analysis confidence score {Score} below threshold {Threshold}. " +
                                "CorrelationId: {CorrelationId}",
                                result.ConfidenceScore, MinConfidenceScore, correlationId);
                        }

                        if (duration.TotalMilliseconds > ProcessingTimeout)
                        {
                            _logger.LogWarning(
                                "Analysis duration {Duration}ms exceeded timeout {Timeout}ms. " +
                                "CorrelationId: {CorrelationId}",
                                duration.TotalMilliseconds, ProcessingTimeout, correlationId);
                        }

                        return result;
                    });

                // Cache successful analysis results
                await _cacheManager.SetAnalysisResultAsync(
                    documentId,
                    analysis,
                    TimeSpan.FromHours(24),
                    cancellationToken);

                _logger.LogInformation(
                    "Document analysis completed successfully. " +
                    "CorrelationId: {CorrelationId}, Duration: {Duration}ms",
                    correlationId, analysis.ProcessingDurationMs);

                return analysis;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Document analysis failed. CorrelationId: {CorrelationId}, Error: {Error}",
                    correlationId, ex.Message);
                throw;
            }
        }

        /// <inheritdoc/>
        public async Task<AnalysisStatus> GetAnalysisStatusAsync(
            string analysisId,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(analysisId))
                throw new ArgumentNullException(nameof(analysisId));

            var correlationId = Guid.NewGuid().ToString();
            using var scope = _logger.BeginScope(
                new { CorrelationId = correlationId, AnalysisId = analysisId });

            try
            {
                _logger.LogInformation(
                    "Checking analysis status. CorrelationId: {CorrelationId}",
                    correlationId);

                // Check cache for completed analysis
                var cachedStatus = await _cacheManager.GetDocumentMetadataAsync(
                    $"status:{analysisId}",
                    cancellationToken);

                if (cachedStatus != null)
                {
                    return JsonSerializer.Deserialize<AnalysisStatus>(
                        cachedStatus.EncryptedName);
                }

                // Query status with resilience policies
                var status = await _circuitBreaker.WrapAsync(_retryPolicy)
                    .ExecuteAsync(async () =>
                    {
                        var startTime = DateTime.UtcNow;
                        
                        var result = await _textractService.GetAnalysisStatusAsync(
                            analysisId,
                            cancellationToken);

                        var duration = DateTime.UtcNow - startTime;

                        // Cache completed status
                        if (result.Status == "Completed" || result.Status == "Failed")
                        {
                            await _cacheManager.SetDocumentMetadataAsync(
                                $"status:{analysisId}",
                                new DocumentMetadata
                                {
                                    EncryptedName = JsonSerializer.Serialize(result)
                                },
                                TimeSpan.FromHours(24),
                                cancellationToken);
                        }

                        _logger.LogInformation(
                            "Status check completed. Status: {Status}, Duration: {Duration}ms, " +
                            "CorrelationId: {CorrelationId}",
                            result.Status, duration.TotalMilliseconds, correlationId);

                        return result;
                    });

                return status;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Status check failed. CorrelationId: {CorrelationId}, Error: {Error}",
                    correlationId, ex.Message);
                throw;
            }
        }
    }
}