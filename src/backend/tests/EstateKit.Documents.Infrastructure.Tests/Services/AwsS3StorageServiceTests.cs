using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using Amazon.S3;
using Amazon.S3.Model;
using EstateKit.Documents.Core.Interfaces;
using EstateKit.Documents.Infrastructure.Configuration;
using EstateKit.Documents.Infrastructure.Security;
using EstateKit.Documents.Infrastructure.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Caching.Memory;
using Moq;
using Xunit;
using FluentAssertions;

namespace EstateKit.Documents.Infrastructure.Tests.Services
{
    public class AwsS3StorageServiceTests : IDisposable
    {
        private readonly Mock<IAmazonS3> _mockS3Client;
        private readonly Mock<ILogger<AwsS3StorageService>> _mockLogger;
        private readonly Mock<IMemoryCache> _mockCache;
        private readonly Mock<DocumentEncryptionService> _mockEncryptionService;
        private readonly AwsConfiguration _awsConfig;
        private readonly AwsS3StorageService _storageService;
        private readonly CancellationToken _cancellationToken;

        public AwsS3StorageServiceTests()
        {
            // Initialize mocks
            _mockS3Client = new Mock<IAmazonS3>();
            _mockLogger = new Mock<ILogger<AwsS3StorageService>>();
            _mockCache = new Mock<IMemoryCache>();
            _mockEncryptionService = new Mock<DocumentEncryptionService>();
            _cancellationToken = CancellationToken.None;

            // Configure AWS settings
            _awsConfig = new AwsConfiguration(new Dictionary<string, string>
            {
                ["AWS:Region"] = "us-east-1",
                ["AWS:S3:BucketName"] = "estatekit-documents-test",
                ["AWS:S3:EnableServerSideEncryption"] = "true",
                ["AWS:KMS:KeyId"] = "12345678-1234-1234-1234-123456789012",
                ["AWS:Cognito:UserPoolId"] = "us-east-1_testpool",
                ["AWS:Cognito:AppClientId"] = "test-client-id",
                ["AWS:Textract:QueueUrl"] = "https://sqs.test.aws"
            });

            // Initialize service
            _storageService = new AwsS3StorageService(
                _mockS3Client.Object,
                _awsConfig,
                _mockEncryptionService.Object,
                _mockLogger.Object,
                _mockCache.Object);
        }

