# ExtractSymbolGraph MCP Tool Implementation Plan

## Overview
The ExtractSymbolGraph tool will create a comprehensive graph representation of symbols and their relationships within C# code. This tool will extract semantic relationships between types, methods, properties, and other code elements to create a navigable graph structure suitable for code analysis, visualization, and understanding complex codebases.

## Tool Specification

### MCP Tool Signature
```csharp
[McpServerTool, Description("Extract a comprehensive symbol graph showing relationships between types, methods, and other code elements.")]
public static async Task<string> ExtractSymbolGraph(
    [Description("Path to the C# file or project")] string path,
    [Description("Graph scope: 'file', 'project', 'solution'")] string scope = "file",
    [Description("Include inheritance relationships")] bool includeInheritance = true,
    [Description("Include method call relationships")] bool includeMethodCalls = true,
    [Description("Include field/property access relationships")] bool includeFieldAccess = true,
    [Description("Include namespace relationships")] bool includeNamespaces = true,
    [Description("Maximum depth for relationship traversal")] int maxDepth = 3)
```

## Data Models

### Core Graph Models
Create `RoslynMCP/Models/SymbolGraph.cs`:

```csharp
public class SymbolGraph
{
    public List<SymbolNode> Nodes { get; set; } = new();
    public List<SymbolEdge> Edges { get; set; } = new();
    public GraphMetadata Metadata { get; set; } = new();
    public Dictionary<string, object> Statistics { get; set; } = new();
}

public class SymbolNode
{
    public string Id { get; set; }
    public string Name { get; set; }
    public string FullName { get; set; }
    public SymbolKind Kind { get; set; }
    public string TypeName { get; set; }
    public Location Location { get; set; }
    public AccessibilityLevel Accessibility { get; set; }
    public List<string> Modifiers { get; set; } = new();
    public Dictionary<string, object> Properties { get; set; } = new();
    public SymbolMetrics Metrics { get; set; } = new();
}

public class SymbolEdge
{
    public string Id { get; set; }
    public string SourceId { get; set; }
    public string TargetId { get; set; }
    public RelationshipType Type { get; set; }
    public string Label { get; set; }
    public Dictionary<string, object> Properties { get; set; } = new();
    public Location Location { get; set; }
}

public enum RelationshipType
{
    Inheritance,
    Implementation,
    Composition,
    Aggregation,
    MethodCall,
    FieldAccess,
    PropertyAccess,
    EventSubscription,
    GenericConstraint,
    Namespace,
    Assembly,
    Dependency,
    Override,
    Instantiation
}

public class SymbolMetrics
{
    public int CyclomaticComplexity { get; set; }
    public int LinesOfCode { get; set; }
    public int NumberOfParameters { get; set; }
    public int NumberOfMethods { get; set; }
    public int NumberOfProperties { get; set; }
    public int NumberOfFields { get; set; }
    public int IncomingReferences { get; set; }
    public int OutgoingReferences { get; set; }
}

public class GraphMetadata
{
    public string Scope { get; set; }
    public string RootPath { get; set; }
    public DateTime GeneratedAt { get; set; }
    public int TotalNodes { get; set; }
    public int TotalEdges { get; set; }
    public Dictionary<string, int> NodeTypeDistribution { get; set; } = new();
    public Dictionary<string, int> EdgeTypeDistribution { get; set; } = new();
    public List<string> ProcessedFiles { get; set; } = new();
}

public enum AccessibilityLevel
{
    Public,
    Private,
    Protected,
    Internal,
    ProtectedInternal,
    PrivateProtected
}
```

## Service Implementation

### Core Service Class
Create `RoslynMCP/Services/SymbolGraphExtractor.cs`:

