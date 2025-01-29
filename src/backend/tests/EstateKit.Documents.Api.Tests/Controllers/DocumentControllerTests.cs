using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using EstateKit.Documents.Api.Controllers.V1;
using EstateKit.Documents.Api.DTOs;
using EstateKit.Documents.Core.Interfaces;
using EstateKit.Documents.Core.Entities;
using EstateKit.Documents.Core.Constants;
using System.Security.Claims;

namespace EstateKit.Documents.Api.Tests.Controllers
{
    [TestClass]
    public class DocumentControllerTests
    {
        private Mock<IDocumentService> _mockDocumentService;
        private Mock<ILogger<DocumentController>> _mockLogger;
        private DocumentController _controller;
        private const string TEST_USER_ID = "test-user-123";

        [TestInitialize]
        public void TestInitialize()
        {
            _mockDocumentService = new Mock<IDocumentService>();
            _mockLogger = new Mock<ILogger<DocumentController>>();
            _controller = new DocumentController(_mockDocumentService.Object, _mockLogger.Object);

            // Setup default authenticated user
            var claims = new[] { new Claim("sub", TEST_USER_ID) };
            var identity = new ClaimsIdentity(claims, "TestAuth");
            var claimsPrincipal = new ClaimsPrincipal(identity);
            _controller.ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = claimsPrincipal }
            };
        }

        [TestMethod]
        public async Task UploadDocument_ValidRequest_ReturnsSuccess()
        {
            // Arrange
            var mockFile = new Mock<IFormFile>();
            mockFile.Setup(f => f.Length).Returns(1024);
            mockFile.Setup(f => f.FileName).Returns("test.pdf");
            mockFile.Setup(f => f.ContentType).Returns("application/pdf");
            mockFile.Setup(f => f.OpenReadStream()).Returns(new MemoryStream());

            var request = new DocumentUploadRequest
            {
                UserId = TEST_USER_ID,
                DocumentType = DocumentTypes.PasswordFiles,
                DocumentName = "Test Document",
                File = mockFile.Object
            };

            var expectedDocument = new Document
            {
                Id = "doc-123",
                UserId = TEST_USER_ID,
                DocumentType = DocumentTypes.PasswordFiles,
                Metadata = new DocumentMetadata
                {
                    EncryptedName = "encrypted_name",
                    ContentType = "application/pdf"
                }
            };

            _mockDocumentService
                .Setup(s => s.UploadDocumentAsync(
                    It.IsAny<string>(),
                    It.IsAny<Stream>(),
                    It.IsAny<string>(),
                    It.IsAny<int>(),
                    It.IsAny<string>()))
                .ReturnsAsync(expectedDocument);

            // Act
            var result = await _controller.UploadDocumentAsync(request);

            // Assert
            Assert.IsInstanceOfType(result.Result, typeof(OkObjectResult));
            var okResult = result.Result as OkObjectResult;
            var response = okResult.Value as DocumentUploadResponse;
            Assert.AreEqual(expectedDocument.Id, response.DocumentId);
            Assert.IsTrue(response.Success);

            _mockDocumentService.Verify(
                s => s.UploadDocumentAsync(
                    TEST_USER_ID,
                    It.IsAny<Stream>(),
                    request.DocumentName,
                    request.DocumentType,
                    mockFile.Object.ContentType),
                Times.Once);
        }

        [TestMethod]
        public async Task UploadDocument_UnauthorizedUser_ReturnsUnauthorized()
        {
            // Arrange
            var request = new DocumentUploadRequest
            {
                UserId = "different-user",
                DocumentType = DocumentTypes.PasswordFiles,
                DocumentName = "Test Document",
                File = Mock.Of<IFormFile>()
            };

            // Act
            var result = await _controller.UploadDocumentAsync(request);

            // Assert
            Assert.IsInstanceOfType(result.Result, typeof(UnauthorizedObjectResult));
            _mockDocumentService.Verify(
                s => s.UploadDocumentAsync(
                    It.IsAny<string>(),
                    It.IsAny<Stream>(),
                    It.IsAny<string>(),
                    It.IsAny<int>(),
                    It.IsAny<string>()),
                Times.Never);
        }

        [TestMethod]
        public async Task UploadDocument_InvalidRequest_ReturnsBadRequest()
        {
            // Arrange
            var request = new DocumentUploadRequest
            {
                UserId = TEST_USER_ID,
                DocumentType = 999, // Invalid document type
                DocumentName = "Test Document",
                File = Mock.Of<IFormFile>()
            };

            // Act
            var result = await _controller.UploadDocumentAsync(request);

            // Assert
            Assert.IsInstanceOfType(result.Result, typeof(BadRequestObjectResult));
            _mockDocumentService.Verify(
                s => s.UploadDocumentAsync(
                    It.IsAny<string>(),
                    It.IsAny<Stream>(),
                    It.IsAny<string>(),
                    It.IsAny<int>(),
                    It.IsAny<string>()),
                Times.Never);
        }

        [TestMethod]
        public async Task GetDocument_ExistingDocument_ReturnsDocument()
        {
            // Arrange
            var documentId = "doc-123";
            var document = new Document
            {
                Id = documentId,
                UserId = TEST_USER_ID,
                Metadata = new DocumentMetadata
                {
                    S3Path = "test/path",
                    ContentType = "application/pdf",
                    EncryptedName = "encrypted_name.pdf"
                }
            };

            _mockDocumentService
                .Setup(s => s.GetDocumentAsync(documentId, TEST_USER_ID))
                .ReturnsAsync(document);

            // Act
            var result = await _controller.GetDocumentAsync(documentId);

            // Assert
            Assert.IsInstanceOfType(result, typeof(FileResult));
            var fileResult = result as FileResult;
            Assert.AreEqual(document.Metadata.ContentType, fileResult.ContentType);
            Assert.AreEqual(document.Metadata.EncryptedName, fileResult.FileDownloadName);

            _mockDocumentService.Verify(
                s => s.GetDocumentAsync(documentId, TEST_USER_ID),
                Times.Once);
        }

        [TestMethod]
        public async Task GetDocument_NonexistentDocument_ReturnsNotFound()
        {
            // Arrange
            var documentId = "non-existent";
            _mockDocumentService
                .Setup(s => s.GetDocumentAsync(documentId, TEST_USER_ID))
                .ThrowsAsync(new KeyNotFoundException());

            // Act
            var result = await _controller.GetDocumentAsync(documentId);

            // Assert
            Assert.IsInstanceOfType(result, typeof(NotFoundResult));
            _mockDocumentService.Verify(
                s => s.GetDocumentAsync(documentId, TEST_USER_ID),
                Times.Once);
        }

        [TestMethod]
        public async Task DeleteDocument_ExistingDocument_ReturnsSuccess()
        {
            // Arrange
            var documentId = "doc-123";
            _mockDocumentService
                .Setup(s => s.DeleteDocumentAsync(documentId, TEST_USER_ID))
                .ReturnsAsync(true);

            // Act
            var result = await _controller.DeleteDocumentAsync(documentId);

            // Assert
            Assert.IsInstanceOfType(result, typeof(OkObjectResult));
            var okResult = result as OkObjectResult;
            dynamic response = okResult.Value;
            Assert.IsTrue((bool)response.success);

            _mockDocumentService.Verify(
                s => s.DeleteDocumentAsync(documentId, TEST_USER_ID),
                Times.Once);
        }

        [TestMethod]
        public async Task DeleteDocument_NonexistentDocument_ReturnsNotFound()
        {
            // Arrange
            var documentId = "non-existent";
            _mockDocumentService
                .Setup(s => s.DeleteDocumentAsync(documentId, TEST_USER_ID))
                .ThrowsAsync(new KeyNotFoundException());

            // Act
            var result = await _controller.DeleteDocumentAsync(documentId);

            // Assert
            Assert.IsInstanceOfType(result, typeof(NotFoundResult));
            _mockDocumentService.Verify(
                s => s.DeleteDocumentAsync(documentId, TEST_USER_ID),
                Times.Once);
        }

        [TestMethod]
        public async Task DeleteDocument_UnauthorizedUser_ReturnsUnauthorized()
        {
            // Arrange
            var documentId = "doc-123";
            _mockDocumentService
                .Setup(s => s.DeleteDocumentAsync(documentId, TEST_USER_ID))
                .ThrowsAsync(new UnauthorizedAccessException());

            // Act
            var result = await _controller.DeleteDocumentAsync(documentId);

            // Assert
            Assert.IsInstanceOfType(result, typeof(UnauthorizedResult));
            _mockDocumentService.Verify(
                s => s.DeleteDocumentAsync(documentId, TEST_USER_ID),
                Times.Once);
        }
    }
}