        [Fact]
        public async Task UploadDocumentAsync_ValidInput_UploadsSuccessfully()
        {
            // Arrange
            var documentContent = Encoding.UTF8.GetBytes("Test document content");
            var documentPath = "test/document.pdf";
            var contentType = "application/pdf";
            var encryptedContent = new byte[] { 1, 2, 3, 4, 5 };
            var encryptedDataKey = new byte[] { 6, 7, 8, 9, 10 };

            _mockEncryptionService
                .Setup(x => x.EncryptDocument(It.IsAny<byte[]>()))
                .ReturnsAsync((encryptedContent, encryptedDataKey));

            _mockS3Client
                .Setup(x => x.PutObjectAsync(It.IsAny<PutObjectRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new PutObjectResponse());

            // Act
            var result = await _storageService.UploadDocumentAsync(
                new MemoryStream(documentContent),
                documentPath,
                contentType);

            // Assert
            result.Should().Be(documentPath);

            _mockS3Client.Verify(x => x.PutObjectAsync(
                It.Is<PutObjectRequest>(r =>
                    r.BucketName == _awsConfig.S3BucketName &&
                    r.Key == documentPath &&
                    r.ContentType == contentType &&
                    r.ServerSideEncryptionMethod == ServerSideEncryptionMethod.AES256),
                It.IsAny<CancellationToken>()),
                Times.Once);

            _mockLogger.Verify(x => x.LogInformation(
                It.Is<string>(s => s.Contains("Document uploaded successfully")),
                It.Is<string>(s => s == documentPath),
                It.Is<int>(i => i == encryptedContent.Length)),
                Times.Once);
        }

        [Fact]
        public async Task UploadDocumentAsync_LargeFile_HandlesChunkedUpload()
        {
            // Arrange
            var largeContent = new byte[6 * 1024 * 1024]; // 6MB
            var documentPath = "test/large-document.pdf";
            var contentType = "application/pdf";
            var uploadId = "test-multipart-upload-id";

            _mockS3Client
                .Setup(x => x.InitiateMultipartUploadAsync(It.IsAny<InitiateMultipartUploadRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new InitiateMultipartUploadResponse { UploadId = uploadId });

            _mockS3Client
                .Setup(x => x.UploadPartAsync(It.IsAny<UploadPartRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new UploadPartResponse { ETag = "test-etag" });

            _mockS3Client
                .Setup(x => x.CompleteMultipartUploadAsync(It.IsAny<CompleteMultipartUploadRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new CompleteMultipartUploadResponse());

            // Act
            var result = await _storageService.UploadDocumentAsync(
                new MemoryStream(largeContent),
                documentPath,
                contentType);

            // Assert
            result.Should().Be(documentPath);

            _mockS3Client.Verify(x => x.InitiateMultipartUploadAsync(
                It.Is<InitiateMultipartUploadRequest>(r =>
                    r.BucketName == _awsConfig.S3BucketName &&
                    r.Key == documentPath &&
                    r.ServerSideEncryptionMethod == ServerSideEncryptionMethod.AES256),
                It.IsAny<CancellationToken>()),
                Times.Once);

            _mockS3Client.Verify(x => x.CompleteMultipartUploadAsync(
                It.Is<CompleteMultipartUploadRequest>(r =>
                    r.BucketName == _awsConfig.S3BucketName &&
                    r.Key == documentPath &&
                    r.UploadId == uploadId),
                It.IsAny<CancellationToken>()),
                Times.Once);
        }

        [Fact]
        public async Task DownloadDocumentAsync_ExistingDocument_DownloadsSuccessfully()
        {
            // Arrange
            var documentPath = "test/document.pdf";
            var encryptedContent = new byte[] { 1, 2, 3, 4, 5 };
            var decryptedContent = new byte[] { 6, 7, 8, 9, 10 };
            var encryptedDataKey = Convert.ToBase64String(new byte[] { 11, 12, 13, 14, 15 });
            var checksum = "test-checksum";

            var response = new GetObjectResponse
            {
                ResponseStream = new MemoryStream(encryptedContent),
                Metadata = new Dictionary<string, string>
                {
                    ["x-amz-key-id"] = encryptedDataKey,
                    ["x-amz-content-sha256"] = checksum
                }
            };

            _mockS3Client
                .Setup(x => x.GetObjectAsync(It.IsAny<GetObjectRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(response);

            _mockEncryptionService
                .Setup(x => x.DecryptDocument(It.IsAny<byte[]>(), It.IsAny<byte[]>()))
                .ReturnsAsync(decryptedContent);

            // Act
            var result = await _storageService.DownloadDocumentAsync(documentPath);

            // Assert
            var resultContent = new MemoryStream();
            await result.CopyToAsync(resultContent);
            resultContent.ToArray().Should().BeEquivalentTo(decryptedContent);

            _mockS3Client.Verify(x => x.GetObjectAsync(
                It.Is<GetObjectRequest>(r =>
                    r.BucketName == _awsConfig.S3BucketName &&
                    r.Key == documentPath),
                It.IsAny<CancellationToken>()),
                Times.Once);
        }

        [Fact]
        public async Task DeleteDocumentAsync_ExistingDocument_DeletesSuccessfully()
        {
            // Arrange
            var documentPath = "test/document.pdf";

            _mockS3Client
                .Setup(x => x.DeleteObjectAsync(It.IsAny<DeleteObjectRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new DeleteObjectResponse());

            // Act
            var result = await _storageService.DeleteDocumentAsync(documentPath);

            // Assert
            result.Should().BeTrue();

            _mockS3Client.Verify(x => x.DeleteObjectAsync(
                It.Is<DeleteObjectRequest>(r =>
                    r.BucketName == _awsConfig.S3BucketName &&
                    r.Key == documentPath),
                It.IsAny<CancellationToken>()),
                Times.Once);

            _mockCache.Verify(x => x.Remove(It.Is<string>(s => s.Contains(documentPath))), Times.Once);
        }

        [Fact]
        public async Task DocumentExistsAsync_ExistingDocument_ReturnsTrue()
        {
            // Arrange
            var documentPath = "test/document.pdf";

            _mockS3Client
                .Setup(x => x.GetObjectMetadataAsync(It.IsAny<GetObjectMetadataRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new GetObjectMetadataResponse());

            // Act
            var result = await _storageService.DocumentExistsAsync(documentPath);

            // Assert
            result.Should().BeTrue();

            _mockS3Client.Verify(x => x.GetObjectMetadataAsync(
                It.Is<GetObjectMetadataRequest>(r =>
                    r.BucketName == _awsConfig.S3BucketName &&
                    r.Key == documentPath),
                It.IsAny<CancellationToken>()),
                Times.Once);
        }

        [Fact]
        public async Task GetDocumentMetadataAsync_ExistingDocument_ReturnsMetadata()
        {
            // Arrange
            var documentPath = "test/document.pdf";
            var contentType = "application/pdf";
            var lastModified = DateTime.UtcNow;
            var contentLength = 1024L;

            var response = new GetObjectMetadataResponse
            {
                Headers = { ContentType = contentType },
                LastModified = lastModified,
                ContentLength = contentLength
            };

            _mockS3Client
                .Setup(x => x.GetObjectMetadataAsync(It.IsAny<GetObjectMetadataRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(response);

            // Act
            var result = await _storageService.GetDocumentMetadataAsync(documentPath);

            // Assert
            result.Should().ContainKey("ContentType").WhoseValue.Should().Be(contentType);
            result.Should().ContainKey("LastModified").WhoseValue.Should().Be(lastModified.ToString("O"));
            result.Should().ContainKey("Size").WhoseValue.Should().Be(contentLength.ToString());
        }

        public void Dispose()
        {
            _storageService?.Dispose();
        }
    }
}