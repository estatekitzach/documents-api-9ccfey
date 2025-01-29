#!/bin/bash

# EstateKit Documents API - Monitoring Infrastructure Setup Script
# Version: 1.0.0
# Dependencies: aws-cli >= 2.0.0, kubectl >= 1.27.0, jq >= 1.6

set -euo pipefail

# Global Variables
readonly SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
readonly TIMESTAMP=$(date +%Y%m%d_%H%M%S)
readonly LOG_FILE="/tmp/estatekit_monitoring_setup_${TIMESTAMP}.log"
readonly REQUIRED_VARS=("AWS_REGION" "ENVIRONMENT" "CLUSTER_NAME" "AWS_PROFILE" "SNS_TOPIC_ARN" "DASHBOARD_NAME")
readonly LOG_RETENTION_DAYS=2555  # 7 years for compliance
readonly METRIC_NAMESPACE="EstateKit/DocumentsAPI"
readonly KMS_KEY_ALIAS="alias/estatekit-monitoring"

# Logging functions
log_info() {
    echo "[INFO] [$(date +'%Y-%m-%d %H:%M:%S')] $*" | tee -a "${LOG_FILE}"
}

log_error() {
    echo "[ERROR] [$(date +'%Y-%m-%d %H:%M:%S')] $*" >&2 | tee -a "${LOG_FILE}"
}

# Validation function
validate_prerequisites() {
    log_info "Validating prerequisites..."
    
    # Check required environment variables
    for var in "${REQUIRED_VARS[@]}"; do
        if [[ -z "${!var:-}" ]]; then
            log_error "Required environment variable $var is not set"
            return 7
        fi
    done
    
    # Check AWS CLI
    if ! aws --version >/dev/null 2>&1; then
        log_error "AWS CLI is not installed or not in PATH"
        return 2
    fi
    
    # Check kubectl
    if ! kubectl version --client >/dev/null 2>&1; then
        log_error "kubectl is not installed or not in PATH"
        return 3
    }
    
    # Check jq
    if ! jq --version >/dev/null 2>&1; then
        log_error "jq is not installed or not in PATH"
        return 4
    }
    
    return 0
}

setup_log_groups() {
    local environment="$1"
    local kms_key_id="$2"
    
    log_info "Setting up CloudWatch log groups..."
    
    local log_groups=(
        "/estatekit/documents-api/${environment}"
        "/estatekit/documents-api/${environment}/processing"
        "/estatekit/documents-api/${environment}/storage"
        "/estatekit/documents-api/${environment}/auth"
    )
    
    for log_group in "${log_groups[@]}"; do
        aws logs create-log-group \
            --log-group-name "${log_group}" \
            --kms-key-id "${kms_key_id}" \
            --tags "Environment=${environment},Service=DocumentsAPI,Compliance=Required" || true
            
        aws logs put-retention-policy \
            --log-group-name "${log_group}" \
            --retention-in-days "${LOG_RETENTION_DAYS}"
    done
    
    return 0
}

setup_metrics() {
    local environment="$1"
    
    log_info "Setting up CloudWatch metrics..."
    
    # API Latency Metric Filter
    aws logs put-metric-filter \
        --log-group-name "/estatekit/documents-api/${environment}" \
        --filter-name "APILatency" \
        --filter-pattern "[timestamp, requestId, duration]" \
        --metric-transformations \
            metricName=APILatency,metricNamespace="${METRIC_NAMESPACE}",metricValue=$duration,defaultValue=0
    
    # Error Rate Metric Filter
    aws logs put-metric-filter \
        --log-group-name "/estatekit/documents-api/${environment}" \
        --filter-name "ErrorRate" \
        --filter-pattern "[timestamp, requestId, level=ERROR]" \
        --metric-transformations \
            metricName=ErrorCount,metricNamespace="${METRIC_NAMESPACE}",metricValue=1,defaultValue=0
    
    # Storage Operations Metric Filter
    aws logs put-metric-filter \
        --log-group-name "/estatekit/documents-api/${environment}/storage" \
        --filter-name "StorageOperations" \
        --filter-pattern "[timestamp, operation, status]" \
        --metric-transformations \
            metricName=StorageOperations,metricNamespace="${METRIC_NAMESPACE}",metricValue=1,defaultValue=0
    
    return 0
}

