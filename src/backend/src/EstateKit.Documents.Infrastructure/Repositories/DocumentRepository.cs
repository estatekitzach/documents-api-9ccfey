using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Linq;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using EstateKit.Documents.Core.Entities;
using EstateKit.Documents.Core.Interfaces;
using EstateKit.Documents.Infrastructure.Configuration;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;
using Polly.CircuitBreaker;
using Polly.Retry;

namespace EstateKit.Documents.Infrastructure.Repositories
{
    /// <summary>
    /// Implementation of IDocumentRepository that provides secure document operations with 
    /// caching, retry policies, and comprehensive error handling using AWS DynamoDB.
    /// </summary>
    public class DocumentRepository : IDocumentRepository
    {
        private readonly IAmazonDynamoDB _dynamoDbClient;
        private readonly ILogger<DocumentRepository> _logger;
        private readonly StorageConfiguration _storageConfig;
        private readonly IMemoryCache _cache;
        private readonly AsyncRetryPolicy _retryPolicy;
        private readonly AsyncCircuitBreakerPolicy _circuitBreaker;

        // Cache configuration
        private const int CACHE_DURATION_MINUTES = 15;
        private const string CACHE_KEY_PREFIX = "doc_";

        // DynamoDB table names
        private const string DOCUMENTS_TABLE = "EstateKit_Documents";
        private const string VERSIONS_TABLE = "EstateKit_DocumentVersions";
        private const string ANALYSIS_TABLE = "EstateKit_DocumentAnalysis";

        public DocumentRepository(
            IAmazonDynamoDB dynamoDbClient,
            ILogger<DocumentRepository> logger,
            StorageConfiguration storageConfig,
            IMemoryCache cache,
            IOptions<DocumentRepositoryOptions> options)
        {
            _dynamoDbClient = dynamoDbClient ?? throw new ArgumentNullException(nameof(dynamoDbClient));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _storageConfig = storageConfig ?? throw new ArgumentNullException(nameof(storageConfig));
            _cache = cache ?? throw new ArgumentNullException(nameof(cache));

            // Configure retry policy with exponential backoff
            _retryPolicy = Policy
                .Handle<ProvisionedThroughputExceededException>()
                .Or<InternalServerErrorException>()
                .WaitAndRetryAsync(3, retryAttempt => 
                    TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)),
                    onRetry: (exception, timeSpan, retryCount, context) =>
                    {
                        _logger.LogWarning(
                            "Retry {RetryCount} after {TimeSpan}ms due to {ExceptionType}: {Message}",
                            retryCount, timeSpan.TotalMilliseconds, 
                            exception.GetType().Name, exception.Message);
                    });

