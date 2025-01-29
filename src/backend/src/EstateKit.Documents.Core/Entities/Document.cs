using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using EstateKit.Documents.Core.Constants;

namespace EstateKit.Documents.Core.Entities
{
    /// <summary>
    /// Represents a document in the EstateKit system with its associated metadata, versions, and analysis results.
    /// Implements thread-safe operations and comprehensive audit tracking.
    /// </summary>
    public class Document
    {
        private readonly ConcurrentBag<DocumentVersion> _versions;
        private readonly ConcurrentBag<DocumentAnalysis> _analysisResults;

        /// <summary>
        /// Unique identifier for the document
        /// </summary>
        public string Id { get; private set; }

        /// <summary>
        /// ID of the user who owns this document
        /// </summary>
        public string UserId { get; set; }

        /// <summary>
        /// Type of document as defined in DocumentTypes constants
        /// </summary>
        public int DocumentType { get; set; }

        /// <summary>
        /// Flag indicating if the document has been marked as deleted
        /// </summary>
        public bool IsDeleted { get; private set; }

        /// <summary>
        /// UTC timestamp when the document was created
        /// </summary>
        public DateTime CreatedAt { get; private set; }

        /// <summary>
        /// UTC timestamp when the document was last updated
        /// </summary>
        public DateTime UpdatedAt { get; private set; }

        /// <summary>
        /// UTC timestamp when the document was deleted (null if not deleted)
        /// </summary>
        public DateTime? DeletedAt { get; private set; }

        /// <summary>
        /// Associated metadata for the document
        /// </summary>
        public DocumentMetadata Metadata { get; set; }

        /// <summary>
        /// Thread-safe read-only collection of document versions
        /// </summary>
        public IReadOnlyCollection<DocumentVersion> Versions => _versions;

        /// <summary>
        /// Thread-safe read-only collection of document analysis results
        /// </summary>
        public IReadOnlyCollection<DocumentAnalysis> AnalysisResults => _analysisResults;

        /// <summary>
        /// Initializes a new instance of the Document class with thread-safe collections
        /// </summary>
        public Document()
        {
            Id = Guid.NewGuid().ToString();
            CreatedAt = DateTime.UtcNow;
            UpdatedAt = DateTime.UtcNow;
            IsDeleted = false;
            _versions = new ConcurrentBag<DocumentVersion>();
            _analysisResults = new ConcurrentBag<DocumentAnalysis>();
        }

        /// <summary>
        /// Validates if the document type is valid using DocumentTypes constants
        /// </summary>
        /// <returns>True if document type is valid, false otherwise</returns>
        public bool ValidateDocumentType()
        {
            return DocumentTypes.IsValidDocumentType(DocumentType);
        }

        /// <summary>
        /// Marks the document as deleted and updates all relevant timestamps atomically
        /// </summary>
        public void MarkAsDeleted()
        {
            if (IsDeleted)
            {
                return;
            }

            IsDeleted = true;
            DeletedAt = DateTime.UtcNow;
            UpdatedAt = DeletedAt.Value;
        }

        /// <summary>
        /// Adds a new version to the document's version history with thread safety
        /// </summary>
        /// <param name="version">The version to add</param>
        /// <exception cref="ArgumentNullException">Thrown when version is null</exception>
        public void AddVersion(DocumentVersion version)
        {
            if (version == null)
            {
                throw new ArgumentNullException(nameof(version));
            }

            _versions.Add(version);
            UpdatedAt = DateTime.UtcNow;
        }

        /// <summary>
        /// Adds a new analysis result to the document with thread safety
        /// </summary>
        /// <param name="analysis">The analysis result to add</param>
        /// <exception cref="ArgumentNullException">Thrown when analysis is null</exception>
        public void AddAnalysisResult(DocumentAnalysis analysis)
        {
            if (analysis == null)
            {
                throw new ArgumentNullException(nameof(analysis));
            }

            _analysisResults.Add(analysis);
            UpdatedAt = DateTime.UtcNow;
        }
    }
}