## Description

### Summary
<!-- Provide a brief description of the changes (minimum 50 characters) -->

### Motivation
<!-- Explain in detail why this change is needed -->

### Impact
<!-- Describe the impact on system components and dependencies -->

## Type of Change
<!-- Check all that apply -->
- [ ] New feature (non-breaking)
- [ ] Bug fix
- [ ] Security fix
- [ ] Performance improvement
- [ ] Documentation update
- [ ] Code refactoring
- [ ] Configuration change
- [ ] Breaking change
- [ ] CI/CD update

## Testing
<!-- Verify all testing requirements are met -->
- [ ] Minimum 80% code coverage requirement met

### Test Types
- [ ] Unit tests added/updated
- [ ] Integration tests added/updated
- [ ] Security tests performed
- [ ] Performance tests completed
- [ ] Load tests performed (if applicable)
- [ ] All CI checks passing

## Security Checklist
<!-- All security requirements must be satisfied -->
- [ ] No sensitive data or credentials exposed
- [ ] Authentication/Authorization properly implemented
- [ ] Input validation and sanitization added
- [ ] Security scanning passed (Snyk + OWASP ZAP)
- [ ] Encryption requirements met (AES-256)
- [ ] OWASP Top 10 vulnerabilities addressed
- [ ] Rate limiting implemented (if applicable)
- [ ] Audit logging added for security events

## Documentation
<!-- Ensure all documentation is updated -->
- [ ] API documentation updated (OpenAPI/Swagger)
- [ ] README.md updated
- [ ] Configuration examples added
- [ ] Architecture diagrams updated
- [ ] Security documentation updated
- [ ] Deployment guide updated
- [ ] Change log updated

## Deployment
<!-- Verify all deployment requirements are met -->
- [ ] Database migrations required and tested
- [ ] New environment variables added to all environments
- [ ] Infrastructure changes documented and tested
- [ ] Backward compatibility maintained
- [ ] Rollback plan documented
- [ ] Blue-green deployment configuration updated
- [ ] AWS service limits checked
- [ ] Monitoring and alerts configured

---
<!-- Validation rules -->
> - Description must be at least 50 characters
> - All required sections must be completed
> - Minimum two reviewers required (including one security reviewer for security changes)
> - Code coverage must meet minimum 80% requirement