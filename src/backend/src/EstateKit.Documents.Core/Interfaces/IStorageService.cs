using System.IO;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace EstateKit.Documents.Core.Interfaces
{
    /// <summary>
    /// Defines the contract for secure document storage operations with AES-256 encryption support
    /// and comprehensive document lifecycle management.
    /// </summary>
    public interface IStorageService
    {
        /// <summary>
        /// Uploads an encrypted document to storage using AES-256 encryption with AWS KMS key management.
        /// </summary>
        /// <param name="documentStream">The document stream to be encrypted and uploaded.</param>
        /// <param name="documentPath">The secure path where the document will be stored.</param>
        /// <param name="contentType">The MIME type of the document being uploaded.</param>
        /// <returns>The encrypted storage path of the uploaded document.</returns>
        /// <exception cref="ArgumentNullException">Thrown when documentStream or documentPath is null.</exception>
        /// <exception cref="ArgumentException">Thrown when documentPath is empty or invalid.</exception>
        /// <exception cref="IOException">Thrown when stream operations fail.</exception>
        Task<string> UploadDocumentAsync(Stream documentStream, string documentPath, string contentType);

        /// <summary>
        /// Downloads and decrypts a document from storage using stored encryption keys.
        /// </summary>
        /// <param name="documentPath">The secure path of the document to download.</param>
        /// <returns>A stream containing the decrypted document data.</returns>
        /// <exception cref="ArgumentNullException">Thrown when documentPath is null.</exception>
        /// <exception cref="ArgumentException">Thrown when documentPath is empty or invalid.</exception>
        /// <exception cref="FileNotFoundException">Thrown when the document does not exist.</exception>
        Task<Stream> DownloadDocumentAsync(string documentPath);

        /// <summary>
        /// Securely deletes a document and its associated encryption metadata from storage.
        /// </summary>
        /// <param name="documentPath">The secure path of the document to delete.</param>
        /// <returns>True if the document was successfully deleted, false otherwise.</returns>
        /// <exception cref="ArgumentNullException">Thrown when documentPath is null.</exception>
        /// <exception cref="ArgumentException">Thrown when documentPath is empty or invalid.</exception>
        Task<bool> DeleteDocumentAsync(string documentPath);

        /// <summary>
        /// Checks if a document exists in storage without exposing internal storage paths.
        /// </summary>
        /// <param name="documentPath">The secure path of the document to check.</param>
        /// <returns>True if the document exists, false otherwise.</returns>
        /// <exception cref="ArgumentNullException">Thrown when documentPath is null.</exception>
        /// <exception cref="ArgumentException">Thrown when documentPath is empty or invalid.</exception>
        Task<bool> DocumentExistsAsync(string documentPath);

        /// <summary>
        /// Retrieves filtered metadata for a stored document excluding sensitive information.
        /// </summary>
        /// <param name="documentPath">The secure path of the document.</param>
        /// <returns>A dictionary containing the filtered document metadata.</returns>
        /// <exception cref="ArgumentNullException">Thrown when documentPath is null.</exception>
        /// <exception cref="ArgumentException">Thrown when documentPath is empty or invalid.</exception>
        /// <exception cref="FileNotFoundException">Thrown when the document does not exist.</exception>
        Task<IDictionary<string, string>> GetDocumentMetadataAsync(string documentPath);
    }
}