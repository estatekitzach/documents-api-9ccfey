using System;
using System.ComponentModel.DataAnnotations;
using EstateKit.Documents.Core.Constants;

namespace EstateKit.Documents.Api.DTOs
{
    /// <summary>
    /// Data Transfer Object (DTO) representing a document analysis request with comprehensive validation.
    /// Supports OCR text extraction and table parsing capabilities through AWS Textract.
    /// </summary>
    public class DocumentAnalysisRequest
    {
        /// <summary>
        /// Unique identifier of the document to be analyzed
        /// </summary>
        [Required(ErrorMessage = "Document ID is required")]
        public string DocumentId { get; set; }

        /// <summary>
        /// Type of document being analyzed (1-4)
        /// 1: Password Files
        /// 2: Medical
        /// 3: Insurance
        /// 4: Personal Identifiers
        /// </summary>
        [Required(ErrorMessage = "Document type is required")]
        [Range(1, 4, ErrorMessage = "Invalid document type")]
        public int DocumentType { get; set; }

        /// <summary>
        /// Indicates whether to perform OCR text extraction on the document
        /// </summary>
        [Required(ErrorMessage = "Text extraction option must be specified")]
        public bool ExtractText { get; set; }

        /// <summary>
        /// Indicates whether to perform table structure extraction on the document
        /// </summary>
        [Required(ErrorMessage = "Table extraction option must be specified")]
        public bool ExtractTables { get; set; }

        /// <summary>
        /// File extension of the document (e.g., .pdf, .doc, .docx, etc.)
        /// </summary>
        [Required(ErrorMessage = "File extension is required")]
        public string FileExtension { get; set; }

        /// <summary>
        /// Initializes a new instance of the DocumentAnalysisRequest class with default analysis options enabled
        /// </summary>
        public DocumentAnalysisRequest()
        {
            // Enable both text and table extraction by default for comprehensive analysis
            ExtractText = true;
            ExtractTables = true;
        }

        /// <summary>
        /// Performs comprehensive validation of the request parameters
        /// </summary>
        /// <returns>True if all validation checks pass, false otherwise</returns>
        public bool Validate()
        {
            try
            {
                // Validate DocumentId
                if (string.IsNullOrWhiteSpace(DocumentId))
                {
                    return false;
                }

                // Validate DocumentType
                if (!DocumentTypes.IsValidDocumentType(DocumentType))
                {
                    return false;
                }

                // Validate FileExtension
                if (string.IsNullOrWhiteSpace(FileExtension))
                {
                    return false;
                }

                // Normalize and validate file extension
                var normalizedExtension = FileExtension.ToLowerInvariant();
                if (!normalizedExtension.StartsWith("."))
                {
                    normalizedExtension = "." + normalizedExtension;
                }

                var allowedExtensions = DocumentTypes.GetAllowedExtensions(DocumentType);
                if (!allowedExtensions.Contains(normalizedExtension))
                {
                    return false;
                }

                // Ensure at least one extraction option is enabled
                if (!ExtractText && !ExtractTables)
                {
                    return false;
                }

                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
}