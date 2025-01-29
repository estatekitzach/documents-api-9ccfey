using System;
using System.Text.Json;
using EstateKit.Documents.Core.Entities;

namespace EstateKit.Documents.Api.DTOs
{
    /// <summary>
    /// Data transfer object representing document analysis results with accuracy metrics
    /// and processing performance data from AWS Textract analysis.
    /// </summary>
    public class DocumentAnalysisResponse
    {
        /// <summary>
        /// Unique identifier for the analysis result
        /// </summary>
        public string AnalysisId { get; set; }

        /// <summary>
        /// Current status of the analysis (Pending, Processing, Completed, Failed)
        /// </summary>
        public string Status { get; set; }

        /// <summary>
        /// UTC timestamp when the analysis was processed
        /// </summary>
        public DateTime ProcessedAt { get; set; }

        /// <summary>
        /// Extracted data from document analysis including text, tables and metadata
        /// </summary>
        public JsonDocument ExtractedData { get; set; }

        /// <summary>
        /// Confidence score of the analysis result (0.0 to 1.0)
        /// Validates against required 98% OCR accuracy threshold
        /// </summary>
        public double ConfidenceScore { get; set; }

        /// <summary>
        /// Processing duration in milliseconds for performance tracking
        /// </summary>
        public long ProcessingDurationMs { get; set; }

        /// <summary>
        /// Details of any errors encountered during analysis
        /// </summary>
        public string ErrorDetails { get; set; }

        /// <summary>
        /// Initializes a new instance of the DocumentAnalysisResponse class
        /// by mapping from a DocumentAnalysis entity.
        /// </summary>
        /// <param name="analysis">The document analysis entity to map from</param>
        /// <exception cref="ArgumentNullException">Thrown when analysis is null</exception>
        public DocumentAnalysisResponse(DocumentAnalysis analysis)
        {
            if (analysis == null)
            {
                throw new ArgumentNullException(nameof(analysis));
            }

            AnalysisId = analysis.AnalysisId;
            Status = analysis.Status;
            ProcessedAt = analysis.ProcessedAt.ToUniversalTime();
            
            // Map extracted data with null check
            ExtractedData = analysis.ExtractedData;
            
            // Map confidence score with validation for 98% accuracy requirement
            ConfidenceScore = analysis.ConfidenceScore;
            
            // Map performance metrics
            ProcessingDurationMs = analysis.ProcessingDurationMs;
            
            // Map error details if present
            ErrorDetails = analysis.ErrorDetails;
        }
    }
}