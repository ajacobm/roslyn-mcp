#!/bin/bash

# Build script for Roslyn MCP Server Docker image
set -e

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
NC='\033[0m' # No Color

# Default values
IMAGE_NAME="roslyn-mcp"
TAG="latest"
BUILD_ARGS=""
PLATFORM=""

# Function to display usage
usage() {
    echo "Usage: $0 [OPTIONS]"
    echo "Build Docker image for Roslyn MCP Server"
    echo ""
    echo "Options:"
    echo "  -t, --tag TAG          Set image tag (default: latest)"
    echo "  -n, --name NAME        Set image name (default: roslyn-mcp)"
    echo "  -p, --platform PLATFORM Set target platform (e.g., linux/amd64,linux/arm64)"
    echo "  -d, --dev              Build development image"
    echo "  -c, --clean            Clean build (no cache)"
    echo "  -h, --help             Show this help message"
    echo ""
    echo "Examples:"
    echo "  $0                     # Build with defaults"
    echo "  $0 -t v1.0            # Build with tag v1.0"
    echo "  $0 -d                 # Build development image"
    echo "  $0 -p linux/amd64,linux/arm64  # Multi-platform build"
}

# Parse command line arguments
while [[ $# -gt 0 ]]; do
    case $1 in
        -t|--tag)
            TAG="$2"
            shift 2
            ;;
        -n|--name)
            IMAGE_NAME="$2"
            shift 2
            ;;
        -p|--platform)
            PLATFORM="$2"
            shift 2
            ;;
        -d|--dev)
            TAG="dev"
            BUILD_ARGS="--target build"
            shift
            ;;
        -c|--clean)
            BUILD_ARGS="$BUILD_ARGS --no-cache"
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

# Set full image name
FULL_IMAGE_NAME="${IMAGE_NAME}:${TAG}"

echo -e "${YELLOW}Building Docker image: ${FULL_IMAGE_NAME}${NC}"

# Change to project root directory
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(dirname "$SCRIPT_DIR")"
cd "$PROJECT_ROOT"

# Build command
BUILD_CMD="docker build -t $FULL_IMAGE_NAME $BUILD_ARGS"

# Add platform if specified
if [[ -n "$PLATFORM" ]]; then
    BUILD_CMD="docker buildx build --platform $PLATFORM -t $FULL_IMAGE_NAME $BUILD_ARGS"
fi

# Add current directory
BUILD_CMD="$BUILD_CMD ."

echo -e "${YELLOW}Running: $BUILD_CMD${NC}"

# Execute build
if eval $BUILD_CMD; then
    echo -e "${GREEN}✅ Successfully built image: $FULL_IMAGE_NAME${NC}"
    
    # Show image info
    echo -e "${YELLOW}Image information:${NC}"
    docker images "$IMAGE_NAME" --format "table {{.Repository}}\t{{.Tag}}\t{{.Size}}\t{{.CreatedAt}}"
    
    echo -e "${GREEN}Build completed successfully!${NC}"
    echo -e "${YELLOW}To run the image:${NC}"
    echo "  docker run -it --rm $FULL_IMAGE_NAME"
    echo -e "${YELLOW}Or use docker-compose:${NC}"
    echo "  docker-compose up"
else
    echo -e "${RED}❌ Build failed!${NC}"
    exit 1
fi
