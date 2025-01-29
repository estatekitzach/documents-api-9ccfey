using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Amazon.Textract;
using Amazon.Textract.Model;
using EstateKit.Documents.Core.Entities;
using EstateKit.Documents.Core.Interfaces;
using EstateKit.Documents.Infrastructure.Configuration;
using EstateKit.Documents.Infrastructure.Services;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using Xunit.Abstractions;

namespace EstateKit.Documents.Infrastructure.Tests.Services
{
    public class TextractAnalysisServiceTests
    {
        private readonly Mock<IAmazonTextract> _textractClientMock;
        private readonly Mock<ILogger<TextractAnalysisService>> _loggerMock;
        private readonly Mock<IMetricsTracker> _metricsTrackerMock;
        private readonly AwsConfiguration _awsConfig;
        private readonly TextractAnalysisService _service;
        private readonly ITestOutputHelper _testOutput;

        public TextractAnalysisServiceTests(ITestOutputHelper testOutput)
        {
            _testOutput = testOutput;
            _textractClientMock = new Mock<IAmazonTextract>(MockBehavior.Strict);
            _loggerMock = new Mock<ILogger<TextractAnalysisService>>();
            _metricsTrackerMock = new Mock<IMetricsTracker>();

            _awsConfig = new AwsConfiguration(new Dictionary<string, string>
            {
                ["AWS:Textract:QueueUrl"] = "https://sqs.us-west-2.amazonaws.com/123456789012/textract-queue",
                ["AWS:Textract:TimeoutSeconds"] = "300",
                ["AWS:S3:BucketName"] = "estatekit-documents",
                ["AWS:Region"] = "us-west-2",
                ["AWS:KMS:KeyId"] = "12345678-1234-1234-1234-123456789012",
                ["AWS:Cognito:UserPoolId"] = "us-west-2_abcdefghi",
                ["AWS:Cognito:AppClientId"] = "abcdefghijklmnop"
            });

            _service = new TextractAnalysisService(
                _textractClientMock.Object,
                _loggerMock.Object,
                _awsConfig,
                _metricsTrackerMock.Object);
        }

