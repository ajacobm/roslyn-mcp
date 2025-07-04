# Multi-stage Dockerfile for Roslyn MCP Server
# Stage 1: Build the application
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build

# Set working directory
WORKDIR /src

# Copy project files
COPY RoslynMCP.sln ./
COPY RoslynMCP/RoslynMCP.csproj ./RoslynMCP/

# Restore dependencies
RUN dotnet restore RoslynMCP/RoslynMCP.csproj

# Copy source code
COPY RoslynMCP/ ./RoslynMCP/

# Build the application
RUN dotnet build RoslynMCP/RoslynMCP.csproj -c Release --no-restore

# Publish the application
RUN dotnet publish RoslynMCP/RoslynMCP.csproj -c Release --no-build -o /app/publish

# Stage 2: Runtime image with MSBuild support
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS runtime

# Install additional dependencies that might be needed
RUN apt-get update && apt-get install -y \
    git \
    && rm -rf /var/lib/apt/lists/*

# Create a non-root user
RUN groupadd -r mcpuser && useradd -r -g mcpuser mcpuser

# Set working directory
WORKDIR /app

# Copy the published application
COPY --from=build /app/publish .

# Create directories for NuGet packages and ensure proper permissions
RUN mkdir -p /home/mcpuser/.nuget/packages && \
    chown -R mcpuser:mcpuser /home/mcpuser && \
    chown -R mcpuser:mcpuser /app

# Set environment variables
ENV DOTNET_CLI_TELEMETRY_OPTOUT=1
ENV DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1
ENV NUGET_PACKAGES=/home/mcpuser/.nuget/packages
ENV DOTNET_NOLOGO=1

# Switch to non-root user
USER mcpuser

# Set the entry point
ENTRYPOINT ["dotnet", "RoslynMCP.dll"]

# Health check (optional - checks if the process is running)
HEALTHCHECK --interval=30s --timeout=10s --start-period=5s --retries=3 \
    CMD pgrep -f "dotnet.*RoslynMCP" > /dev/null || exit 1

# Labels for metadata
LABEL maintainer="Roslyn MCP Server" \
      description="Model Context Protocol server for C# code analysis using Roslyn" \
      version="1.0"
