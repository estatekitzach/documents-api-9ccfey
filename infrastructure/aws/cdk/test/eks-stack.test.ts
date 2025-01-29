// aws-cdk-lib version: 2.x
import { describe, expect, test, beforeEach } from '@jest/globals';
import { App } from 'aws-cdk-lib';
import { Template, Match } from 'aws-cdk-lib/assertions';
import { EksStack } from '../lib/eks-stack';
import { NetworkingStack } from '../lib/networking-stack';
import { SecurityStack } from '../lib/security-stack';

let app: App;
let template: Template;
let networkingStack: NetworkingStack;
let securityStack: SecurityStack;
let eksStack: EksStack;

beforeEach(() => {
  app = new App();
  
  // Create required dependency stacks
  networkingStack = new NetworkingStack(app, 'TestNetworkingStack', {
    environment: 'test',
    vpcCidr: '10.0.0.0/16',
    maxAzs: 2
  });

  securityStack = new SecurityStack(app, 'TestSecurityStack', {
    environment: 'test',
    networkingStack: networkingStack
  });

  // Create EKS stack with test configuration
  eksStack = new EksStack(app, 'TestEksStack', {
    environment: 'test',
    networkingStack: networkingStack,
    securityStack: securityStack,
    clusterConfig: {
      version: '1.27',
      logging: {
        api: true,
        audit: true,
        authenticator: true,
        controllerManager: true,
        scheduler: true
      }
    }
  });

  template = Template.fromStack(eksStack);
});

describe('EKS Cluster Configuration', () => {
  test('creates EKS cluster with correct configuration', () => {
    template.hasResourceProperties('Custom::AWSCDK-EKS-Cluster', {
      Version: Match.stringLikeRegexp('1.27'),
      Name: Match.stringLikeRegexp('estatekit-documents-test'),
      RoleArn: Match.anyValue(),
      ResourcesVpcConfig: {
        EndpointPrivateAccess: true,
        EndpointPublicAccess: false,
        SubnetIds: Match.arrayWith([Match.anyValue()]),
        SecurityGroupIds: Match.arrayWith([Match.anyValue()])
      },
      EncryptionConfig: [{
        Provider: {
          KeyArn: Match.anyValue()
        },
        Resources: ['secrets']
      }],
      Logging: {
        ClusterLogging: {
          EnabledTypes: Match.arrayWith([
            { Type: 'api' },
            { Type: 'audit' },
            { Type: 'authenticator' },
            { Type: 'controllerManager' },
            { Type: 'scheduler' }
          ])
        }
      }
    });
  });

  test('configures cluster security group rules', () => {
    template.hasResourceProperties('AWS::EC2::SecurityGroup', {
      SecurityGroupIngress: Match.arrayWith([
        Match.objectLike({
          FromPort: 443,
          ToPort: 443,
          IpProtocol: 'tcp'
        })
      ]),
      VpcId: Match.anyValue()
    });
  });
});

describe('Node Group Configuration', () => {
  test('creates managed node group with correct configuration', () => {
    template.hasResourceProperties('Custom::AWSCDK-EKS-Nodegroup', {
      ClusterName: Match.anyValue(),
      NodegroupName: Match.stringLikeRegexp('DocumentsApiNodeGroup'),
      InstanceTypes: ['t3.xlarge'],
      ScalingConfig: {
        MinSize: 2,
        MaxSize: 10,
        DesiredSize: 2
      },
      AmiType: 'AL2_x86_64',
      CapacityType: 'ON_DEMAND',
      DiskSize: 100,
      Labels: {
        'role': 'documents-api',
        'environment': 'test'
      },
      Tags: Match.objectLike({
        'Name': 'estatekit-documents-node-test',
        'kubernetes.io/cluster-autoscaler/enabled': 'true',
        'kubernetes.io/cluster-autoscaler/node-template/resources/ephemeral-storage': '100Gi'
      })
    });
  });

  test('configures node group IAM role with required permissions', () => {
    template.hasResourceProperties('AWS::IAM::Role', {
      AssumeRolePolicyDocument: Match.objectLike({
        Statement: Match.arrayWith([
          Match.objectLike({
            Action: 'sts:AssumeRole',
            Effect: 'Allow',
            Principal: {
              Service: 'ec2.amazonaws.com'
            }
          })
        ])
      }),
      ManagedPolicyArns: Match.arrayWith([
        Match.stringLikeRegexp('AmazonEKSWorkerNodePolicy'),
        Match.stringLikeRegexp('AmazonEKS_CNI_Policy'),
        Match.stringLikeRegexp('AmazonEC2ContainerRegistryReadOnly')
      ])
    });
  });
});

