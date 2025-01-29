// aws-cdk-lib version: 2.x
import { App, Stack } from 'aws-cdk-lib';
import { Template, Match } from 'aws-cdk-lib/assertions';
import { S3Stack } from '../lib/s3-stack';
import { SecurityStack } from '../lib/security-stack';
import { NetworkingStack } from '../lib/networking-stack';

class TestContext {
  app: App;
  securityStack: SecurityStack;
  networkingStack: NetworkingStack;
  s3Stack: S3Stack;
  template: Template;

  constructor() {
    this.app = new App();
    
    // Initialize mock stacks
    this.networkingStack = new NetworkingStack(this.app, 'NetworkingStack', {
      environment: 'test',
      vpcCidr: '10.0.0.0/16',
      maxAzs: 2
    });

    this.securityStack = new SecurityStack(this.app, 'SecurityStack', {
      environment: 'test',
      networkingStack: this.networkingStack
    });

    this.s3Stack = new S3Stack(this.app, 'S3Stack', {
      environment: 'test',
      securityStack: this.securityStack,
      networkingStack: this.networkingStack
    });

    this.template = Template.fromStack(this.s3Stack);
  }

  assertBucketEncryption(bucketLogicalId: string): void {
    this.template.hasResourceProperties('AWS::S3::Bucket', {
      BucketEncryption: {
        ServerSideEncryptionConfiguration: [{
          ServerSideEncryptionByDefault: {
            SSEAlgorithm: 'aws:kms',
            KMSMasterKeyID: {
              'Fn::GetAtt': [Match.stringLikeRegexp('DocumentEncryptionKey'), 'Arn']
            }
          }
        }]
      }
    });
  }

  assertBucketVersioning(bucketLogicalId: string): void {
    this.template.hasResourceProperties('AWS::S3::Bucket', {
      VersioningConfiguration: {
        Status: 'Enabled'
      }
    });
  }

  assertPublicAccessBlock(bucketLogicalId: string): void {
    this.template.hasResourceProperties('AWS::S3::Bucket', {
      PublicAccessBlockConfiguration: {
        BlockPublicAcls: true,
        BlockPublicPolicy: true,
        IgnorePublicAcls: true,
        RestrictPublicBuckets: true
      }
    });
  }

  assertLifecycleRules(bucketLogicalId: string, isArchive: boolean): void {
    const lifecycleRules = isArchive ? [{
      Status: 'Enabled',
      Transitions: [{
        StorageClass: 'GLACIER',
        TransitionInDays: 0
      }],
      ExpirationInDays: 2555
    }] : [{
      Id: 'GlacierTransition',
      Status: 'Enabled',
      Transitions: [{
        StorageClass: 'GLACIER',
        TransitionInDays: 90
      }],
      NoncurrentVersionExpiration: {
        NoncurrentDays: 2555
      }
    }];

    this.template.hasResourceProperties('AWS::S3::Bucket', {
      LifecycleConfiguration: {
        Rules: Match.arrayWith(lifecycleRules)
      }
    });
  }
}

describe('S3Stack', () => {
  let testContext: TestContext;

  beforeEach(() => {
    testContext = new TestContext();
  });

  describe('Document Bucket Configuration', () => {
    it('should have encryption enabled with KMS key', () => {
      testContext.assertBucketEncryption('DocumentBucket');
    });

    it('should have versioning enabled', () => {
      testContext.assertBucketVersioning('DocumentBucket');
    });

    it('should block all public access', () => {
      testContext.assertPublicAccessBlock('DocumentBucket');
    });

    it('should have correct CORS configuration', () => {
      testContext.template.hasResourceProperties('AWS::S3::Bucket', {
        CorsConfiguration: {
          CorsRules: [{
            AllowedHeaders: ['*'],
            AllowedMethods: ['GET', 'PUT', 'POST', 'DELETE'],
            AllowedOrigins: ['*'],
            MaxAge: 3600
          }]
        }
      });
    });

    it('should enforce SSL', () => {
      testContext.template.hasResourceProperties('AWS::S3::BucketPolicy', {
        PolicyDocument: {
          Statement: Match.arrayWith([{
            Effect: 'Deny',
            Principal: '*',
            Action: 's3:*',
            Condition: {
              Bool: {
                'aws:SecureTransport': 'false'
              }
            }
          }])
        }
      });
    });
  });

  describe('Archive Bucket Configuration', () => {
    it('should have encryption enabled', () => {
      testContext.assertBucketEncryption('ArchiveBucket');
    });

    it('should have immediate Glacier transition', () => {
      testContext.assertLifecycleRules('ArchiveBucket', true);
    });

    it('should have 7-year retention policy', () => {
      testContext.template.hasResourceProperties('AWS::S3::Bucket', {
        LifecycleConfiguration: {
          Rules: Match.arrayWith([{
            ExpirationInDays: 2555
          }])
        }
      });
    });
  });

  describe('Security Configurations', () => {
    it('should grant read/write permissions to API role', () => {
      testContext.template.hasResourceProperties('AWS::IAM::Policy', {
        PolicyDocument: {
          Statement: Match.arrayWith([{
            Effect: 'Allow',
            Action: ['s3:PutObject', 's3:GetObject', 's3:DeleteObject', 's3:ListBucket'],
            Resource: Match.anyValue()
          }])
        }
      });
    });

    it('should configure VPC endpoint policy', () => {
      testContext.template.hasResourceProperties('AWS::EC2::VPCEndpoint', {
        PolicyDocument: Match.objectLike({
          Statement: [{
            Effect: 'Allow',
            Principal: '*',
            Action: ['s3:*']
          }]
        })
      });
    });
  });

  describe('Lifecycle Policies', () => {
    it('should configure 90-day Glacier transition for document bucket', () => {
      testContext.assertLifecycleRules('DocumentBucket', false);
    });

    it('should configure cleanup of incomplete multipart uploads', () => {
      testContext.template.hasResourceProperties('AWS::S3::Bucket', {
        LifecycleConfiguration: {
          Rules: Match.arrayWith([{
            AbortIncompleteMultipartUpload: {
              DaysAfterInitiation: 7
            }
          }])
        }
      });
    });

    it('should configure version cleanup rules', () => {
      testContext.template.hasResourceProperties('AWS::S3::Bucket', {
        LifecycleConfiguration: {
          Rules: Match.arrayWith([{
            NoncurrentVersionTransitions: [{
              StorageClass: 'GLACIER',
              TransitionAfter: 30
            }]
          }])
        }
      });
    });
  });
});