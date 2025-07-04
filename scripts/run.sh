#!/bin/bash

# Run script for Roslyn MCP Server Docker container
set -e

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
NC='\033[0m' # No Color

# Default values
IMAGE_NAME="roslyn-mcp:latest"
CONTAINER_NAME="roslyn-mcp"
WORKSPACE_DIR=""
DETACHED=false
INTERACTIVE=true
REMOVE=true
DEV_MODE=false

# Function to display usage
usage() {
    echo "Usage: $0 [OPTIONS]"
    echo "Run Roslyn MCP Server Docker container"
    echo ""
    echo "Options:"
    echo "  -i, --image IMAGE      Docker image to run (default: roslyn-mcp:latest)"
    echo "  -n, --name NAME        Container name (default: roslyn-mcp)"
    echo "  -w, --workspace DIR    Workspace directory to mount"
    echo "  -d, --detached         Run in detached mode"
    echo "  -k, --keep             Keep container after exit (don't use --rm)"
    echo "  -b, --background       Run non-interactively"
    echo "  -v, --dev              Run in development mode"
    echo "  -h, --help             Show this help message"
    echo ""
    echo "Examples:"
    echo "  $0                                    # Run with defaults"
    echo "  $0 -w /path/to/csharp/projects       # Mount workspace"
    echo "  $0 -d                                # Run detached"
    echo "  $0 -v                                # Development mode"
    echo "  $0 -i roslyn-mcp:v1.0 -n my-server  # Custom image and name"
}

# Parse command line arguments
while [[ $# -gt 0 ]]; do
    case $1 in
        -i|--image)
            IMAGE_NAME="$2"
            shift 2
            ;;
        -n|--name)
            CONTAINER_NAME="$2"
            shift 2
            ;;
        -w|--workspace)
            WORKSPACE_DIR="$2"
            shift 2
            ;;
        -d|--detached)
            DETACHED=true
            shift
            ;;
        -k|--keep)
            REMOVE=false
            shift
            ;;
        -b|--background)
            INTERACTIVE=false
            shift
            ;;
        -v|--dev)
            DEV_MODE=true
            IMAGE_NAME="roslyn-mcp:dev"
            shift
            ;;
        -h|--help)
            usage
            exit 0
            ;;
        *)
            echo -e "${RED}Unknown option: $1${NC}"
            usage
            exit 1
            ;;
    esac
done

echo -e "${YELLOW}Starting Roslyn MCP Server container...${NC}"

# Change to project root directory
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(dirname "$SCRIPT_DIR")"
cd "$PROJECT_ROOT"

# Build docker run command
RUN_CMD="docker run"

# Add flags
if [[ "$REMOVE" == true ]]; then
    RUN_CMD="$RUN_CMD --rm"
fi

if [[ "$INTERACTIVE" == true ]]; then
    RUN_CMD="$RUN_CMD -it"
fi

if [[ "$DETACHED" == true ]]; then
    RUN_CMD="$RUN_CMD -d"
fi

# Add container name
RUN_CMD="$RUN_CMD --name $CONTAINER_NAME"

# Add volume mounts
if [[ -n "$WORKSPACE_DIR" ]]; then
    if [[ ! -d "$WORKSPACE_DIR" ]]; then
        echo -e "${RED}Error: Workspace directory does not exist: $WORKSPACE_DIR${NC}"
        exit 1
    fi
    WORKSPACE_DIR=$(realpath "$WORKSPACE_DIR")
    RUN_CMD="$RUN_CMD -v $WORKSPACE_DIR:/workspace:ro"
    echo -e "${BLUE}Mounting workspace: $WORKSPACE_DIR -> /workspace${NC}"
fi

# Add NuGet cache volume
RUN_CMD="$RUN_CMD -v roslyn-mcp-nuget:/home/mcpuser/.nuget/packages"

# Development mode specific settings
if [[ "$DEV_MODE" == true ]]; then
    echo -e "${BLUE}Running in development mode${NC}"
    RUN_CMD="$RUN_CMD -v $(pwd):/src -w /src"
    RUN_CMD="$RUN_CMD $IMAGE_NAME dotnet run --project RoslynMCP/RoslynMCP.csproj"
else
    RUN_CMD="$RUN_CMD $IMAGE_NAME"
fi

echo -e "${YELLOW}Running: $RUN_CMD${NC}"

# Check if image exists
if ! docker image inspect "$IMAGE_NAME" >/dev/null 2>&1; then
    echo -e "${RED}Error: Docker image '$IMAGE_NAME' not found${NC}"
    echo -e "${YELLOW}Build the image first with:${NC}"
    echo "  ./scripts/build.sh"
    echo -e "${YELLOW}Or use docker-compose:${NC}"
    echo "  docker-compose up --build"
    exit 1
fi

# Stop existing container if running
if docker ps -q -f name="$CONTAINER_NAME" | grep -q .; then
    echo -e "${YELLOW}Stopping existing container: $CONTAINER_NAME${NC}"
    docker stop "$CONTAINER_NAME" >/dev/null
fi

# Remove existing container if it exists and we're not keeping it
if [[ "$REMOVE" == false ]] && docker ps -aq -f name="$CONTAINER_NAME" | grep -q .; then
    echo -e "${YELLOW}Removing existing container: $CONTAINER_NAME${NC}"
    docker rm "$CONTAINER_NAME" >/dev/null
fi

# Execute run command
if eval $RUN_CMD; then
    if [[ "$DETACHED" == true ]]; then
        echo -e "${GREEN}✅ Container started successfully in detached mode${NC}"
        echo -e "${YELLOW}Container name: $CONTAINER_NAME${NC}"
        echo -e "${YELLOW}View logs with:${NC}"
        echo "  docker logs -f $CONTAINER_NAME"
        echo -e "${YELLOW}Stop container with:${NC}"
        echo "  docker stop $CONTAINER_NAME"
    else
        echo -e "${GREEN}✅ Container finished successfully${NC}"
    fi
else
    echo -e "${RED}❌ Failed to run container${NC}"
    exit 1
fi
