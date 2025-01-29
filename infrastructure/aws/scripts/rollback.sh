#!/usr/bin/env bash

# AWS CLI v2.x
# kubectl latest
# kustomize v5.0.0

set -euo pipefail

# Environment-specific rollback timeouts (in seconds)
declare -A MAX_ROLLBACK_TIMES=(
    ["development"]=300  # 5 minutes
    ["staging"]=600      # 10 minutes
    ["production"]=900   # 15 minutes
)

# Default configuration
NAMESPACE=${NAMESPACE:-"estatekit-documents"}
DEPLOYMENT_NAME=${DEPLOYMENT_NAME:-"estatekit-documents-api"}
HEALTH_CHECK_INTERVAL=${HEALTH_CHECK_INTERVAL:-5}
HEALTH_CHECK_TIMEOUT=${HEALTH_CHECK_TIMEOUT:-60}

# Logging utilities
log_info() {
    echo "[INFO] $(date '+%Y-%m-%d %H:%M:%S') - $1"
}

log_error() {
    echo "[ERROR] $(date '+%Y-%m-%d %H:%M:%S') - $1" >&2
}

log_warning() {
    echo "[WARN] $(date '+%Y-%m-%d %H:%M:%S') - $1"
}

# Check prerequisites for rollback operation
check_prerequisites() {
    local required_vars=("ENVIRONMENT" "AWS_REGION" "CLUSTER_NAME" "ROLLBACK_VERSION")
    
    # Verify required environment variables
    for var in "${required_vars[@]}"; do
        if [[ -z "${!var:-}" ]]; then
            log_error "Required environment variable $var is not set"
            return 1
        fi
    done

    # Verify AWS CLI version
    if ! aws --version | grep -q "aws-cli/2"; then
        log_error "AWS CLI v2.x is required"
        return 1
    fi

    # Verify kubectl access
    if ! kubectl version --short > /dev/null 2>&1; then
        log_error "kubectl is not properly configured"
        return 1
    }

    # Verify kustomize installation
    if ! kustomize version | grep -q "v5.0.0"; then
        log_error "kustomize v5.0.0 is required"
        return 1
    }

    # Verify AWS credentials
    if ! aws sts get-caller-identity > /dev/null 2>&1; then
        log_error "Invalid AWS credentials"
        return 1
    }

    # Verify EKS cluster access
    if ! aws eks describe-cluster --name "$CLUSTER_NAME" --region "$AWS_REGION" > /dev/null 2>&1; then
        log_error "Cannot access EKS cluster $CLUSTER_NAME"
        return 1
    }

    return 0
}

# Validate rollback target version
validate_rollback_target() {
    local rollback_version=$1
    
    # Check if version exists in deployment history
    if ! kubectl rollout history deployment/"$DEPLOYMENT_NAME" -n "$NAMESPACE" | grep -q "$rollback_version"; then
        log_error "Rollback version $rollback_version not found in deployment history"
        return 1
    }

    # Verify previous health status
    if ! aws cloudwatch get-metric-statistics \
        --namespace EstateKit \
        --metric-name HealthStatus \
        --dimensions Name=Version,Value="$rollback_version" \
        --start-time "$(date -u -v-7d '+%Y-%m-%dT%H:%M:%SZ')" \
        --end-time "$(date -u '+%Y-%m-%dT%H:%M:%SZ')" \
        --period 3600 \
        --statistics Average \
        --region "$AWS_REGION" | grep -q '"Average": 1.0'; then
        log_warning "Target version $rollback_version has previous health issues"
    fi

    return 0
}

# Prepare for rollback operation
prepare_rollback() {
    local environment=$1
    local rollback_version=$2

    # Create backup of current state
    kubectl get deployment "$DEPLOYMENT_NAME" -n "$NAMESPACE" -o yaml > "backup_${DEPLOYMENT_NAME}_$(date +%Y%m%d_%H%M%S).yaml"

    # Scale down current deployment gradually
    local replicas
    replicas=$(kubectl get deployment "$DEPLOYMENT_NAME" -n "$NAMESPACE" -o jsonpath='{.spec.replicas}')
    
    for ((i=replicas-1; i>=replicas/2; i--)); do
        kubectl scale deployment "$DEPLOYMENT_NAME" -n "$NAMESPACE" --replicas="$i"
        sleep 5
    done

    # Initialize CloudWatch metrics for rollback monitoring
    aws cloudwatch put-metric-data \
        --namespace EstateKit \
        --metric-name RollbackStatus \
        --value 0 \
        --dimensions Environment="$environment",Version="$rollback_version" \
        --region "$AWS_REGION"

    return 0
}

