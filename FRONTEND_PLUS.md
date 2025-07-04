# Frontend Plus: Multi-Language Analysis Extension Plan

## Overview
This document outlines the plan to extend the Roslyn MCP Server beyond pure C# analysis to include XAML/WPF, SQL extraction, and ASP.NET markup analysis. The goal is to provide comprehensive analysis of modern .NET applications that span multiple languages and technologies.

## Current State
The Roslyn MCP Server currently provides 7 tools focused on C# code analysis:
- **Core Tools**: ValidateFile, ExtractProjectMetadata, FindUsages
- **Advanced Tools**: ChunkCodeBySemantics, AnalyzeCodeStructure, GenerateCodeFacts, ExtractSymbolGraph

## Extension Scope

### 1. SQL Analysis (High Priority - Phase 1)
**Complexity**: Low-Medium (3-4 days)
**Value**: High - Critical for data access pattern analysis

#### Target Scenarios
- String literal SQL in C# code
- Entity Framework raw SQL (`FromSqlRaw()`, `ExecuteSqlCommand()`)
- Dapper queries and micro-ORM patterns
- Stored procedure calls and database connections

#### Implementation Strategy
```csharp
// Detection patterns:
var sql = "SELECT * FROM Users WHERE Id = @id";
context.Database.ExecuteSqlRaw("UPDATE Users SET LastLogin = GETDATE()");
connection.Query<User>("SELECT * FROM Users");
```

#### Enhanced Tool Capabilities
- **ChunkCodeBySemantics**: Create "Data Access" chunks grouping related database operations
- **ExtractSymbolGraph**: Add "Queries Table" relationship edges
- **GenerateCodeFacts**: Document SQL operations and data access patterns
- **AnalyzeCodeStructure**: Detect data access patterns and potential performance issues

### 2. XAML/WPF Analysis (Medium Priority - Phase 2)
**Complexity**: Medium (2 weeks)
**Value**: High for MVVM applications

#### Target Scenarios
- WPF applications using XAML markup
- UWP/WinUI applications
- MVVM pattern analysis across View-ViewModel-Model layers

#### XAML Analysis Capabilities
- UI element hierarchy and relationships
- Data binding analysis (OneWay, TwoWay, etc.)
- Resource usage and dependencies (ResourceDictionary, Styles, Templates)
- Event handler connections between XAML and code-behind
- Named element mapping to code-behind

#### Integration Approach
**Extend existing tools rather than create separate ones** because MVVM applications are inherently interconnected:

- **ExtractProjectMetadata**: Include XAML files, resources, UI element counts
- **ExtractSymbolGraph**: Add XAML→Code-behind relationships and data binding edges
- **ChunkCodeBySemantics**: Create "UI Feature" chunks spanning XAML + ViewModel + Code-behind
- **AnalyzeCodeStructure**: Detect MVVM patterns, view-viewmodel relationships

#### New Specialized Tool
- **AnalyzeXamlBindings**: Deep dive into data binding analysis, resource usage, UI hierarchy

### 3. ASP.NET Markup Analysis (Lower Priority - Phase 3+)
**Complexity**: High to Extreme (3-6+ weeks depending on scope)
**Value**: Medium to High depending on application type

#### Technology Breakdown

##### ASP.NET Web Forms (.aspx/.ascx) - High Complexity
**Challenges**:
- Server controls with complex lifecycle and ViewState
- Inline code blocks (`<% %>`) mixing C# directly in markup
- Data binding syntax (`<%# Eval() %>`, `<%# Bind() %>`)
- Master pages and content placeholder relationships
- User controls and nested component hierarchies

##### Razor Pages/MVC (.cshtml) - Very High Complexity
**Challenges**:
- Not XML-based - C# code mixed with HTML
- Razor syntax requires actual C# compilation: `@Model.Property`, `@if()`, `@foreach()`
- Requires Microsoft.AspNetCore.Razor.Language integration

```csharp
// Example complexity:
@model UserViewModel
@{
    var users = await UserService.GetUsersAsync();
    ViewBag.Title = "Users";
}
@foreach(var user in users)
{
    <div class="user">@user.Name</div>
}
```

