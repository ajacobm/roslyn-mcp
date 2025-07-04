# Roslyn Code Analysis MCP Server

## Overview
A Model Context Protocol (MCP) server that provides C# code analysis capabilities using the Roslyn compiler platform. This tool helps validate C# files, find symbol references, and perform static code analysis within the context of a .NET project.

## Features
- **Code Validation**: Analyze C# files for syntax errors, semantic issues, and compiler warnings
- **Symbol Reference Finding**: Locate all usages of a symbol across a project
- **Project Context Analysis**: Validate files within their project context
- **Code Analyzer Support**: Run Microsoft recommended code analyzers

## Tools

### Core Analysis Tools
- `ValidateFile`: Validates a C# file using Roslyn and runs code analyzers
- `ExtractProjectMetadata`: Extracts comprehensive metadata from .NET projects including types, members, namespaces, and dependencies
- `FindUsages`: Finds all references to a symbol at a specified position

### Advanced Analysis Tools
- `ChunkCodeBySemantics`: Breaks down C# code into semantically meaningful chunks for analysis
- `AnalyzeCodeStructure`: Analyzes architectural patterns, metrics, and code smells
- `GenerateCodeFacts`: Extracts factual information about code for documentation and analysis
- `ExtractSymbolGraph`: Creates a comprehensive graph representation of symbols and their relationships within C# code

## Example config
```json
{
    "servers": {
        "RoslynMCP": {
            "type": "stdio",
            "command": "dotnet",
            "args": [
                "run",
                "--no-build",
                "--project",
                "E:/Source/roslyn-mcp/RoslynMCP/RoslynMCP/RoslynMCP.csproj"
            ]
        }
    }
}
```

## Example prompt
```
When done implementing changes, run these validation steps as human will not accept work unless these are done:
- Always use Roslyn validation tool on C# (.cs) files
```

## Getting Started

### Option 1: Run with .NET (Local Development)
1. Build the project
2. Run the application with:
   ```
   dotnet run
   ```
3. The server will start and listen for MCP commands via standard I/O

### Option 2: Run with Docker (Recommended for Production)
1. Build the Docker image:
   ```bash
   ./scripts/build.sh
   ```
2. Run the container:
   ```bash
   ./scripts/run.sh -w /path/to/your/csharp/projects
   ```
3. Or use docker-compose:
   ```bash
   docker-compose up --build
   ```

For detailed Docker setup instructions, see [DOCKER.md](DOCKER.md).

## Requirements

### Local Development
- .NET 9 SDK
- MSBuild tools
- NuGet packages for Roslyn analyzers (automatically loaded if available)

### Docker
- Docker Engine
- docker-compose (optional, for easier management)

## Example Usage
Validate a C# file:
```
ValidateFile --filePath="/path/to/your/file.cs" --runAnalyzers=true
```

Find all usages of a symbol:
```
FindUsages --filePath="/path/to/your/file.cs" --line=10 --column=15
```

## Technical Details
- Uses `Microsoft.CodeAnalysis` libraries for code analysis
- Integrates with MSBuild to load full project context
- Supports standard diagnostic analyzers
- Includes detailed output with syntax, semantic, and analyzer diagnostics
