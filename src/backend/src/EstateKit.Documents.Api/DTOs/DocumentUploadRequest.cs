using System;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;
using EstateKit.Documents.Core.Constants;

namespace EstateKit.Documents.Api.DTOs
{
    /// <summary>
    /// Data Transfer Object for handling document upload requests with comprehensive validation
    /// </summary>
    public class DocumentUploadRequest
    {
        /// <summary>
        /// Maximum allowed file size in bytes (100MB)
        /// </summary>
        private const long MaxFileSize = 100 * 1024 * 1024;

        /// <summary>
        /// Unique identifier of the user uploading the document
        /// </summary>
        [Required(ErrorMessage = "UserId is required")]
        [RegularExpression(@"^[a-zA-Z0-9\-_]{1,50}$", ErrorMessage = "UserId contains invalid characters or exceeds length limit")]
        public string UserId { get; set; }

        /// <summary>
        /// Type of document being uploaded (1: Password Files, 2: Medical, 3: Insurance, 4: Personal Identifiers)
        /// </summary>
        [Required(ErrorMessage = "DocumentType is required")]
        [Range(1, 4, ErrorMessage = "DocumentType must be between 1 and 4")]
        public int DocumentType { get; set; }

        /// <summary>
        /// Name of the document being uploaded
        /// </summary>
        [Required(ErrorMessage = "DocumentName is required")]
        [StringLength(50, ErrorMessage = "DocumentName cannot exceed 50 characters")]
        [RegularExpression(@"^[a-zA-Z0-9\-_\s\.]{1,50}$", ErrorMessage = "DocumentName contains invalid characters")]
        public string DocumentName { get; set; }

        /// <summary>
        /// The uploaded file content
        /// </summary>
        [Required(ErrorMessage = "File is required")]
        public IFormFile File { get; set; }

        /// <summary>
        /// Validates the document upload request with comprehensive security checks
        /// </summary>
        /// <returns>True if the request is valid, false otherwise</returns>
        public bool Validate()
        {
            try
            {
                // Validate document type
                if (!DocumentTypes.IsValidDocumentType(DocumentType))
                {
                    return false;
                }

                // Validate file presence and size
                if (File == null || File.Length == 0 || File.Length > MaxFileSize)
                {
                    return false;
                }

                // Get file extension and validate
                var fileExtension = Path.GetExtension(File.FileName)?.ToLowerInvariant();
                if (string.IsNullOrEmpty(fileExtension))
                {
                    return false;
                }

                // Validate file extension against allowed types
                if (!DocumentTypes.IsValidFileExtension(DocumentType, fileExtension))
                {
                    return false;
                }

                // Validate content type matches extension
                if (!ValidateContentTypeMatchesExtension(File.ContentType, fileExtension))
                {
                    return false;
                }

                // Validate file name for security
                if (!ValidateFileName(File.FileName))
                {
                    return false;
                }

                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>
        /// Validates that the content type matches the file extension
        /// </summary>
        private bool ValidateContentTypeMatchesExtension(string contentType, string extension)
        {
            // Mapping of file extensions to expected content types
            var contentTypeMap = new System.Collections.Generic.Dictionary<string, string[]>
            {
                { ".pdf", new[] { "application/pdf" } },
                { ".doc", new[] { "application/msword" } },
                { ".docx", new[] { "application/vnd.openxmlformats-officedocument.wordprocessingml.document" } },
                { ".jpg", new[] { "image/jpeg" } },
                { ".jpeg", new[] { "image/jpeg" } },
                { ".png", new[] { "image/png" } },
                { ".gif", new[] { "image/gif" } },
                { ".txt", new[] { "text/plain" } },
                { ".xlsx", new[] { "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet" } }
            };

            return contentTypeMap.ContainsKey(extension) && 
                   contentTypeMap[extension].Contains(contentType.ToLowerInvariant());
        }

        /// <summary>
        /// Validates file name for security concerns
        /// </summary>
        private bool ValidateFileName(string fileName)
        {
            // Check for null or empty
            if (string.IsNullOrEmpty(fileName))
            {
                return false;
            }

            // Check length
            if (fileName.Length > 255)
            {
                return false;
            }

            // Check for potentially dangerous characters
            var invalidChars = Path.GetInvalidFileNameChars();
            if (fileName.Any(c => invalidChars.Contains(c)))
            {
                return false;
            }

            // Check for common malicious patterns
            var maliciousPatterns = new[]
            {
                @"\.\.\/",    // Directory traversal
                @"\.\.\\",    // Directory traversal
                @"^\.",       // Hidden files
                @"\s+$",      // Trailing whitespace
                @"^COM\d",    // Windows reserved names
                @"^LPT\d",    // Windows reserved names
                @"^PRN",      // Windows reserved names
                @"^AUX",      // Windows reserved names
                @"^NUL",      // Windows reserved names
                @"^CON"       // Windows reserved names
            };

            return !maliciousPatterns.Any(pattern => 
                Regex.IsMatch(fileName, pattern, RegexOptions.IgnoreCase));
        }
    }
}