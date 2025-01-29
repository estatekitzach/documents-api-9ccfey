// aws-cdk-lib version: 2.x
import { Stack, StackProps, Tags } from 'aws-cdk-lib';
// constructs version: 10.x
import { Construct } from 'constructs';
// aws-cdk-lib/aws-ec2 version: 2.x
import {
  Vpc,
  SubnetType,
  SecurityGroup,
  InterfaceVpcEndpoint,
  Port,
  FlowLog,
  FlowLogDestination,
  FlowLogTrafficType,
  NetworkAcl,
  AclTraffic,
  TrafficDirection,
  GatewayVpcEndpoint,
  GatewayVpcEndpointAwsService,
  InterfaceVpcEndpointAwsService,
  IVpc,
  ISecurityGroup,
  IInterfaceVpcEndpoint,
  Peer,
  AclCidr
} from 'aws-cdk-lib/aws-ec2';

interface NetworkingStackProps extends StackProps {
  environment: string;
  vpcCidr: string;
  maxAzs: number;
}

export class NetworkingStack extends Stack {
  public readonly vpc: IVpc;
  public readonly apiSecurityGroup: ISecurityGroup;
  public readonly vpcEndpoints: IInterfaceVpcEndpoint[];

  constructor(scope: Construct, id: string, props: NetworkingStackProps) {
    super(scope, id, props);

    // Validate CIDR block format
    if (!/^(?:\d{1,3}\.){3}\d{1,3}\/\d{1,2}$/.test(props.vpcCidr)) {
      throw new Error('Invalid VPC CIDR format');
    }

    // Create core networking infrastructure
    this.vpc = this.createVpc(props);
    this.apiSecurityGroup = this.createSecurityGroups();
    this.vpcEndpoints = [];
    this.createVpcEndpoints();
    this.configureNetworkAcls();

    // Tag all resources
    Tags.of(this).add('Environment', props.environment);
    Tags.of(this).add('Project', 'EstateKit');
    Tags.of(this).add('Service', 'DocumentsAPI');
  }

  private createVpc(props: NetworkingStackProps): IVpc {
    const vpc = new Vpc(this, 'DocumentsApiVpc', {
      ipAddresses: props.vpcCidr,
      maxAzs: props.maxAzs,
      natGateways: props.environment === 'prod' ? 2 : 1,
      subnetConfiguration: [
        {
          name: 'Public',
          subnetType: SubnetType.PUBLIC,
          cidrMask: 24,
        },
        {
          name: 'Private',
          subnetType: SubnetType.PRIVATE_WITH_EGRESS,
          cidrMask: 24,
        },
        {
          name: 'Isolated',
          subnetType: SubnetType.PRIVATE_ISOLATED,
          cidrMask: 24,
        }
      ],
      enableDnsHostnames: true,
      enableDnsSupport: true,
      flowLogs: {
        'VpcFlowLogs': {
          destination: FlowLogDestination.toCloudWatchLogs(),
          trafficType: FlowLogTrafficType.ALL
        }
      }
    });

    Tags.of(vpc).add('Name', `${props.environment}-documents-api-vpc`);
    return vpc;
  }

  private createSecurityGroups(): ISecurityGroup {
    const apiSecurityGroup = new SecurityGroup(this, 'ApiSecurityGroup', {
      vpc: this.vpc,
      description: 'Security group for EstateKit Documents API',
      allowAllOutbound: false
    });

    // Inbound rules
    apiSecurityGroup.addIngressRule(
      Peer.anyIpv4(),
      Port.tcp(443),
      'Allow HTTPS inbound'
    );

    // Outbound rules
    apiSecurityGroup.addEgressRule(
      Peer.anyIpv4(),
      Port.tcp(443),
      'Allow HTTPS outbound'
    );

    Tags.of(apiSecurityGroup).add('Name', 'documents-api-sg');
    return apiSecurityGroup;
  }

  private createVpcEndpoints(): void {
    // S3 Gateway Endpoint
    const s3Endpoint = new GatewayVpcEndpoint(this, 'S3GatewayEndpoint', {
      vpc: this.vpc,
      service: GatewayVpcEndpointAwsService.S3
    });

    // Interface Endpoints
    const interfaceEndpoints = [
      { service: InterfaceVpcEndpointAwsService.ECR, name: 'EcrApiEndpoint' },
      { service: InterfaceVpcEndpointAwsService.ECR_DOCKER, name: 'EcrDockerEndpoint' },
      { service: InterfaceVpcEndpointAwsService.CLOUDWATCH, name: 'CloudWatchEndpoint' },
      { service: InterfaceVpcEndpointAwsService.SSM, name: 'SsmEndpoint' },
      { service: InterfaceVpcEndpointAwsService.SECRETS_MANAGER, name: 'SecretsManagerEndpoint' }
    ];

    interfaceEndpoints.forEach(endpoint => {
      const vpcEndpoint = new InterfaceVpcEndpoint(this, endpoint.name, {
        vpc: this.vpc,
        service: endpoint.service,
        privateDnsEnabled: true,
        subnets: {
          subnetType: SubnetType.PRIVATE_WITH_EGRESS
        },
        securityGroups: [this.apiSecurityGroup]
      });
      this.vpcEndpoints.push(vpcEndpoint);
    });
  }

  private configureNetworkAcls(): void {
    // Public Subnet NACL
    const publicNacl = new NetworkAcl(this, 'PublicNacl', {
      vpc: this.vpc,
      subnetSelection: { subnetType: SubnetType.PUBLIC }
    });

    // Allow HTTPS inbound
    publicNacl.addEntry('AllowHttpsInbound', {
      direction: TrafficDirection.INBOUND,
      ruleNumber: 100,
      cidr: AclCidr.anyIpv4(),
      traffic: AclTraffic.tcp(Port.tcp(443)),
      ruleAction: 'allow'
    });

    // Allow ephemeral ports inbound
    publicNacl.addEntry('AllowEphemeralInbound', {
      direction: TrafficDirection.INBOUND,
      ruleNumber: 200,
      cidr: AclCidr.anyIpv4(),
      traffic: AclTraffic.tcpPortRange(1024, 65535),
      ruleAction: 'allow'
    });

    // Private Subnet NACL
    const privateNacl = new NetworkAcl(this, 'PrivateNacl', {
      vpc: this.vpc,
      subnetSelection: { subnetType: SubnetType.PRIVATE_WITH_EGRESS }
    });

    // Allow VPC CIDR inbound
    privateNacl.addEntry('AllowVpcInbound', {
      direction: TrafficDirection.INBOUND,
      ruleNumber: 100,
      cidr: AclCidr.ipv4(this.vpc.vpcCidrBlock),
      traffic: AclTraffic.allTraffic(),
      ruleAction: 'allow'
    });

    // Allow ephemeral ports outbound
    privateNacl.addEntry('AllowEphemeralOutbound', {
      direction: TrafficDirection.OUTBOUND,
      ruleNumber: 100,
      cidr: AclCidr.anyIpv4(),
      traffic: AclTraffic.tcpPortRange(1024, 65535),
      ruleAction: 'allow'
    });
  }
}