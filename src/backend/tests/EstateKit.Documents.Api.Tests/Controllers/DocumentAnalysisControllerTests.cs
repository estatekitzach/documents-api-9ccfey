using System;
using System.Threading.Tasks;
using System.Text.Json;
using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using FluentAssertions;
using EstateKit.Documents.Api.Controllers.V1;
using EstateKit.Documents.Core.Interfaces;
using EstateKit.Documents.Core.Entities;
using EstateKit.Documents.Api.DTOs;

namespace EstateKit.Documents.Api.Tests.Controllers
{
    public class DocumentAnalysisControllerTests
    {
        private readonly Mock<IDocumentAnalysisService> _mockDocumentAnalysisService;
        private readonly Mock<ILogger<DocumentAnalysisController>> _mockLogger;
        private readonly DocumentAnalysisController _controller;
        private const double MinimumConfidenceThreshold = 0.98;
        private const int MaxProcessingTimeSeconds = 3;

        public DocumentAnalysisControllerTests()
        {
            _mockDocumentAnalysisService = new Mock<IDocumentAnalysisService>(MockBehavior.Strict);
            _mockLogger = new Mock<ILogger<DocumentAnalysisController>>(MockBehavior.Strict);
            
            // Setup basic logger mock to accept any parameters
            _mockLogger.Setup(x => x.Log(
                It.IsAny<LogLevel>(),
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception, string>>()
            ));

            _controller = new DocumentAnalysisController(_mockDocumentAnalysisService.Object, _mockLogger.Object);
        }

        [Fact]
        public async Task AnalyzeDocument_ValidRequest_ReturnsOkResult()
        {
            // Arrange
            var request = new DocumentAnalysisRequest
            {
                DocumentId = "test-doc-001",
                DocumentType = 1,
                ExtractText = true,
                ExtractTables = true,
                FileExtension = ".pdf"
            };

            var analysisResult = new DocumentAnalysis(request.DocumentId)
            {
                ConfidenceScore = 0.99,
                Status = "Completed",
                ExtractedData = JsonDocument.Parse("{}"),
                ProcessingDurationMs = 1500
            };

            _mockDocumentAnalysisService
                .Setup(x => x.AnalyzeDocumentAsync(
                    It.IsAny<string>(),
                    null,
                    It.IsAny<AnalysisOptions>()))
                .ReturnsAsync(analysisResult);

            // Act
            var stopwatch = Stopwatch.StartNew();
            var result = await _controller.AnalyzeDocumentAsync(request);
            stopwatch.Stop();

            // Assert
            var okResult = result.Result.Should().BeOfType<OkObjectResult>().Subject;
            var response = okResult.Value.Should().BeOfType<DocumentAnalysisResponse>().Subject;

            // Verify performance requirements
            stopwatch.ElapsedMilliseconds.Should().BeLessThan(MaxProcessingTimeSeconds * 1000);

            // Verify confidence score meets minimum threshold
            response.ConfidenceScore.Should().BeGreaterOrEqualTo(MinimumConfidenceThreshold);

            // Verify response content
            response.AnalysisId.Should().NotBeNullOrEmpty();
            response.Status.Should().Be("Completed");
            response.ProcessingDurationMs.Should().Be(1500);
            response.ExtractedData.Should().NotBeNull();

            // Verify service interaction
            _mockDocumentAnalysisService.Verify(
                x => x.AnalyzeDocumentAsync(
                    It.IsAny<string>(),
                    null,
                    It.IsAny<AnalysisOptions>()),
                Times.Once);
        }

        [Fact]
        public async Task AnalyzeDocument_LowConfidenceScore_ReturnsWarning()
        {
            // Arrange
            var request = new DocumentAnalysisRequest
            {
                DocumentId = "test-doc-002",
                DocumentType = 1,
                ExtractText = true,
                ExtractTables = true,
                FileExtension = ".pdf"
            };

            var analysisResult = new DocumentAnalysis(request.DocumentId)
            {
                ConfidenceScore = 0.95,
                Status = "Completed",
                ExtractedData = JsonDocument.Parse("{}"),
                ProcessingDurationMs = 2000
            };

            _mockDocumentAnalysisService
                .Setup(x => x.AnalyzeDocumentAsync(
                    It.IsAny<string>(),
                    null,
                    It.IsAny<AnalysisOptions>()))
                .ReturnsAsync(analysisResult);

            // Act
            var result = await _controller.AnalyzeDocumentAsync(request);

            // Assert
            var okResult = result.Result.Should().BeOfType<OkObjectResult>().Subject;
            var response = okResult.Value.Should().BeOfType<DocumentAnalysisResponse>().Subject;

            // Verify confidence score below threshold
            response.ConfidenceScore.Should().BeLessThan(MinimumConfidenceThreshold);
            response.Status.Should().Be("Completed");

            // Verify service interaction
            _mockDocumentAnalysisService.Verify(
                x => x.AnalyzeDocumentAsync(
                    It.IsAny<string>(),
                    null,
                    It.IsAny<AnalysisOptions>()),
                Times.Once);
        }

        [Fact]
        public async Task AnalyzeDocument_PerformanceThresholdExceeded_ReturnsError()
        {
            // Arrange
            var request = new DocumentAnalysisRequest
            {
                DocumentId = "test-doc-003",
                DocumentType = 1,
                ExtractText = true,
                ExtractTables = true,
                FileExtension = ".pdf"
            };

            var analysisResult = new DocumentAnalysis(request.DocumentId)
            {
                Status = "Processing",
                ProcessingDurationMs = 5000
            };

            _mockDocumentAnalysisService
                .Setup(x => x.AnalyzeDocumentAsync(
                    It.IsAny<string>(),
                    null,
                    It.IsAny<AnalysisOptions>()))
                .ReturnsAsync(analysisResult);

            // Act
            var result = await _controller.AnalyzeDocumentAsync(request);

            // Assert
            var statusResult = result.Result.Should().BeOfType<ObjectResult>().Subject;
            statusResult.StatusCode.Should().Be(503);

            // Verify service interaction
            _mockDocumentAnalysisService.Verify(
                x => x.AnalyzeDocumentAsync(
                    It.IsAny<string>(),
                    null,
                    It.IsAny<AnalysisOptions>()),
                Times.Once);
        }

        [Fact]
        public async Task GetAnalysisStatus_ProcessingTimeout_ReturnsError()
        {
            // Arrange
            var analysisId = "test-analysis-001";
            var status = new AnalysisStatus
            {
                Status = "Processing",
                ProcessingDurationMs = 4000,
                ErrorDetails = "Processing timeout exceeded"
            };

            _mockDocumentAnalysisService
                .Setup(x => x.GetAnalysisStatusAsync(analysisId))
                .ReturnsAsync(status);

            // Act
            var result = await _controller.GetAnalysisStatusAsync(analysisId);

            // Assert
            var statusResult = result.Result.Should().BeOfType<ObjectResult>().Subject;
            statusResult.StatusCode.Should().Be(504);

            // Verify service interaction
            _mockDocumentAnalysisService.Verify(
                x => x.GetAnalysisStatusAsync(analysisId),
                Times.Once);
        }
    }
}