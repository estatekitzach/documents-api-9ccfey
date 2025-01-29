using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Diagnostics;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using EstateKit.Documents.Core.Entities;
using EstateKit.Documents.Infrastructure.Configuration;
using EstateKit.Documents.Infrastructure.Repositories;
using FluentAssertions; // v6.12.0
using Microsoft.Extensions.Logging;
using Moq; // v4.20.0
using Xunit; // v2.7.0
using Xunit.Abstractions;

namespace EstateKit.Documents.Infrastructure.Tests.Repositories
{
    public class DocumentRepositoryTests
    {
        private readonly Mock<IAmazonDynamoDB> _dynamoDbMock;
        private readonly Mock<ILogger<DocumentRepository>> _loggerMock;
        private readonly Mock<StorageConfiguration> _storageConfigMock;
        private readonly DocumentRepository _repository;
        private readonly ITestOutputHelper _output;

        // Constants for test validation
        private const int PERFORMANCE_THRESHOLD_MS = 3000; // 3 seconds per technical spec
        private const string TEST_TABLE_NAME = "EstateKit_Documents";

        public DocumentRepositoryTests(ITestOutputHelper output)
        {
            _output = output;
            _dynamoDbMock = new Mock<IAmazonDynamoDB>();
            _loggerMock = new Mock<ILogger<DocumentRepository>>();
            _storageConfigMock = new Mock<StorageConfiguration>();
            
            // Configure storage mock with default settings
            _storageConfigMock.Setup(x => x.IsInitialized).Returns(true);
            _storageConfigMock.Setup(x => x.MaxDocumentSizeBytes).Returns(104857600); // 100MB

            _repository = new DocumentRepository(
                _dynamoDbMock.Object,
                _loggerMock.Object,
                _storageConfigMock.Object);
        }

        [Fact]
        public async Task GetDocumentByIdAsync_ValidId_ReturnsDocument()
        {
            // Arrange
            var documentId = Guid.NewGuid().ToString();
            var userId = Guid.NewGuid().ToString();
            var expectedDocument = new Dictionary<string, AttributeValue>
            {
                { "Id", new AttributeValue { S = documentId } },
                { "UserId", new AttributeValue { S = userId } },
                { "DocumentType", new AttributeValue { N = "1" } },
                { "IsDeleted", new AttributeValue { BOOL = false } },
                { "CreatedAt", new AttributeValue { S = DateTime.UtcNow.ToString("O") } }
            };

            _dynamoDbMock.Setup(x => x.GetItemAsync(
                It.IsAny<GetItemRequest>(),
                default
            )).ReturnsAsync(new GetItemResponse { Item = expectedDocument });

            // Act
            var stopwatch = Stopwatch.StartNew();
            var result = await _repository.GetDocumentByIdAsync(documentId);
            stopwatch.Stop();

            // Assert
            // Performance validation
            stopwatch.ElapsedMilliseconds.Should().BeLessThan(PERFORMANCE_THRESHOLD_MS,
                "Document retrieval should complete within 3 seconds per technical requirements");

            // Result validation
            result.Should().NotBeNull();
            result.Id.Should().Be(documentId);
            result.UserId.Should().Be(userId);
            result.DocumentType.Should().Be(1);
            result.IsDeleted.Should().BeFalse();

            // Verify DynamoDB interaction
            _dynamoDbMock.Verify(x => x.GetItemAsync(
                It.Is<GetItemRequest>(req => 
                    req.TableName == TEST_TABLE_NAME &&
                    req.Key["Id"].S == documentId &&
                    req.ConsistentRead == true),
                default
            ), Times.Once);
        }

