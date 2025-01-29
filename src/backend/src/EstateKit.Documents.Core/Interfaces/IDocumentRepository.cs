using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using EstateKit.Documents.Core.Entities;

namespace EstateKit.Documents.Core.Interfaces
{
    /// <summary>
    /// Defines the contract for secure document repository operations in the EstateKit Documents API.
    /// Implements comprehensive document lifecycle management with proper validation and async support.
    /// </summary>
    public interface IDocumentRepository
    {
        /// <summary>
        /// Retrieves a document by its unique identifier with proper access control
        /// </summary>
        /// <param name="documentId">Unique identifier of the document</param>
        /// <returns>Document if found and accessible, null otherwise</returns>
        /// <exception cref="ArgumentNullException">Thrown when documentId is null or empty</exception>
        Task<Document?> GetDocumentByIdAsync(string documentId);

        /// <summary>
        /// Retrieves all accessible documents for a specific user with proper filtering
        /// </summary>
        /// <param name="userId">ID of the user requesting documents</param>
        /// <returns>Collection of documents accessible to the user</returns>
        /// <exception cref="ArgumentNullException">Thrown when userId is null or empty</exception>
        Task<IEnumerable<Document>> GetDocumentsByUserIdAsync(string userId);

        /// <summary>
        /// Adds a new document to the repository with proper validation and metadata generation
        /// </summary>
        /// <param name="document">Document entity to add</param>
        /// <returns>Added document with generated ID and metadata</returns>
        /// <exception cref="ArgumentNullException">Thrown when document is null</exception>
        /// <exception cref="ArgumentException">Thrown when document validation fails</exception>
        Task<Document> AddDocumentAsync(Document document);

        /// <summary>
        /// Updates an existing document with optimistic concurrency control
        /// </summary>
        /// <param name="document">Document entity with updated information</param>
        /// <returns>True if update successful, false if document not found or concurrency conflict</returns>
        /// <exception cref="ArgumentNullException">Thrown when document is null</exception>
        /// <exception cref="ArgumentException">Thrown when document validation fails</exception>
        Task<bool> UpdateDocumentAsync(Document document);

        /// <summary>
        /// Marks a document as deleted with proper audit trail
        /// </summary>
        /// <param name="documentId">ID of the document to delete</param>
        /// <returns>True if deletion successful, false if document not found or already deleted</returns>
        /// <exception cref="ArgumentNullException">Thrown when documentId is null or empty</exception>
        Task<bool> DeleteDocumentAsync(string documentId);

        /// <summary>
        /// Adds a new version to an existing document with proper versioning control
        /// </summary>
        /// <param name="documentId">ID of the parent document</param>
        /// <param name="version">Version entity to add</param>
        /// <returns>Added version with generated ID and timestamp</returns>
        /// <exception cref="ArgumentNullException">Thrown when any parameter is null</exception>
        /// <exception cref="ArgumentException">Thrown when document not found or version validation fails</exception>
        Task<DocumentVersion> AddDocumentVersionAsync(string documentId, DocumentVersion version);

        /// <summary>
        /// Retrieves all versions of a document with proper ordering and access control
        /// </summary>
        /// <param name="documentId">ID of the document</param>
        /// <returns>Ordered collection of document versions</returns>
        /// <exception cref="ArgumentNullException">Thrown when documentId is null or empty</exception>
        Task<IEnumerable<DocumentVersion>> GetDocumentVersionsAsync(string documentId);

        /// <summary>
        /// Adds analysis results for a document with proper validation and status tracking
        /// </summary>
        /// <param name="documentId">ID of the analyzed document</param>
        /// <param name="analysis">Analysis result entity to add</param>
        /// <returns>Added analysis result with generated ID and timestamp</returns>
        /// <exception cref="ArgumentNullException">Thrown when any parameter is null</exception>
        /// <exception cref="ArgumentException">Thrown when document not found or analysis validation fails</exception>
        Task<DocumentAnalysis> AddAnalysisResultAsync(string documentId, DocumentAnalysis analysis);

        /// <summary>
        /// Retrieves all analysis results for a document with proper ordering and access control
        /// </summary>
        /// <param name="documentId">ID of the document</param>
        /// <returns>Ordered collection of document analysis results</returns>
        /// <exception cref="ArgumentNullException">Thrown when documentId is null or empty</exception>
        Task<IEnumerable<DocumentAnalysis>> GetAnalysisResultsAsync(string documentId);
    }
}