using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Security.Cryptography;
using Microsoft.Extensions.Logging;

namespace EstateKit.Documents.Infrastructure.Security
{
    /// <summary>
    /// Service responsible for secure document encryption and decryption using envelope encryption
    /// with AWS KMS, implementing AES-256 encryption with comprehensive security measures.
    /// </summary>
    public class DocumentEncryptionService : IDisposable
    {
        private readonly KeyManagementService _keyManagementService;
        private readonly ILogger<DocumentEncryptionService> _logger;
        private bool _disposed;

        private const int AesKeySize = 256;
        private const int IvSize = 16;

        /// <summary>
        /// Initializes a new instance of DocumentEncryptionService with required dependencies.
        /// </summary>
        /// <param name="keyManagementService">Service for AWS KMS operations</param>
        /// <param name="logger">Logger for secure audit logging</param>
        /// <exception cref="ArgumentNullException">Thrown when required dependencies are null</exception>
        public DocumentEncryptionService(
            KeyManagementService keyManagementService,
            ILogger<DocumentEncryptionService> logger)
        {
            _keyManagementService = keyManagementService ?? throw new ArgumentNullException(nameof(keyManagementService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Encrypts document content using envelope encryption with AWS KMS and AES-256.
        /// </summary>
        /// <param name="documentContent">Document content to encrypt</param>
        /// <returns>Tuple containing encrypted content and encrypted data key</returns>
        /// <exception cref="ArgumentNullException">Thrown when document content is null</exception>
        /// <exception cref="InvalidOperationException">Thrown when encryption fails</exception>
        public async Task<(byte[] encryptedContent, byte[] encryptedDataKey)> EncryptDocument(byte[] documentContent)
        {
            if (documentContent == null) throw new ArgumentNullException(nameof(documentContent));
            await EnsureNotDisposed();

            try
            {
                // Generate data key using KMS
                var (plaintextKey, encryptedKey) = await _keyManagementService.GenerateDataKey();

                using var aes = Aes.Create();
                aes.KeySize = AesKeySize;
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.PKCS7;

                // Generate random IV
                aes.GenerateIV();
                aes.Key = plaintextKey;

                using var msEncrypted = new MemoryStream();
                // Write IV to output
                await msEncrypted.WriteAsync(aes.IV, 0, aes.IV.Length);

                // Create encryptor and encrypt content
                using (var encryptor = aes.CreateEncryptor())
                using (var cryptoStream = new CryptoStream(msEncrypted, encryptor, CryptoStreamMode.Write))
                {
                    await cryptoStream.WriteAsync(documentContent, 0, documentContent.Length);
                    await cryptoStream.FlushFinalBlockAsync();
                }

                _logger.LogInformation(
                    "Document encrypted successfully. Size: {ContentSize} bytes",
                    documentContent.Length);

                return (msEncrypted.ToArray(), encryptedKey);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to encrypt document content");
                throw new InvalidOperationException("Document encryption failed", ex);
            }
            finally
            {
                // Ensure secure cleanup
                Array.Clear(documentContent, 0, documentContent.Length);
            }
        }

        /// <summary>
        /// Decrypts document content using the encrypted data key and AWS KMS.
        /// </summary>
        /// <param name="encryptedContent">Encrypted document content</param>
        /// <param name="encryptedDataKey">Encrypted data key</param>
        /// <returns>Decrypted document content</returns>
        /// <exception cref="ArgumentNullException">Thrown when parameters are null</exception>
        /// <exception cref="InvalidOperationException">Thrown when decryption fails</exception>
        public async Task<byte[]> DecryptDocument(byte[] encryptedContent, byte[] encryptedDataKey)
        {
            if (encryptedContent == null) throw new ArgumentNullException(nameof(encryptedContent));
            if (encryptedDataKey == null) throw new ArgumentNullException(nameof(encryptedDataKey));
            await EnsureNotDisposed();

            try
            {
                // Decrypt the data key using KMS
                var plaintextKey = await _keyManagementService.DecryptData(encryptedDataKey);

                using var aes = Aes.Create();
                aes.KeySize = AesKeySize;
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.PKCS7;
                aes.Key = plaintextKey;

                using var msDecrypted = new MemoryStream();
                using var msEncrypted = new MemoryStream(encryptedContent);

                // Read IV from encrypted content
                var iv = new byte[IvSize];
                await msEncrypted.ReadAsync(iv, 0, iv.Length);
                aes.IV = iv;

                // Create decryptor and decrypt content
                using (var decryptor = aes.CreateDecryptor())
                using (var cryptoStream = new CryptoStream(msEncrypted, decryptor, CryptoStreamMode.Read))
                {
                    await cryptoStream.CopyToAsync(msDecrypted);
                }

                _logger.LogInformation(
                    "Document decrypted successfully. Size: {ContentSize} bytes",
                    msDecrypted.Length);

                return msDecrypted.ToArray();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to decrypt document content");
                throw new InvalidOperationException("Document decryption failed", ex);
            }
            finally
            {
                // Ensure secure cleanup
                if (encryptedContent != null) Array.Clear(encryptedContent, 0, encryptedContent.Length);
                if (encryptedDataKey != null) Array.Clear(encryptedDataKey, 0, encryptedDataKey.Length);
            }
        }

        /// <summary>
        /// Encrypts document name directly using AWS KMS.
        /// </summary>
        /// <param name="documentName">Document name to encrypt</param>
        /// <returns>Encrypted document name</returns>
        /// <exception cref="ArgumentNullException">Thrown when document name is null</exception>
        /// <exception cref="InvalidOperationException">Thrown when encryption fails</exception>
        public async Task<byte[]> EncryptDocumentName(string documentName)
        {
            if (string.IsNullOrEmpty(documentName)) throw new ArgumentNullException(nameof(documentName));
            await EnsureNotDisposed();

            try
            {
                var nameBytes = Encoding.UTF8.GetBytes(documentName);
                var encryptedName = await _keyManagementService.EncryptData(nameBytes);

                _logger.LogInformation(
                    "Document name encrypted successfully. Original length: {NameLength}",
                    documentName.Length);

                return encryptedName;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to encrypt document name");
                throw new InvalidOperationException("Document name encryption failed", ex);
            }
        }

        /// <summary>
        /// Decrypts document name using AWS KMS.
        /// </summary>
        /// <param name="encryptedName">Encrypted document name</param>
        /// <returns>Decrypted document name</returns>
        /// <exception cref="ArgumentNullException">Thrown when encrypted name is null</exception>
        /// <exception cref="InvalidOperationException">Thrown when decryption fails</exception>
        public async Task<string> DecryptDocumentName(byte[] encryptedName)
        {
            if (encryptedName == null) throw new ArgumentNullException(nameof(encryptedName));
            await EnsureNotDisposed();

            try
            {
                var decryptedBytes = await _keyManagementService.DecryptData(encryptedName);
                var documentName = Encoding.UTF8.GetString(decryptedBytes);

                _logger.LogInformation(
                    "Document name decrypted successfully. Length: {NameLength}",
                    documentName.Length);

                return documentName;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to decrypt document name");
                throw new InvalidOperationException("Document name decryption failed", ex);
            }
            finally
            {
                // Ensure secure cleanup
                Array.Clear(encryptedName, 0, encryptedName.Length);
            }
        }

        private async Task EnsureNotDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(DocumentEncryptionService));
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            GC.SuppressFinalize(this);
        }
    }
}