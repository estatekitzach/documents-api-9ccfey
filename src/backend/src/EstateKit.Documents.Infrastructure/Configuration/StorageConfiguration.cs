using EstateKit.Documents.Core.Constants;
using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;

namespace EstateKit.Documents.Infrastructure.Configuration
{
    /// <summary>
    /// Thread-safe configuration class for secure document storage settings and encrypted path generation.
    /// Implements comprehensive validation and security measures for document storage management.
    /// </summary>
    public sealed class StorageConfiguration
    {
        // Default maximum document size (100MB)
        private const long DEFAULT_MAX_DOCUMENT_SIZE = 104857600;
        private const string STORAGE_CONFIG_SECTION = "Storage";
        private const string PATH_SEPARATOR = "/";

        /// <summary>
        /// Gets the maximum allowed document size in bytes
        /// </summary>
        public long MaxDocumentSizeBytes { get; private set; }

        /// <summary>
        /// Gets the immutable mapping of document types to their storage paths
        /// </summary>
        public ImmutableDictionary<int, string> DocumentTypePaths { get; private set; }

        /// <summary>
        /// Gets the default storage path for documents
        /// </summary>
        public string DefaultStoragePath { get; private set; }

        /// <summary>
        /// Gets the immutable list of allowed file extensions
        /// </summary>
        public ImmutableList<string> AllowedFileExtensions { get; private set; }

        /// <summary>
        /// Gets whether the configuration has been properly initialized
        /// </summary>
        public bool IsInitialized { get; private set; }

        /// <summary>
        /// Initializes a new instance of the StorageConfiguration class with thread-safe settings
        /// </summary>
        /// <param name="configuration">The application configuration</param>
        /// <exception cref="ArgumentNullException">Thrown when configuration is null</exception>
        /// <exception cref="InvalidOperationException">Thrown when configuration is invalid</exception>
        public StorageConfiguration(IConfiguration configuration)
        {
            if (configuration == null)
            {
                throw new ArgumentNullException(nameof(configuration));
            }

            try
            {
                // Initialize document type paths with thread-safe immutable dictionary
                var pathsDictionary = new Dictionary<int, string>
                {
                    { DocumentTypes.PasswordFiles, "passwords" },
                    { DocumentTypes.Medical, "medical" },
                    { DocumentTypes.Insurance, "insurance" },
                    { DocumentTypes.PersonalIdentifiers, "personal" }
                };
                DocumentTypePaths = pathsDictionary.ToImmutableDictionary();

                // Initialize allowed extensions with thread-safe immutable list
                AllowedFileExtensions = new[]
                {
                    ".pdf", ".doc", ".docx", ".jpg", ".jpeg", ".png", ".gif", ".txt", ".xlsx"
                }.ToImmutableList();

                // Get storage configuration section
                var storageSection = configuration.GetSection(STORAGE_CONFIG_SECTION);
                
                // Set maximum document size with validation
                MaxDocumentSizeBytes = storageSection.GetValue<long>("MaxDocumentSizeBytes", DEFAULT_MAX_DOCUMENT_SIZE);
                if (MaxDocumentSizeBytes <= 0)
                {
                    throw new InvalidOperationException("MaxDocumentSizeBytes must be greater than 0");
                }

                // Set and validate default storage path
                DefaultStoragePath = storageSection.GetValue<string>("DefaultStoragePath", "documents");
                if (string.IsNullOrWhiteSpace(DefaultStoragePath))
                {
                    throw new InvalidOperationException("DefaultStoragePath cannot be empty");
                }

                // Validate configuration
                if (!Validate())
                {
                    throw new InvalidOperationException("Storage configuration validation failed");
                }

                IsInitialized = true;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("Failed to initialize storage configuration", ex);
            }
        }