            // Configure circuit breaker
            _circuitBreaker = Policy
                .Handle<Exception>()
                .CircuitBreakerAsync(
                    exceptionsAllowedBeforeBreaking: 5,
                    durationOfBreak: TimeSpan.FromSeconds(30),
                    onBreak: (exception, duration) =>
                    {
                        _logger.LogError(
                            "Circuit breaker opened for {DurationSeconds}s due to {ExceptionType}: {Message}",
                            duration.TotalSeconds, exception.GetType().Name, exception.Message);
                    },
                    onReset: () =>
                    {
                        _logger.LogInformation("Circuit breaker reset");
                    });
        }

        /// <inheritdoc/>
        public async Task<Document?> GetDocumentByIdAsync(string documentId)
        {
            if (string.IsNullOrEmpty(documentId))
            {
                throw new ArgumentNullException(nameof(documentId));
            }

            try
            {
                // Check cache first
                string cacheKey = $"{CACHE_KEY_PREFIX}{documentId}";
                if (_cache.TryGetValue(cacheKey, out Document? cachedDocument))
                {
                    _logger.LogDebug("Cache hit for document {DocumentId}", documentId);
                    return cachedDocument;
                }

                // Create get item request
                var request = new GetItemRequest
                {
                    TableName = DOCUMENTS_TABLE,
                    Key = new Dictionary<string, AttributeValue>
                    {
                        { "Id", new AttributeValue { S = documentId } }
                    },
                    ConsistentRead = true
                };

                // Execute with retry policy and circuit breaker
                var response = await _circuitBreaker.ExecuteAsync(() => 
                    _retryPolicy.ExecuteAsync(async () => 
                        await _dynamoDbClient.GetItemAsync(request)));

                if (response.Item == null || !response.IsItemSet)
                {
                    return null;
                }

                // Map DynamoDB item to Document entity
                var document = MapToDocument(response.Item);

                // Cache the result
                var cacheOptions = new MemoryCacheEntryOptions()
                    .SetAbsoluteExpiration(TimeSpan.FromMinutes(CACHE_DURATION_MINUTES))
                    .SetSlidingExpiration(TimeSpan.FromMinutes(5));

                _cache.Set(cacheKey, document, cacheOptions);

                return document;
            }
            catch (Exception ex) when (ex is not ArgumentNullException)
            {
                _logger.LogError(ex, "Error retrieving document {DocumentId}", documentId);
                throw;
            }
        }

        /// <inheritdoc/>
        public async Task<IEnumerable<Document>> GetDocumentsByUserIdAsync(string userId)
        {
            if (string.IsNullOrEmpty(userId))
            {
                throw new ArgumentNullException(nameof(userId));
            }

            try
            {
                var request = new QueryRequest
                {
                    TableName = DOCUMENTS_TABLE,
                    IndexName = "UserIdIndex",
                    KeyConditionExpression = "UserId = :userId",
                    ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                    {
                        { ":userId", new AttributeValue { S = userId } },
                        { ":isDeleted", new AttributeValue { BOOL = false } }
                    },
                    FilterExpression = "IsDeleted = :isDeleted"
                };

                var documents = new List<Document>();
                QueryResponse response;

                do
                {
                    response = await _circuitBreaker.ExecuteAsync(() =>
                        _retryPolicy.ExecuteAsync(async () =>
                            await _dynamoDbClient.QueryAsync(request)));

                    documents.AddRange(response.Items.Select(MapToDocument));
                    request.ExclusiveStartKey = response.LastEvaluatedKey;

                } while (response.LastEvaluatedKey.Count > 0);

                return documents;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving documents for user {UserId}", userId);
                throw;
            }
        }

        /// <inheritdoc/>
        public async Task<Document> AddDocumentAsync(Document document)
        {
            if (document == null)
            {
                throw new ArgumentNullException(nameof(document));
            }

            try
            {
                var item = MapFromDocument(document);
                var request = new PutItemRequest
                {
                    TableName = DOCUMENTS_TABLE,
                    Item = item,
                    ConditionExpression = "attribute_not_exists(Id)"
                };

                await _circuitBreaker.ExecuteAsync(() =>
                    _retryPolicy.ExecuteAsync(async () =>
                        await _dynamoDbClient.PutItemAsync(request)));

                // Invalidate cache
                _cache.Remove($"{CACHE_KEY_PREFIX}{document.Id}");

                return document;
            }
            catch (ConditionalCheckFailedException)
            {
                throw new InvalidOperationException($"Document with ID {document.Id} already exists");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error adding document {DocumentId}", document.Id);
                throw;
            }
        }

        /// <inheritdoc/>
        public async Task<bool> UpdateDocumentAsync(Document document)
        {
            if (document == null)
            {
                throw new ArgumentNullException(nameof(document));
            }

            try
            {
                var request = new UpdateItemRequest
                {
                    TableName = DOCUMENTS_TABLE,
                    Key = new Dictionary<string, AttributeValue>
                    {
                        { "Id", new AttributeValue { S = document.Id } }
                    },
                    UpdateExpression = "SET UpdatedAt = :updatedAt, DocumentType = :documentType",
                    ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                    {
                        { ":updatedAt", new AttributeValue { S = DateTime.UtcNow.ToString("O") } },
                        { ":documentType", new AttributeValue { N = document.DocumentType.ToString() } }
                    },
                    ConditionExpression = "attribute_exists(Id) AND IsDeleted = :isDeleted",
                    ExpressionAttributeValues = {
                        { ":isDeleted", new AttributeValue { BOOL = false } }
                    }
                };

                await _circuitBreaker.ExecuteAsync(() =>
                    _retryPolicy.ExecuteAsync(async () =>
                        await _dynamoDbClient.UpdateItemAsync(request)));

                // Invalidate cache
                _cache.Remove($"{CACHE_KEY_PREFIX}{document.Id}");

                return true;
            }
            catch (ConditionalCheckFailedException)
            {
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating document {DocumentId}", document.Id);
                throw;
            }
        }

        /// <inheritdoc/>
        public async Task<bool> DeleteDocumentAsync(string documentId)
        {
            if (string.IsNullOrEmpty(documentId))
            {
                throw new ArgumentNullException(nameof(documentId));
            }

            try
            {
                var request = new UpdateItemRequest
                {
                    TableName = DOCUMENTS_TABLE,
                    Key = new Dictionary<string, AttributeValue>
                    {
                        { "Id", new AttributeValue { S = documentId } }
                    },
                    UpdateExpression = "SET IsDeleted = :isDeleted, DeletedAt = :deletedAt",
                    ExpressionAttributeValues = new Dictionary<string, AttributeValue>
                    {
                        { ":isDeleted", new AttributeValue { BOOL = true } },
                        { ":deletedAt", new AttributeValue { S = DateTime.UtcNow.ToString("O") } }
                    },
                    ConditionExpression = "attribute_exists(Id) AND IsDeleted = :currentIsDeleted",
                    ExpressionAttributeValues = {
                        { ":currentIsDeleted", new AttributeValue { BOOL = false } }
                    }
                };

                await _circuitBreaker.ExecuteAsync(() =>
                    _retryPolicy.ExecuteAsync(async () =>
                        await _dynamoDbClient.UpdateItemAsync(request)));

                // Invalidate cache
                _cache.Remove($"{CACHE_KEY_PREFIX}{documentId}");

                return true;
            }
            catch (ConditionalCheckFailedException)
            {
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting document {DocumentId}", documentId);
                throw;
            }
        }

        private Document MapToDocument(Dictionary<string, AttributeValue> item)
        {
            var document = new Document
            {
                Id = item["Id"].S,
                UserId = item["UserId"].S,
                DocumentType = int.Parse(item["DocumentType"].N),
                IsDeleted = item.ContainsKey("IsDeleted") && item["IsDeleted"].BOOL
            };

            if (item.ContainsKey("Metadata"))
            {
                document.Metadata = MapToDocumentMetadata(item["Metadata"].M);
            }

            return document;
        }

        private Dictionary<string, AttributeValue> MapFromDocument(Document document)
        {
            var item = new Dictionary<string, AttributeValue>
            {
                { "Id", new AttributeValue { S = document.Id } },
                { "UserId", new AttributeValue { S = document.UserId } },
                { "DocumentType", new AttributeValue { N = document.DocumentType.ToString() } },
                { "IsDeleted", new AttributeValue { BOOL = document.IsDeleted } },
                { "CreatedAt", new AttributeValue { S = document.CreatedAt.ToString("O") } },
                { "UpdatedAt", new AttributeValue { S = document.UpdatedAt.ToString("O") } }
            };

            if (document.DeletedAt.HasValue)
            {
                item.Add("DeletedAt", new AttributeValue { S = document.DeletedAt.Value.ToString("O") });
            }

            if (document.Metadata != null)
            {
                item.Add("Metadata", new AttributeValue { M = MapFromDocumentMetadata(document.Metadata) });
            }

            return item;
        }

        private DocumentMetadata MapToDocumentMetadata(Dictionary<string, AttributeValue> metadata)
        {
            return new DocumentMetadata
            {
                DocumentId = metadata["DocumentId"].S,
                EncryptedName = metadata["EncryptedName"].S,
                ContentType = metadata["ContentType"].S,
                FileSize = long.Parse(metadata["FileSize"].N),
                FileExtension = metadata["FileExtension"].S,
                S3Path = metadata["S3Path"].S,
                Checksum = metadata["Checksum"].S
            };
        }

        private Dictionary<string, AttributeValue> MapFromDocumentMetadata(DocumentMetadata metadata)
        {
            return new Dictionary<string, AttributeValue>
            {
                { "DocumentId", new AttributeValue { S = metadata.DocumentId } },
                { "EncryptedName", new AttributeValue { S = metadata.EncryptedName } },
                { "ContentType", new AttributeValue { S = metadata.ContentType } },
                { "FileSize", new AttributeValue { N = metadata.FileSize.ToString() } },
                { "FileExtension", new AttributeValue { S = metadata.FileExtension } },
                { "S3Path", new AttributeValue { S = metadata.S3Path } },
                { "Checksum", new AttributeValue { S = metadata.Checksum } }
            };
        }
    }
}