```csharp
public class SymbolGraphExtractor
{
    private readonly MSBuildWorkspace _workspace;
    private readonly Dictionary<ISymbol, string> _symbolToIdMap = new();
    private readonly Dictionary<string, SymbolNode> _nodes = new();
    private readonly List<SymbolEdge> _edges = new();

    public SymbolGraphExtractor(MSBuildWorkspace workspace)
    {
        _workspace = workspace;
    }

    public async Task<SymbolGraph> ExtractSymbolGraphAsync(
        string path, 
        string scope, 
        bool includeInheritance,
        bool includeMethodCalls,
        bool includeFieldAccess,
        bool includeNamespaces,
        int maxDepth)
    {
        // Implementation details...
    }

    private async Task ProcessDocument(Document document, SymbolGraphOptions options)
    {
        // Extract symbols from document
    }

    private void ProcessSymbol(ISymbol symbol, SemanticModel semanticModel, SymbolGraphOptions options)
    {
        // Create nodes for symbols
    }

    private void ExtractRelationships(ISymbol symbol, SemanticModel semanticModel, SymbolGraphOptions options)
    {
        // Extract various relationship types
    }

    private void ExtractInheritanceRelationships(INamedTypeSymbol typeSymbol)
    {
        // Extract inheritance and implementation relationships
    }

    private void ExtractMethodCallRelationships(IMethodSymbol methodSymbol, SemanticModel semanticModel)
    {
        // Extract method call relationships
    }

    private void ExtractFieldAccessRelationships(ISymbol symbol, SemanticModel semanticModel)
    {
        // Extract field and property access relationships
    }

    private string GenerateSymbolId(ISymbol symbol)
    {
        // Generate unique identifier for symbol
    }

    private SymbolNode CreateSymbolNode(ISymbol symbol)
    {
        // Create node representation of symbol
    }

    private SymbolEdge CreateSymbolEdge(string sourceId, string targetId, RelationshipType type, Location location = null)
    {
        // Create edge representation
    }
}

public class SymbolGraphOptions
{
    public string Scope { get; set; }
    public bool IncludeInheritance { get; set; }
    public bool IncludeMethodCalls { get; set; }
    public bool IncludeFieldAccess { get; set; }
    public bool IncludeNamespaces { get; set; }
    public int MaxDepth { get; set; }
}
```

## Implementation Strategy

### Phase 1: Basic Symbol Extraction
1. **Symbol Discovery**: Traverse syntax trees to identify all symbols
2. **Node Creation**: Create SymbolNode objects for each discovered symbol
3. **Basic Metadata**: Extract symbol kind, name, location, accessibility

### Phase 2: Relationship Extraction
1. **Inheritance Relationships**: Extract base classes and implemented interfaces
2. **Composition Relationships**: Identify field and property types
3. **Method Signatures**: Extract parameter and return types

### Phase 3: Advanced Relationships
1. **Method Calls**: Analyze method bodies for invocation expressions
2. **Field/Property Access**: Track member access expressions
3. **Event Subscriptions**: Identify event handler registrations

### Phase 4: Graph Optimization
1. **Deduplication**: Remove duplicate nodes and edges
2. **Filtering**: Apply scope and depth limitations
3. **Metrics Calculation**: Compute graph statistics and symbol metrics

## Key Features

### Relationship Types Supported
- **Inheritance**: Class inheritance and interface implementation
- **Composition**: Field and property type relationships
- **Method Calls**: Direct method invocations
- **Field Access**: Property and field access patterns
- **Generic Constraints**: Generic type parameter relationships
- **Namespace**: Namespace containment relationships

### Scope Options
- **File**: Single file analysis
- **Project**: Entire project analysis
- **Solution**: Cross-project analysis (if solution file available)

### Output Formats
- **JSON**: Structured graph data suitable for programmatic analysis
- **Graph Visualization**: Compatible with graph visualization tools
- **Metrics**: Statistical analysis of the symbol graph

## Integration Points

### Existing Infrastructure
- Leverage existing MSBuildWorkspace creation in `CreateWorkspace()`
- Use existing project discovery logic from `FindContainingProjectAsync()`
- Integrate with existing error handling patterns

### Service Dependencies
- **ProjectMetadataExtractor**: For project-level context
- **MetricsCalculator**: For symbol complexity metrics
- **PatternDetector**: For identifying architectural patterns in the graph

## Usage Examples

### Basic File Analysis
```csharp
var result = await ExtractSymbolGraph("MyClass.cs", "file", true, true, true, true, 3);
```

### Project-Wide Analysis
```csharp
var result = await ExtractSymbolGraph("MyProject.csproj", "project", true, false, false, true, 2);
```

### Focused Inheritance Analysis
```csharp
var result = await ExtractSymbolGraph("MyClass.cs", "file", true, false, false, false, 1);
```

## Expected Output Structure

### Standard JSON Format
```json
{
  "nodes": [
    {
      "id": "MyNamespace.MyClass",
      "name": "MyClass",
      "fullName": "MyNamespace.MyClass",
      "kind": "NamedType",
      "typeName": "class",
      "location": { "file": "MyClass.cs", "line": 10, "column": 14 },
      "accessibility": "Public",
      "modifiers": ["public", "sealed"],
      "properties": { "isAbstract": false, "isSealed": true },
      "metrics": { "cyclomaticComplexity": 5, "linesOfCode": 45 }
    }
  ],
  "edges": [
    {
      "id": "edge_1",
      "sourceId": "MyNamespace.MyClass",
      "targetId": "System.Object",
      "type": "Inheritance",
      "label": "inherits from",
      "properties": { "isImplicit": true }
    }
  ],
  "metadata": {
    "scope": "file",
    "rootPath": "MyClass.cs",
    "generatedAt": "2025-01-03T22:30:00Z",
    "totalNodes": 15,
    "totalEdges": 23,
    "nodeTypeDistribution": { "NamedType": 3, "Method": 8, "Property": 4 },
    "edgeTypeDistribution": { "Inheritance": 2, "MethodCall": 15, "FieldAccess": 6 }
  }
}
```

