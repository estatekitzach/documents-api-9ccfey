using System;
using System.Text.Json;
using System.Threading;

namespace EstateKit.Documents.Core.Entities
{
    /// <summary>
    /// Represents the analysis results of a document processed through AWS Textract
    /// with comprehensive status tracking and performance optimization features.
    /// </summary>
    public class DocumentAnalysis
    {
        private readonly object _statusLock = new object();

        /// <summary>
        /// Unique identifier for the analysis result
        /// </summary>
        public string AnalysisId { get; private set; }

        /// <summary>
        /// ID of the document being analyzed
        /// </summary>
        public string DocumentId { get; private set; }

        /// <summary>
        /// Navigation property to the associated document
        /// </summary>
        public Document Document { get; set; }

        /// <summary>
        /// Extracted data from document analysis including text, tables and metadata
        /// </summary>
        public JsonDocument ExtractedData { get; private set; }

        /// <summary>
        /// UTC timestamp when the analysis was processed
        /// </summary>
        public DateTime ProcessedAt { get; private set; }

        /// <summary>
        /// Current status of the analysis (Pending, Processing, Completed, Failed)
        /// </summary>
        public string Status { get; private set; }

        /// <summary>
        /// Details of any errors encountered during analysis
        /// </summary>
        public string ErrorDetails { get; private set; }

        /// <summary>
        /// Number of retry attempts for failed analysis
        /// </summary>
        public int RetryCount { get; private set; }

        /// <summary>
        /// Confidence score of the analysis result (0.0 to 1.0)
        /// </summary>
        public double ConfidenceScore { get; private set; }

        /// <summary>
        /// Processing duration in milliseconds for performance tracking
        /// </summary>
        public long ProcessingDurationMs { get; private set; }

        /// <summary>
        /// Initializes a new instance of the DocumentAnalysis class
        /// </summary>
        /// <param name="documentId">ID of the document to analyze</param>
        /// <exception cref="ArgumentNullException">Thrown when documentId is null or empty</exception>
        public DocumentAnalysis(string documentId)
        {
            if (string.IsNullOrEmpty(documentId))
            {
                throw new ArgumentNullException(nameof(documentId));
            }

            AnalysisId = Guid.NewGuid().ToString();
            DocumentId = documentId;
            ProcessedAt = DateTime.UtcNow;
            Status = "Pending";
            RetryCount = 0;
            ConfidenceScore = 0.0;
            ProcessingDurationMs = 0;
        }

        /// <summary>
        /// Updates the analysis result with extracted data from Textract
        /// </summary>
        /// <param name="extractedData">JSON document containing extracted text and metadata</param>
        /// <param name="confidenceScore">Confidence score of the analysis (0.0 to 1.0)</param>
        /// <param name="processingDuration">Processing duration in milliseconds</param>
        /// <exception cref="ArgumentNullException">Thrown when extractedData is null</exception>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when confidenceScore is invalid</exception>
        public void UpdateAnalysisResult(JsonDocument extractedData, double confidenceScore, long processingDuration)
        {
            if (extractedData == null)
            {
                throw new ArgumentNullException(nameof(extractedData));
            }

            if (confidenceScore < 0.0 || confidenceScore > 1.0)
            {
                throw new ArgumentOutOfRangeException(nameof(confidenceScore), 
                    "Confidence score must be between 0.0 and 1.0");
            }

            lock (_statusLock)
            {
                ExtractedData = extractedData;
                ConfidenceScore = confidenceScore;
                ProcessingDurationMs = processingDuration;
                Status = "Completed";
                ProcessedAt = DateTime.UtcNow;
                ErrorDetails = null;
            }
        }

        /// <summary>
        /// Updates the current status of the analysis job with thread-safe operation
        /// </summary>
        /// <param name="status">New status value</param>
        /// <param name="errorDetails">Optional error details if status is Failed</param>
        /// <exception cref="ArgumentNullException">Thrown when status is null</exception>
        public void UpdateStatus(string status, string errorDetails = null)
        {
            if (string.IsNullOrEmpty(status))
            {
                throw new ArgumentNullException(nameof(status));
            }

            lock (_statusLock)
            {
                Status = status;
                
                if (!string.IsNullOrEmpty(errorDetails))
                {
                    ErrorDetails = errorDetails;
                }

                if (status == "Failed")
                {
                    RetryCount++;
                }

                if (status == "Completed" || status == "Failed")
                {
                    ProcessedAt = DateTime.UtcNow;
                }
            }
        }
    }
}