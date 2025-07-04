# Roslyn MCP Server - Advanced Code Analysis Tools Implementation

## Project Overview
This is a Model Context Protocol (MCP) server that provides advanced C# code analysis capabilities using Microsoft Roslyn. The server exposes tools for code validation, project metadata extraction, symbol usage analysis, and advanced semantic analysis.

## Current Implementation Status

### ✅ Completed Features
1. **Basic MCP Server Infrastructure**
   - Dockerfile with multi-stage build (production ready)
   - Docker Compose configuration for development and production
   - Build scripts and documentation
   - .NET 8.0 based implementation

2. **Core Analysis Tools** (in `Program.cs` - `RoslynTools` class)
   - `ValidateFile` - Validates C# files using Roslyn and runs code analyzers
   - `ExtractProjectMetadata` - Extracts comprehensive metadata from .NET projects  
   - `FindUsages` - Finds all references to a symbol at specified position

3. **Supporting Infrastructure**
   - `ProjectMetadataExtractor` service for deep project analysis
   - `ProjectMetadata` model with comprehensive data structures
   - MSBuild workspace integration with proper C# language service registration
   - Error handling and logging throughout

### 🚧 Next Phase: Advanced Analysis Tools

The following three tools are referenced in the codebase but need implementation:

#### 1. ChunkCodeBySemantics
**Purpose**: Break down C# code into semantically meaningful chunks for better analysis and understanding.

**Planned Functionality**:
- Parse syntax trees and identify logical code boundaries
- Group related code elements (classes, methods, properties, etc.)
- Create chunks based on semantic relationships and dependencies
- Include metadata about each chunk (type, complexity, relationships)
- Support different chunking strategies (by class, by feature, by namespace)

**Expected Output**: JSON structure with code chunks, their boundaries, types, and relationships

#### 2. AnalyzeCodeStructure  
**Purpose**: Analyze architectural and structural patterns in C# code.

**Planned Functionality**:
- Detect common design patterns (Singleton, Factory, Observer, Strategy, etc.)
- Analyze inheritance hierarchies and composition relationships
- Calculate coupling and cohesion metrics
- Identify code smells and architectural issues
- Analyze dependency flows and detect circular dependencies
- Measure complexity metrics (cyclomatic, cognitive, etc.)

**Expected Output**: Structured analysis report with patterns, metrics, and recommendations

#### 3. GenerateCodeFacts
**Purpose**: Extract factual information about code for documentation, analysis, or AI training.

**Planned Functionality**:
- Generate natural language descriptions of code elements
- Extract key facts about methods, classes, and their behaviors
- Identify preconditions, postconditions, and side effects
- Generate summaries of complex algorithms and business logic
- Create fact-based documentation suitable for embedding
- Extract API contracts and usage patterns

**Expected Output**: Structured facts in various formats (JSON, markdown, plain text)

## Implementation Plan

### Step 1: Create Data Models
Create the following model files in `RoslynMCP/Models/`:

1. **CodeChunk.cs**
```csharp
public class CodeChunk
{
    public string Id { get; set; }
    public string Type { get; set; } // Class, Method, Property, etc.
    public string Name { get; set; }
    public string Content { get; set; }
    public Location Location { get; set; }
    public List<string> Dependencies { get; set; }
    public Dictionary<string, object> Metadata { get; set; }
    public int ComplexityScore { get; set; }
}

public class ChunkingResult
{
    public List<CodeChunk> Chunks { get; set; }
    public Dictionary<string, List<string>> Relationships { get; set; }
    public ChunkingMetadata Metadata { get; set; }
}
```

2. **StructureAnalysis.cs**
```csharp
public class StructureAnalysis
{
    public List<DesignPattern> DetectedPatterns { get; set; }
    public List<CodeMetric> Metrics { get; set; }
    public List<CodeSmell> CodeSmells { get; set; }
    public DependencyGraph Dependencies { get; set; }
    public ArchitecturalInsights Insights { get; set; }
}

public class DesignPattern
{
    public string Name { get; set; }
    public string Description { get; set; }
    public List<Location> Locations { get; set; }
    public double Confidence { get; set; }
}
```

3. **CodeFacts.cs**
```csharp
public class CodeFacts
{
    public List<CodeFact> Facts { get; set; }
    public Dictionary<string, string> Summaries { get; set; }
    public List<ApiContract> Contracts { get; set; }
    public CodeDocumentation Documentation { get; set; }
}

public class CodeFact
{
    public string Type { get; set; } // Method, Class, Property, etc.
    public string Subject { get; set; }
    public string Predicate { get; set; }
    public string Object { get; set; }
    public double Confidence { get; set; }
    public Location Location { get; set; }
}
```