### Neo4j Cypher-Compatible Format
```json
{
  "cypherStatements": {
    "nodes": [
      "CREATE (n:Symbol {id: 'MyNamespace.MyClass', name: 'MyClass', fullName: 'MyNamespace.MyClass', kind: 'NamedType', typeName: 'class', accessibility: 'Public', isAbstract: false, isSealed: true, cyclomaticComplexity: 5, linesOfCode: 45, file: 'MyClass.cs', line: 10, column: 14})"
    ],
    "relationships": [
      "MATCH (a:Symbol {id: 'MyNamespace.MyClass'}), (b:Symbol {id: 'System.Object'}) CREATE (a)-[:INHERITS_FROM {isImplicit: true}]->(b)"
    ]
  },
  "bulkImport": {
    "nodes": [
      {
        "labels": ["Symbol", "Class"],
        "properties": {
          "id": "MyNamespace.MyClass",
          "name": "MyClass",
          "fullName": "MyNamespace.MyClass",
          "kind": "NamedType",
          "typeName": "class",
          "accessibility": "Public",
          "isAbstract": false,
          "isSealed": true,
          "cyclomaticComplexity": 5,
          "linesOfCode": 45,
          "file": "MyClass.cs",
          "line": 10,
          "column": 14
        }
      }
    ],
    "relationships": [
      {
        "type": "INHERITS_FROM",
        "startNodeId": "MyNamespace.MyClass",
        "endNodeId": "System.Object",
        "properties": {
          "isImplicit": true
        }
      }
    ]
  }
}
```

## Graph Database Compatibility

### Neo4j Integration
The ExtractSymbolGraph tool is designed to be fully compatible with graph databases, particularly Neo4j. The output format includes multiple representations to support different integration approaches:

#### Direct Cypher Import
- **Cypher Statements**: Ready-to-execute CREATE and MATCH statements
- **Batch Processing**: Optimized for bulk data insertion
- **Relationship Mapping**: Direct translation of C# relationships to Neo4j relationships

#### Bulk Import Format
- **CSV-Compatible**: Node and relationship data structured for Neo4j's bulk import tools
- **Label Strategy**: Automatic labeling based on symbol types (Class, Method, Property, etc.)
- **Property Flattening**: Complex properties flattened for graph database storage

#### Integration Patterns

##### Pattern 1: Direct API Integration
```csharp
// External service can consume the JSON output directly
var graphData = await ExtractSymbolGraph("MyProject.csproj", "project");
var neo4jClient = new Neo4jClient();
await neo4jClient.ExecuteCypherStatements(graphData.CypherStatements);
```

##### Pattern 2: ETL Pipeline Integration
```json
{
  "extractionMetadata": {
    "timestamp": "2025-01-03T22:30:00Z",
    "version": "1.0.0",
    "sourceProject": "MyProject.csproj"
  },
  "nodes": [...],
  "relationships": [...],
  "cypherStatements": [...],
  "bulkImport": {
    "nodes": [...],
    "relationships": [...]
  }
}
```

##### Pattern 3: Streaming Integration
For large codebases, the tool can be extended to support streaming output:
```csharp
// Future enhancement for large projects
await ExtractSymbolGraphStream("LargeProject.sln", streamHandler);
```

### Graph Database Schema Design

#### Node Labels and Properties
```cypher
// Class nodes
(:Symbol:Class {
  id: string,
  name: string,
  fullName: string,
  accessibility: string,
  isAbstract: boolean,
  isSealed: boolean,
  file: string,
  line: integer,
  column: integer,
  cyclomaticComplexity: integer,
  linesOfCode: integer
})

// Method nodes
(:Symbol:Method {
  id: string,
  name: string,
  fullName: string,
  returnType: string,
  parameterCount: integer,
  isStatic: boolean,
  isVirtual: boolean,
  accessibility: string,
  cyclomaticComplexity: integer
})

// Property nodes
(:Symbol:Property {
  id: string,
  name: string,
  propertyType: string,
  hasGetter: boolean,
  hasSetter: boolean,
  accessibility: string
})
```