##### Blazor (.razor) - Extreme Complexity
**Challenges**:
- Component-based architecture
- C# event handlers directly in markup
- Two-way data binding with `@bind`
- Component parameters and cascading values
- Lifecycle methods mixed with rendering logic

#### Recommended ASP.NET Approach
**Phase 3a: Basic Web Structure Analysis**
- Detect web project types (Web Forms, MVC, Blazor)
- File relationship mapping (Controller→View, Page→PageModel)
- Basic project structure analysis

**Phase 3b: Advanced Razor Analysis** (if needed)
- Integrate Microsoft.AspNetCore.Razor.Language
- Razor syntax tree analysis
- Component relationship mapping

## Implementation Architecture

### Enhanced Service Layer
```
Services/
├── Core Analysis (existing)
│   ├── CodeChunker.cs
│   ├── StructureAnalyzer.cs
│   └── SymbolGraphExtractor.cs
├── Multi-Language Extensions
│   ├── SqlExtractor.cs              // SQL string detection and parsing
│   ├── XamlAnalyzer.cs              // XAML-specific parsing
│   ├── MvvmPatternDetector.cs       // Cross-cutting MVVM analysis
│   └── WebMarkupAnalyzer.cs         // ASP.NET markup analysis
└── Integration
    ├── MultiLanguageChunker.cs      // Cross-language chunking
    └── UnifiedGraphExtractor.cs     // Multi-language symbol graphs
```

### Enhanced Data Models
```
Models/
├── Core (existing)
├── Multi-Language Extensions
│   ├── SqlMetadata.cs               // Queries, tables, operations
│   ├── XamlMetadata.cs              // UI elements, bindings, resources
│   ├── MvvmRelationships.cs         // View-ViewModel-Model mappings
│   └── WebMarkupMetadata.cs         // ASP.NET markup structures
└── Unified
    ├── MultiLanguageChunk.cs        // Cross-language code chunks
    └── UnifiedSymbolGraph.cs        // Multi-language symbol relationships
```

### Generic Markup Analyzer Interface
```csharp
public interface IMarkupAnalyzer
{
    bool CanAnalyze(string filePath);
    MarkupMetadata Analyze(string content, string filePath);
    IEnumerable<SymbolRelationship> ExtractRelationships(string content, string filePath);
}

// Implementations:
public class XamlAnalyzer : IMarkupAnalyzer { }
public class WebFormsAnalyzer : IMarkupAnalyzer { }
public class RazorAnalyzer : IMarkupAnalyzer { }
```

## Enhanced Tool Capabilities

### ChunkCodeBySemantics with Multi-Language Support
- **Feature Chunks**: Spanning XAML + C# + SQL for complete features
- **Data Access Chunks**: Grouping related database operations across methods
- **UI Chunks**: Combining Views with their ViewModels and Models
- **Cross-Language Dependencies**: Understanding relationships between different file types

### ExtractSymbolGraph with Rich Relationships
- **XAML Relationships**: XAML → Code-behind connections, data binding chains
- **Database Relationships**: Method → Database Table edges, query dependencies
- **MVVM Relationships**: View → ViewModel → Model relationship mapping
- **Web Relationships**: Controller → View → Model relationships

### AnalyzeCodeStructure with Pattern Detection
- **MVVM Compliance**: Analysis of proper separation of concerns
- **Data Access Patterns**: Repository, Unit of Work, Active Record detection
- **Web Patterns**: MVC, MVP, API controller patterns
- **UI-Business Logic Separation**: Validation of architectural boundaries

### GenerateCodeFacts with Multi-Language Context
- **SQL Operations**: Documentation of CRUD operations and data access
- **UI Behavior**: Description of user interactions and data flow
- **Cross-Layer Communication**: How data flows through application layers

## Implementation Timeline

### Phase 1: SQL Analysis (Week 1)
**Priority**: High
**Effort**: 3-4 days
**Deliverables**:
- SqlExtractor service for detecting SQL in C# code
- Enhanced chunking with data access grouping
- SQL relationship edges in symbol graph
- Basic SQL operation classification (CRUD)

