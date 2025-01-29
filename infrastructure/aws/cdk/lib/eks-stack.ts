// aws-cdk-lib version: 2.x
import { Stack, CfnOutput, Tags } from 'aws-cdk-lib';
// constructs version: 10.x
import { Construct } from 'constructs';
// aws-cdk-lib/aws-eks version: 2.x
import * as eks from 'aws-cdk-lib/aws-eks';
import { NodegroupAmiType, CapacityType, KubernetesVersion } from 'aws-cdk-lib/aws-eks';
import { NetworkingStack } from './networking-stack';
import { SecurityStack } from './security-stack';

export interface EksStackProps {
  environment: string;
  networkingStack: NetworkingStack;
  securityStack: SecurityStack;
  clusterConfig: {
    version: eks.KubernetesVersion;
    logging: {
      api: boolean;
      audit: boolean;
      authenticator: boolean;
      controllerManager: boolean;
      scheduler: boolean;
    };
  };
}

export class EksStack extends Stack {
  public readonly cluster: eks.Cluster;
  public readonly nodeGroup: eks.Nodegroup;

  constructor(scope: Construct, id: string, props: EksStackProps) {
    super(scope, id);

    // Create EKS cluster
    this.cluster = this.createCluster(props);

    // Create node group
    this.nodeGroup = this.createNodeGroup(props);

    // Configure cluster add-ons
    this.configureClusterAddons(props);

    // Configure autoscaling
    this.configureAutoscaling(props);

    // Tag resources
    Tags.of(this).add('Environment', props.environment);
    Tags.of(this).add('Project', 'EstateKit');
    Tags.of(this).add('Service', 'DocumentsAPI');

    // Export cluster outputs
    this.exportClusterOutputs();
  }

  private createCluster(props: EksStackProps): eks.Cluster {
    return new eks.Cluster(this, 'DocumentsApiCluster', {
      version: props.clusterConfig.version,
      clusterName: `estatekit-documents-${props.environment}`,
      vpc: props.networkingStack.vpc,
      vpcSubnets: [{ subnetType: props.networkingStack.vpc.privateSubnets[0].subnetType }],
      defaultCapacity: 0,
      securityGroup: props.networkingStack.apiSecurityGroup,
      endpointAccess: eks.EndpointAccess.PRIVATE,
      role: props.securityStack.apiRole,
      secretsEncryptionKey: props.securityStack.documentEncryptionKey,
      outputClusterName: true,
      outputConfigCommand: true,
      clusterLogging: [
        props.clusterConfig.logging.api && eks.ClusterLoggingTypes.API,
        props.clusterConfig.logging.audit && eks.ClusterLoggingTypes.AUDIT,
        props.clusterConfig.logging.authenticator && eks.ClusterLoggingTypes.AUTHENTICATOR,
        props.clusterConfig.logging.controllerManager && eks.ClusterLoggingTypes.CONTROLLER_MANAGER,
        props.clusterConfig.logging.scheduler && eks.ClusterLoggingTypes.SCHEDULER
      ].filter(Boolean) as eks.ClusterLoggingTypes[],
      kubectlLayer: {
        version: KubernetesVersion.V1_27
      }
    });
  }

  private createNodeGroup(props: EksStackProps): eks.Nodegroup {
    return this.cluster.addNodegroupCapacity('DocumentsApiNodeGroup', {
      instanceTypes: ['t3.xlarge'],
      minSize: 2,
      maxSize: 10,
      desiredSize: 2,
      amiType: NodegroupAmiType.AL2_X86_64,
      capacityType: CapacityType.ON_DEMAND,
      diskSize: 100,
      labels: {
        'role': 'documents-api',
        'environment': props.environment
      },
      subnets: { subnetType: props.networkingStack.vpc.privateSubnets[0].subnetType },
      tags: {
        'Name': `estatekit-documents-node-${props.environment}`,
        'kubernetes.io/cluster-autoscaler/enabled': 'true',
        'kubernetes.io/cluster-autoscaler/node-template/resources/ephemeral-storage': '100Gi'
      }
    });
  }