#### Relationship Types
```cypher
// Inheritance relationships
(child:Class)-[:INHERITS_FROM]->(parent:Class)
(class:Class)-[:IMPLEMENTS]->(interface:Interface)

// Composition relationships
(class:Class)-[:HAS_FIELD]->(fieldType:Class)
(class:Class)-[:HAS_PROPERTY]->(propertyType:Class)

// Method relationships
(method:Method)-[:CALLS]->(targetMethod:Method)
(method:Method)-[:ACCESSES]->(field:Field)
(method:Method)-[:BELONGS_TO]->(class:Class)

// Namespace relationships
(symbol:Symbol)-[:IN_NAMESPACE]->(namespace:Namespace)
```

### External Integration Considerations

#### Data Consistency
- **Unique Identifiers**: Consistent symbol IDs across multiple extractions
- **Versioning**: Support for tracking code changes over time
- **Incremental Updates**: Ability to update only changed portions of the graph

#### Performance Optimization
- **Batch Size Control**: Configurable batch sizes for bulk operations
- **Connection Pooling**: Efficient database connection management
- **Transaction Management**: Proper transaction boundaries for large imports

#### Error Handling
- **Partial Failures**: Graceful handling of partial import failures
- **Rollback Support**: Transaction rollback capabilities
- **Validation**: Pre-import validation of graph data integrity

### Integration Examples

#### Example 1: CI/CD Pipeline Integration
```yaml
# GitHub Actions example
- name: Extract Symbol Graph
  run: |
    dotnet run --project RoslynMCP -- ExtractSymbolGraph ./src/MyProject.csproj project > graph.json
    
- name: Import to Neo4j
  run: |
    curl -X POST http://neo4j:7474/db/data/cypher \
      -H "Content-Type: application/json" \
      -d @graph.json
```

#### Example 2: Microservice Architecture
```csharp
// Code analysis service
public class CodeAnalysisService
{
    public async Task<SymbolGraph> AnalyzeProject(string projectPath)
    {
        return await ExtractSymbolGraph(projectPath, "project");
    }
}

// Graph database service
public class GraphDatabaseService
{
    public async Task ImportSymbolGraph(SymbolGraph graph)
    {
        await _neo4jClient.ExecuteBatch(graph.CypherStatements);
    }
}
```

#### Example 3: Real-time Code Analysis
```csharp
// File watcher integration
public class CodeChangeHandler
{
    public async Task OnFileChanged(string filePath)
    {
        var graph = await ExtractSymbolGraph(filePath, "file");
        await _graphDb.UpdateSymbols(graph);
        await _notificationService.NotifyDependents(graph.AffectedSymbols);
    }
}
```

## Testing Strategy

### Unit Tests
- Test symbol node creation for various symbol types
- Test relationship extraction for different code patterns
- Test graph filtering and scope limitations
- Test Neo4j format generation and validation

### Integration Tests
- Test with real C# projects of varying complexity
- Validate graph completeness and accuracy
- Performance testing with large codebases
- Test Neo4j import/export workflows

### Graph Database Tests
- Validate Cypher statement generation
- Test bulk import format compatibility
- Verify relationship integrity in Neo4j
- Performance testing with large graph imports

### Validation
- Compare with existing code analysis tools
- Verify relationship accuracy through manual inspection
- Test edge cases (partial classes, generic types, nested types)
- Validate graph database schema compliance

## Performance Considerations

### Optimization Strategies
- **Lazy Loading**: Load symbols on-demand based on scope
- **Caching**: Cache symbol information to avoid recomputation
- **Parallel Processing**: Process multiple files concurrently
- **Memory Management**: Dispose of large syntax trees after processing

### Scalability
- **Incremental Analysis**: Support for analyzing only changed files
- **Streaming Output**: For very large graphs, support streaming JSON output
- **Configurable Depth**: Allow users to limit analysis depth for performance

## Implementation Timeline

### Week 1: Foundation
- Create data models (SymbolGraph.cs)
- Implement basic SymbolGraphExtractor service
- Add MCP tool method to Program.cs

### Week 2: Core Functionality
- Implement symbol discovery and node creation
- Add basic relationship extraction (inheritance, composition)
- Integrate with existing workspace infrastructure

### Week 3: Advanced Features
- Add method call analysis
- Implement field/property access tracking
- Add graph filtering and optimization

### Week 4: Polish & Testing
- Performance optimization
- Comprehensive testing
- Documentation and examples

## Success Criteria

1. **Completeness**: Extract all major symbol types and relationships
2. **Accuracy**: Relationships correctly represent code structure
3. **Performance**: Handle medium-sized projects (100+ files) efficiently
4. **Usability**: Clear, well-structured JSON output
5. **Integration**: Seamless integration with existing MCP tools

This ExtractSymbolGraph tool will provide a powerful foundation for code analysis, visualization, and understanding complex C# codebases through their symbol relationships.
