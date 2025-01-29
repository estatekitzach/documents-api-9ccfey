using System;
using System.Threading;

namespace EstateKit.Documents.Core.Entities
{
    /// <summary>
    /// Represents a specific version of a document with comprehensive version tracking 
    /// and data integrity validation in the EstateKit Documents API.
    /// </summary>
    public class DocumentVersion
    {
        private readonly SemaphoreSlim _concurrencyLock;

        /// <summary>
        /// Unique identifier for the document version
        /// </summary>
        public string Id { get; private set; }

        /// <summary>
        /// Reference to the parent document's ID
        /// </summary>
        public string DocumentId { get; private set; }

        /// <summary>
        /// S3 storage key for this version of the document
        /// </summary>
        public string S3Key { get; private set; }

        /// <summary>
        /// Cryptographic checksum for data integrity validation
        /// </summary>
        public string Checksum { get; private set; }

        /// <summary>
        /// Sequential version number for this document version
        /// </summary>
        public int VersionNumber { get; private set; }

        /// <summary>
        /// UTC timestamp when this version was created
        /// </summary>
        public DateTime CreatedAt { get; private set; }

        /// <summary>
        /// Navigation property to the parent document
        /// </summary>
        public virtual Document Document { get; set; }

        /// <summary>
        /// Initializes a new instance of the DocumentVersion class with validated parameters
        /// </summary>
        /// <param name="documentId">ID of the parent document</param>
        /// <param name="s3Key">S3 storage key for the document version</param>
        /// <param name="checksum">Cryptographic checksum for data integrity</param>
        /// <exception cref="ArgumentNullException">Thrown when any parameter is null or empty</exception>
        /// <exception cref="ArgumentException">Thrown when S3 key format is invalid</exception>
        public DocumentVersion(string documentId, string s3Key, string checksum)
        {
            if (string.IsNullOrEmpty(documentId))
            {
                throw new ArgumentNullException(nameof(documentId));
            }

            if (string.IsNullOrEmpty(s3Key))
            {
                throw new ArgumentNullException(nameof(s3Key));
            }

            if (string.IsNullOrEmpty(checksum))
            {
                throw new ArgumentNullException(nameof(checksum));
            }

            if (!ValidateS3Key(s3Key))
            {
                throw new ArgumentException("Invalid S3 key format", nameof(s3Key));
            }

            Id = Guid.NewGuid().ToString();
            DocumentId = documentId;
            S3Key = s3Key;
            Checksum = checksum;
            VersionNumber = 1;
            CreatedAt = DateTime.UtcNow;
            _concurrencyLock = new SemaphoreSlim(1, 1);
        }

        /// <summary>
        /// Updates version information with thread-safe operations and validation
        /// </summary>
        /// <param name="s3Key">New S3 storage key</param>
        /// <param name="checksum">New cryptographic checksum</param>
        /// <param name="versionNumber">New version number</param>
        /// <exception cref="ArgumentNullException">Thrown when any parameter is null or empty</exception>
        /// <exception cref="ArgumentException">Thrown when S3 key format is invalid or version number is invalid</exception>
        public virtual async void UpdateVersion(string s3Key, string checksum, int versionNumber)
        {
            if (string.IsNullOrEmpty(s3Key))
            {
                throw new ArgumentNullException(nameof(s3Key));
            }

            if (string.IsNullOrEmpty(checksum))
            {
                throw new ArgumentNullException(nameof(checksum));
            }

            if (versionNumber <= VersionNumber)
            {
                throw new ArgumentException("New version number must be greater than current version", nameof(versionNumber));
            }

            if (!ValidateS3Key(s3Key))
            {
                throw new ArgumentException("Invalid S3 key format", nameof(s3Key));
            }

            await _concurrencyLock.WaitAsync();
            try
            {
                S3Key = s3Key;
                Checksum = checksum;
                VersionNumber = versionNumber;
            }
            finally
            {
                _concurrencyLock.Release();
            }
        }

        /// <summary>
        /// Validates S3 key format and structure
        /// </summary>
        /// <param name="s3Key">S3 key to validate</param>
        /// <returns>True if the S3 key is valid, false otherwise</returns>
        private bool ValidateS3Key(string s3Key)
        {
            // S3 key must start with a valid document type path
            var validPaths = new[] { "/passwords/", "/medical/", "/insurance/", "/personal/" };
            bool hasValidPrefix = false;
            foreach (var path in validPaths)
            {
                if (s3Key.StartsWith(path, StringComparison.OrdinalIgnoreCase))
                {
                    hasValidPrefix = true;
                    break;
                }
            }

            if (!hasValidPrefix)
            {
                return false;
            }

            // S3 key must contain encrypted user ID and filename segments
            var segments = s3Key.Split('/');
            if (segments.Length != 4) // Format: /type/user_id/filename
            {
                return false;
            }

            // Validate no empty segments and no invalid characters
            return segments.Skip(1).All(segment => 
                !string.IsNullOrEmpty(segment) && 
                segment.All(c => char.IsLetterOrDigit(c) || c == '-' || c == '_' || c == '.'));
        }
    }
}