  private configureClusterAddons(props: EksStackProps): void {
    // Install AWS VPC CNI
    this.cluster.addHelmChart('AwsVpcCni', {
      chart: 'aws-vpc-cni',
      repository: 'https://aws.github.io/eks-charts',
      namespace: 'kube-system',
      values: {
        env: {
          ENABLE_PREFIX_DELEGATION: 'true',
          WARM_PREFIX_TARGET: '1'
        }
      }
    });

    // Install AWS Load Balancer Controller
    this.cluster.addHelmChart('AwsLoadBalancerController', {
      chart: 'aws-load-balancer-controller',
      repository: 'https://aws.github.io/eks-charts',
      namespace: 'kube-system',
      values: {
        clusterName: this.cluster.clusterName,
        serviceAccount: {
          create: true,
          annotations: {
            'eks.amazonaws.com/role-arn': props.securityStack.apiRole.roleArn
          }
        }
      }
    });

    // Install metrics server
    this.cluster.addHelmChart('MetricsServer', {
      chart: 'metrics-server',
      repository: 'https://kubernetes-sigs.github.io/metrics-server/',
      namespace: 'kube-system'
    });

    // Install Calico network policy
    this.cluster.addHelmChart('Calico', {
      chart: 'calico',
      repository: 'https://docs.projectcalico.org/charts',
      namespace: 'kube-system'
    });
  }

  private configureAutoscaling(props: EksStackProps): void {
    // Install Cluster Autoscaler
    this.cluster.addHelmChart('ClusterAutoscaler', {
      chart: 'cluster-autoscaler',
      repository: 'https://kubernetes.github.io/autoscaler',
      namespace: 'kube-system',
      values: {
        autoDiscovery: {
          clusterName: this.cluster.clusterName
        },
        awsRegion: Stack.of(this).region,
        rbac: {
          serviceAccount: {
            annotations: {
              'eks.amazonaws.com/role-arn': props.securityStack.apiRole.roleArn
            }
          }
        },
        extraArgs: {
          'scale-down-delay-after-add': '5m',
          'scale-down-unneeded-time': '5m',
          'max-node-provision-time': '15m',
          'skip-nodes-with-system-pods': 'false'
        }
      }
    });

    // Configure HPA settings
    this.cluster.addManifest('HpaConfig', {
      apiVersion: 'autoscaling/v2',
      kind: 'HorizontalPodAutoscaler',
      metadata: {
        name: 'documents-api-hpa',
        namespace: 'default'
      },
      spec: {
        scaleTargetRef: {
          apiVersion: 'apps/v1',
          kind: 'Deployment',
          name: 'documents-api'
        },
        minReplicas: 2,
        maxReplicas: 10,
        metrics: [
          {
            type: 'Resource',
            resource: {
              name: 'cpu',
              target: {
                type: 'Utilization',
                averageUtilization: 70
              }
            }
          },
          {
            type: 'Resource',
            resource: {
              name: 'memory',
              target: {
                type: 'Utilization',
                averageUtilization: 80
              }
            }
          }
        ]
      }
    });
  }

  private exportClusterOutputs(): void {
    new CfnOutput(this, 'ClusterEndpoint', {
      value: this.cluster.clusterEndpoint,
      description: 'EKS cluster endpoint',
      exportName: 'EksClusterEndpoint'
    });

    new CfnOutput(this, 'ClusterName', {
      value: this.cluster.clusterName,
      description: 'EKS cluster name',
      exportName: 'EksClusterName'
    });

    new CfnOutput(this, 'ClusterSecurityGroupId', {
      value: this.cluster.clusterSecurityGroup.securityGroupId,
      description: 'EKS cluster security group ID',
      exportName: 'EksClusterSecurityGroupId'
    });
  }
}