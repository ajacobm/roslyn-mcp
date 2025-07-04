# Docker Setup for Roslyn MCP Server

This document provides instructions for building and running the Roslyn MCP Server using Docker.

## Quick Start

### Build and Run with Docker Compose
```bash
# Build and start the MCP server
docker-compose up --build

# Run in detached mode
docker-compose up -d --build

# View logs
docker-compose logs -f roslyn-mcp
```

### Build and Run with Docker
```bash
# Build the image
docker build -t roslyn-mcp:latest .

# Run the container
docker run -it --rm \
  --name roslyn-mcp \
  -v $(pwd)/workspace:/workspace:ro \
  roslyn-mcp:latest
```

## Development Setup

### Development Mode with Docker Compose
```bash
# Run development service with source code mounted
docker-compose --profile dev up roslyn-mcp-dev

# This allows you to make changes to the source code
# and run the application without rebuilding the image
```

### Manual Development Setup
```bash
# Build development image
docker build --target build -t roslyn-mcp:dev .

# Run with source mounted
docker run -it --rm \
  --name roslyn-mcp-dev \
  -v $(pwd):/src \
  -w /src \
  roslyn-mcp:dev \
  dotnet run --project RoslynMCP/RoslynMCP.csproj
```

## Usage Examples

### Analyzing External C# Projects
```bash
# Create a workspace directory with your C# projects
mkdir -p workspace
cp -r /path/to/your/csharp/project workspace/

# Run the container with the workspace mounted
docker-compose up roslyn-mcp
```

### Using as MCP Server
The container is designed to work as an MCP server via stdin/stdout. You can integrate it with MCP clients by configuring them to use the containerized server:

```json
{
  "servers": {
    "RoslynMCP": {
      "type": "stdio",
      "command": "docker",
      "args": [
        "run", "-i", "--rm",
        "-v", "/path/to/your/projects:/workspace:ro",
        "roslyn-mcp:latest"
      ]
    }
  }
}
```

## Configuration

### Environment Variables
- `DOTNET_CLI_TELEMETRY_OPTOUT=1` - Disables .NET telemetry
- `DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1` - Skips first-time setup
- `DOTNET_NOLOGO=1` - Disables .NET logo display
- `NUGET_PACKAGES=/home/mcpuser/.nuget/packages` - NuGet packages location

### Volume Mounts
- `/workspace` - Mount your C# projects here for analysis (read-only)
- `/home/mcpuser/.nuget/packages` - NuGet packages cache (persistent)

### Resource Limits
The default configuration sets:
- Memory limit: 1GB
- CPU limit: 0.5 cores
- Memory reservation: 256MB
- CPU reservation: 0.1 cores

Adjust these in `docker-compose.yml` based on your needs.

## Troubleshooting

### Common Issues

#### MSBuild Not Found
If you encounter MSBuild-related errors, ensure you're using the full SDK image in the runtime stage (which we do by default).

#### Permission Issues
The container runs as a non-root user (`mcpuser`). If you encounter permission issues with mounted volumes:

```bash
# Fix ownership of workspace directory
sudo chown -R 1000:1000 workspace/
```

#### Memory Issues
If the container runs out of memory during analysis of large projects:

```bash
# Increase memory limit in docker-compose.yml
deploy:
  resources:
    limits:
      memory: 2G  # Increase from 1G
```

### Debugging

#### View Container Logs
```bash
docker-compose logs -f roslyn-mcp
```

#### Interactive Shell
```bash
# Get shell access to running container
docker-compose exec roslyn-mcp /bin/bash

# Or run a new container with shell
docker run -it --rm roslyn-mcp:latest /bin/bash
```

#### Check Health Status
```bash
docker-compose ps
# Look for "healthy" status
```

## Building for Production

### Multi-Architecture Build
```bash
# Build for multiple architectures
docker buildx build --platform linux/amd64,linux/arm64 -t roslyn-mcp:latest .
```

### Optimized Production Build
```bash
# Build with specific optimizations
docker build \
  --build-arg DOTNET_CONFIGURATION=Release \
  --build-arg DOTNET_VERBOSITY=minimal \
  -t roslyn-mcp:prod .
```

## Security Considerations

- The container runs as a non-root user (`mcpuser`)
- Workspace is mounted read-only by default
- No network ports are exposed (stdin/stdout communication only)
- Minimal attack surface with only necessary dependencies

## Performance Tips

1. **Use volume for NuGet cache**: The docker-compose setup includes a persistent volume for NuGet packages to avoid re-downloading on each run.

2. **Mount only necessary directories**: Only mount the specific C# projects you need to analyze.

3. **Adjust resource limits**: Tune memory and CPU limits based on your project size and analysis needs.

4. **Use multi-stage builds**: The Dockerfile uses multi-stage builds to minimize the final image size.
