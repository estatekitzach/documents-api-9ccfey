// aws-cdk-lib version: 2.x
import { Stack, Duration, Tags } from 'aws-cdk-lib';
// constructs version: 10.x
import { Construct } from 'constructs';
// aws-cdk-lib/aws-cloudwatch version: 2.x
import * as cloudwatch from 'aws-cdk-lib/aws-cloudwatch';
// aws-cdk-lib/aws-logs version: 2.x
import * as logs from 'aws-cdk-lib/aws-logs';
import { EksStack } from './eks-stack';

export interface MonitoringStackProps {
  environment: string;
  eksStack: EksStack;
  retentionDays: number;
  alertEndpoints: string[];
}

export class MonitoringStack extends Stack {
  public readonly dashboard: cloudwatch.Dashboard;
  public readonly logGroups: logs.LogGroup[];
  public readonly alarms: cloudwatch.Alarm[];
  public readonly metricFilters: logs.MetricFilter[];

  constructor(scope: Construct, id: string, props: MonitoringStackProps) {
    super(scope, id);

    // Create log groups with retention
    this.logGroups = this.createLogGroups(props);

    // Create metric filters for log analysis
    this.metricFilters = this.createMetricFilters();

    // Create alarms with specific thresholds
    this.alarms = this.createAlarms(props);

    // Create comprehensive dashboard
    this.dashboard = this.createDashboard();

    // Enable Container Insights
    this.enableContainerInsights(props);

    // Tag resources
    Tags.of(this).add('Environment', props.environment);
    Tags.of(this).add('Project', 'EstateKit');
    Tags.of(this).add('Service', 'DocumentsAPI');
  }

  private createLogGroups(props: MonitoringStackProps): logs.LogGroup[] {
    const groups = [
      new logs.LogGroup(this, 'ApiLogs', {
        logGroupName: `/estatekit/documents-api/${props.environment}/api`,
        retention: props.retentionDays,
        removalPolicy: props.environment === 'prod' ? RemovalPolicy.RETAIN : RemovalPolicy.DESTROY
      }),
      new logs.LogGroup(this, 'DocumentProcessingLogs', {
        logGroupName: `/estatekit/documents-api/${props.environment}/processing`,
        retention: props.retentionDays,
        removalPolicy: props.environment === 'prod' ? RemovalPolicy.RETAIN : RemovalPolicy.DESTROY
      }),
      new logs.LogGroup(this, 'StorageLogs', {
        logGroupName: `/estatekit/documents-api/${props.environment}/storage`,
        retention: props.retentionDays,
        removalPolicy: props.environment === 'prod' ? RemovalPolicy.RETAIN : RemovalPolicy.DESTROY
      }),
      new logs.LogGroup(this, 'AuthLogs', {
        logGroupName: `/estatekit/documents-api/${props.environment}/auth`,
        retention: props.retentionDays,
        removalPolicy: props.environment === 'prod' ? RemovalPolicy.RETAIN : RemovalPolicy.DESTROY
      })
    ];

    return groups;
  }

  private createMetricFilters(): logs.MetricFilter[] {
    const filters = [];

    // API Error Rate Filter
    filters.push(new logs.MetricFilter(this, 'ApiErrorFilter', {
      logGroup: this.logGroups[0],
      metricNamespace: 'EstateKit/DocumentsAPI',
      metricName: 'ErrorCount',
      filterPattern: logs.FilterPattern.literal('ERROR'),
      metricValue: '1'
    }));

    // API Latency Filter
    filters.push(new logs.MetricFilter(this, 'ApiLatencyFilter', {
      logGroup: this.logGroups[0],
      metricNamespace: 'EstateKit/DocumentsAPI',
      metricName: 'APILatency',
      filterPattern: logs.FilterPattern.spaceDelimited('timestamp', 'requestId', 'latency').whereNumber('latency', '>=', 1000),
      metricValue: '$latency'
    }));

    // Document Processing Filter
    filters.push(new logs.MetricFilter(this, 'ProcessingFilter', {
      logGroup: this.logGroups[1],
      metricNamespace: 'EstateKit/DocumentsAPI',
      metricName: 'ProcessingTime',
      filterPattern: logs.FilterPattern.spaceDelimited('timestamp', 'documentId', 'processingTime'),
      metricValue: '$processingTime'
    }));

    return filters;
  }