        [Fact]
        public async Task GetDocumentsByUserIdAsync_ValidUserId_ReturnsDocuments()
        {
            // Arrange
            var userId = Guid.NewGuid().ToString();
            var documents = new List<Dictionary<string, AttributeValue>>
            {
                new Dictionary<string, AttributeValue>
                {
                    { "Id", new AttributeValue { S = Guid.NewGuid().ToString() } },
                    { "UserId", new AttributeValue { S = userId } },
                    { "DocumentType", new AttributeValue { N = "1" } },
                    { "IsDeleted", new AttributeValue { BOOL = false } }
                },
                new Dictionary<string, AttributeValue>
                {
                    { "Id", new AttributeValue { S = Guid.NewGuid().ToString() } },
                    { "UserId", new AttributeValue { S = userId } },
                    { "DocumentType", new AttributeValue { N = "2" } },
                    { "IsDeleted", new AttributeValue { BOOL = false } }
                }
            };

            _dynamoDbMock.Setup(x => x.QueryAsync(
                It.IsAny<QueryRequest>(),
                default
            )).ReturnsAsync(new QueryResponse { 
                Items = documents,
                LastEvaluatedKey = new Dictionary<string, AttributeValue>()
            });

            // Act
            var stopwatch = Stopwatch.StartNew();
            var results = await _repository.GetDocumentsByUserIdAsync(userId);
            stopwatch.Stop();

            // Assert
            // Performance validation
            stopwatch.ElapsedMilliseconds.Should().BeLessThan(PERFORMANCE_THRESHOLD_MS,
                "Document list retrieval should complete within 3 seconds");

            // Result validation
            results.Should().NotBeNull();
            results.Should().HaveCount(2);
            results.Should().AllSatisfy(doc =>
            {
                doc.UserId.Should().Be(userId);
                doc.IsDeleted.Should().BeFalse();
            });

            // Verify DynamoDB query
            _dynamoDbMock.Verify(x => x.QueryAsync(
                It.Is<QueryRequest>(req =>
                    req.TableName == TEST_TABLE_NAME &&
                    req.IndexName == "UserIdIndex" &&
                    req.KeyConditionExpression == "UserId = :userId" &&
                    req.FilterExpression == "IsDeleted = :isDeleted"),
                default
            ), Times.Once);
        }

        [Fact]
        public async Task AddDocumentAsync_ValidDocument_Success()
        {
            // Arrange
            var document = new Document
            {
                UserId = Guid.NewGuid().ToString(),
                DocumentType = 1,
                Metadata = new DocumentMetadata
                {
                    EncryptedName = "encrypted_test_doc.pdf",
                    ContentType = "application/pdf",
                    FileSize = 1024,
                    FileExtension = ".pdf",
                    S3Path = "/passwords/encrypted_user_id/encrypted_filename.pdf",
                    Checksum = new string('a', 64)
                }
            };

            _dynamoDbMock.Setup(x => x.PutItemAsync(
                It.IsAny<PutItemRequest>(),
                default
            )).ReturnsAsync(new PutItemResponse());

            // Act
            var stopwatch = Stopwatch.StartNew();
            var result = await _repository.AddDocumentAsync(document);
            stopwatch.Stop();

            // Assert
            // Performance validation
            stopwatch.ElapsedMilliseconds.Should().BeLessThan(PERFORMANCE_THRESHOLD_MS,
                "Document addition should complete within 3 seconds");

            // Result validation
            result.Should().NotBeNull();
            result.Id.Should().NotBeEmpty();
            result.UserId.Should().Be(document.UserId);
            result.DocumentType.Should().Be(1);
            result.IsDeleted.Should().BeFalse();

            // Verify DynamoDB interaction
            _dynamoDbMock.Verify(x => x.PutItemAsync(
                It.Is<PutItemRequest>(req =>
                    req.TableName == TEST_TABLE_NAME &&
                    req.ConditionExpression == "attribute_not_exists(Id)"),
                default
            ), Times.Once);
        }

