using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Logging;
using EstateKit.Documents.Core.Interfaces;
using EstateKit.Documents.Api.DTOs;

namespace EstateKit.Documents.Api.Controllers.V1
{
    /// <summary>
    /// Controller responsible for handling document status-related endpoints in the EstateKit Documents API.
    /// Provides secure access to document status information with comprehensive monitoring and caching.
    /// </summary>
    [ApiController]
    [Route("api/v1/documents")]
    [Authorize]
    [ApiVersion("1.0")]
    [Produces("application/json")]
    public class DocumentStatusController : ControllerBase
    {
        private readonly IDocumentService _documentService;
        private readonly ILogger<DocumentStatusController> _logger;

        /// <summary>
        /// Initializes a new instance of the DocumentStatusController
        /// </summary>
        /// <param name="documentService">Service for document operations</param>
        /// <param name="logger">Logger for monitoring and diagnostics</param>
        /// <exception cref="ArgumentNullException">Thrown when required services are null</exception>
        public DocumentStatusController(
            IDocumentService documentService,
            ILogger<DocumentStatusController> logger)
        {
            _documentService = documentService ?? throw new ArgumentNullException(nameof(documentService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            _logger.LogInformation("DocumentStatusController initialized");
        }

        /// <summary>
        /// Retrieves the current status of a document including processing state and metadata
        /// </summary>
        /// <param name="documentId">Unique identifier of the document</param>
        /// <returns>Document status information or appropriate error response</returns>
        /// <response code="200">Returns the document status information</response>
        /// <response code="401">User is not authenticated</response>
        /// <response code="403">User is not authorized to access this document</response>
        /// <response code="404">Document not found</response>
        [HttpGet("{documentId}/status")]
        [ProducesResponseType(typeof(DocumentStatusResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        [Authorize(Policy = "DocumentAccess")]
        [ResponseCache(Duration = 60, VaryByHeader = "Authorization")]
        public async Task<ActionResult<DocumentStatusResponse>> GetDocumentStatusAsync(string documentId)
        {
            try
            {
                // Validate document ID
                if (string.IsNullOrWhiteSpace(documentId))
                {
                    _logger.LogWarning("Invalid document ID provided");
                    return BadRequest("Document ID is required");
                }

                // Get user ID from claims
                var userId = User.FindFirst("sub")?.Value;
                if (string.IsNullOrEmpty(userId))
                {
                    _logger.LogWarning("User ID not found in claims");
                    return Unauthorized("User ID not found in claims");
                }

                // Create correlation ID for request tracking
                var correlationId = Guid.NewGuid().ToString();
                _logger.LogInformation(
                    "Processing document status request. CorrelationId: {CorrelationId}, DocumentId: {DocumentId}, UserId: {UserId}",
                    correlationId, documentId, userId);

                // Retrieve document with authorization check
                var document = await _documentService.GetDocumentAsync(documentId, userId);
                if (document == null)
                {
                    _logger.LogWarning(
                        "Document not found. CorrelationId: {CorrelationId}, DocumentId: {DocumentId}",
                        correlationId, documentId);
                    return NotFound($"Document with ID {documentId} not found");
                }

                // Get latest analysis status if available
                var latestAnalysis = document.AnalysisResults.MaxBy(a => a.ProcessedAt);

                // Create response DTO
                var response = new DocumentStatusResponse(document, latestAnalysis);

                _logger.LogInformation(
                    "Document status request completed successfully. CorrelationId: {CorrelationId}, DocumentId: {DocumentId}, Status: {Status}",
                    correlationId, documentId, response.Status);

                return Ok(response);
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger.LogWarning(ex, "Unauthorized access attempt for document {DocumentId}", documentId);
                return Forbid();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing document status request for document {DocumentId}", documentId);
                return StatusCode(StatusCodes.Status500InternalServerError, "An error occurred while processing your request");
            }
        }
    }
}