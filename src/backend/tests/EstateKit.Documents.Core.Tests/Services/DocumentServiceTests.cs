using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using EstateKit.Documents.Core.Interfaces;
using EstateKit.Documents.Core.Entities;
using EstateKit.Documents.Core.Constants;
using EstateKit.Documents.Infrastructure.Services;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using FluentAssertions;
using System.Text.Json;

namespace EstateKit.Documents.Core.Tests.Services
{
    public class DocumentServiceTests
    {
        private readonly Mock<IDocumentRepository> _mockDocumentRepository;
        private readonly Mock<IStorageService> _mockStorageService;
        private readonly Mock<IDocumentAnalysisService> _mockAnalysisService;
        private readonly Mock<ILogger<DocumentService>> _mockLogger;
        private readonly IDocumentService _documentService;

        public DocumentServiceTests()
        {
            _mockDocumentRepository = new Mock<IDocumentRepository>();
            _mockStorageService = new Mock<IStorageService>();
            _mockAnalysisService = new Mock<IDocumentAnalysisService>();
            _mockLogger = new Mock<ILogger<DocumentService>>();

            _documentService = new DocumentService(
                _mockDocumentRepository.Object,
                _mockStorageService.Object,
                _mockAnalysisService.Object,
                _mockLogger.Object);
        }

        [Fact]
        public async Task UploadDocumentAsync_ValidInput_SuccessfullyUploadsDocument()
        {
            // Arrange
            var userId = "test-user-123";
            var fileName = "test-document.pdf";
            var documentType = DocumentTypes.PasswordFiles;
            var contentType = "application/pdf";
            var documentContent = "Test document content";
            var documentStream = new MemoryStream(Encoding.UTF8.GetBytes(documentContent));
            var s3Path = "/passwords/encrypted-user/encrypted-filename.pdf";

            _mockStorageService
                .Setup(x => x.UploadDocumentAsync(
                    It.IsAny<Stream>(),
                    It.IsAny<string>(),
                    It.Is<string>(ct => ct == contentType)))
                .ReturnsAsync(s3Path);

            _mockDocumentRepository
                .Setup(x => x.AddDocumentAsync(It.IsAny<Document>()))
                .ReturnsAsync((Document doc) => doc);

            _mockDocumentRepository
                .Setup(x => x.AddDocumentVersionAsync(It.IsAny<string>(), It.IsAny<DocumentVersion>()))
                .ReturnsAsync((string id, DocumentVersion version) => version);

            // Act
            var result = await _documentService.UploadDocumentAsync(
                userId,
                documentStream,
                fileName,
                documentType,
                contentType);

            // Assert
            result.Should().NotBeNull();
            result.UserId.Should().Be(userId);
            result.DocumentType.Should().Be(documentType);
            result.IsDeleted.Should().BeFalse();
            result.Metadata.Should().NotBeNull();
            result.Metadata.ContentType.Should().Be(contentType);
            result.Metadata.S3Path.Should().Be(s3Path);

            _mockStorageService.Verify(
                x => x.UploadDocumentAsync(
                    It.IsAny<Stream>(),
                    It.IsAny<string>(),
                    It.Is<string>(ct => ct == contentType)),
                Times.Once);

            _mockDocumentRepository.Verify(
                x => x.AddDocumentAsync(It.IsAny<Document>()),
                Times.Once);

            _mockDocumentRepository.Verify(
                x => x.AddDocumentVersionAsync(It.IsAny<string>(), It.IsAny<DocumentVersion>()),
                Times.Once);
        }

        [Fact]
        public async Task GetDocumentAsync_ExistingDocument_ReturnsDocument()
        {
            // Arrange
            var documentId = "test-doc-123";
            var userId = "test-user-123";
            var document = new Document
            {
                Id = documentId,
                UserId = userId,
                DocumentType = DocumentTypes.PasswordFiles,
                Metadata = new DocumentMetadata()
            };

            _mockDocumentRepository
                .Setup(x => x.GetDocumentByIdAsync(documentId))
                .ReturnsAsync(document);

            // Act
            var result = await _documentService.GetDocumentAsync(documentId, userId);

            // Assert
            result.Should().NotBeNull();
            result.Id.Should().Be(documentId);
            result.UserId.Should().Be(userId);

            _mockDocumentRepository.Verify(
                x => x.GetDocumentByIdAsync(documentId),
                Times.Once);
        }

        [Fact]
        public async Task GetDocumentAsync_UnauthorizedUser_ThrowsUnauthorizedAccessException()
        {
            // Arrange
            var documentId = "test-doc-123";
            var userId = "test-user-123";
            var differentUserId = "different-user";
            var document = new Document
            {
                Id = documentId,
                UserId = userId,
                DocumentType = DocumentTypes.PasswordFiles
            };

            _mockDocumentRepository
                .Setup(x => x.GetDocumentByIdAsync(documentId))
                .ReturnsAsync(document);

            // Act & Assert
            await Assert.ThrowsAsync<UnauthorizedAccessException>(
                () => _documentService.GetDocumentAsync(documentId, differentUserId));
        }

