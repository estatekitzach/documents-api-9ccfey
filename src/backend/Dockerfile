# Build Stage
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Copy solution and project files first for layer caching
COPY ["src/EstateKit.Documents.Api/EstateKit.Documents.Api.csproj", "EstateKit.Documents.Api/"]
COPY ["src/EstateKit.Documents.Core/EstateKit.Documents.Core.csproj", "EstateKit.Documents.Core/"]
COPY ["src/EstateKit.Documents.Infrastructure/EstateKit.Documents.Infrastructure.csproj", "EstateKit.Documents.Infrastructure/"]

# Restore NuGet packages independently
RUN dotnet restore "EstateKit.Documents.Api/EstateKit.Documents.Api.csproj"

# Copy all source code
COPY src/. .

# Build and publish with optimizations
RUN dotnet build "EstateKit.Documents.Api/EstateKit.Documents.Api.csproj" -c Release -o /app/build \
    && dotnet publish "EstateKit.Documents.Api/EstateKit.Documents.Api.csproj" \
    -c Release \
    -o /app/publish \
    --no-restore \
    --self-contained true \
    /p:PublishTrimmed=true \
    /p:PublishSingleFile=true

# Production Stage
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS final

# Create non-root user and group
RUN groupadd -g 1000 estatekit && \
    useradd -u 1000 -g estatekit -s /bin/bash -m estatekit

# Set working directory
WORKDIR /app

# Copy published artifacts from build stage
COPY --from=build /app/publish .

# Set correct permissions
RUN chown -R estatekit:estatekit /app && \
    chmod -R 755 /app && \
    find /app -type f -exec chmod 644 {} \;

# Install curl for healthcheck
RUN apt-get update && \
    apt-get install -y --no-install-recommends curl && \
    rm -rf /var/lib/apt/lists/*

# Environment variables
ENV ASPNETCORE_ENVIRONMENT=Production \
    ASPNETCORE_URLS=http://+:80 \
    DOTNET_RUNNING_IN_CONTAINER=true \
    DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=true \
    TZ=UTC

# Configure health check
HEALTHCHECK --interval=30s --timeout=5s --start-period=10s --retries=3 \
    CMD curl -f http://localhost/health || exit 1

# Expose port
EXPOSE 80

# Switch to non-root user
USER estatekit

# Set entry point
ENTRYPOINT ["./EstateKit.Documents.Api"]

# Security labels
LABEL maintainer="EstateKit Development Team" \
      description="EstateKit Documents API" \
      security.capabilities="cap_net_bind_service=+ep" \
      org.opencontainers.image.source="https://github.com/estatekit/documents-api"