        /// <summary>
        /// Generates a secure storage path for a document with encryption and validation
        /// </summary>
        /// <param name="documentType">The type of document</param>
        /// <param name="userId">The encrypted user ID</param>
        /// <param name="fileName">The file name to store</param>
        /// <returns>A secure storage path</returns>
        /// <exception cref="ArgumentException">Thrown when parameters are invalid</exception>
        /// <exception cref="InvalidOperationException">Thrown when configuration is not initialized</exception>
        public string GetDocumentPath(int documentType, string userId, string fileName)
        {
            if (!IsInitialized)
            {
                throw new InvalidOperationException("Storage configuration is not initialized");
            }

            if (string.IsNullOrWhiteSpace(userId))
            {
                throw new ArgumentException("User ID cannot be empty", nameof(userId));
            }

            if (string.IsNullOrWhiteSpace(fileName))
            {
                throw new ArgumentException("File name cannot be empty", nameof(fileName));
            }

            if (!DocumentTypes.IsValidDocumentType(documentType))
            {
                throw new ArgumentException($"Invalid document type: {documentType}", nameof(documentType));
            }

            try
            {
                // Get base path for document type
                var basePath = DocumentTypePaths.GetValueOrDefault(documentType) ?? DefaultStoragePath;

                // Combine paths with security checks
                var path = Path.Combine(
                    basePath,
                    userId, // Already encrypted by the caller
                    fileName // Already encrypted by the caller
                ).Replace("\\", PATH_SEPARATOR);

                // Validate final path
                if (!ValidatePath(path))
                {
                    throw new InvalidOperationException($"Generated path is invalid: {path}");
                }

                return path;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("Failed to generate document path", ex);
            }
        }

        /// <summary>
        /// Validates if a file extension is allowed
        /// </summary>
        /// <param name="fileExtension">The file extension to validate</param>
        /// <returns>True if the extension is allowed, false otherwise</returns>
        public bool IsFileExtensionAllowed(string fileExtension)
        {
            if (string.IsNullOrWhiteSpace(fileExtension))
            {
                return false;
            }

            // Normalize extension
            var normalizedExtension = fileExtension.ToLowerInvariant();
            if (!normalizedExtension.StartsWith("."))
            {
                normalizedExtension = "." + normalizedExtension;
            }

            return AllowedFileExtensions.Contains(normalizedExtension);
        }

        /// <summary>
        /// Validates the storage configuration
        /// </summary>
        /// <returns>True if configuration is valid, false otherwise</returns>
        public bool Validate()
        {
            try
            {
                // Validate maximum document size
                if (MaxDocumentSizeBytes <= 0)
                {
                    return false;
                }

                // Validate document type paths
                if (DocumentTypePaths == null || !DocumentTypePaths.Any())
                {
                    return false;
                }

                // Validate all document types have paths
                var allDocumentTypes = new[] 
                {
                    DocumentTypes.PasswordFiles,
                    DocumentTypes.Medical,
                    DocumentTypes.Insurance,
                    DocumentTypes.PersonalIdentifiers
                };

                if (allDocumentTypes.Any(dt => !DocumentTypePaths.ContainsKey(dt)))
                {
                    return false;
                }

                // Validate allowed extensions
                if (AllowedFileExtensions == null || !AllowedFileExtensions.Any())
                {
                    return false;
                }

                // Validate default storage path
                if (string.IsNullOrWhiteSpace(DefaultStoragePath))
                {
                    return false;
                }

                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Validates a generated storage path
        /// </summary>
        /// <param name="path">The path to validate</param>
        /// <returns>True if the path is valid, false otherwise</returns>
        private bool ValidatePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return false;
            }

            // Check for path traversal attempts
            if (path.Contains("..") || path.Contains("//"))
            {
                return false;
            }

            // Ensure path starts with valid document type folder
            return DocumentTypePaths.Values.Any(validPath => 
                path.StartsWith(validPath + PATH_SEPARATOR, StringComparison.OrdinalIgnoreCase));
        }
    }
}