#!/usr/bin/env bash

# EstateKit Documents API Deployment Script
# Version: 1.0.0
# AWS CLI Version: 2.x
# kubectl Version: latest
# kustomize Version: v5.0.0

set -euo pipefail
IFS=$'\n\t'

# Default environment variables
ENVIRONMENT=${ENVIRONMENT:-"development"}
AWS_REGION=${AWS_REGION:-"us-west-2"}
CLUSTER_NAME=${CLUSTER_NAME:-"estatekit-documents-${ENVIRONMENT}"}
NAMESPACE=${NAMESPACE:-"estatekit-documents"}
DEPLOYMENT_NAME=${DEPLOYMENT_NAME:-"estatekit-documents-api"}
HEALTH_CHECK_ENDPOINT=${HEALTH_CHECK_ENDPOINT:-"/v1/health"}
LOG_GROUP=${LOG_GROUP:-"/aws/eks/${CLUSTER_NAME}/deployments"}

# Environment-specific configurations
case "${ENVIRONMENT}" in
    "development")
        DEPLOYMENT_TIMEOUT="5m"
        MIN_REPLICAS=2
        MAX_REPLICAS=4
        ;;
    "staging")
        DEPLOYMENT_TIMEOUT="10m"
        MIN_REPLICAS=3
        MAX_REPLICAS=6
        ;;
    "production")
        DEPLOYMENT_TIMEOUT="15m"
        MIN_REPLICAS=4
        MAX_REPLICAS=8
        ;;
    *)
        echo "Error: Invalid environment specified"
        exit 1
        ;;
esac

# Logging configuration
setup_logging() {
    local timestamp=$(date +%Y-%m-%d-%H-%M-%S)
    local log_stream="deployment-${timestamp}"
    
    # Create CloudWatch log stream
    aws logs create-log-stream \
        --log-group-name "${LOG_GROUP}" \
        --log-stream-name "${log_stream}" \
        --region "${AWS_REGION}" || true

    export LOG_STREAM="${log_stream}"
}

log_message() {
    local level=$1
    local message=$2
    local timestamp=$(date -u +"%Y-%m-%dT%H:%M:%SZ")
    
    # Log to CloudWatch
    aws logs put-log-events \
        --log-group-name "${LOG_GROUP}" \
        --log-stream-name "${LOG_STREAM}" \
        --log-events timestamp=$(date +%s000),message="[${level}] ${message}" \
        --region "${AWS_REGION}" || true
    
    # Log to stdout
    echo "[${timestamp}] [${level}] ${message}"
}

check_prerequisites() {
    log_message "INFO" "Checking deployment prerequisites..."

    # Check AWS CLI version
    if ! aws --version | grep -q "aws-cli/2"; then
        log_message "ERROR" "AWS CLI version 2.x is required"
        return 1
    }

    # Verify AWS authentication
    if ! aws sts get-caller-identity &>/dev/null; then
        log_message "ERROR" "AWS authentication failed"
        return 1
    }

    # Check kubectl installation
    if ! command -v kubectl &>/dev/null; then
        log_message "ERROR" "kubectl is not installed"
        return 1
    }

    # Check kustomize installation
    if ! command -v kustomize &>/dev/null; then
        log_message "ERROR" "kustomize is not installed"
        return 1
    }

    # Validate EKS cluster access
    if ! aws eks describe-cluster --name "${CLUSTER_NAME}" --region "${AWS_REGION}" &>/dev/null; then
        log_message "ERROR" "Cannot access EKS cluster: ${CLUSTER_NAME}"
        return 1
    }

    # Update kubeconfig
    aws eks update-kubeconfig --name "${CLUSTER_NAME}" --region "${AWS_REGION}"

    # Verify namespace
    if ! kubectl get namespace "${NAMESPACE}" &>/dev/null; then
        log_message "INFO" "Creating namespace: ${NAMESPACE}"
        kubectl create namespace "${NAMESPACE}"
    }

    log_message "INFO" "Prerequisites check completed successfully"
    return 0
}

prepare_deployment() {
    local deployment_id=$(date +%s)
    log_message "INFO" "Preparing deployment configuration..."

    # Generate deployment labels
    export BLUE_LABEL="deployment=${DEPLOYMENT_NAME}-blue-${deployment_id}"
    export GREEN_LABEL="deployment=${DEPLOYMENT_NAME}-green-${deployment_id}"

    # Prepare kustomize overlay
    cd "infrastructure/aws/kubernetes/overlays/${ENVIRONMENT}"
    
    # Update deployment configuration
    kustomize edit set namespace "${NAMESPACE}"
    kustomize edit set image "estatekit-documents-api=${ECR_REPOSITORY}:${IMAGE_TAG}"

    # Validate kustomize build
    if ! kustomize build .; then
        log_message "ERROR" "Kustomize build validation failed"
        return 1
    }

    log_message "INFO" "Deployment preparation completed"
    return 0
}

