using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;

namespace EstateKit.Documents.Infrastructure.Configuration
{
    /// <summary>
    /// Provides comprehensive AWS service configuration settings for the EstateKit Documents API.
    /// Implements secure defaults and validation for all AWS service integrations.
    /// </summary>
    public sealed class AwsConfiguration
    {
        // AWS Region Configuration
        public string Region { get; }

        // S3 Storage Configuration
        public string S3BucketName { get; }
        public string S3ReplicaBucketName { get; }
        public bool EnableServerSideEncryption { get; }
        public bool EnableCrossRegionReplication { get; }
        public int S3PresignedUrlExpirationMinutes { get; }
        public int MaxDocumentSizeBytes { get; }
        public string AllowedFileTypes { get; }

        // Textract Configuration
        public string TextractQueueUrl { get; }
        public int TextractTimeoutSeconds { get; }

        // Authentication Configuration
        public string CognitoUserPoolId { get; }
        public string CognitoAppClientId { get; }

        // Security Configuration
        public string KmsKeyId { get; }
        public string KmsReplicaKeyId { get; }

        // Monitoring Configuration
        public string CloudWatchLogGroup { get; }

        // Regular expressions for validation
        private static readonly Regex RegionRegex = new(@"^[a-z]{2}-[a-z]+-\d{1}$", RegexOptions.Compiled);
        private static readonly Regex BucketNameRegex = new(@"^[a-z0-9][a-z0-9.-]*[a-z0-9]$", RegexOptions.Compiled);
        private static readonly Regex CognitoPoolIdRegex = new(@"^[\w-]+_[A-Za-z0-9]+$", RegexOptions.Compiled);
        private static readonly Regex KmsKeyIdRegex = new(@"^[A-Fa-f0-9]{8}-[A-Fa-f0-9]{4}-[A-Fa-f0-9]{4}-[A-Fa-f0-9]{4}-[A-Fa-f0-9]{12}$", RegexOptions.Compiled);

        /// <summary>
        /// Initializes a new instance of AWS configuration with secure defaults
        /// </summary>
        /// <param name="configuration">IConfiguration instance containing AWS settings</param>
        /// <exception cref="ArgumentNullException">Thrown when configuration is null</exception>
        /// <exception cref="ArgumentException">Thrown when configuration validation fails</exception>
        public AwsConfiguration(IConfiguration configuration)
        {
            if (configuration == null)
                throw new ArgumentNullException(nameof(configuration));

            // Bind configuration values with secure defaults
            Region = configuration["AWS:Region"] ?? throw new ArgumentException("AWS Region must be specified");
            S3BucketName = configuration["AWS:S3:BucketName"] ?? throw new ArgumentException("S3 bucket name must be specified");
            S3ReplicaBucketName = configuration["AWS:S3:ReplicaBucketName"] ?? string.Empty;
            TextractQueueUrl = configuration["AWS:Textract:QueueUrl"] ?? throw new ArgumentException("Textract queue URL must be specified");
            CognitoUserPoolId = configuration["AWS:Cognito:UserPoolId"] ?? throw new ArgumentException("Cognito user pool ID must be specified");
            CognitoAppClientId = configuration["AWS:Cognito:AppClientId"] ?? throw new ArgumentException("Cognito app client ID must be specified");
            KmsKeyId = configuration["AWS:KMS:KeyId"] ?? throw new ArgumentException("KMS key ID must be specified");
            KmsReplicaKeyId = configuration["AWS:KMS:ReplicaKeyId"] ?? string.Empty;
            CloudWatchLogGroup = configuration["AWS:CloudWatch:LogGroup"] ?? "/estatekit/documents/api";

            // Parse boolean values with secure defaults
            EnableServerSideEncryption = configuration.GetValue<bool>("AWS:S3:EnableServerSideEncryption", true);
            EnableCrossRegionReplication = configuration.GetValue<bool>("AWS:S3:EnableCrossRegionReplication", false);

            // Parse numeric values with secure defaults
            TextractTimeoutSeconds = configuration.GetValue<int>("AWS:Textract:TimeoutSeconds", 300);
            S3PresignedUrlExpirationMinutes = configuration.GetValue<int>("AWS:S3:PresignedUrlExpirationMinutes", 15);
            MaxDocumentSizeBytes = configuration.GetValue<int>("AWS:S3:MaxDocumentSizeBytes", 104857600); // 100MB default
            
            // Parse allowed file types with secure defaults
            AllowedFileTypes = configuration["AWS:S3:AllowedFileTypes"] ?? ".pdf,.doc,.docx,.jpg,.png,.gif,.txt,.xlsx";

            // Validate configuration
            if (!Validate())
                throw new ArgumentException("AWS configuration validation failed");
        }

        /// <summary>
        /// Performs comprehensive validation of all AWS configuration values
        /// </summary>
        /// <returns>True if configuration is valid, false otherwise</returns>
        public bool Validate()
        {
            try
            {
                // Validate AWS region
                if (!RegionRegex.IsMatch(Region))
                    return false;

                // Validate S3 bucket names
                if (!BucketNameRegex.IsMatch(S3BucketName))
                    return false;

                if (EnableCrossRegionReplication && !BucketNameRegex.IsMatch(S3ReplicaBucketName))
                    return false;

                // Validate Cognito IDs
                if (!CognitoPoolIdRegex.IsMatch(CognitoUserPoolId))
                    return false;

                if (string.IsNullOrEmpty(CognitoAppClientId))
                    return false;

                // Validate KMS key IDs
                if (!KmsKeyIdRegex.IsMatch(KmsKeyId))
                    return false;

                if (EnableCrossRegionReplication && !KmsKeyIdRegex.IsMatch(KmsReplicaKeyId))
                    return false;

                // Validate Textract configuration
                if (!Uri.TryCreate(TextractQueueUrl, UriKind.Absolute, out _))
                    return false;

                if (TextractTimeoutSeconds < 60 || TextractTimeoutSeconds > 900)
                    return false;

                // Validate S3 configuration
                if (S3PresignedUrlExpirationMinutes < 1 || S3PresignedUrlExpirationMinutes > 60)
                    return false;

                if (MaxDocumentSizeBytes < 1048576 || MaxDocumentSizeBytes > 524288000) // 1MB to 500MB
                    return false;

                // Validate allowed file types
                var fileTypes = AllowedFileTypes.Split(',');
                if (!fileTypes.All(t => t.StartsWith(".") && t.Length > 1))
                    return false;

                // Validate CloudWatch log group
                if (string.IsNullOrEmpty(CloudWatchLogGroup) || !CloudWatchLogGroup.StartsWith("/"))
                    return false;

                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}