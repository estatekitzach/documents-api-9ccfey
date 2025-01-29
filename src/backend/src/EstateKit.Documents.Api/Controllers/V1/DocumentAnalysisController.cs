using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Logging;
using EstateKit.Documents.Core.Interfaces;
using EstateKit.Documents.Api.DTOs;
using System.Diagnostics;

namespace EstateKit.Documents.Api.Controllers.V1
{
    /// <summary>
    /// Controller handling document analysis operations through AWS Textract integration
    /// with enhanced performance monitoring and accuracy tracking.
    /// </summary>
    [ApiController]
    [Route("api/v1/documents/analysis")]
    [Authorize]
    public class DocumentAnalysisController : ControllerBase
    {
        private readonly IDocumentAnalysisService _documentAnalysisService;
        private readonly ILogger<DocumentAnalysisController> _logger;
        private const int MaxProcessingTime = 3000; // 3 seconds as per technical spec
        private const double MinConfidenceScore = 0.98; // 98% accuracy requirement

        /// <summary>
        /// Initializes a new instance of the DocumentAnalysisController
        /// </summary>
        /// <param name="documentAnalysisService">Service for document analysis operations</param>
        /// <param name="logger">Logger for telemetry and monitoring</param>
        /// <exception cref="ArgumentNullException">Thrown when required services are null</exception>
        public DocumentAnalysisController(
            IDocumentAnalysisService documentAnalysisService,
            ILogger<DocumentAnalysisController> logger)
        {
            _documentAnalysisService = documentAnalysisService ?? 
                throw new ArgumentNullException(nameof(documentAnalysisService));
            _logger = logger ?? 
                throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Initiates document analysis using AWS Textract with performance monitoring
        /// </summary>
        /// <param name="request">Analysis request parameters</param>
        /// <returns>Analysis job details with performance metrics</returns>
        [HttpPost]
        [ProducesResponseType(typeof(DocumentAnalysisResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
        public async Task<ActionResult<DocumentAnalysisResponse>> AnalyzeDocumentAsync(
            [FromBody] DocumentAnalysisRequest request)
        {
            var stopwatch = Stopwatch.StartNew();
            var correlationId = Guid.NewGuid().ToString();

            try
            {
                _logger.LogInformation(
                    "Starting document analysis. CorrelationId: {CorrelationId}, DocumentId: {DocumentId}, DocumentType: {DocumentType}",
                    correlationId, request.DocumentId, request.DocumentType);

                if (!request.Validate())
                {
                    _logger.LogWarning(
                        "Invalid analysis request. CorrelationId: {CorrelationId}, DocumentId: {DocumentId}",
                        correlationId, request.DocumentId);
                    return BadRequest("Invalid analysis request parameters");
                }

                var options = new AnalysisOptions
                {
                    MinimumConfidence = MinConfidenceScore,
                    ProcessingTimeout = MaxProcessingTime,
                    ExtractTables = request.ExtractTables
                };

                var analysis = await _documentAnalysisService.AnalyzeDocumentAsync(
                    request.DocumentId,
                    null, // Document stream is retrieved by service using DocumentId
                    options);

                var response = new DocumentAnalysisResponse(analysis);

                stopwatch.Stop();
                _logger.LogInformation(
                    "Document analysis initiated successfully. CorrelationId: {CorrelationId}, " +
                    "AnalysisId: {AnalysisId}, ProcessingTime: {ProcessingTime}ms",
                    correlationId, response.AnalysisId, stopwatch.ElapsedMilliseconds);

                return Ok(response);
            }
            catch (ArgumentException ex)
            {
                _logger.LogWarning(
                    ex,
                    "Invalid argument in analysis request. CorrelationId: {CorrelationId}",
                    correlationId);
                return BadRequest(ex.Message);
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogError(
                    ex,
                    "Analysis operation failed. CorrelationId: {CorrelationId}",
                    correlationId);
                return StatusCode(StatusCodes.Status500InternalServerError, 
                    "Document analysis operation failed");
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Unexpected error during document analysis. CorrelationId: {CorrelationId}",
                    correlationId);
                return StatusCode(StatusCodes.Status500InternalServerError, 
                    "An unexpected error occurred");
            }
        }

        /// <summary>
        /// Retrieves the status of an ongoing document analysis job with performance metrics
        /// </summary>
        /// <param name="analysisId">Unique identifier of the analysis job</param>
        /// <returns>Current analysis status with performance metrics</returns>
        [HttpGet("{analysisId}/status")]
        [ProducesResponseType(typeof(DocumentAnalysisResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
        public async Task<ActionResult<DocumentAnalysisResponse>> GetAnalysisStatusAsync(
            string analysisId)
        {
            var stopwatch = Stopwatch.StartNew();
            var correlationId = Guid.NewGuid().ToString();

            try
            {
                _logger.LogInformation(
                    "Retrieving analysis status. CorrelationId: {CorrelationId}, AnalysisId: {AnalysisId}",
                    correlationId, analysisId);

                if (string.IsNullOrEmpty(analysisId))
                {
                    _logger.LogWarning(
                        "Invalid analysisId provided. CorrelationId: {CorrelationId}",
                        correlationId);
                    return BadRequest("Analysis ID is required");
                }

                var status = await _documentAnalysisService.GetAnalysisStatusAsync(analysisId);

                if (status == null)
                {
                    _logger.LogWarning(
                        "Analysis job not found. CorrelationId: {CorrelationId}, AnalysisId: {AnalysisId}",
                        correlationId, analysisId);
                    return NotFound("Analysis job not found");
                }

                var response = new DocumentAnalysisResponse(status);

                stopwatch.Stop();
                _logger.LogInformation(
                    "Analysis status retrieved successfully. CorrelationId: {CorrelationId}, " +
                    "AnalysisId: {AnalysisId}, Status: {Status}, ProcessingTime: {ProcessingTime}ms",
                    correlationId, analysisId, response.Status, stopwatch.ElapsedMilliseconds);

                return Ok(response);
            }
            catch (KeyNotFoundException ex)
            {
                _logger.LogWarning(
                    ex,
                    "Analysis job not found. CorrelationId: {CorrelationId}, AnalysisId: {AnalysisId}",
                    correlationId, analysisId);
                return NotFound("Analysis job not found");
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Error retrieving analysis status. CorrelationId: {CorrelationId}, AnalysisId: {AnalysisId}",
                    correlationId, analysisId);
                return StatusCode(StatusCodes.Status500InternalServerError, 
                    "An error occurred while retrieving analysis status");
            }
        }
    }
}