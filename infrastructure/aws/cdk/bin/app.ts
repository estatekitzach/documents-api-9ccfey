#!/usr/bin/env node
// aws-cdk-lib version: 2.x
import { App, Environment, Tags, Duration, RemovalPolicy } from 'aws-cdk-lib';

// Internal stack imports
import { EksStack } from '../lib/eks-stack';
import { S3Stack } from '../lib/s3-stack';
import { CognitoStack } from '../lib/cognito-stack';
import { MonitoringStack } from '../lib/monitoring-stack';
import { NetworkingStack } from '../lib/networking-stack';
import { SecurityStack } from '../lib/security-stack';

// Environment validation and configuration
function getEnvironment(stage: string): Environment {
  // Validate stage
  if (!['dev', 'staging', 'prod'].includes(stage)) {
    throw new Error('Invalid stage. Must be dev, staging, or prod');
  }

  // Get and validate AWS account
  const account = process.env.CDK_DEFAULT_ACCOUNT;
  if (!account || !/^\d{12}$/.test(account)) {
    throw new Error('Invalid AWS account ID');
  }

  // Get and validate AWS region
  const region = process.env.CDK_DEFAULT_REGION;
  if (!region || !/^[a-z]{2}-[a-z]+-\d{1}$/.test(region)) {
    throw new Error('Invalid AWS region');
  }

  // Validate account/region combination for compliance
  const validRegions = ['us-east-1', 'us-west-2', 'eu-west-1'];
  if (stage === 'prod' && !validRegions.includes(region)) {
    throw new Error('Production must be deployed in compliant regions only');
  }

  return { account, region };
}

// Configuration validation
function validateConfig(app: App): void {
  const requiredContextKeys = [
    'vpcCidr',
    'maxAzs',
    'retentionDays',
    'alertEndpoints'
  ];

  requiredContextKeys.forEach(key => {
    if (!app.node.tryGetContext(key)) {
      throw new Error(`Missing required context key: ${key}`);
    }
  });

  // Validate CIDR format
  const vpcCidr = app.node.tryGetContext('vpcCidr');
  if (!/^(?:\d{1,3}\.){3}\d{1,3}\/\d{1,2}$/.test(vpcCidr)) {
    throw new Error('Invalid VPC CIDR format');
  }

  // Validate numeric values
  const maxAzs = app.node.tryGetContext('maxAzs');
  if (!Number.isInteger(maxAzs) || maxAzs < 2 || maxAzs > 3) {
    throw new Error('maxAzs must be 2 or 3');
  }

  const retentionDays = app.node.tryGetContext('retentionDays');
  if (!Number.isInteger(retentionDays) || retentionDays < 1) {
    throw new Error('Invalid retention days');
  }
}

// Resource tagging
function applyTags(app: App, stage: string): void {
  Tags.of(app).add('Environment', stage);
  Tags.of(app).add('Project', 'EstateKit');
  Tags.of(app).add('Service', 'DocumentsAPI');
  Tags.of(app).add('ManagedBy', 'CDK');
  Tags.of(app).add('CostCenter', 'EstateKit-Documents');
  
  if (stage === 'prod') {
    Tags.of(app).add('Compliance', 'SOC2');
    Tags.of(app).add('DataClassification', 'Confidential');
    Tags.of(app).add('BackupPolicy', 'Daily');
    Tags.of(app).add('SecurityZone', 'HighSecurity');
  }
}

// Main CDK app
const app = new App({
  context: {
    stage: process.env.STAGE || 'dev'
  }
});

// Validate configuration
validateConfig(app);

// Get environment configuration
const stage = app.node.tryGetContext('stage');
const env = getEnvironment(stage);

// Create stacks with dependencies
const networkingStack = new NetworkingStack(app, `EstateKit-Documents-Network-${stage}`, {
  environment: stage,
  vpcCidr: app.node.tryGetContext('vpcCidr'),
  maxAzs: app.node.tryGetContext('maxAzs'),
  env
});

const securityStack = new SecurityStack(app, `EstateKit-Documents-Security-${stage}`, {
  environment: stage,
  networkingStack,
  env
});

const s3Stack = new S3Stack(app, `EstateKit-Documents-Storage-${stage}`, {
  environment: stage,
  securityStack,
  networkingStack,
  env
});

const cognitoStack = new CognitoStack(app, `EstateKit-Documents-Auth-${stage}`, {
  environment: stage,
  networkingStack,
  env
});

const eksStack = new EksStack(app, `EstateKit-Documents-EKS-${stage}`, {
  environment: stage,
  networkingStack,
  securityStack,
  clusterConfig: {
    version: KubernetesVersion.V1_27,
    logging: {
      api: true,
      audit: true,
      authenticator: true,
      controllerManager: true,
      scheduler: true
    }
  },
  env
});

const monitoringStack = new MonitoringStack(app, `EstateKit-Documents-Monitoring-${stage}`, {
  environment: stage,
  eksStack,
  retentionDays: app.node.tryGetContext('retentionDays'),
  alertEndpoints: app.node.tryGetContext('alertEndpoints'),
  env
});

// Apply stack dependencies
s3Stack.addDependency(securityStack);
eksStack.addDependency(networkingStack);
eksStack.addDependency(securityStack);
monitoringStack.addDependency(eksStack);
cognitoStack.addDependency(networkingStack);

// Apply resource tags
applyTags(app, stage);

// Synthesize CloudFormation templates
app.synth();