### Phase 2: XAML/WPF Integration (Weeks 2-3)
**Priority**: Medium-High
**Effort**: 2 weeks
**Deliverables**:
- XamlAnalyzer service for XAML parsing
- MVVM pattern detection across file types
- UI element hierarchy analysis
- Data binding relationship mapping
- AnalyzeXamlBindings specialized tool

### Phase 3a: Basic Web Structure (Week 4)
**Priority**: Medium
**Effort**: 1 week
**Deliverables**:
- Web project type detection
- Basic file relationship mapping
- Project structure analysis for web applications

### Phase 3b: Advanced Razor Analysis (Future)
**Priority**: Low-Medium
**Effort**: 3-4 weeks
**Deliverables**:
- Razor syntax analysis integration
- Component relationship mapping
- Advanced web pattern detection

## Success Criteria

### Phase 1 Success Metrics
- ✅ Detect 95%+ of SQL strings in common patterns
- ✅ Correctly classify CRUD operations
- ✅ Group related data access code into meaningful chunks
- ✅ Generate useful facts about data access patterns

### Phase 2 Success Metrics
- ✅ Parse XAML files and extract UI hierarchy
- ✅ Map data bindings to ViewModel properties
- ✅ Detect MVVM pattern compliance
- ✅ Create meaningful cross-language chunks

### Phase 3 Success Metrics
- ✅ Identify web project types correctly
- ✅ Map basic file relationships (Controller→View)
- ✅ Provide useful web application structure analysis

## Risk Assessment

### Low Risk
- **SQL Analysis**: Well-defined patterns, existing string analysis capabilities
- **Basic XAML**: XML parsing is straightforward, clear relationship patterns

### Medium Risk
- **Advanced XAML**: Data binding expressions can be complex
- **MVVM Detection**: Requires understanding of architectural patterns

### High Risk
- **Razor Analysis**: Requires deep integration with ASP.NET compilation
- **Blazor Analysis**: Rapidly evolving technology with complex component model

## Dependencies and Requirements

### New NuGet Packages
```xml
<!-- For XAML Analysis -->
<PackageReference Include="System.Xaml" Version="8.0.0" />

<!-- For SQL Analysis -->
<PackageReference Include="Microsoft.SqlServer.TransactSql.ScriptDom" Version="161.8905.0" />

<!-- For Razor Analysis (Phase 3b) -->
<PackageReference Include="Microsoft.AspNetCore.Razor.Language" Version="8.0.0" />
```

### Infrastructure Requirements
- Enhanced error handling for multi-language parsing
- Configurable analysis scope (which languages to include)
- Performance optimization for large multi-language projects
- Memory management for complex parsing operations

## Integration with Existing Tools

### Backward Compatibility
- All existing C#-only functionality remains unchanged
- New multi-language features are opt-in via parameters
- Existing JSON output formats are preserved and extended

### Configuration Options
```csharp
// Enhanced tool signatures
ExtractSymbolGraph(
    string path,
    string scope = "file",
    bool includeXaml = false,        // NEW
    bool includeSql = false,         // NEW
    bool includeWebMarkup = false,   // NEW
    // ... existing parameters
)

ChunkCodeBySemantics(
    string path,
    string strategy = "semantic",
    bool crossLanguage = false,      // NEW
    // ... existing parameters
)
```

## Future Considerations

### Extensibility
- Plugin architecture for additional markup analyzers
- Support for other .NET languages (F#, VB.NET)
- Integration with external tools (Swagger, OpenAPI)

### Performance Optimization
- Lazy loading of language-specific analyzers
- Caching of parsed markup structures
- Parallel processing of different file types

### Advanced Features
- Real-time analysis of file changes
- Integration with build systems
- Export to specialized tools (UI designers, database tools)

## Conclusion

This multi-phase approach provides a clear path to extend the Roslyn MCP Server's capabilities while managing complexity and risk. Starting with SQL analysis provides immediate value with manageable complexity, while the XAML integration addresses the common MVVM scenario. ASP.NET markup analysis can be approached incrementally based on specific needs and requirements.

The key insight is that modern .NET applications are inherently multi-language, and analyzing them in isolation loses critical architectural context. By extending the existing tools rather than creating completely separate ones, we maintain the holistic view that makes the analysis truly valuable.