setup_alarms() {
    local environment="$1"
    local sns_topic_arn="$2"
    
    log_info "Setting up CloudWatch alarms..."
    
    # API Latency Alarm
    aws cloudwatch put-metric-alarm \
        --alarm-name "${environment}-APILatencyHigh" \
        --alarm-description "API Latency exceeds 1000ms" \
        --metric-name "APILatency" \
        --namespace "${METRIC_NAMESPACE}" \
        --statistic "Average" \
        --period 60 \
        --evaluation-periods 3 \
        --threshold 1000 \
        --comparison-operator "GreaterThanThreshold" \
        --alarm-actions "${sns_topic_arn}"
    
    # Error Rate Alarm
    aws cloudwatch put-metric-alarm \
        --alarm-name "${environment}-ErrorRateHigh" \
        --alarm-description "Error rate exceeds 1%" \
        --metric-name "ErrorCount" \
        --namespace "${METRIC_NAMESPACE}" \
        --statistic "Sum" \
        --period 300 \
        --evaluation-periods 2 \
        --threshold 1 \
        --comparison-operator "GreaterThanThreshold" \
        --alarm-actions "${sns_topic_arn}"
    
    # CPU Usage Alarm
    aws cloudwatch put-metric-alarm \
        --alarm-name "${environment}-CPUUsageHigh" \
        --alarm-description "CPU Usage exceeds 85%" \
        --metric-name "CPUUtilization" \
        --namespace "AWS/EKS" \
        --dimensions Name=ClusterName,Value="${CLUSTER_NAME}" \
        --statistic "Average" \
        --period 300 \
        --evaluation-periods 5 \
        --threshold 85 \
        --comparison-operator "GreaterThanThreshold" \
        --alarm-actions "${sns_topic_arn}"
    
    return 0
}

setup_dashboard() {
    local environment="$1"
    local dashboard_name="$2"
    
    log_info "Setting up CloudWatch dashboard..."
    
    # Create dashboard JSON
    local dashboard_json=$(cat <<EOF
{
    "widgets": [
        {
            "type": "metric",
            "properties": {
                "metrics": [
                    ["${METRIC_NAMESPACE}", "APILatency", "Environment", "${environment}"]
                ],
                "period": 60,
                "stat": "Average",
                "region": "${AWS_REGION}",
                "title": "API Latency"
            }
        },
        {
            "type": "metric",
            "properties": {
                "metrics": [
                    ["${METRIC_NAMESPACE}", "ErrorCount", "Environment", "${environment}"]
                ],
                "period": 300,
                "stat": "Sum",
                "region": "${AWS_REGION}",
                "title": "Error Rate"
            }
        },
        {
            "type": "metric",
            "properties": {
                "metrics": [
                    ["AWS/EKS", "CPUUtilization", "ClusterName", "${CLUSTER_NAME}"]
                ],
                "period": 300,
                "stat": "Average",
                "region": "${AWS_REGION}",
                "title": "CPU Usage"
            }
        }
    ]
}
EOF
)

    aws cloudwatch put-dashboard \
        --dashboard-name "${dashboard_name}" \
        --dashboard-body "${dashboard_json}"
        
    return 0
}

enable_container_insights() {
    local cluster_name="$1"
    local environment="$2"
    
    log_info "Enabling Container Insights..."
    
    # Enable Container Insights
    kubectl apply -f - <<EOF
apiVersion: v1
kind: Namespace
metadata:
  name: amazon-cloudwatch
---
apiVersion: v1
kind: ServiceAccount
metadata:
  name: cloudwatch-agent
  namespace: amazon-cloudwatch
---
apiVersion: apps/v1
kind: DaemonSet
metadata:
  name: cloudwatch-agent
  namespace: amazon-cloudwatch
spec:
  selector:
    matchLabels:
      name: cloudwatch-agent
  template:
    metadata:
      labels:
        name: cloudwatch-agent
    spec:
      serviceAccountName: cloudwatch-agent
      containers:
        - name: cloudwatch-agent
          image: amazon/cloudwatch-agent:latest
          resources:
            limits:
              cpu: 200m
              memory: 200Mi
            requests:
              cpu: 200m
              memory: 200Mi
EOF

    # Enable CloudWatch metrics
    aws eks update-cluster-config \
        --name "${cluster_name}" \
        --region "${AWS_REGION}" \
        --logging '{"clusterLogging":[{"types":["api","audit","authenticator","controllerManager","scheduler"],"enabled":true}]}'
        
    return 0
}

main() {
    log_info "Starting monitoring setup for EstateKit Documents API..."
    
    # Validate prerequisites
    validate_prerequisites || return $?
    
    # Create KMS key for log encryption
    local kms_key_id=$(aws kms create-key --description "EstateKit Monitoring Encryption Key" --query 'KeyMetadata.KeyId' --output text)
    aws kms create-alias --alias-name "${KMS_KEY_ALIAS}" --target-key-id "${kms_key_id}"
    
    # Setup monitoring components
    setup_log_groups "${ENVIRONMENT}" "${kms_key_id}" || return $?
    setup_metrics "${ENVIRONMENT}" || return $?
    setup_alarms "${ENVIRONMENT}" "${SNS_TOPIC_ARN}" || return $?
    setup_dashboard "${ENVIRONMENT}" "${DASHBOARD_NAME}" || return $?
    enable_container_insights "${CLUSTER_NAME}" "${ENVIRONMENT}" || return $?
    
    log_info "Monitoring setup completed successfully"
    log_info "Setup log file: ${LOG_FILE}"
    return 0
}

# Execute main function
main "$@"