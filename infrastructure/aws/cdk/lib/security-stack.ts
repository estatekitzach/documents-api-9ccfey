// aws-cdk-lib version: 2.x
import { Stack, CfnOutput, Duration, RemovalPolicy, Tags } from 'aws-cdk-lib';
// constructs version: 10.x
import { Construct } from 'constructs';
// aws-cdk-lib/aws-kms version: 2.x
import * as kms from 'aws-cdk-lib/aws-kms';
// aws-cdk-lib/aws-iam version: 2.x
import * as iam from 'aws-cdk-lib/aws-iam';
import { NetworkingStack } from './networking-stack';

interface SecurityStackProps {
  environment: string;
  networkingStack: NetworkingStack;
}

export class SecurityStack extends Stack {
  public readonly documentEncryptionKey: kms.Key;
  public readonly apiRole: iam.Role;

  constructor(scope: Construct, id: string, props: SecurityStackProps) {
    super(scope, id);

    // Create KMS key for document encryption
    this.documentEncryptionKey = this.createDocumentEncryptionKey(props);

    // Create IAM role for API service
    this.apiRole = this.createApiRole(props);

    // Configure comprehensive security policies
    this.configureSecurityPolicies(props);

    // Tag all resources
    Tags.of(this).add('Environment', props.environment);
    Tags.of(this).add('Project', 'EstateKit');
    Tags.of(this).add('Service', 'DocumentsAPI');
    Tags.of(this).add('SecurityContext', 'HighSecurity');

    // Export security resource ARNs
    this.exportSecurityResources();
  }

  private createDocumentEncryptionKey(props: SecurityStackProps): kms.Key {
    const key = new kms.Key(this, 'DocumentEncryptionKey', {
      enableKeyRotation: true,
      rotationSchedule: Duration.days(90),
      pendingWindow: Duration.days(7),
      removalPolicy: RemovalPolicy.RETAIN,
      description: 'KMS key for EstateKit Documents API encryption',
      alias: `estatekit-documents-${props.environment}`,
      enabled: true
    });

    // Add key administrators
    key.addAlias(`alias/estatekit-documents-${props.environment}`);
    
    // Configure key policy
    key.addToResourcePolicy(new iam.PolicyStatement({
      sid: 'Enable IAM User Permissions',
      effect: iam.Effect.ALLOW,
      principals: [new iam.AccountRootPrincipal()],
      actions: [
        'kms:Create*',
        'kms:Describe*',
        'kms:Enable*',
        'kms:List*',
        'kms:Put*'
      ],
      resources: ['*']
    }));

    return key;
  }

  private createApiRole(props: SecurityStackProps): iam.Role {
    const role = new iam.Role(this, 'ApiServiceRole', {
      assumedBy: new iam.ServicePrincipal('eks.amazonaws.com'),
      description: 'IAM role for EstateKit Documents API service',
      maxSessionDuration: Duration.hours(1),
      roleName: `estatekit-documents-api-${props.environment}`
    });

    // KMS key usage policy
    role.addToPolicy(new iam.PolicyStatement({
      effect: iam.Effect.ALLOW,
      actions: [
        'kms:Decrypt',
        'kms:GenerateDataKey',
        'kms:DescribeKey'
      ],
      resources: [this.documentEncryptionKey.keyArn]
    }));

    // S3 access policy
    role.addToPolicy(new iam.PolicyStatement({
      effect: iam.Effect.ALLOW,
      actions: [
        's3:PutObject',
        's3:GetObject',
        's3:DeleteObject',
        's3:ListBucket'
      ],
      resources: [
        `arn:aws:s3:::estatekit-documents-${props.environment}`,
        `arn:aws:s3:::estatekit-documents-${props.environment}/*`
      ],
      conditions: {
        'StringEquals': {
          's3:x-amz-server-side-encryption': 'aws:kms',
          's3:x-amz-server-side-encryption-aws-kms-key-id': this.documentEncryptionKey.keyArn
        }
      }
    }));

    // Textract access policy
    role.addToPolicy(new iam.PolicyStatement({
      effect: iam.Effect.ALLOW,
      actions: [
        'textract:AnalyzeDocument',
        'textract:GetDocumentAnalysis',
        'textract:StartDocumentAnalysis'
      ],
      resources: ['*']
    }));

    // CloudWatch logs policy
    role.addToPolicy(new iam.PolicyStatement({
      effect: iam.Effect.ALLOW,
      actions: [
        'logs:CreateLogGroup',
        'logs:CreateLogStream',
        'logs:PutLogEvents'
      ],
      resources: [`arn:aws:logs:${Stack.of(this).region}:${Stack.of(this).account}:log-group:/aws/estatekit-documents-${props.environment}:*`]
    }));

    return role;
  }

  private configureSecurityPolicies(props: SecurityStackProps): void {
    // Configure VPC endpoint policies
    props.networkingStack.vpcEndpoints.forEach(endpoint => {
      endpoint.addToPolicy(new iam.PolicyStatement({
        effect: iam.Effect.ALLOW,
        principals: [new iam.ArnPrincipal(this.apiRole.roleArn)],
        actions: ['*'],
        resources: ['*'],
        conditions: {
          'StringEquals': {
            'aws:SourceVpc': props.networkingStack.vpc.vpcId
          }
        }
      }));
    });

    // Configure service control policies if in production
    if (props.environment === 'prod') {
      this.apiRole.addToPolicy(new iam.PolicyStatement({
        effect: iam.Effect.DENY,
        actions: ['s3:DeleteBucket'],
        resources: [`arn:aws:s3:::estatekit-documents-${props.environment}`]
      }));
    }
  }

  private exportSecurityResources(): void {
    new CfnOutput(this, 'DocumentEncryptionKeyArn', {
      value: this.documentEncryptionKey.keyArn,
      description: 'ARN of KMS key for document encryption',
      exportName: 'DocumentEncryptionKeyArn'
    });

    new CfnOutput(this, 'ApiServiceRoleArn', {
      value: this.apiRole.roleArn,
      description: 'ARN of IAM role for API service',
      exportName: 'ApiServiceRoleArn'
    });
  }
}