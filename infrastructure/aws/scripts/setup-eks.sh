#!/bin/bash

# EstateKit Documents API - EKS Cluster Setup Script
# Version: 1.0
# Required tools:
# - aws-cli v2.x
# - kubectl v1.27+
# - eksctl latest

set -euo pipefail

# Global variables
CLUSTER_NAME="estatekit-documents-cluster"
NODE_TYPE="t3.xlarge"
MIN_NODES="2"
MAX_NODES="10"
NAMESPACE="estatekit-documents"
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
LOG_FILE="/tmp/eks-setup-$(date +%Y%m%d-%H%M%S).log"

# Logging function
log() {
    local timestamp=$(date '+%Y-%m-%d %H:%M:%S')
    echo "[$timestamp] $1" | tee -a "$LOG_FILE"
}

error_exit() {
    log "ERROR: $1"
    exit 1
}

# Check prerequisites
check_prerequisites() {
    log "Checking prerequisites..."

    # Check AWS CLI
    if ! aws --version 2>&1 | grep -q "aws-cli/2"; then
        error_exit "AWS CLI v2 is required"
    fi

    # Check kubectl
    if ! kubectl version --client 2>&1 | grep -q "v1.27"; then
        error_exit "kubectl v1.27+ is required"
    }

    # Check eksctl
    if ! command -v eksctl &> /dev/null; then
        error_exit "eksctl is required"
    }

    # Check AWS credentials
    if ! aws sts get-caller-identity &> /dev/null; then
        error_exit "AWS credentials not configured"
    }

    # Check required environment variables
    [[ -z "${AWS_REGION:-}" ]] && error_exit "AWS_REGION environment variable is required"

    log "Prerequisites check completed successfully"
}

# Create EKS cluster
create_eks_cluster() {
    log "Creating EKS cluster: $CLUSTER_NAME"

    eksctl create cluster \
        --name "$CLUSTER_NAME" \
        --region "$AWS_REGION" \
        --version 1.27 \
        --node-type "$NODE_TYPE" \
        --nodes-min "$MIN_NODES" \
        --nodes-max "$MAX_NODES" \
        --with-oidc \
        --ssh-access \
        --ssh-public-key ~/.ssh/id_rsa.pub \
        --managed \
        --alb-ingress-access \
        --node-private-networking \
        --full-ecr-access \
        --asg-access \
        --verbose 3 \
        || error_exit "Failed to create EKS cluster"

    log "EKS cluster created successfully"
}

# Setup Kubernetes resources
setup_kubernetes_resources() {
    log "Setting up Kubernetes resources"

    # Create namespace
    kubectl create namespace "$NAMESPACE" || log "Namespace $NAMESPACE already exists"

    # Apply resource quotas
    cat <<EOF | kubectl apply -f -
apiVersion: v1
kind: ResourceQuota
metadata:
  name: compute-resources
  namespace: $NAMESPACE
spec:
  hard:
    requests.cpu: "4"
    requests.memory: 8Gi
    limits.cpu: "8"
    limits.memory: 16Gi
EOF

    # Apply network policies
    cat <<EOF | kubectl apply -f -
apiVersion: networking.k8s.io/v1
kind: NetworkPolicy
metadata:
  name: default-deny
  namespace: $NAMESPACE
spec:
  podSelector: {}
  policyTypes:
  - Ingress
  - Egress
EOF

    log "Kubernetes resources setup completed"
}

# Install cluster add-ons
install_cluster_addons() {
    log "Installing cluster add-ons"

    # Install metrics server
    kubectl apply -f https://github.com/kubernetes-sigs/metrics-server/releases/latest/download/components.yaml

    # Install AWS Load Balancer Controller
    eksctl create iamserviceaccount \
        --cluster="$CLUSTER_NAME" \
        --namespace=kube-system \
        --name=aws-load-balancer-controller \
        --attach-policy-arn=arn:aws:iam::aws:policy/AWSLoadBalancerControllerIAMPolicy \
        --override-existing-serviceaccounts \
        --approve

    helm repo add eks https://aws.github.io/eks-charts
    helm repo update
    helm install aws-load-balancer-controller eks/aws-load-balancer-controller \
        -n kube-system \
        --set clusterName="$CLUSTER_NAME" \
        --set serviceAccount.create=false \
        --set serviceAccount.name=aws-load-balancer-controller

    # Install Cluster Autoscaler
    eksctl create iamserviceaccount \
        --cluster="$CLUSTER_NAME" \
        --namespace=kube-system \
        --name=cluster-autoscaler \
        --attach-policy-arn=arn:aws:iam::aws:policy/AutoScalingFullAccess \
        --override-existing-serviceaccounts \
        --approve

    log "Cluster add-ons installed successfully"
}

# Configure monitoring
configure_monitoring() {
    log "Configuring monitoring and logging"

    # Enable control plane logging
    aws eks update-cluster-config \
        --region "$AWS_REGION" \
        --name "$CLUSTER_NAME" \
        --logging '{"clusterLogging":[{"types":["api","audit","authenticator","controllerManager","scheduler"],"enabled":true}]}'

    # Install CloudWatch agent
    kubectl apply -f https://raw.githubusercontent.com/aws-samples/amazon-cloudwatch-container-insights/latest/k8s-deployment-manifest-templates/deployment-mode/daemonset/container-insights-monitoring/cloudwatch-namespace.yaml
    kubectl apply -f https://raw.githubusercontent.com/aws-samples/amazon-cloudwatch-container-insights/latest/k8s-deployment-manifest-templates/deployment-mode/daemonset/container-insights-monitoring/cwagent/cwagent-serviceaccount.yaml
    kubectl apply -f https://raw.githubusercontent.com/aws-samples/amazon-cloudwatch-container-insights/latest/k8s-deployment-manifest-templates/deployment-mode/daemonset/container-insights-monitoring/cwagent/cwagent-configmap.yaml
    kubectl apply -f https://raw.githubusercontent.com/aws-samples/amazon-cloudwatch-container-insights/latest/k8s-deployment-manifest-templates/deployment-mode/daemonset/container-insights-monitoring/cwagent/cwagent-daemonset.yaml

    log "Monitoring and logging configured successfully"
}

# Verify cluster
verify_cluster() {
    log "Verifying cluster setup"

    # Check node status
    kubectl get nodes || error_exit "Failed to get nodes"

    # Check core components
    kubectl get pods -n kube-system || error_exit "Failed to get system pods"

    # Check metrics server
    kubectl get apiservice v1beta1.metrics.k8s.io || error_exit "Metrics server not running"

    # Check autoscaler
    kubectl get deployment cluster-autoscaler -n kube-system || log "Autoscaler not found"

    # Check load balancer controller
    kubectl get deployment aws-load-balancer-controller -n kube-system || log "Load balancer controller not found"

    log "Cluster verification completed successfully"
}

# Main execution
main() {
    log "Starting EKS cluster setup for EstateKit Documents API"

    check_prerequisites
    create_eks_cluster
    setup_kubernetes_resources
    install_cluster_addons
    configure_monitoring
    verify_cluster

    log "EKS cluster setup completed successfully"
}

# Execute main function
main "$@"