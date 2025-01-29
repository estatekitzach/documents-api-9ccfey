using System;
using System.Text.Json.Serialization;
using EstateKit.Documents.Core.Entities;

namespace EstateKit.Documents.Api.DTOs
{
    /// <summary>
    /// Data Transfer Object representing the status response for a document in the EstateKit Documents API.
    /// Provides a standardized response format for document status queries including processing state and timestamps.
    /// </summary>
    public class DocumentStatusResponse
    {
        /// <summary>
        /// Unique identifier of the document
        /// </summary>
        [JsonPropertyName("documentId")]
        public string DocumentId { get; private set; }

        /// <summary>
        /// Current status of the document (Active/Deleted)
        /// </summary>
        [JsonPropertyName("status")]
        public string Status { get; private set; }

        /// <summary>
        /// Flag indicating if the document has been marked as deleted
        /// </summary>
        [JsonPropertyName("isDeleted")]
        public bool IsDeleted { get; private set; }

        /// <summary>
        /// Current processing status of document analysis (Pending/Processing/Completed/Failed)
        /// </summary>
        [JsonPropertyName("processingStatus")]
        public string ProcessingStatus { get; private set; }

        /// <summary>
        /// UTC timestamp when the document was created
        /// </summary>
        [JsonPropertyName("createdAt")]
        public DateTime CreatedAt { get; private set; }

        /// <summary>
        /// UTC timestamp when the document was last updated
        /// </summary>
        [JsonPropertyName("updatedAt")]
        public DateTime UpdatedAt { get; private set; }

        /// <summary>
        /// UTC timestamp when the document analysis was processed (null if not processed)
        /// </summary>
        [JsonPropertyName("processedAt")]
        public DateTime? ProcessedAt { get; private set; }

        /// <summary>
        /// Initializes a new instance of the DocumentStatusResponse class
        /// </summary>
        /// <param name="document">The document entity</param>
        /// <param name="analysis">The document analysis entity (optional)</param>
        /// <exception cref="ArgumentNullException">Thrown when document is null</exception>
        public DocumentStatusResponse(Document document, DocumentAnalysis analysis = null)
        {
            if (document == null)
            {
                throw new ArgumentNullException(nameof(document));
            }

            DocumentId = document.Id;
            Status = document.IsDeleted ? "Deleted" : "Active";
            IsDeleted = document.IsDeleted;
            ProcessingStatus = analysis?.Status ?? "Pending";
            CreatedAt = document.CreatedAt;
            UpdatedAt = document.UpdatedAt;
            ProcessedAt = analysis?.ProcessedAt;
        }
    }
}