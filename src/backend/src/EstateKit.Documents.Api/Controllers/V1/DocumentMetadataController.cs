using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Logging;
using EstateKit.Documents.Core.Interfaces;
using EstateKit.Documents.Api.DTOs;
using System.Security.Claims;

namespace EstateKit.Documents.Api.Controllers.V1
{
    /// <summary>
    /// Controller responsible for secure document metadata operations
    /// </summary>
    [ApiController]
    [Route("api/v1/documents")]
    [Authorize]
    [ApiVersion("1.0")]
    public class DocumentMetadataController : ControllerBase
    {
        private readonly IDocumentService _documentService;
        private readonly ILogger<DocumentMetadataController> _logger;

        /// <summary>
        /// Initializes a new instance of the DocumentMetadataController
        /// </summary>
        /// <param name="documentService">Service for document operations</param>
        /// <param name="logger">Logger for controller operations</param>
        /// <exception cref="ArgumentNullException">Thrown when required services are null</exception>
        public DocumentMetadataController(
            IDocumentService documentService,
            ILogger<DocumentMetadataController> logger)
        {
            _documentService = documentService ?? throw new ArgumentNullException(nameof(documentService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Retrieves metadata for a specific document with comprehensive security checks
        /// </summary>
        /// <param name="documentId">ID of the document to retrieve metadata for</param>
        /// <returns>Document metadata response or appropriate error status</returns>
        [HttpGet("{documentId}/metadata")]
        [ProducesResponseType(typeof(DocumentMetadataResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<ActionResult<DocumentMetadataResponse>> GetDocumentMetadataAsync(string documentId)
        {
            try
            {
                // Validate document ID
                if (string.IsNullOrEmpty(documentId))
                {
                    _logger.LogWarning("GetDocumentMetadataAsync called with null or empty documentId");
                    return BadRequest("Document ID is required");
                }

                // Extract user ID from claims
                var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                if (string.IsNullOrEmpty(userId))
                {
                    _logger.LogWarning("User ID not found in claims for document metadata request");
                    return Unauthorized("User identification required");
                }

                // Validate user access to document
                var document = await _documentService.GetDocumentAsync(documentId, userId);
                if (document == null)
                {
                    _logger.LogWarning("Document not found or access denied. DocumentId: {DocumentId}, UserId: {UserId}", 
                        documentId, userId);
                    return NotFound("Document not found");
                }

                // Create and return metadata response
                var response = DocumentMetadataResponse.FromEntity(document.Metadata);
                
                _logger.LogInformation("Document metadata retrieved successfully. DocumentId: {DocumentId}, UserId: {UserId}", 
                    documentId, userId);
                
                return Ok(response);
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger.LogWarning(ex, "Unauthorized access attempt to document metadata. DocumentId: {DocumentId}", 
                    documentId);
                return Forbid();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving document metadata. DocumentId: {DocumentId}", documentId);
                return StatusCode(StatusCodes.Status500InternalServerError, 
                    "An error occurred while retrieving document metadata");
            }
        }
    }
}