  private createAlarms(props: MonitoringStackProps): cloudwatch.Alarm[] {
    const alarms = [];

    // API Latency Alarm (>1000ms)
    alarms.push(new cloudwatch.Alarm(this, 'ApiLatencyAlarm', {
      metric: new cloudwatch.Metric({
        namespace: 'EstateKit/DocumentsAPI',
        metricName: 'APILatency',
        statistic: 'Average',
        period: Duration.minutes(1)
      }),
      threshold: 1000,
      evaluationPeriods: 3,
      datapointsToAlarm: 2,
      alarmDescription: 'API latency exceeds 1000ms',
      actionsEnabled: true,
      treatMissingData: cloudwatch.TreatMissingData.NOT_BREACHING
    }));

    // Error Rate Alarm (>1%)
    alarms.push(new cloudwatch.Alarm(this, 'ErrorRateAlarm', {
      metric: new cloudwatch.Metric({
        namespace: 'EstateKit/DocumentsAPI',
        metricName: 'ErrorCount',
        statistic: 'Sum',
        period: Duration.minutes(5)
      }).createMetric({
        period: Duration.minutes(5),
        statistic: 'Average',
        unit: cloudwatch.Unit.PERCENT
      }),
      threshold: 1,
      evaluationPeriods: 3,
      datapointsToAlarm: 2,
      alarmDescription: 'Error rate exceeds 1%',
      actionsEnabled: true
    }));

    // CPU Usage Alarm (>85%)
    alarms.push(new cloudwatch.Alarm(this, 'CpuUsageAlarm', {
      metric: new cloudwatch.Metric({
        namespace: 'AWS/EKS',
        metricName: 'pod_cpu_utilization',
        dimensions: {
          ClusterName: props.eksStack.cluster.clusterName
        },
        statistic: 'Average',
        period: Duration.minutes(5)
      }),
      threshold: 85,
      evaluationPeriods: 3,
      datapointsToAlarm: 2,
      alarmDescription: 'CPU usage exceeds 85%',
      actionsEnabled: true
    }));

    // Memory Usage Alarm (>90%)
    alarms.push(new cloudwatch.Alarm(this, 'MemoryUsageAlarm', {
      metric: new cloudwatch.Metric({
        namespace: 'AWS/EKS',
        metricName: 'pod_memory_utilization',
        dimensions: {
          ClusterName: props.eksStack.cluster.clusterName
        },
        statistic: 'Average',
        period: Duration.minutes(5)
      }),
      threshold: 90,
      evaluationPeriods: 3,
      datapointsToAlarm: 2,
      alarmDescription: 'Memory usage exceeds 90%',
      actionsEnabled: true
    }));

    return alarms;
  }

  private createDashboard(): cloudwatch.Dashboard {
    const dashboard = new cloudwatch.Dashboard(this, 'DocumentsApiDashboard', {
      dashboardName: 'EstateKit-DocumentsAPI-Monitoring'
    });

    // API Performance Widgets
    dashboard.addWidgets(
      new cloudwatch.GraphWidget({
        title: 'API Latency',
        left: [
          new cloudwatch.Metric({
            namespace: 'EstateKit/DocumentsAPI',
            metricName: 'APILatency',
            statistic: 'Average',
            period: Duration.minutes(1)
          })
        ]
      }),
      new cloudwatch.GraphWidget({
        title: 'Error Rate',
        left: [
          new cloudwatch.Metric({
            namespace: 'EstateKit/DocumentsAPI',
            metricName: 'ErrorCount',
            statistic: 'Sum',
            period: Duration.minutes(5)
          })
        ]
      })
    );

    // Resource Usage Widgets
    dashboard.addWidgets(
      new cloudwatch.GraphWidget({
        title: 'CPU Usage',
        left: [
          new cloudwatch.Metric({
            namespace: 'AWS/EKS',
            metricName: 'pod_cpu_utilization',
            statistic: 'Average',
            period: Duration.minutes(5)
          })
        ]
      }),
      new cloudwatch.GraphWidget({
        title: 'Memory Usage',
        left: [
          new cloudwatch.Metric({
            namespace: 'AWS/EKS',
            metricName: 'pod_memory_utilization',
            statistic: 'Average',
            period: Duration.minutes(5)
          })
        ]
      })
    );

    return dashboard;
  }

  private enableContainerInsights(props: MonitoringStackProps): void {
    props.eksStack.cluster.addHelmChart('ContainerInsights', {
      chart: 'aws-cloudwatch-metrics',
      repository: 'https://aws.github.io/eks-charts',
      namespace: 'amazon-cloudwatch',
      values: {
        clusterName: props.eksStack.cluster.clusterName,
        serviceAccount: {
          create: true,
          name: 'cloudwatch-agent'
        }
      }
    });
  }
}