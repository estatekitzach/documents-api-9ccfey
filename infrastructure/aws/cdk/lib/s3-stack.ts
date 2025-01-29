// aws-cdk-lib version: 2.x
import { Stack, CfnOutput, Duration, RemovalPolicy, Tags } from 'aws-cdk-lib';
// constructs version: 10.x
import { Construct } from 'constructs';
// aws-cdk-lib/aws-s3 version: 2.x
import * as s3 from 'aws-cdk-lib/aws-s3';
import { BucketEncryption, BlockPublicAccess, LifecycleRule, StorageClass, Transition } from 'aws-cdk-lib/aws-s3';
import { SecurityStack } from './security-stack';
import { NetworkingStack } from './networking-stack';

interface S3StackProps {
  environment: string;
  securityStack: SecurityStack;
  networkingStack: NetworkingStack;
}

export class S3Stack extends Stack {
  public readonly documentBucket: s3.Bucket;
  public readonly archiveBucket: s3.Bucket;

  constructor(scope: Construct, id: string, props: S3StackProps) {
    super(scope, id);

    // Create primary document storage bucket
    this.documentBucket = this.createDocumentBucket(props);

    // Create archive storage bucket
    this.archiveBucket = this.createArchiveBucket(props);

    // Configure bucket policies and access controls
    this.configureBucketPolicies(props);

    // Tag all resources
    Tags.of(this).add('Environment', props.environment);
    Tags.of(this).add('Project', 'EstateKit');
    Tags.of(this).add('Service', 'DocumentsAPI');

    // Export bucket ARNs
    this.exportBucketArns();
  }

  private createDocumentBucket(props: S3StackProps): s3.Bucket {
    const lifecycleRules = this.configureLifecycleRules();

    return new s3.Bucket(this, 'DocumentBucket', {
      bucketName: `estatekit-documents-${props.environment}`,
      encryption: BucketEncryption.KMS,
      encryptionKey: props.securityStack.documentEncryptionKey,
      versioned: true,
      blockPublicAccess: BlockPublicAccess.BLOCK_ALL,
      removalPolicy: RemovalPolicy.RETAIN,
      lifecycleRules,
      serverAccessLogsPrefix: 'access-logs/',
      enforceSSL: true,
      intelligentTieringConfigurations: [{
        name: 'OptimizeCosts',
        archiveAccessTierTime: Duration.days(90),
        deepArchiveAccessTierTime: Duration.days(180)
      }],
      cors: [{
        allowedMethods: [s3.HttpMethods.GET, s3.HttpMethods.PUT, s3.HttpMethods.POST, s3.HttpMethods.DELETE],
        allowedOrigins: ['*'],
        allowedHeaders: ['*'],
        maxAge: 3600
      }],
      metrics: [{
        id: 'EntireBucket'
      }],
      objectOwnership: s3.ObjectOwnership.BUCKET_OWNER_ENFORCED
    });
  }

  private createArchiveBucket(props: S3StackProps): s3.Bucket {
    return new s3.Bucket(this, 'ArchiveBucket', {
      bucketName: `estatekit-documents-archive-${props.environment}`,
      encryption: BucketEncryption.KMS,
      encryptionKey: props.securityStack.documentEncryptionKey,
      versioned: true,
      blockPublicAccess: BlockPublicAccess.BLOCK_ALL,
      removalPolicy: RemovalPolicy.RETAIN,
      lifecycleRules: [{
        enabled: true,
        transitions: [{
          storageClass: StorageClass.GLACIER,
          transitionAfter: Duration.days(0)
        }],
        expiration: Duration.days(2555) // 7 years retention
      }],
      serverAccessLogsPrefix: 'access-logs/',
      enforceSSL: true,
      objectOwnership: s3.ObjectOwnership.BUCKET_OWNER_ENFORCED
    });
  }

  private configureLifecycleRules(): LifecycleRule[] {
    return [
      {
        // Transition to Glacier after 90 days
        enabled: true,
        id: 'GlacierTransition',
        transitions: [{
          storageClass: StorageClass.GLACIER,
          transitionAfter: Duration.days(90)
        }],
        noncurrentVersionExpiration: Duration.days(2555) // 7 years retention
      },
      {
        // Clean up incomplete multipart uploads
        enabled: true,
        id: 'CleanupIncomplete',
        abortIncompleteMultipartUploadAfter: Duration.days(7)
      },
      {
        // Move older versions to Glacier
        enabled: true,
        id: 'ArchiveOldVersions',
        noncurrentVersionTransitions: [{
          storageClass: StorageClass.GLACIER,
          transitionAfter: Duration.days(30)
        }]
      }
    ];
  }

  private configureBucketPolicies(props: S3StackProps): void {
    // Configure VPC endpoint access
    const vpcEndpoint = props.networkingStack.vpc.addGatewayEndpoint('S3Endpoint', {
      service: s3.GatewayVpcEndpointAwsService.S3
    });

    // Grant access to API role
    this.documentBucket.grantReadWrite(props.securityStack.apiRole);
    this.archiveBucket.grantRead(props.securityStack.apiRole);

    // Configure bucket policies
    this.documentBucket.addToResourcePolicy(s3.PolicyStatement.fromJson({
      Effect: 'Deny',
      Principal: '*',
      Action: 's3:*',
      Resource: [
        this.documentBucket.bucketArn,
        `${this.documentBucket.bucketArn}/*`
      ],
      Condition: {
        Bool: {
          'aws:SecureTransport': 'false'
        }
      }
    }));

    // Configure replication for disaster recovery if in production
    if (props.environment === 'prod') {
      this.configureCrossRegionReplication(props);
    }
  }

  private configureCrossRegionReplication(props: S3StackProps): void {
    const replicationRole = this.createReplicationRole();

    this.documentBucket.addReplicationConfiguration({
      role: replicationRole,
      rules: [{
        destination: {
          bucket: this.archiveBucket,
          storageClass: StorageClass.STANDARD_IA
        },
        prefix: '',
        enabled: true
      }]
    });
  }

  private createReplicationRole(): s3.IBucketReplicationRole {
    return new s3.BucketReplicationRole(this, 'ReplicationRole', {
      replicationSourceBucket: this.documentBucket,
      replicationDestinationBucket: this.archiveBucket
    });
  }

  private exportBucketArns(): void {
    new CfnOutput(this, 'DocumentBucketArn', {
      value: this.documentBucket.bucketArn,
      description: 'ARN of primary document storage bucket',
      exportName: 'DocumentBucketArn'
    });

    new CfnOutput(this, 'ArchiveBucketArn', {
      value: this.archiveBucket.bucketArn,
      description: 'ARN of archive storage bucket',
      exportName: 'ArchiveBucketArn'
    });
  }
}