        [Fact]
        public async Task AnalyzeDocument_ValidInput_ReturnsAnalysisResult()
        {
            // Arrange
            var documentId = "test-doc-001";
            var documentStream = new MemoryStream(new byte[] { 1, 2, 3, 4, 5 });
            var jobId = "job-001";
            var analysisId = Guid.NewGuid().ToString();

            var startResponse = new StartDocumentAnalysisResponse
            {
                JobId = jobId,
                HttpStatusCode = System.Net.HttpStatusCode.OK
            };

            var analysisResponse = new GetDocumentAnalysisResponse
            {
                JobStatus = JobStatus.SUCCEEDED,
                Blocks = new List<Block>
                {
                    new Block
                    {
                        BlockType = BlockType.LINE,
                        Text = "Sample Text",
                        Confidence = 99.5f,
                        Id = "block-001"
                    }
                },
                DocumentMetadata = new DocumentMetadata
                {
                    Pages = 1
                },
                HttpStatusCode = System.Net.HttpStatusCode.OK
            };

            _textractClientMock
                .Setup(x => x.StartDocumentAnalysisAsync(It.IsAny<StartDocumentAnalysisRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(startResponse);

            _textractClientMock
                .Setup(x => x.GetDocumentAnalysisAsync(It.IsAny<GetDocumentAnalysisRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(analysisResponse);

            _metricsTrackerMock
                .Setup(x => x.TrackOperation(It.IsAny<string>()))
                .Returns(new MetricsScope { ElapsedMilliseconds = 2500 });

            // Act
            var result = await _service.AnalyzeDocumentAsync(documentId, documentStream);

            // Assert
            Assert.NotNull(result);
            Assert.Equal("Completed", result.Status);
            Assert.True(result.ConfidenceScore >= 0.98); // Verifying 98% accuracy requirement
            Assert.True(result.ProcessingDurationMs < 3000); // Verifying 3-second performance requirement
            Assert.NotNull(result.ExtractedData);

            _textractClientMock.Verify(
                x => x.StartDocumentAnalysisAsync(
                    It.Is<StartDocumentAnalysisRequest>(r => 
                        r.DocumentLocation.S3Object.Bucket == _awsConfig.S3BucketName &&
                        r.DocumentLocation.S3Object.Name == documentId),
                    It.IsAny<CancellationToken>()),
                Times.Once);
        }

        [Fact]
        public async Task AnalyzeDocument_InvalidInput_ThrowsException()
        {
            // Arrange
            string documentId = null;
            var documentStream = new MemoryStream();

            // Act & Assert
            await Assert.ThrowsAsync<ArgumentNullException>(() =>
                _service.AnalyzeDocumentAsync(documentId, documentStream));

            _loggerMock.Verify(
                x => x.Log(
                    LogLevel.Error,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => true),
                    It.IsAny<Exception>(),
                    It.Is<Func<It.IsAnyType, Exception, string>>((v, t) => true)),
                Times.Once);
        }

        [Fact]
        public async Task AnalyzeDocument_ServiceTimeout_HandlesGracefully()
        {
            // Arrange
            var documentId = "test-doc-002";
            var documentStream = new MemoryStream(new byte[] { 1, 2, 3, 4, 5 });
            var jobId = "job-002";

            var startResponse = new StartDocumentAnalysisResponse
            {
                JobId = jobId,
                HttpStatusCode = System.Net.HttpStatusCode.OK
            };

            var analysisResponse = new GetDocumentAnalysisResponse
            {
                JobStatus = JobStatus.IN_PROGRESS,
                HttpStatusCode = System.Net.HttpStatusCode.OK
            };

            _textractClientMock
                .Setup(x => x.StartDocumentAnalysisAsync(It.IsAny<StartDocumentAnalysisRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(startResponse);

            _textractClientMock
                .Setup(x => x.GetDocumentAnalysisAsync(It.IsAny<GetDocumentAnalysisRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(analysisResponse);

            var options = new AnalysisOptions { ProcessingTimeout = 1000 }; // 1 second timeout

            // Act
            var result = await _service.AnalyzeDocumentAsync(documentId, documentStream, options);

            // Assert
            Assert.NotNull(result);
            Assert.Equal("Failed", result.Status);
            Assert.Contains("timeout", result.ErrorDetails.ToLower());
        }

        [Fact]
        public async Task GetAnalysisStatus_ValidId_ReturnsCorrectStatus()
        {
            // Arrange
            var analysisId = "analysis-001";
            var response = new GetDocumentAnalysisResponse
            {
                JobStatus = JobStatus.SUCCEEDED,
                ProgressPercent = 100,
                DocumentMetadata = new DocumentMetadata { Pages = 1 },
                HttpStatusCode = System.Net.HttpStatusCode.OK
            };

            _textractClientMock
                .Setup(x => x.GetDocumentAnalysisAsync(
                    It.Is<GetDocumentAnalysisRequest>(r => r.JobId == analysisId),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(response);

            // Act
            var result = await _service.GetAnalysisStatusAsync(analysisId);

            // Assert
            Assert.NotNull(result);
            Assert.Equal("SUCCEEDED", result.Status);
            Assert.Equal(100, result.ProgressPercentage);
            Assert.Equal(1, result.PagesProcessed);
        }

        [Fact]
        public async Task GetAnalysisStatus_InvalidId_ThrowsException()
        {
            // Arrange
            string analysisId = null;

            // Act & Assert
            await Assert.ThrowsAsync<ArgumentNullException>(() =>
                _service.GetAnalysisStatusAsync(analysisId));

            _loggerMock.Verify(
                x => x.Log(
                    LogLevel.Error,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => true),
                    It.IsAny<Exception>(),
                    It.Is<Func<It.IsAnyType, Exception, string>>((v, t) => true)),
                Times.Once);
        }
    }
}