using System;
using EstateKit.Documents.Core.Constants;

namespace EstateKit.Documents.Core.Entities
{
    /// <summary>
    /// Represents secure metadata associated with a document in the EstateKit system.
    /// Implements AES-256 encryption for sensitive fields and comprehensive integrity verification.
    /// </summary>
    public class DocumentMetadata
    {
        /// <summary>
        /// Unique identifier for the metadata record
        /// </summary>
        public string Id { get; private set; }

        /// <summary>
        /// Reference to the parent document's ID
        /// </summary>
        public string DocumentId { get; set; }

        /// <summary>
        /// AES-256 encrypted original document name
        /// </summary>
        public string EncryptedName { get; private set; }

        /// <summary>
        /// MIME type of the document content
        /// </summary>
        public string ContentType { get; private set; }

        /// <summary>
        /// Size of the document in bytes
        /// </summary>
        public long FileSize { get; private set; }

        /// <summary>
        /// Validated file extension (including dot)
        /// </summary>
        public string FileExtension { get; private set; }

        /// <summary>
        /// Secure S3 storage path for the document
        /// </summary>
        public string S3Path { get; private set; }

        /// <summary>
        /// SHA-256 checksum for document integrity verification
        /// </summary>
        public string Checksum { get; private set; }

        /// <summary>
        /// UTC timestamp when the metadata was created
        /// </summary>
        public DateTime CreatedAt { get; private set; }

        /// <summary>
        /// UTC timestamp when the metadata was last updated
        /// </summary>
        public DateTime UpdatedAt { get; private set; }

        /// <summary>
        /// Navigation property to the parent document
        /// </summary>
        public Document Document { get; set; }

        /// <summary>
        /// Initializes a new instance of DocumentMetadata with secure defaults
        /// </summary>
        public DocumentMetadata()
        {
            Id = Guid.NewGuid().ToString();
            CreatedAt = DateTime.UtcNow;
            UpdatedAt = CreatedAt;
            EncryptedName = string.Empty;
            ContentType = "application/octet-stream";
            FileSize = 0;
            FileExtension = string.Empty;
            S3Path = string.Empty;
            Checksum = string.Empty;
        }

        /// <summary>
        /// Updates document metadata while maintaining security and integrity
        /// </summary>
        /// <param name="encryptedName">AES-256 encrypted document name</param>
        /// <param name="contentType">MIME type of the document</param>
        /// <param name="fileSize">Size in bytes</param>
        /// <param name="fileExtension">File extension with leading dot</param>
        /// <param name="s3Path">Secure S3 storage path</param>
        /// <param name="checksum">SHA-256 checksum</param>
        /// <exception cref="ArgumentNullException">Thrown when required parameters are null</exception>
        /// <exception cref="ArgumentException">Thrown when parameters fail validation</exception>
        public void UpdateMetadata(
            string encryptedName,
            string contentType,
            long fileSize,
            string fileExtension,
            string s3Path,
            string checksum)
        {
            // Validate required parameters
            if (string.IsNullOrWhiteSpace(encryptedName))
                throw new ArgumentNullException(nameof(encryptedName));
            if (string.IsNullOrWhiteSpace(contentType))
                throw new ArgumentNullException(nameof(contentType));
            if (string.IsNullOrWhiteSpace(fileExtension))
                throw new ArgumentNullException(nameof(fileExtension));
            if (string.IsNullOrWhiteSpace(s3Path))
                throw new ArgumentNullException(nameof(s3Path));
            if (string.IsNullOrWhiteSpace(checksum))
                throw new ArgumentNullException(nameof(checksum));

            // Validate file size
            if (fileSize <= 0)
                throw new ArgumentException("File size must be greater than 0", nameof(fileSize));

            // Validate checksum format (SHA-256 = 64 characters)
            if (checksum.Length != 64)
                throw new ArgumentException("Invalid checksum format", nameof(checksum));

            // Normalize and validate file extension
            var normalizedExtension = fileExtension.ToLowerInvariant();
            if (!normalizedExtension.StartsWith("."))
                normalizedExtension = "." + normalizedExtension;

            // Validate file extension against document type (requires Document navigation property)
            if (Document != null && !DocumentTypes.IsValidFileExtension(Document.DocumentType, normalizedExtension))
                throw new ArgumentException($"Invalid file extension for document type: {normalizedExtension}", nameof(fileExtension));

            // Update metadata properties
            EncryptedName = encryptedName;
            ContentType = contentType;
            FileSize = fileSize;
            FileExtension = normalizedExtension;
            S3Path = s3Path;
            Checksum = checksum;
            UpdatedAt = DateTime.UtcNow;
        }
    }
}