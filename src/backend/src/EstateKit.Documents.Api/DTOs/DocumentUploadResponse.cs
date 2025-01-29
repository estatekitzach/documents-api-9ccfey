using System;
using System.Text.Json.Serialization;
using EstateKit.Documents.Core.Entities;

namespace EstateKit.Documents.Api.DTOs
{
    /// <summary>
    /// Data Transfer Object (DTO) for document upload responses with secure handling of sensitive data.
    /// Provides a standardized response format for document upload operations including metadata and status.
    /// </summary>
    [JsonSerializable(typeof(DocumentUploadResponse))]
    public class DocumentUploadResponse
    {
        /// <summary>
        /// Unique identifier of the uploaded document
        /// </summary>
        [JsonPropertyName("documentId")]
        public string DocumentId { get; private set; }

        /// <summary>
        /// Encrypted name of the uploaded document
        /// </summary>
        [JsonPropertyName("documentName")]
        public string DocumentName { get; private set; }

        /// <summary>
        /// Type of the uploaded document as defined in DocumentTypes constants
        /// </summary>
        [JsonPropertyName("documentType")]
        public int DocumentType { get; private set; }

        /// <summary>
        /// Secure storage path where the document is stored
        /// </summary>
        [JsonPropertyName("storagePath")]
        public string StoragePath { get; private set; }

        /// <summary>
        /// UTC timestamp when the document was uploaded
        /// </summary>
        [JsonPropertyName("uploadedAt")]
        public DateTime UploadedAt { get; private set; }

        /// <summary>
        /// Indicates if the upload operation was successful
        /// </summary>
        [JsonPropertyName("success")]
        public bool Success { get; private set; }

        /// <summary>
        /// Status message describing the result of the upload operation
        /// </summary>
        [JsonPropertyName("message")]
        public string Message { get; private set; }

        /// <summary>
        /// Initializes a new instance of DocumentUploadResponse for successful uploads
        /// </summary>
        /// <param name="document">The uploaded document entity</param>
        /// <param name="storagePath">The secure storage path where the document is stored</param>
        /// <exception cref="ArgumentNullException">Thrown when document or storagePath is null</exception>
        /// <exception cref="ArgumentException">Thrown when storagePath is empty</exception>
        public DocumentUploadResponse(Document document, string storagePath)
        {
            if (document == null)
            {
                throw new ArgumentNullException(nameof(document));
            }

            if (string.IsNullOrEmpty(storagePath))
            {
                throw new ArgumentException("Storage path cannot be null or empty", nameof(storagePath));
            }

            DocumentId = document.Id ?? throw new ArgumentException("Document ID cannot be null", nameof(document));
            DocumentType = document.DocumentType;
            DocumentName = document.Metadata?.EncryptedName ?? throw new ArgumentException("Document metadata cannot be null", nameof(document));
            StoragePath = storagePath;
            UploadedAt = document.CreatedAt;
            Success = true;
            Message = "Document uploaded successfully";
        }

        /// <summary>
        /// Private constructor for creating failure responses
        /// </summary>
        private DocumentUploadResponse()
        {
        }

        /// <summary>
        /// Creates a response indicating upload failure with the specified error message
        /// </summary>
        /// <param name="errorMessage">Description of the error that occurred</param>
        /// <returns>A DocumentUploadResponse indicating failure</returns>
        /// <exception cref="ArgumentNullException">Thrown when errorMessage is null</exception>
        /// <exception cref="ArgumentException">Thrown when errorMessage is empty</exception>
        public static DocumentUploadResponse CreateFailureResponse(string errorMessage)
        {
            if (string.IsNullOrEmpty(errorMessage))
            {
                throw new ArgumentException("Error message cannot be null or empty", nameof(errorMessage));
            }

            return new DocumentUploadResponse
            {
                Success = false,
                Message = errorMessage,
                UploadedAt = DateTime.UtcNow,
                DocumentId = string.Empty,
                DocumentName = string.Empty,
                DocumentType = 0,
                StoragePath = string.Empty
            };
        }
    }
}