using System;
using System.IO;
using System.Threading.Tasks;
using System.Text.Json;
using System.Collections.Generic;
using System.Linq;
using Amazon.Textract;
using Amazon.Textract.Model;
using Microsoft.Extensions.Logging;
using EstateKit.Documents.Core.Interfaces;
using EstateKit.Documents.Core.Entities;
using EstateKit.Documents.Infrastructure.Configuration;

namespace EstateKit.Documents.Infrastructure.Services
{
    /// <summary>
    /// Implementation of IDocumentAnalysisService using AWS Textract for high-accuracy document analysis
    /// with comprehensive performance monitoring and error handling.
    /// </summary>
    public class TextractAnalysisService : IDocumentAnalysisService
    {
        private readonly IAmazonTextract _textractClient;
        private readonly ILogger<TextractAnalysisService> _logger;
        private readonly AwsConfiguration _awsConfig;
        private readonly IMetricsTracker _metricsTracker;

        public TextractAnalysisService(
            IAmazonTextract textractClient,
            ILogger<TextractAnalysisService> logger,
            AwsConfiguration awsConfig,
            IMetricsTracker metricsTracker)
        {
            _textractClient = textractClient ?? throw new ArgumentNullException(nameof(textractClient));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _awsConfig = awsConfig ?? throw new ArgumentNullException(nameof(awsConfig));
            _metricsTracker = metricsTracker ?? throw new ArgumentNullException(nameof(metricsTracker));

            ValidateConfiguration();
        }

        /// <inheritdoc/>
        public async Task<DocumentAnalysis> AnalyzeDocumentAsync(
            string documentId,
            Stream documentStream,
            AnalysisOptions options = null)
        {
            try
            {
                _logger.LogInformation("Starting document analysis for document ID: {DocumentId}", documentId);
                using var metrics = _metricsTracker.TrackOperation("TextractAnalysis");

                ValidateInputParameters(documentId, documentStream);
                options ??= new AnalysisOptions();

                var analysis = new DocumentAnalysis(documentId);
                analysis.UpdateStatus("Processing");

                var startAnalysisRequest = new StartDocumentAnalysisRequest
                {
                    DocumentLocation = new DocumentLocation
                    {
                        S3Object = new Amazon.Textract.Model.S3Object
                        {
                            Bucket = _awsConfig.S3BucketName,
                            Name = documentId
                        }
                    },
                    FeatureTypes = new List<string> { "TABLES", "FORMS" },
                    JobTag = analysis.AnalysisId,
                    NotificationChannel = new NotificationChannel
                    {
                        SNSTopicArn = _awsConfig.TextractQueueUrl,
                        RoleArn = _awsConfig.TextractRoleArn
                    }
                };

                var startResponse = await _textractClient.StartDocumentAnalysisAsync(startAnalysisRequest);
                var jobId = startResponse.JobId;

                var analysisResponse = await WaitForAnalysisCompletionAsync(
                    jobId, 
                    options.ProcessingTimeout,
                    options.MaxRetryAttempts);

                if (analysisResponse != null)
                {
                    var processedResult = await ProcessTextractResponseAsync(
                        analysisResponse, 
                        options.MinimumConfidence);

                    analysis.UpdateAnalysisResult(
                        processedResult,
                        CalculateAverageConfidence(analysisResponse),
                        metrics.ElapsedMilliseconds);

                    _logger.LogInformation(
                        "Document analysis completed successfully. DocumentId: {DocumentId}, AnalysisId: {AnalysisId}, Duration: {Duration}ms",
                        documentId, analysis.AnalysisId, metrics.ElapsedMilliseconds);
                }
                else
                {
                    analysis.UpdateStatus("Failed", "Analysis timeout or maximum retries exceeded");
                    _logger.LogError(
                        "Document analysis failed. DocumentId: {DocumentId}, AnalysisId: {AnalysisId}",
                        documentId, analysis.AnalysisId);
                }

                return analysis;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, 
                    "Error during document analysis. DocumentId: {DocumentId}", documentId);
                throw;
            }
        }

