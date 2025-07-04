#!/bin/bash

# Test script for Roslyn MCP Server Docker setup
set -e

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
NC='\033[0m' # No Color

# Test configuration
IMAGE_NAME="roslyn-mcp:test"
CONTAINER_NAME="roslyn-mcp-test"
TEST_WORKSPACE="test-workspace"

echo -e "${BLUE}🧪 Roslyn MCP Server Docker Test Suite${NC}"
echo "========================================"

# Function to cleanup test resources
cleanup() {
    echo -e "${YELLOW}🧹 Cleaning up test resources...${NC}"
    
    # Stop and remove test container
    if docker ps -q -f name="$CONTAINER_NAME" | grep -q .; then
        docker stop "$CONTAINER_NAME" >/dev/null 2>&1 || true
    fi
    if docker ps -aq -f name="$CONTAINER_NAME" | grep -q .; then
        docker rm "$CONTAINER_NAME" >/dev/null 2>&1 || true
    fi
    
    # Remove test workspace
    if [[ -d "$TEST_WORKSPACE" ]]; then
        rm -rf "$TEST_WORKSPACE"
    fi
    
    # Remove test image
    if docker image inspect "$IMAGE_NAME" >/dev/null 2>&1; then
        docker rmi "$IMAGE_NAME" >/dev/null 2>&1 || true
    fi
}

# Set trap to cleanup on exit
trap cleanup EXIT

# Test 1: Build the Docker image
echo -e "${YELLOW}📦 Test 1: Building Docker image...${NC}"
if ./scripts/build.sh -t test; then
    echo -e "${GREEN}✅ Build test passed${NC}"
else
    echo -e "${RED}❌ Build test failed${NC}"
    exit 1
fi

# Test 2: Create test workspace with sample C# project
echo -e "${YELLOW}📁 Test 2: Creating test workspace...${NC}"
mkdir -p "$TEST_WORKSPACE/SampleProject"

# Create a simple C# project for testing
cat > "$TEST_WORKSPACE/SampleProject/SampleProject.csproj" << 'EOF'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net8.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>
</Project>
EOF

cat > "$TEST_WORKSPACE/SampleProject/Program.cs" << 'EOF'
using System;

namespace SampleProject
{
    class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("Hello, World!");
            var calculator = new Calculator();
            int result = calculator.Add(5, 3);
            Console.WriteLine($"5 + 3 = {result}");
        }
    }

    public class Calculator
    {
        public int Add(int a, int b)
        {
            return a + b;
        }

        public int Subtract(int a, int b)
        {
            return a - b;
        }
    }
}
EOF

echo -e "${GREEN}✅ Test workspace created${NC}"

# Test 3: Run container with workspace mounted
echo -e "${YELLOW}🚀 Test 3: Running container with test workspace...${NC}"
WORKSPACE_PATH=$(realpath "$TEST_WORKSPACE")

# Start container in detached mode
if docker run -d --name "$CONTAINER_NAME" \
    -v "$WORKSPACE_PATH:/workspace:ro" \
    -v roslyn-mcp-test-nuget:/home/mcpuser/.nuget/packages \
    "$IMAGE_NAME" tail -f /dev/null; then
    echo -e "${GREEN}✅ Container started successfully${NC}"
else
    echo -e "${RED}❌ Failed to start container${NC}"
    exit 1
fi

# Test 4: Check if container is healthy
echo -e "${YELLOW}🏥 Test 4: Checking container health...${NC}"
sleep 5  # Give container time to start

if docker exec "$CONTAINER_NAME" pgrep -f "tail" >/dev/null; then
    echo -e "${GREEN}✅ Container is running${NC}"
else
    echo -e "${RED}❌ Container health check failed${NC}"
    docker logs "$CONTAINER_NAME"
    exit 1
fi

# Test 5: Test file access in container
echo -e "${YELLOW}📂 Test 5: Testing workspace access...${NC}"
if docker exec "$CONTAINER_NAME" ls -la /workspace/SampleProject/ >/dev/null; then
    echo -e "${GREEN}✅ Workspace mounted and accessible${NC}"
else
    echo -e "${RED}❌ Workspace access failed${NC}"
    exit 1
fi

# Test 6: Test .NET runtime in container
echo -e "${YELLOW}🔧 Test 6: Testing .NET runtime...${NC}"
if docker exec "$CONTAINER_NAME" dotnet --version >/dev/null; then
    DOTNET_VERSION=$(docker exec "$CONTAINER_NAME" dotnet --version)
    echo -e "${GREEN}✅ .NET runtime working (version: $DOTNET_VERSION)${NC}"
else
    echo -e "${RED}❌ .NET runtime test failed${NC}"
    exit 1
fi

# Test 7: Test MSBuild availability
echo -e "${YELLOW}🔨 Test 7: Testing MSBuild availability...${NC}"
if docker exec "$CONTAINER_NAME" dotnet build --help >/dev/null 2>&1; then
    echo -e "${GREEN}✅ MSBuild is available${NC}"
else
    echo -e "${RED}❌ MSBuild test failed${NC}"
    exit 1
fi

# Test 8: Test user permissions
echo -e "${YELLOW}👤 Test 8: Testing user permissions...${NC}"
USER_ID=$(docker exec "$CONTAINER_NAME" id -u)
if [[ "$USER_ID" != "0" ]]; then
    echo -e "${GREEN}✅ Container running as non-root user (UID: $USER_ID)${NC}"
else
    echo -e "${RED}❌ Container running as root (security concern)${NC}"
    exit 1
fi

# Test 9: Test docker-compose functionality
echo -e "${YELLOW}🐳 Test 9: Testing docker-compose...${NC}"
if command -v docker-compose >/dev/null 2>&1; then
    if docker-compose config >/dev/null 2>&1; then
        echo -e "${GREEN}✅ docker-compose configuration is valid${NC}"
    else
        echo -e "${RED}❌ docker-compose configuration invalid${NC}"
        exit 1
    fi
else
    echo -e "${YELLOW}⚠️  docker-compose not available, skipping test${NC}"
fi

# Test 10: Test build script functionality
echo -e "${YELLOW}🛠️  Test 10: Testing build script options...${NC}"
if ./scripts/build.sh -h >/dev/null 2>&1; then
    echo -e "${GREEN}✅ Build script help works${NC}"
else
    echo -e "${RED}❌ Build script test failed${NC}"
    exit 1
fi

# Test 11: Test run script functionality
echo -e "${YELLOW}🏃 Test 11: Testing run script options...${NC}"
if ./scripts/run.sh -h >/dev/null 2>&1; then
    echo -e "${GREEN}✅ Run script help works${NC}"
else
    echo -e "${RED}❌ Run script test failed${NC}"
    exit 1
fi

echo ""
echo -e "${GREEN}🎉 All tests passed successfully!${NC}"
echo "========================================"
echo -e "${BLUE}Docker setup is ready for use.${NC}"
echo ""
echo -e "${YELLOW}Next steps:${NC}"
echo "1. Build the production image: ./scripts/build.sh"
echo "2. Run with your workspace: ./scripts/run.sh -w /path/to/your/projects"
echo "3. Or use docker-compose: docker-compose up"
echo ""
echo -e "${BLUE}For more information, see DOCKER.md${NC}"
