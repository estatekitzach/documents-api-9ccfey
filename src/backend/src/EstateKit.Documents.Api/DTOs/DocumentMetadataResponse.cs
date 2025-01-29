using System;
using System.Text.Json.Serialization;
using EstateKit.Documents.Core.Entities;
using EstateKit.Documents.Core.Services;

namespace EstateKit.Documents.Api.DTOs
{
    /// <summary>
    /// Response DTO for document metadata information with secure field filtering and name decryption.
    /// Implements secure mapping from internal document metadata while filtering sensitive information.
    /// </summary>
    public class DocumentMetadataResponse
    {
        /// <summary>
        /// Unique identifier for the document metadata
        /// </summary>
        [JsonPropertyName("id")]
        public string Id { get; set; }

        /// <summary>
        /// Decrypted original document name
        /// </summary>
        [JsonPropertyName("documentName")]
        public string DocumentName { get; set; }

        /// <summary>
        /// MIME type of the document content
        /// </summary>
        [JsonPropertyName("contentType")]
        public string ContentType { get; set; }

        /// <summary>
        /// Size of the document in bytes
        /// </summary>
        [JsonPropertyName("fileSize")]
        public long FileSize { get; set; }

        /// <summary>
        /// File extension including the leading dot
        /// </summary>
        [JsonPropertyName("fileExtension")]
        public string FileExtension { get; set; }

        /// <summary>
        /// UTC timestamp when the document metadata was created
        /// </summary>
        [JsonPropertyName("createdAt")]
        public DateTime CreatedAt { get; set; }

        /// <summary>
        /// UTC timestamp when the document metadata was last updated
        /// </summary>
        [JsonPropertyName("updatedAt")]
        public DateTime UpdatedAt { get; set; }

        /// <summary>
        /// Initializes a new instance of the DocumentMetadataResponse class
        /// </summary>
        public DocumentMetadataResponse()
        {
            Id = string.Empty;
            DocumentName = string.Empty;
            ContentType = string.Empty;
            FileSize = 0;
            FileExtension = string.Empty;
            CreatedAt = DateTime.UtcNow;
            UpdatedAt = DateTime.UtcNow;
        }

        /// <summary>
        /// Creates a response DTO from a DocumentMetadata entity with secure name decryption
        /// </summary>
        /// <param name="metadata">The source document metadata entity</param>
        /// <param name="encryptionService">Service for securely decrypting document names</param>
        /// <returns>A new DocumentMetadataResponse instance with decrypted document name</returns>
        /// <exception cref="ArgumentNullException">Thrown when metadata or encryptionService is null</exception>
        public static DocumentMetadataResponse FromEntity(DocumentMetadata metadata, IDocumentEncryptionService encryptionService)
        {
            if (metadata == null)
                throw new ArgumentNullException(nameof(metadata));
            
            if (encryptionService == null)
                throw new ArgumentNullException(nameof(encryptionService));

            return new DocumentMetadataResponse
            {
                Id = metadata.Id,
                DocumentName = encryptionService.DecryptDocumentName(metadata.EncryptedName),
                ContentType = metadata.ContentType,
                FileSize = metadata.FileSize,
                FileExtension = metadata.FileExtension,
                CreatedAt = metadata.CreatedAt.ToUniversalTime(),
                UpdatedAt = metadata.UpdatedAt.ToUniversalTime()
            };
        }
    }
}