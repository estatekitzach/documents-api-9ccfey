# EstateKit Documents API

[![Build Status](https://github.com/estatekit/documents-api/workflows/CI/badge.svg)](https://github.com/estatekit/documents-api/actions)
[![Code Coverage](https://codecov.io/gh/estatekit/documents-api/branch/main/graph/badge.svg)](https://codecov.io/gh/estatekit/documents-api)
[![Security Scan](https://snyk.io/test/github/estatekit/documents-api/badge.svg)](https://snyk.io/test/github/estatekit/documents-api)

## Overview

EstateKit Documents API is a secure document management system designed to provide document storage, analysis, and lifecycle management capabilities for the EstateKit application ecosystem. The system leverages AWS cloud services to deliver enterprise-grade document processing capabilities with a focus on security, scalability, and compliance.

### Key Features

- Secure document storage with AES-256 encryption
- Automated document analysis using AWS Textract
- OAuth 2.0 authentication via AWS Cognito
- Comprehensive document lifecycle management
- Multi-region high availability deployment
- Enterprise-grade security and compliance measures

## Architecture

The system is built using a microservices architecture deployed on AWS EKS with the following key components:

- **API Layer**: .NET Core 9.0 REST API
- **Storage**: AWS S3 with cross-region replication
- **Analysis**: AWS Textract for document processing
- **Authentication**: AWS Cognito OAuth service
- **Monitoring**: AWS CloudWatch with custom metrics
- **Security**: AWS KMS for key management

## Prerequisites

### Development Environment
- .NET Core 9.0 SDK
- Docker Desktop (latest version)
- AWS CLI v2+
- Visual Studio 2024+
- Git

### AWS Services
- AWS Account with appropriate IAM permissions
- Configured S3 buckets
- Cognito user pool
- Textract service access
- KMS key permissions

### Infrastructure
- Kubernetes cluster access
- Container registry access
- CI/CD pipeline permissions

## Getting Started

### Local Development Setup

1. Clone the repository:
```bash
git clone https://github.com/estatekit/documents-api.git
cd documents-api
```

2. Configure environment variables:
```bash
cp .env.example .env
# Edit .env with your configuration
```

3. Start local development environment:
```bash
docker-compose up -d
```

4. Verify the setup:
```bash
curl http://localhost:5000/health
```

### AWS Configuration

1. Configure AWS credentials:
```bash
aws configure
```

2. Set up required AWS services:
```bash
./scripts/setup-aws-services.sh
```

3. Verify AWS connectivity:
```bash
./scripts/verify-aws-setup.sh
```

## Development

### Workflow

1. Create feature branch:
```bash
git checkout -b feature/your-feature-name
```

2. Implement changes following code standards
3. Run tests:
```bash
dotnet test
```

4. Create pull request following PR template

### Tools Configuration

- **IDE Setup**: Use provided `.editorconfig`
- **Debug Profile**: Configure using `Properties/launchSettings.json`
- **Test Environment**: Use `docker-compose.override.yml`

## API Documentation

### Endpoints

#### Document Upload
```http
POST /v1/documents/upload
Content-Type: multipart/form-data
Authorization: Bearer <token>
```

#### Document Analysis
```http
POST /v1/documents/analyze
Content-Type: application/json
Authorization: Bearer <token>
```

Complete API documentation available at `/swagger` endpoint when running locally.

## Security

### Features

- TLS 1.3 transport encryption
- AES-256 storage encryption
- OAuth 2.0 authentication
- Role-based access control
- Audit logging
- Compliance with financial regulations

### Best Practices

- Regular security scanning
- Automated vulnerability assessment
- Key rotation policies
- Incident response procedures

## Monitoring

### CloudWatch Setup

1. Configure metrics collection
2. Set up log groups
3. Define alert thresholds
4. Configure dashboards

### Key Metrics

- API response times
- Error rates
- Storage utilization
- Processing queue length
- Security events

## Troubleshooting

### Common Issues

1. Authentication Failures
   - Verify OAuth token validity
   - Check Cognito configuration
   - Validate IAM permissions

2. Upload Issues
   - Confirm file size limits
   - Verify S3 bucket permissions
   - Check network connectivity

3. Analysis Errors
   - Validate document format
   - Check Textract service status
   - Review error logs

## Contributing

### Guidelines

1. Follow C# coding standards
2. Maintain test coverage above 80%
3. Update documentation
4. Include security considerations

### Pull Request Process

1. Create feature branch
2. Implement changes
3. Add tests
4. Update documentation
5. Submit PR

## License

Copyright © 2024 EstateKit

Licensed under the Apache License, Version 2.0 (the "License");
you may not use this file except in compliance with the License.
You may obtain a copy of the License at

    http://www.apache.org/licenses/LICENSE-2.0

Unless required by applicable law or agreed to in writing, software
distributed under the License is distributed on an "AS IS" BASIS,
WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
See the License for the specific language governing permissions and
limitations under the License.