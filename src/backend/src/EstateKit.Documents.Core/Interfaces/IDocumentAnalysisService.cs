using System;
using System.IO;
using System.Threading.Tasks;
using System.Text.Json;
using EstateKit.Documents.Core.Entities;

namespace EstateKit.Documents.Core.Interfaces
{
    /// <summary>
    /// Defines the contract for document analysis operations using AWS Textract
    /// with enhanced performance monitoring, error handling, and accuracy tracking.
    /// </summary>
    public interface IDocumentAnalysisService
    {
        /// <summary>
        /// Analyzes a document using AWS Textract to extract text, tables, and metadata
        /// with comprehensive performance and accuracy tracking.
        /// </summary>
        /// <param name="documentId">Unique identifier of the document to analyze</param>
        /// <param name="documentStream">Stream containing the document content</param>
        /// <param name="options">Optional analysis configuration parameters</param>
        /// <returns>
        /// A DocumentAnalysis object containing extracted data, accuracy metrics,
        /// and performance statistics
        /// </returns>
        /// <exception cref="ArgumentNullException">
        /// Thrown when documentId or documentStream is null
        /// </exception>
        /// <exception cref="ArgumentException">
        /// Thrown when documentStream is empty or invalid
        /// </exception>
        /// <exception cref="InvalidOperationException">
        /// Thrown when analysis operation fails critically
        /// </exception>
        Task<DocumentAnalysis> AnalyzeDocumentAsync(
            string documentId,
            Stream documentStream,
            AnalysisOptions options = null);

        /// <summary>
        /// Retrieves the current status of a document analysis job with detailed
        /// metrics and performance data.
        /// </summary>
        /// <param name="analysisId">Unique identifier of the analysis job</param>
        /// <returns>
        /// Current analysis status including processing stage, performance metrics,
        /// and any error details
        /// </returns>
        /// <exception cref="ArgumentNullException">
        /// Thrown when analysisId is null or empty
        /// </exception>
        /// <exception cref="KeyNotFoundException">
        /// Thrown when analysis job is not found
        /// </exception>
        Task<AnalysisStatus> GetAnalysisStatusAsync(string analysisId);
    }

    /// <summary>
    /// Configurable options for document analysis operations
    /// </summary>
    public class AnalysisOptions
    {
        /// <summary>
        /// Minimum required confidence score for extracted text (0.0 to 1.0)
        /// Default: 0.98 as per technical specification
        /// </summary>
        public double MinimumConfidence { get; set; } = 0.98;

        /// <summary>
        /// Maximum processing time in milliseconds
        /// Default: 3000ms as per technical specification
        /// </summary>
        public int ProcessingTimeout { get; set; } = 3000;

        /// <summary>
        /// Enable table extraction and analysis
        /// </summary>
        public bool ExtractTables { get; set; } = true;

        /// <summary>
        /// Enable forms/key-value pair extraction
        /// </summary>
        public bool ExtractForms { get; set; } = true;

        /// <summary>
        /// Maximum number of retry attempts for failed analysis
        /// </summary>
        public int MaxRetryAttempts { get; set; } = 3;
    }

    /// <summary>
    /// Represents the current status of a document analysis job
    /// with detailed metrics and performance data
    /// </summary>
    public class AnalysisStatus
    {
        /// <summary>
        /// Current processing stage of the analysis
        /// </summary>
        public string Status { get; set; }

        /// <summary>
        /// Detailed progress information (0-100%)
        /// </summary>
        public int ProgressPercentage { get; set; }

        /// <summary>
        /// Current processing duration in milliseconds
        /// </summary>
        public long ProcessingDurationMs { get; set; }

        /// <summary>
        /// Current confidence score of processed content
        /// </summary>
        public double CurrentConfidence { get; set; }

        /// <summary>
        /// Number of pages processed so far
        /// </summary>
        public int PagesProcessed { get; set; }

        /// <summary>
        /// Detailed error information if analysis failed
        /// </summary>
        public string ErrorDetails { get; set; }

        /// <summary>
        /// Number of retry attempts made
        /// </summary>
        public int RetryCount { get; set; }

        /// <summary>
        /// Estimated time remaining in milliseconds
        /// </summary>
        public long EstimatedTimeRemainingMs { get; set; }

        /// <summary>
        /// Performance metrics for monitoring and optimization
        /// </summary>
        public JsonDocument PerformanceMetrics { get; set; }

        /// <summary>
        /// Resource usage statistics for the analysis job
        /// </summary>
        public JsonDocument ResourceUsage { get; set; }
    }
}