        /// <inheritdoc/>
        public async Task<AnalysisStatus> GetAnalysisStatusAsync(string analysisId)
        {
            try
            {
                _logger.LogInformation("Getting analysis status for ID: {AnalysisId}", analysisId);

                if (string.IsNullOrEmpty(analysisId))
                    throw new ArgumentNullException(nameof(analysisId));

                var request = new GetDocumentAnalysisRequest { JobId = analysisId };
                var response = await _textractClient.GetDocumentAnalysisAsync(request);

                return new AnalysisStatus
                {
                    Status = response.JobStatus.Value,
                    ProgressPercentage = (int)(response.ProgressPercent ?? 0),
                    ProcessingDurationMs = (long)response.ElapsedTime.TotalMilliseconds,
                    CurrentConfidence = CalculateAverageConfidence(response),
                    PagesProcessed = response.DocumentMetadata.Pages,
                    ErrorDetails = response.StatusMessage,
                    EstimatedTimeRemainingMs = (long)(response.EstimatedTimeRemaining?.TotalMilliseconds ?? 0),
                    PerformanceMetrics = CreatePerformanceMetrics(response),
                    ResourceUsage = CreateResourceUsageMetrics(response)
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting analysis status. AnalysisId: {AnalysisId}", analysisId);
                throw;
            }
        }

        private async Task<GetDocumentAnalysisResponse> WaitForAnalysisCompletionAsync(
            string jobId, 
            int timeoutMs,
            int maxRetries)
        {
            var startTime = DateTime.UtcNow;
            var retryCount = 0;
            var delay = TimeSpan.FromSeconds(5);

            while (true)
            {
                if (DateTime.UtcNow - startTime > TimeSpan.FromMilliseconds(timeoutMs))
                {
                    _logger.LogWarning("Analysis timeout reached for JobId: {JobId}", jobId);
                    return null;
                }

                try
                {
                    var response = await _textractClient.GetDocumentAnalysisAsync(
                        new GetDocumentAnalysisRequest { JobId = jobId });

                    switch (response.JobStatus.Value.ToUpper())
                    {
                        case "SUCCEEDED":
                            return response;
                        case "FAILED":
                            _logger.LogError(
                                "Analysis job failed. JobId: {JobId}, Error: {Error}",
                                jobId, response.StatusMessage);
                            return null;
                        case "IN_PROGRESS":
                            await Task.Delay(delay);
                            continue;
                        default:
                            if (++retryCount > maxRetries)
                            {
                                _logger.LogError(
                                    "Max retries exceeded for analysis job. JobId: {JobId}",
                                    jobId);
                                return null;
                            }
                            await Task.Delay(delay);
                            delay = TimeSpan.FromSeconds(Math.Min(delay.TotalSeconds * 2, 30));
                            continue;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, 
                        "Error checking analysis status. JobId: {JobId}", jobId);
                    if (++retryCount > maxRetries)
                        return null;
                    await Task.Delay(delay);
                    delay = TimeSpan.FromSeconds(Math.Min(delay.TotalSeconds * 2, 30));
                }
            }
        }

        private async Task<JsonDocument> ProcessTextractResponseAsync(
            GetDocumentAnalysisResponse response,
            double minimumConfidence)
        {
            var result = new
            {
                Pages = response.DocumentMetadata.Pages,
                Blocks = response.Blocks
                    .Where(b => b.Confidence >= minimumConfidence)
                    .Select(b => new
                    {
                        b.BlockType,
                        b.Text,
                        b.Confidence,
                        b.Geometry,
                        Relationships = b.Relationships?.Select(r => new
                        {
                            r.Type,
                            r.Ids
                        })
                    }),
                Tables = ExtractTables(response.Blocks),
                Forms = ExtractForms(response.Blocks),
                Metadata = new
                {
                    response.DocumentMetadata,
                    ProcessedAt = DateTime.UtcNow,
                    Version = "1.0"
                }
            };

            return JsonSerializer.SerializeToDocument(result);
        }

        private static double CalculateAverageConfidence(GetDocumentAnalysisResponse response)
        {
            var blocks = response.Blocks.Where(b => b.Confidence.HasValue);
            return blocks.Any() ? blocks.Average(b => b.Confidence.Value) : 0;
        }

        private static JsonDocument CreatePerformanceMetrics(GetDocumentAnalysisResponse response)
        {
            var metrics = new
            {
                ElapsedTime = response.ElapsedTime,
                ProcessingRate = response.DocumentMetadata.Pages / response.ElapsedTime.TotalSeconds,
                BlockCount = response.Blocks.Count,
                PageCount = response.DocumentMetadata.Pages
            };

            return JsonSerializer.SerializeToDocument(metrics);
        }

        private static JsonDocument CreateResourceUsageMetrics(GetDocumentAnalysisResponse response)
        {
            var usage = new
            {
                MemoryUsage = "N/A", // Textract doesn't provide this
                CpuUtilization = "N/A", // Textract doesn't provide this
                ApiCalls = 1, // Increment for each API call
                DataProcessed = response.DocumentMetadata.Pages * 1024 // Rough estimate
            };

            return JsonSerializer.SerializeToDocument(usage);
        }

        private static IEnumerable<object> ExtractTables(List<Block> blocks)
        {
            return blocks
                .Where(b => b.BlockType == BlockType.TABLE)
                .Select(table => new
                {
                    Cells = blocks
                        .Where(b => b.BlockType == BlockType.CELL && 
                               b.Relationships?.Any(r => r.Ids.Contains(table.Id)) == true)
                        .Select(cell => new
                        {
                            cell.RowIndex,
                            cell.ColumnIndex,
                            cell.Text,
                            cell.Confidence
                        })
                });
        }

        private static IEnumerable<object> ExtractForms(List<Block> blocks)
        {
            return blocks
                .Where(b => b.BlockType == BlockType.KEY_VALUE_SET)
                .Select(kv => new
                {
                    Key = blocks.FirstOrDefault(b => 
                        kv.Relationships?.Any(r => 
                            r.Type == RelationshipType.CHILD && r.Ids.Contains(b.Id)) == true)?.Text,
                    Value = blocks.FirstOrDefault(b => 
                        kv.Relationships?.Any(r => 
                            r.Type == RelationshipType.VALUE && r.Ids.Contains(b.Id)) == true)?.Text,
                    Confidence = kv.Confidence
                });
        }

        private void ValidateConfiguration()
        {
            if (string.IsNullOrEmpty(_awsConfig.TextractQueueUrl))
                throw new InvalidOperationException("Textract queue URL is not configured");

            if (_awsConfig.TextractTimeoutSeconds < 60 || _awsConfig.TextractTimeoutSeconds > 900)
                throw new InvalidOperationException("Invalid Textract timeout configuration");
        }

        private static void ValidateInputParameters(string documentId, Stream documentStream)
        {
            if (string.IsNullOrEmpty(documentId))
                throw new ArgumentNullException(nameof(documentId));

            if (documentStream == null || !documentStream.CanRead)
                throw new ArgumentException("Invalid document stream", nameof(documentStream));
        }
    }
}