describe('Cluster Add-ons Configuration', () => {
  test('installs required add-ons', () => {
    // AWS VPC CNI
    template.hasResourceProperties('Custom::AWSCDK-EKS-HelmChart', {
      Chart: 'aws-vpc-cni',
      Repository: 'https://aws.github.io/eks-charts',
      Namespace: 'kube-system',
      Values: Match.objectLike({
        env: {
          ENABLE_PREFIX_DELEGATION: 'true',
          WARM_PREFIX_TARGET: '1'
        }
      })
    });

    // AWS Load Balancer Controller
    template.hasResourceProperties('Custom::AWSCDK-EKS-HelmChart', {
      Chart: 'aws-load-balancer-controller',
      Repository: 'https://aws.github.io/eks-charts',
      Namespace: 'kube-system'
    });

    // Metrics Server
    template.hasResourceProperties('Custom::AWSCDK-EKS-HelmChart', {
      Chart: 'metrics-server',
      Repository: 'https://kubernetes-sigs.github.io/metrics-server/',
      Namespace: 'kube-system'
    });

    // Calico Network Policy
    template.hasResourceProperties('Custom::AWSCDK-EKS-HelmChart', {
      Chart: 'calico',
      Repository: 'https://docs.projectcalico.org/charts',
      Namespace: 'kube-system'
    });
  });
});

describe('Autoscaling Configuration', () => {
  test('configures cluster autoscaler', () => {
    template.hasResourceProperties('Custom::AWSCDK-EKS-HelmChart', {
      Chart: 'cluster-autoscaler',
      Repository: 'https://kubernetes.github.io/autoscaler',
      Namespace: 'kube-system',
      Values: Match.objectLike({
        autoDiscovery: {
          clusterName: Match.anyValue()
        },
        rbac: {
          serviceAccount: {
            annotations: Match.objectLike({
              'eks.amazonaws.com/role-arn': Match.anyValue()
            })
          }
        },
        extraArgs: {
          'scale-down-delay-after-add': '5m',
          'scale-down-unneeded-time': '5m',
          'max-node-provision-time': '15m',
          'skip-nodes-with-system-pods': 'false'
        }
      })
    });
  });

  test('configures horizontal pod autoscaling', () => {
    template.hasResourceProperties('Custom::AWSCDK-EKS-KubernetesResource', {
      Manifest: Match.arrayWith([
        Match.objectLike({
          apiVersion: 'autoscaling/v2',
          kind: 'HorizontalPodAutoscaler',
          metadata: {
            name: 'documents-api-hpa',
            namespace: 'default'
          },
          spec: {
            minReplicas: 2,
            maxReplicas: 10,
            metrics: Match.arrayWith([
              Match.objectLike({
                type: 'Resource',
                resource: {
                  name: 'cpu',
                  target: {
                    type: 'Utilization',
                    averageUtilization: 70
                  }
                }
              }),
              Match.objectLike({
                type: 'Resource',
                resource: {
                  name: 'memory',
                  target: {
                    type: 'Utilization',
                    averageUtilization: 80
                  }
                }
              })
            ])
          }
        })
      ])
    });
  });
});