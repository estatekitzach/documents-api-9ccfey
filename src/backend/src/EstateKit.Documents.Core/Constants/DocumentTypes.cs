using System;
using System.Collections.Generic;
using System.Linq;

namespace EstateKit.Documents.Core.Constants
{
    /// <summary>
    /// Provides thread-safe document type constants, validation, and utility functions 
    /// for secure document handling in the EstateKit Documents API.
    /// </summary>
    public static class DocumentTypes
    {
        /// <summary>
        /// Document type constant for Password Files (ID: 1)
        /// </summary>
        public const int PasswordFiles = 1;

        /// <summary>
        /// Document type constant for Medical Documents (ID: 2)
        /// </summary>
        public const int Medical = 2;

        /// <summary>
        /// Document type constant for Insurance Documents (ID: 3)
        /// </summary>
        public const int Insurance = 3;

        /// <summary>
        /// Document type constant for Personal Identifiers (ID: 4)
        /// </summary>
        public const int PersonalIdentifiers = 4;

        /// <summary>
        /// Thread-safe readonly dictionary of allowed file extensions per document type
        /// </summary>
        private static readonly IReadOnlyDictionary<int, string[]> AllowedExtensions;

        /// <summary>
        /// Thread-safe readonly dictionary of storage path patterns per document type
        /// </summary>
        private static readonly IReadOnlyDictionary<int, string> StoragePaths;

        /// <summary>
        /// Static constructor to initialize thread-safe document type mappings
        /// </summary>
        static DocumentTypes()
        {
            var extensions = new Dictionary<int, string[]>
            {
                [PasswordFiles] = new[] { ".pdf", ".doc", ".docx", ".jpg", ".jpeg", ".png", ".gif", ".txt", ".xlsx" },
                [Medical] = new[] { ".pdf", ".doc", ".docx", ".jpg", ".jpeg", ".png", ".gif", ".txt", ".xlsx" },
                [Insurance] = new[] { ".pdf", ".doc", ".docx", ".jpg", ".jpeg", ".png", ".gif", ".txt", ".xlsx" },
                [PersonalIdentifiers] = new[] { ".pdf", ".doc", ".docx", ".jpg", ".jpeg", ".png", ".gif", ".txt", ".xlsx" }
            };

            var paths = new Dictionary<int, string>
            {
                [PasswordFiles] = "/passwords/{encrypted_user_id}/{encrypted_filename}",
                [Medical] = "/medical/{encrypted_user_id}/{encrypted_filename}",
                [Insurance] = "/insurance/{encrypted_user_id}/{encrypted_filename}",
                [PersonalIdentifiers] = "/personal/{encrypted_user_id}/{encrypted_filename}"
            };

            AllowedExtensions = extensions;
            StoragePaths = paths;
        }

        /// <summary>
        /// Validates if a given document type ID is valid
        /// </summary>
        /// <param name="documentType">The document type ID to validate</param>
        /// <returns>True if the document type is valid, false otherwise</returns>
        public static bool IsValidDocumentType(int documentType)
        {
            return AllowedExtensions.ContainsKey(documentType);
        }

        /// <summary>
        /// Retrieves the secure storage path pattern for a document type
        /// </summary>
        /// <param name="documentType">The document type ID</param>
        /// <returns>The storage path pattern for the document type</returns>
        /// <exception cref="ArgumentException">Thrown when document type is invalid</exception>
        public static string GetStoragePath(int documentType)
        {
            if (!IsValidDocumentType(documentType))
            {
                throw new ArgumentException($"Invalid document type: {documentType}", nameof(documentType));
            }

            return StoragePaths[documentType];
        }

        /// <summary>
        /// Retrieves allowed file extensions for a document type
        /// </summary>
        /// <param name="documentType">The document type ID</param>
        /// <returns>Array of allowed file extensions for the document type</returns>
        /// <exception cref="ArgumentException">Thrown when document type is invalid</exception>
        public static string[] GetAllowedExtensions(int documentType)
        {
            if (!IsValidDocumentType(documentType))
            {
                throw new ArgumentException($"Invalid document type: {documentType}", nameof(documentType));
            }

            return AllowedExtensions[documentType];
        }

        /// <summary>
        /// Validates if a file extension is allowed for a given document type
        /// </summary>
        /// <param name="documentType">The document type ID</param>
        /// <param name="fileExtension">The file extension to validate (with or without leading dot)</param>
        /// <returns>True if the extension is valid for the document type, false otherwise</returns>
        /// <exception cref="ArgumentException">Thrown when document type is invalid</exception>
        /// <exception cref="ArgumentNullException">Thrown when fileExtension is null</exception>
        public static bool IsValidFileExtension(int documentType, string fileExtension)
        {
            if (fileExtension == null)
            {
                throw new ArgumentNullException(nameof(fileExtension));
            }

            if (!IsValidDocumentType(documentType))
            {
                throw new ArgumentException($"Invalid document type: {documentType}", nameof(documentType));
            }

            // Normalize extension to lowercase with leading dot
            var normalizedExtension = fileExtension.ToLowerInvariant();
            if (!normalizedExtension.StartsWith("."))
            {
                normalizedExtension = "." + normalizedExtension;
            }

            return AllowedExtensions[documentType].Contains(normalizedExtension);
        }
    }
}