        [Fact]
        public async Task DeleteDocumentAsync_ExistingDocument_SuccessfullyDeletes()
        {
            // Arrange
            var documentId = "test-doc-123";
            var userId = "test-user-123";
            var s3Path = "/passwords/encrypted-user/encrypted-filename.pdf";
            var document = new Document
            {
                Id = documentId,
                UserId = userId,
                DocumentType = DocumentTypes.PasswordFiles,
                Metadata = new DocumentMetadata()
            };
            document.Metadata.UpdateMetadata(
                "encrypted-name",
                "application/pdf",
                1000,
                ".pdf",
                s3Path,
                "checksum123");

            _mockDocumentRepository
                .Setup(x => x.GetDocumentByIdAsync(documentId))
                .ReturnsAsync(document);

            _mockStorageService
                .Setup(x => x.DeleteDocumentAsync(s3Path))
                .ReturnsAsync(true);

            _mockDocumentRepository
                .Setup(x => x.DeleteDocumentAsync(documentId))
                .ReturnsAsync(true);

            // Act
            var result = await _documentService.DeleteDocumentAsync(documentId, userId);

            // Assert
            result.Should().BeTrue();

            _mockStorageService.Verify(
                x => x.DeleteDocumentAsync(s3Path),
                Times.Once);

            _mockDocumentRepository.Verify(
                x => x.DeleteDocumentAsync(documentId),
                Times.Once);
        }

        [Fact]
        public async Task AnalyzeDocumentAsync_ValidDocument_InitiatesAnalysis()
        {
            // Arrange
            var documentId = "test-doc-123";
            var userId = "test-user-123";
            var s3Path = "/passwords/encrypted-user/encrypted-filename.pdf";
            var document = new Document
            {
                Id = documentId,
                UserId = userId,
                DocumentType = DocumentTypes.PasswordFiles,
                Metadata = new DocumentMetadata()
            };
            document.Metadata.UpdateMetadata(
                "encrypted-name",
                "application/pdf",
                1000,
                ".pdf",
                s3Path,
                "checksum123");

            var documentStream = new MemoryStream(Encoding.UTF8.GetBytes("Test content"));
            var analysisResult = new DocumentAnalysis(documentId);

            _mockDocumentRepository
                .Setup(x => x.GetDocumentByIdAsync(documentId))
                .ReturnsAsync(document);

            _mockStorageService
                .Setup(x => x.DownloadDocumentAsync(s3Path))
                .ReturnsAsync(documentStream);

            _mockAnalysisService
                .Setup(x => x.AnalyzeDocumentAsync(
                    documentId,
                    It.IsAny<Stream>(),
                    It.IsAny<AnalysisOptions>()))
                .ReturnsAsync(analysisResult);

            _mockDocumentRepository
                .Setup(x => x.AddAnalysisResultAsync(documentId, analysisResult))
                .ReturnsAsync(analysisResult);

            // Act
            var result = await _documentService.AnalyzeDocumentAsync(documentId, userId);

            // Assert
            result.Should().NotBeNull();
            result.DocumentId.Should().Be(documentId);
            result.Status.Should().Be("Pending");

            _mockStorageService.Verify(
                x => x.DownloadDocumentAsync(s3Path),
                Times.Once);

            _mockAnalysisService.Verify(
                x => x.AnalyzeDocumentAsync(
                    documentId,
                    It.IsAny<Stream>(),
                    It.IsAny<AnalysisOptions>()),
                Times.Once);

            _mockDocumentRepository.Verify(
                x => x.AddAnalysisResultAsync(documentId, analysisResult),
                Times.Once);
        }

        [Fact]
        public async Task GetAnalysisStatusAsync_ValidAnalysis_ReturnsStatus()
        {
            // Arrange
            var analysisId = "test-analysis-123";
            var userId = "test-user-123";
            var status = new AnalysisStatus
            {
                Status = "Processing",
                ProgressPercentage = 50,
                ProcessingDurationMs = 1500,
                CurrentConfidence = 0.98,
                PagesProcessed = 2,
                RetryCount = 0
            };

            _mockAnalysisService
                .Setup(x => x.GetAnalysisStatusAsync(analysisId))
                .ReturnsAsync(status);

            // Act
            var result = await _documentService.GetAnalysisStatusAsync(analysisId, userId);

            // Assert
            result.Should().NotBeNull();
            result.AnalysisId.Should().Be(analysisId);
            result.Status.Should().Be("Processing");
            result.ProcessingDurationMs.Should().Be(1500);
            result.ConfidenceScore.Should().Be(0.98);

            _mockAnalysisService.Verify(
                x => x.GetAnalysisStatusAsync(analysisId),
                Times.Once);
        }

        [Theory]
        [InlineData(null, "test-user", "userId")]
        [InlineData("test-doc", null, "userId")]
        public async Task GetDocumentAsync_NullInput_ThrowsArgumentNullException(
            string documentId,
            string userId,
            string expectedParam)
        {
            // Act & Assert
            var exception = await Assert.ThrowsAsync<ArgumentNullException>(
                () => _documentService.GetDocumentAsync(documentId, userId));

            exception.ParamName.Should().Be(expectedParam);
        }

        [Fact]
        public async Task UploadDocumentAsync_InvalidDocumentType_ThrowsArgumentException()
        {
            // Arrange
            var userId = "test-user-123";
            var fileName = "test.pdf";
            var invalidDocumentType = 999;
            var contentType = "application/pdf";
            var documentStream = new MemoryStream();

            // Act & Assert
            await Assert.ThrowsAsync<ArgumentException>(() =>
                _documentService.UploadDocumentAsync(
                    userId,
                    documentStream,
                    fileName,
                    invalidDocumentType,
                    contentType));
        }

        [Fact]
        public async Task UploadDocumentAsync_InvalidFileExtension_ThrowsArgumentException()
        {
            // Arrange
            var userId = "test-user-123";
            var fileName = "test.invalid";
            var documentType = DocumentTypes.PasswordFiles;
            var contentType = "application/octet-stream";
            var documentStream = new MemoryStream();

            // Act & Assert
            await Assert.ThrowsAsync<ArgumentException>(() =>
                _documentService.UploadDocumentAsync(
                    userId,
                    documentStream,
                    fileName,
                    documentType,
                    contentType));
        }
    }
}