        [Fact]
        public async Task UpdateDocumentAsync_ValidDocument_Success()
        {
            // Arrange
            var document = new Document
            {
                UserId = Guid.NewGuid().ToString(),
                DocumentType = 2,
                Metadata = new DocumentMetadata
                {
                    EncryptedName = "encrypted_updated_doc.pdf",
                    ContentType = "application/pdf",
                    FileSize = 2048,
                    FileExtension = ".pdf",
                    S3Path = "/medical/encrypted_user_id/encrypted_filename.pdf",
                    Checksum = new string('b', 64)
                }
            };

            _dynamoDbMock.Setup(x => x.UpdateItemAsync(
                It.IsAny<UpdateItemRequest>(),
                default
            )).ReturnsAsync(new UpdateItemResponse());

            // Act
            var stopwatch = Stopwatch.StartNew();
            var result = await _repository.UpdateDocumentAsync(document);
            stopwatch.Stop();

            // Assert
            // Performance validation
            stopwatch.ElapsedMilliseconds.Should().BeLessThan(PERFORMANCE_THRESHOLD_MS,
                "Document update should complete within 3 seconds");

            // Result validation
            result.Should().BeTrue();

            // Verify DynamoDB interaction
            _dynamoDbMock.Verify(x => x.UpdateItemAsync(
                It.Is<UpdateItemRequest>(req =>
                    req.TableName == TEST_TABLE_NAME &&
                    req.Key["Id"].S == document.Id &&
                    req.ConditionExpression.Contains("attribute_exists(Id)") &&
                    req.ConditionExpression.Contains("IsDeleted = :isDeleted")),
                default
            ), Times.Once);
        }

        [Fact]
        public async Task DeleteDocumentAsync_ValidId_Success()
        {
            // Arrange
            var documentId = Guid.NewGuid().ToString();

            _dynamoDbMock.Setup(x => x.UpdateItemAsync(
                It.IsAny<UpdateItemRequest>(),
                default
            )).ReturnsAsync(new UpdateItemResponse());

            // Act
            var stopwatch = Stopwatch.StartNew();
            var result = await _repository.DeleteDocumentAsync(documentId);
            stopwatch.Stop();

            // Assert
            // Performance validation
            stopwatch.ElapsedMilliseconds.Should().BeLessThan(PERFORMANCE_THRESHOLD_MS,
                "Document deletion should complete within 3 seconds");

            // Result validation
            result.Should().BeTrue();

            // Verify DynamoDB interaction
            _dynamoDbMock.Verify(x => x.UpdateItemAsync(
                It.Is<UpdateItemRequest>(req =>
                    req.TableName == TEST_TABLE_NAME &&
                    req.Key["Id"].S == documentId &&
                    req.UpdateExpression.Contains("IsDeleted = :isDeleted") &&
                    req.UpdateExpression.Contains("DeletedAt = :deletedAt")),
                default
            ), Times.Once);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData(" ")]
        public async Task GetDocumentByIdAsync_InvalidId_ThrowsArgumentNullException(string documentId)
        {
            // Act & Assert
            await Assert.ThrowsAsync<ArgumentNullException>(() => 
                _repository.GetDocumentByIdAsync(documentId));
        }

        [Fact]
        public async Task AddDocumentAsync_NullDocument_ThrowsArgumentNullException()
        {
            // Act & Assert
            await Assert.ThrowsAsync<ArgumentNullException>(() => 
                _repository.AddDocumentAsync(null));
        }

        [Fact]
        public async Task UpdateDocumentAsync_DocumentNotFound_ReturnsFalse()
        {
            // Arrange
            var document = new Document();
            _dynamoDbMock.Setup(x => x.UpdateItemAsync(
                It.IsAny<UpdateItemRequest>(),
                default
            )).ThrowsAsync(new ConditionalCheckFailedException("Document not found"));

            // Act
            var result = await _repository.UpdateDocumentAsync(document);

            // Assert
            result.Should().BeFalse();
        }

        [Fact]
        public async Task DeleteDocumentAsync_AlreadyDeleted_ReturnsFalse()
        {
            // Arrange
            var documentId = Guid.NewGuid().ToString();
            _dynamoDbMock.Setup(x => x.UpdateItemAsync(
                It.IsAny<UpdateItemRequest>(),
                default
            )).ThrowsAsync(new ConditionalCheckFailedException("Document already deleted"));

            // Act
            var result = await _repository.DeleteDocumentAsync(documentId);

            // Assert
            result.Should().BeFalse();
        }
    }
}