### Step 2: Create Service Classes
Create the following service files in `RoslynMCP/Services/`:

1. **CodeChunker.cs** - Main service for semantic code chunking
2. **StructureAnalyzer.cs** - Main service for code structure analysis
3. **CodeFactsGenerator.cs** - Main service for generating code facts
4. **PatternDetector.cs** - Helper service for detecting design patterns
5. **MetricsCalculator.cs** - Helper service for calculating code metrics

### Step 3: Add MCP Tool Methods
Add three new tool methods to the `RoslynTools` class in `Program.cs`:

```csharp
[McpServerTool, Description("Break down C# code into semantically meaningful chunks for analysis.")]
public static async Task<string> ChunkCodeBySemantics(
    [Description("Path to the C# file or project")] string path,
    [Description("Chunking strategy: 'class', 'method', 'feature', 'namespace'")] string strategy = "class",
    [Description("Include dependency relationships")] bool includeDependencies = true)

[McpServerTool, Description("Analyze code structure, patterns, and architectural metrics.")]
public static async Task<string> AnalyzeCodeStructure(
    [Description("Path to the C# file or project")] string path,
    [Description("Include design pattern detection")] bool detectPatterns = true,
    [Description("Calculate complexity metrics")] bool calculateMetrics = true)

[McpServerTool, Description("Generate factual information about code for documentation and analysis.")]
public static async Task<string> GenerateCodeFacts(
    [Description("Path to the C# file or project")] string path,
    [Description("Output format: 'json', 'markdown', 'text'")] string format = "json",
    [Description("Include natural language descriptions")] bool includeDescriptions = true)
```

## Technical Architecture

### Current Project Structure
```
RoslynMCP/
├── Program.cs                 # Main entry point + MCP tools
├── RoslynMCP.csproj          # Project file with dependencies
├── Models/
│   └── ProjectMetadata.cs    # Existing metadata models
└── Services/
    └── ProjectMetadataExtractor.cs  # Existing extraction service
```

### Target Project Structure
```
RoslynMCP/
├── Program.cs                 # Main entry point + all MCP tools
├── RoslynMCP.csproj          # Project file with dependencies
├── Models/
│   ├── ProjectMetadata.cs    # Existing metadata models
│   ├── CodeChunk.cs          # New: Code chunking models
│   ├── StructureAnalysis.cs  # New: Structure analysis models
│   └── CodeFacts.cs          # New: Code facts models
└── Services/
    ├── ProjectMetadataExtractor.cs  # Existing extraction service
    ├── CodeChunker.cs               # New: Semantic chunking service
    ├── StructureAnalyzer.cs         # New: Structure analysis service
    ├── CodeFactsGenerator.cs        # New: Facts generation service
    ├── PatternDetector.cs           # New: Pattern detection helper
    └── MetricsCalculator.cs         # New: Metrics calculation helper
```

## Key Dependencies
The project currently uses:
- Microsoft.CodeAnalysis.CSharp (4.8.0)
- Microsoft.CodeAnalysis.Workspaces.MSBuild (4.8.0)
- ModelContextProtocol.Server (0.1.0)

Additional dependencies may be needed for advanced metrics calculation.

## Development Environment
- .NET 8.0 SDK
- Docker support with multi-stage builds
- MSBuild workspace integration
- Comprehensive error handling and logging

## Testing Strategy
- Build and test using existing Docker infrastructure
- Use the current project itself as test data
- Validate tools work with various C# project structures
- Ensure proper error handling for edge cases

## Next Session Tasks
1. Implement the data models in the Models/ directory
2. Create the service classes with core functionality
3. Add the three new MCP tool methods to Program.cs
4. Test the implementation with the current project
5. Update documentation and Docker build if needed

## Important Notes
- Maintain consistency with existing code patterns and error handling
- Use the existing workspace creation and C# language service registration
- Follow the established JSON serialization patterns
- Ensure all new functionality works within the Docker container environment
- Keep the MCP tool interface simple and well-documented

## Current Build Status
✅ Project builds successfully with warnings only (no errors)
✅ Docker image builds and runs correctly
✅ All existing tools functional and tested

Ready for implementation of the three advanced analysis tools.