# Execute rollback with zero-downtime
execute_rollback() {
    local start_time
    start_time=$(date +%s)
    local max_time=${MAX_ROLLBACK_TIMES[$ENVIRONMENT]}

    # Start rollback operation
    if ! kubectl rollout undo deployment/"$DEPLOYMENT_NAME" -n "$NAMESPACE" --to-revision="$ROLLBACK_VERSION"; then
        log_error "Failed to initiate rollback"
        return 1
    }

    # Monitor rollback progress
    while true; do
        local current_time
        current_time=$(date +%s)
        local elapsed=$((current_time - start_time))

        if [[ $elapsed -gt $max_time ]]; then
            log_error "Rollback exceeded maximum allowed time of $max_time seconds"
            return 1
        fi

        if kubectl rollout status deployment/"$DEPLOYMENT_NAME" -n "$NAMESPACE" --timeout=5s; then
            break
        fi

        sleep "$HEALTH_CHECK_INTERVAL"
    done

    return 0
}

# Validate rollback success
validate_rollback() {
    local failures=0

    # Check pod status
    if ! kubectl get pods -n "$NAMESPACE" -l app="$DEPLOYMENT_NAME" | grep -q "Running"; then
        log_error "Pods are not in Running state"
        ((failures++))
    fi

    # Verify health check endpoints
    local endpoints=("/health" "/ready" "/metrics")
    for endpoint in "${endpoints[@]}"; do
        if ! curl -sf "http://$DEPLOYMENT_NAME.$NAMESPACE.svc.cluster.local$endpoint" > /dev/null; then
            log_error "Health check failed for endpoint $endpoint"
            ((failures++))
        fi
    done

    # Check resource usage
    if ! kubectl top pod -n "$NAMESPACE" -l app="$DEPLOYMENT_NAME" > /dev/null; then
        log_warning "Unable to verify resource usage"
    fi

    return "$failures"
}

# Send monitoring notifications
notify_monitoring() {
    local status=$1
    local details=$2

    # Update CloudWatch metrics
    aws cloudwatch put-metric-data \
        --namespace EstateKit \
        --metric-name RollbackStatus \
        --value "$([[ $status -eq 0 ]] && echo 1 || echo 0)" \
        --dimensions Environment="$ENVIRONMENT",Version="$ROLLBACK_VERSION" \
        --region "$AWS_REGION"

    # Log detailed event
    aws logs put-log-events \
        --log-group-name "/estatekit/rollback" \
        --log-stream-name "$ENVIRONMENT" \
        --log-events timestamp="$(date +%s)000",message="$details" \
        --region "$AWS_REGION"

    return 0
}

# Main execution
main() {
    log_info "Starting rollback operation for $DEPLOYMENT_NAME in $ENVIRONMENT environment"

    if ! check_prerequisites; then
        log_error "Prerequisites check failed"
        exit 1
    fi

    if ! validate_rollback_target "$ROLLBACK_VERSION"; then
        log_error "Rollback target validation failed"
        exit 1
    }

    if ! prepare_rollback "$ENVIRONMENT" "$ROLLBACK_VERSION"; then
        log_error "Rollback preparation failed"
        exit 1
    }

    if ! execute_rollback; then
        log_error "Rollback execution failed"
        notify_monitoring 1 "Rollback failed during execution"
        exit 1
    fi

    if ! validate_rollback; then
        log_error "Rollback validation failed"
        notify_monitoring 1 "Rollback failed validation"
        exit 1
    }

    log_info "Rollback completed successfully"
    notify_monitoring 0 "Rollback completed successfully"
    exit 0
}

main "$@"