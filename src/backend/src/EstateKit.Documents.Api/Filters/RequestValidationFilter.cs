using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Logging;
using EstateKit.Documents.Api.DTOs;
using EstateKit.Documents.Core.Constants;

namespace EstateKit.Documents.Api.Filters
{
    /// <summary>
    /// Thread-safe action filter that performs comprehensive validation of incoming API requests
    /// for document operations with detailed security logging.
    /// </summary>
    public class RequestValidationFilter : IActionFilter
    {
        private readonly ILogger<RequestValidationFilter> _logger;

        /// <summary>
        /// Initializes a new instance of the RequestValidationFilter with thread-safe logger initialization
        /// </summary>
        /// <param name="logger">Logger instance for security audit logging</param>
        /// <exception cref="ArgumentNullException">Thrown if logger is null</exception>
        public RequestValidationFilter(ILogger<RequestValidationFilter> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            
            _logger.LogInformation("RequestValidationFilter initialized for security validation");
        }

        /// <summary>
        /// Executes before the action method, performing comprehensive request validation with security logging
        /// </summary>
        /// <param name="context">The action executing context</param>
        public void OnActionExecuting(ActionExecutingContext context)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            try
            {
                // Handle DocumentUploadRequest validation
                if (context.ActionArguments.TryGetValue("request", out var uploadRequest) && 
                    uploadRequest is DocumentUploadRequest docUploadRequest)
                {
                    _logger.LogInformation(
                        "Validating document upload request. UserId: {UserId}, DocumentType: {DocumentType}", 
                        docUploadRequest.UserId,
                        docUploadRequest.DocumentType
                    );

                    // Validate document type
                    if (!DocumentTypes.IsValidDocumentType(docUploadRequest.DocumentType))
                    {
                        LogValidationFailure("Invalid document type", docUploadRequest.DocumentType);
                        context.Result = new BadRequestObjectResult("Invalid document type specified");
                        return;
                    }

                    // Comprehensive request validation
                    if (!docUploadRequest.Validate())
                    {
                        LogValidationFailure("Document upload request validation failed", docUploadRequest.DocumentType);
                        context.Result = new BadRequestObjectResult("Request validation failed");
                        return;
                    }

                    _logger.LogInformation(
                        "Document upload request validation successful. UserId: {UserId}, DocumentType: {DocumentType}",
                        docUploadRequest.UserId,
                        docUploadRequest.DocumentType
                    );
                }

                // Handle DocumentAnalysisRequest validation
                if (context.ActionArguments.TryGetValue("request", out var analysisRequest) && 
                    analysisRequest is DocumentAnalysisRequest docAnalysisRequest)
                {
                    _logger.LogInformation(
                        "Validating document analysis request. DocumentId: {DocumentId}, DocumentType: {DocumentType}",
                        docAnalysisRequest.DocumentId,
                        docAnalysisRequest.DocumentType
                    );

                    // Validate document type
                    if (!DocumentTypes.IsValidDocumentType(docAnalysisRequest.DocumentType))
                    {
                        LogValidationFailure("Invalid document type for analysis", docAnalysisRequest.DocumentType);
                        context.Result = new BadRequestObjectResult("Invalid document type specified");
                        return;
                    }

                    // Comprehensive analysis request validation
                    if (!docAnalysisRequest.Validate())
                    {
                        LogValidationFailure("Document analysis request validation failed", docAnalysisRequest.DocumentType);
                        context.Result = new BadRequestObjectResult("Request validation failed");
                        return;
                    }

                    _logger.LogInformation(
                        "Document analysis request validation successful. DocumentId: {DocumentId}, DocumentType: {DocumentType}",
                        docAnalysisRequest.DocumentId,
                        docAnalysisRequest.DocumentType
                    );
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Unexpected error during request validation"
                );
                context.Result = new BadRequestObjectResult("Request validation error");
            }
        }

        /// <summary>
        /// Executes after the action method, logging completion status
        /// </summary>
        /// <param name="context">The action executed context</param>
        public void OnActionExecuted(ActionExecutedContext context)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            if (context.Exception != null)
            {
                _logger.LogError(
                    context.Exception,
                    "Request processing failed with exception"
                );
            }
            else
            {
                _logger.LogInformation(
                    "Request processing completed successfully. StatusCode: {StatusCode}",
                    (context.Result as ObjectResult)?.StatusCode ?? 200
                );
            }
        }

        /// <summary>
        /// Logs validation failures with appropriate security context
        /// </summary>
        /// <param name="message">The validation failure message</param>
        /// <param name="documentType">The document type being validated</param>
        private void LogValidationFailure(string message, int documentType)
        {
            _logger.LogWarning(
                "Validation failed: {Message}. DocumentType: {DocumentType}",
                message,
                documentType
            );
        }
    }
}