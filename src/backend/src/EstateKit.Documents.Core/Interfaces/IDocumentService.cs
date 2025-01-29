using System;
using System.IO;
using System.Threading.Tasks;
using System.Collections.Generic;
using EstateKit.Documents.Core.Entities;

namespace EstateKit.Documents.Core.Interfaces
{
    /// <summary>
    /// Defines the contract for secure document management operations in the EstateKit Documents API.
    /// Provides comprehensive document lifecycle management with AWS service integration.
    /// </summary>
    public interface IDocumentService
    {
        /// <summary>
        /// Uploads a new document with secure storage and version tracking
        /// </summary>
        /// <param name="userId">ID of the user uploading the document</param>
        /// <param name="documentStream">Stream containing the document content</param>
        /// <param name="fileName">Original name of the document</param>
        /// <param name="documentType">Type of document as defined in DocumentTypes</param>
        /// <param name="contentType">MIME type of the document content</param>
        /// <returns>Created document entity with complete metadata</returns>
        /// <exception cref="ArgumentNullException">Thrown when required parameters are null</exception>
        /// <exception cref="ArgumentException">Thrown when document type or file extension is invalid</exception>
        /// <exception cref="UnauthorizedAccessException">Thrown when user is not authorized</exception>
        Task<Document> UploadDocumentAsync(
            string userId,
            Stream documentStream,
            string fileName,
            int documentType,
            string contentType);

        /// <summary>
        /// Retrieves a document by ID with access control validation
        /// </summary>
        /// <param name="documentId">ID of the document to retrieve</param>
        /// <param name="userId">ID of the user requesting the document</param>
        /// <returns>Retrieved document with metadata if authorized</returns>
        /// <exception cref="ArgumentNullException">Thrown when required parameters are null</exception>
        /// <exception cref="UnauthorizedAccessException">Thrown when user is not authorized</exception>
        /// <exception cref="KeyNotFoundException">Thrown when document is not found</exception>
        Task<Document> GetDocumentAsync(string documentId, string userId);

        /// <summary>
        /// Performs a soft delete of a document with audit trail
        /// </summary>
        /// <param name="documentId">ID of the document to delete</param>
        /// <param name="userId">ID of the user requesting deletion</param>
        /// <returns>True if deletion successful, false otherwise</returns>
        /// <exception cref="ArgumentNullException">Thrown when required parameters are null</exception>
        /// <exception cref="UnauthorizedAccessException">Thrown when user is not authorized</exception>
        /// <exception cref="KeyNotFoundException">Thrown when document is not found</exception>
        Task<bool> DeleteDocumentAsync(string documentId, string userId);

        /// <summary>
        /// Initiates document analysis using AWS Textract with progress tracking
        /// </summary>
        /// <param name="documentId">ID of the document to analyze</param>
        /// <param name="userId">ID of the user requesting analysis</param>
        /// <returns>Analysis job details including job ID and initial status</returns>
        /// <exception cref="ArgumentNullException">Thrown when required parameters are null</exception>
        /// <exception cref="UnauthorizedAccessException">Thrown when user is not authorized</exception>
        /// <exception cref="KeyNotFoundException">Thrown when document is not found</exception>
        /// <exception cref="InvalidOperationException">Thrown when document is not in analyzable state</exception>
        Task<DocumentAnalysis> AnalyzeDocumentAsync(string documentId, string userId);

        /// <summary>
        /// Retrieves the current status and results of a document analysis job
        /// </summary>
        /// <param name="analysisId">ID of the analysis job to check</param>
        /// <param name="userId">ID of the user requesting status</param>
        /// <returns>Current analysis status and results if complete</returns>
        /// <exception cref="ArgumentNullException">Thrown when required parameters are null</exception>
        /// <exception cref="UnauthorizedAccessException">Thrown when user is not authorized</exception>
        /// <exception cref="KeyNotFoundException">Thrown when analysis job is not found</exception>
        Task<DocumentAnalysis> GetAnalysisStatusAsync(string analysisId, string userId);
    }
}