deploy_application() {
    log_message "INFO" "Starting blue-green deployment..."

    # Apply new deployment (blue)
    if ! kustomize build . | kubectl apply -f -; then
        log_message "ERROR" "Failed to apply new deployment"
        return 1
    }

    # Wait for deployment rollout
    if ! kubectl rollout status deployment/${DEPLOYMENT_NAME} -n "${NAMESPACE}" --timeout="${DEPLOYMENT_TIMEOUT}"; then
        log_message "ERROR" "Deployment rollout failed"
        rollback_deployment
        return 1
    }

    # Validate deployment
    if ! validate_deployment; then
        log_message "ERROR" "Deployment validation failed"
        rollback_deployment
        return 1
    }

    # Switch traffic to new deployment
    if ! kubectl patch service ${DEPLOYMENT_NAME} -n "${NAMESPACE}" -p "{\"spec\":{\"selector\":${BLUE_LABEL}}}"; then
        log_message "ERROR" "Failed to switch traffic to new deployment"
        rollback_deployment
        return 1
    }

    log_message "INFO" "Deployment completed successfully"
    return 0
}

validate_deployment() {
    log_message "INFO" "Validating deployment..."

    # Check pod status
    local ready_pods=$(kubectl get pods -n "${NAMESPACE}" -l "${BLUE_LABEL}" -o jsonpath='{.items[*].status.containerStatuses[*].ready}' | tr ' ' '\n' | grep -c "true")
    local total_pods=$(kubectl get pods -n "${NAMESPACE}" -l "${BLUE_LABEL}" -o jsonpath='{.items[*].status.containerStatuses[*].ready}' | tr ' ' '\n' | wc -l)

    if [ "${ready_pods}" -ne "${total_pods}" ]; then
        log_message "ERROR" "Not all pods are ready: ${ready_pods}/${total_pods}"
        return 1
    }

    # Health check
    local endpoint="http://${DEPLOYMENT_NAME}.${NAMESPACE}:80${HEALTH_CHECK_ENDPOINT}"
    if ! curl -s -f "${endpoint}" &>/dev/null; then
        log_message "ERROR" "Health check failed: ${endpoint}"
        return 1
    }

    # Check resource usage
    if ! kubectl top pods -n "${NAMESPACE}" -l "${BLUE_LABEL}" &>/dev/null; then
        log_message "WARNING" "Unable to verify resource usage"
    }

    log_message "INFO" "Deployment validation successful"
    return 0
}

rollback_deployment() {
    log_message "WARNING" "Initiating deployment rollback..."

    # Switch back to previous deployment
    if kubectl patch service ${DEPLOYMENT_NAME} -n "${NAMESPACE}" -p "{\"spec\":{\"selector\":${GREEN_LABEL}}}" &>/dev/null; then
        log_message "INFO" "Traffic switched back to previous deployment"
    fi

    # Remove failed deployment
    if kubectl delete deployment -n "${NAMESPACE}" -l "${BLUE_LABEL}" &>/dev/null; then
        log_message "INFO" "Cleaned up failed deployment"
    fi

    log_message "INFO" "Rollback completed"
    return 0
}

cleanup() {
    log_message "INFO" "Performing deployment cleanup..."

    # Remove old deployments
    if kubectl get deployment -n "${NAMESPACE}" -l "${GREEN_LABEL}" &>/dev/null; then
        kubectl delete deployment -n "${NAMESPACE}" -l "${GREEN_LABEL}"
        log_message "INFO" "Removed old deployment"
    fi

    log_message "INFO" "Cleanup completed"
}

main() {
    setup_logging

    log_message "INFO" "Starting deployment process for ${ENVIRONMENT} environment"

    if ! check_prerequisites; then
        log_message "ERROR" "Prerequisites check failed"
        exit 1
    fi

    if ! prepare_deployment; then
        log_message "ERROR" "Deployment preparation failed"
        exit 1
    fi

    if ! deploy_application; then
        log_message "ERROR" "Deployment failed"
        exit 1
    fi

    cleanup
    log_message "INFO" "Deployment process completed successfully"
}

main "$@"