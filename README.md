# Roslyn Multi-Language Analysis MCP Server

## Overview
A Model Context Protocol (MCP) server that provides comprehensive code analysis capabilities for modern .NET applications. Built on the Roslyn compiler platform, it analyzes C# code along with XAML markup and embedded SQL queries to provide holistic insights into multi-language .NET projects.

## Features
- **Multi-Language Analysis**: Analyze C#, XAML, and SQL in unified context
- **Code Validation**: Syntax errors, semantic issues, and compiler warnings
- **MVVM Pattern Analysis**: Detect and analyze View-ViewModel-Model relationships
- **SQL Extraction**: Find and analyze SQL queries embedded in C# code
- **Cross-Language Chunking**: Group related code across multiple languages
- **Symbol Reference Finding**: Locate all usages of symbols across projects
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
- `ExtractSymbolGraph`: Creates a comprehensive graph representation of symbols and their relationships within C# code (now supports XAML and SQL analysis)

### Multi-Language Analysis Tools
- `ChunkMultiLanguageCode`: Break down code into cross-language chunks spanning C#, XAML, and SQL
- `ExtractSqlFromCode`: Extract and analyze SQL queries embedded in C# code
- `AnalyzeXamlFile`: Analyze XAML files for UI structure, data bindings, and resources
- `AnalyzeMvvmRelationships`: Analyze MVVM relationships between Views, ViewModels, and Models in a project

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

For comprehensive multi-language analysis examples and best practices, see [MULTI_LANGUAGE_GUIDE.md](MULTI_LANGUAGE_GUIDE.md).

## Requirements

### Local Development
- .NET 9 SDK
- MSBuild tools
- NuGet packages for Roslyn analyzers (automatically loaded if available)

### Docker
- Docker Engine
- docker-compose (optional, for easier management)

## Example Usage

### Core Analysis
Validate a C# file:
```
ValidateFile --filePath="/path/to/your/file.cs" --runAnalyzers=true
```

Find all usages of a symbol:
```
FindUsages --filePath="/path/to/your/file.cs" --line=10 --column=15
```

### Multi-Language Analysis

**Analyze MVVM relationships in a WPF project:**
```
AnalyzeMvvmRelationships --projectPath="/path/to/your/WpfApp.csproj"
```

**Extract SQL queries from data access code:**
```
ExtractSqlFromCode --filePath="/path/to/your/UserRepository.cs"
```

**Analyze XAML file structure and bindings:**
```
AnalyzeXamlFile --filePath="/path/to/your/MainWindow.xaml"
```

**Chunk code by feature across multiple languages:**
```
ChunkMultiLanguageCode --path="/path/to/your/project.csproj" --strategy="feature" --includeXaml=true --includeSql=true
```

**Chunk by MVVM pattern:**
```
ChunkMultiLanguageCode --path="/path/to/your/project.csproj" --strategy="mvvm" --includeXaml=true
```

**Chunk by data access patterns:**
```
ChunkMultiLanguageCode --path="/path/to/your/project.csproj" --strategy="dataaccess" --includeSql=true
```

### Advanced Symbol Graph Analysis
Extract comprehensive symbol relationships including XAML and SQL:
```
ExtractSymbolGraph --path="/path/to/your/project.csproj" --scope="project" --includeXaml=true --includeSql=true
```

## Technical Details
- Uses `Microsoft.CodeAnalysis` libraries for C# code analysis
- Integrates with MSBuild to load full project context
- Uses `Portable.Xaml` for cross-platform XAML parsing
- Uses `Microsoft.SqlServer.TransactSql.ScriptDom` for SQL analysis
- Supports standard diagnostic analyzers
- Includes detailed output with syntax, semantic, and analyzer diagnostics
- Cross-language relationship detection and analysis
- MVVM pattern recognition and architectural analysis

## Supported Technologies

### Languages
- **C#**: Full Roslyn-based analysis including syntax, semantics, and symbol graphs
- **XAML**: UI structure, data bindings, resources, and MVVM relationships
- **SQL**: Embedded queries in C# code (string literals, Entity Framework, Dapper)

### Frameworks
- **.NET 8.0+**: Primary target framework
- **WPF**: XAML analysis and MVVM pattern detection
- **Entity Framework**: SQL query extraction and analysis
- **Dapper**: Micro-ORM query pattern detection
- **Raw ADO.NET**: SQL string literal detection

### Analysis Strategies
- **Feature-based**: Group related functionality across languages
- **MVVM-based**: Analyze View-ViewModel-Model relationships
- **Data Access**: Focus on database interaction patterns
- **Component-based**: Analyze reusable UI components
