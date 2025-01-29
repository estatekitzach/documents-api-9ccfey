using System;
using System.Threading.Tasks;
using System.IO;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Logging;
using EstateKit.Documents.Core.Interfaces;
using EstateKit.Documents.Api.DTOs;
using EstateKit.Documents.Core.Constants;

namespace EstateKit.Documents.Api.Controllers.V1
{
    /// <summary>
    /// REST API controller implementing secure document lifecycle management
    /// with comprehensive validation, error handling, and monitoring.
    /// </summary>
    [ApiController]
    [Route("api/v1/documents")]
    [Authorize]
    [ApiVersion("1.0")]
    [EnableRateLimiting("standard")]
    public class DocumentController : ControllerBase
    {
        private readonly IDocumentService _documentService;
        private readonly ILogger<DocumentController> _logger;

        /// <summary>
        /// Initializes a new instance of the DocumentController
        /// </summary>
        /// <param name="documentService">Document service for core operations</param>
        /// <param name="logger">Logger for monitoring and diagnostics</param>
        /// <exception cref="ArgumentNullException">Thrown when required services are null</exception>
        public DocumentController(IDocumentService documentService, ILogger<DocumentController> logger)
        {
            _documentService = documentService ?? throw new ArgumentNullException(nameof(documentService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Handles secure document upload with comprehensive validation and monitoring
        /// </summary>
        /// <param name="request">Document upload request containing file and metadata</param>
        /// <returns>Upload result with document metadata</returns>
        [HttpPost("upload")]
        [ProducesResponseType(typeof(DocumentUploadResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
        public async Task<ActionResult<DocumentUploadResponse>> UploadDocumentAsync([FromForm] DocumentUploadRequest request)
        {
            try
            {
                _logger.LogInformation("Document upload requested for user {UserId}, type {DocumentType}", 
                    request.UserId, request.DocumentType);

                // Validate request
                if (!request.Validate())
                {
                    _logger.LogWarning("Invalid upload request for user {UserId}", request.UserId);
                    return BadRequest(DocumentUploadResponse.CreateFailureResponse("Invalid request parameters"));
                }

                // Validate user authorization
                var authenticatedUserId = User.FindFirst("sub")?.Value;
                if (string.IsNullOrEmpty(authenticatedUserId) || authenticatedUserId != request.UserId)
                {
                    _logger.LogWarning("Unauthorized upload attempt by user {UserId}", request.UserId);
                    return Unauthorized(DocumentUploadResponse.CreateFailureResponse("Unauthorized access"));
                }

                using var stream = request.File.OpenReadStream();
                var document = await _documentService.UploadDocumentAsync(
                    request.UserId,
                    stream,
                    request.DocumentName,
                    request.DocumentType,
                    request.File.ContentType);

                var storagePath = DocumentTypes.GetStoragePath(request.DocumentType);
                var response = new DocumentUploadResponse(document, storagePath);

                _logger.LogInformation("Document {DocumentId} uploaded successfully for user {UserId}", 
                    document.Id, request.UserId);

                return Ok(response);
            }
            catch (ArgumentException ex)
            {
                _logger.LogWarning(ex, "Invalid argument in upload request for user {UserId}", request.UserId);
                return BadRequest(DocumentUploadResponse.CreateFailureResponse(ex.Message));
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger.LogWarning(ex, "Unauthorized access in upload request for user {UserId}", request.UserId);
                return Unauthorized(DocumentUploadResponse.CreateFailureResponse(ex.Message));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing upload request for user {UserId}", request.UserId);
                return StatusCode(StatusCodes.Status500InternalServerError, 
                    DocumentUploadResponse.CreateFailureResponse("An internal error occurred"));
            }
        }

        /// <summary>
        /// Retrieves a document by ID with access control validation
        /// </summary>
        /// <param name="id">Document identifier</param>
        /// <returns>Document content with metadata</returns>
        [HttpGet("{id}")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> GetDocumentAsync(string id)
        {
            try
            {
                var userId = User.FindFirst("sub")?.Value;
                if (string.IsNullOrEmpty(userId))
                {
                    _logger.LogWarning("Unauthorized document access attempt for document {DocumentId}", id);
                    return Unauthorized();
                }

                var document = await _documentService.GetDocumentAsync(id, userId);
                
                _logger.LogInformation("Document {DocumentId} retrieved successfully for user {UserId}", 
                    id, userId);

                return File(document.Metadata.S3Path, document.Metadata.ContentType, document.Metadata.EncryptedName);
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger.LogWarning(ex, "Unauthorized access to document {DocumentId}", id);
                return Unauthorized();
            }
            catch (KeyNotFoundException ex)
            {
                _logger.LogWarning(ex, "Document {DocumentId} not found", id);
                return NotFound();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving document {DocumentId}", id);
                return StatusCode(StatusCodes.Status500InternalServerError);
            }
        }

        /// <summary>
        /// Performs a soft delete of a document with audit trail
        /// </summary>
        /// <param name="id">Document identifier</param>
        /// <returns>Success/failure status</returns>
        [HttpDelete("{id}")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> DeleteDocumentAsync(string id)
        {
            try
            {
                var userId = User.FindFirst("sub")?.Value;
                if (string.IsNullOrEmpty(userId))
                {
                    _logger.LogWarning("Unauthorized document deletion attempt for document {DocumentId}", id);
                    return Unauthorized();
                }

                var success = await _documentService.DeleteDocumentAsync(id, userId);
                
                _logger.LogInformation("Document {DocumentId} deleted successfully for user {UserId}", 
                    id, userId);

                return Ok(new { success = true, message = "Document deleted successfully" });
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger.LogWarning(ex, "Unauthorized deletion attempt for document {DocumentId}", id);
                return Unauthorized();
            }
            catch (KeyNotFoundException ex)
            {
                _logger.LogWarning(ex, "Document {DocumentId} not found for deletion", id);
                return NotFound();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting document {DocumentId}", id);
                return StatusCode(StatusCodes.Status500InternalServerError);
            }
        }
    }
}