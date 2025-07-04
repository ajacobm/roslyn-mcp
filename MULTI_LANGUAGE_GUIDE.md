# Multi-Language Analysis Guide

## Overview

The Roslyn MCP Server provides comprehensive analysis capabilities for modern .NET applications that span multiple languages and technologies. This guide demonstrates how to effectively use the multi-language features to analyze C#, XAML, and SQL code in unified context.

## Table of Contents

1. [Getting Started](#getting-started)
2. [Multi-Language Chunking Strategies](#multi-language-chunking-strategies)
3. [SQL Analysis](#sql-analysis)
4. [XAML Analysis](#xaml-analysis)
5. [MVVM Pattern Analysis](#mvvm-pattern-analysis)
6. [Real-World Examples](#real-world-examples)
7. [Best Practices](#best-practices)
8. [Troubleshooting](#troubleshooting)

## Getting Started

### Prerequisites

Ensure your project includes the necessary dependencies for multi-language analysis:

```xml
<PackageReference Include="Portable.Xaml" Version="0.26.0" />
<PackageReference Include="Microsoft.SqlServer.TransactSql.ScriptDom" Version="161.8905.0" />
```

### Basic Multi-Language Analysis

Start with a simple feature-based analysis that includes all supported languages:

```bash
ChunkMultiLanguageCode --path="MyWpfApp.csproj" --strategy="feature" --includeXaml=true --includeSql=true
```

This will analyze your entire project and group related code across C#, XAML, and SQL into feature-based chunks.

## Multi-Language Chunking Strategies

### 1. Feature-Based Chunking (`strategy="feature"`)

Groups related functionality across all languages, ideal for understanding complete features.

**Use Case**: Understanding how a user management feature spans across UI, business logic, and data access.

**Example**:
```bash
ChunkMultiLanguageCode --path="UserManagement.csproj" --strategy="feature" --includeXaml=true --includeSql=true
```

**Output Structure**:
```json
{
  "multiLanguageChunks": [
    {
      "name": "UserManagement",
      "type": "Feature",
      "components": [
        {
          "language": "XAML",
          "filePath": "Views/UserListView.xaml",
          "role": "View"
        },
        {
          "language": "CSharp",
          "filePath": "ViewModels/UserListViewModel.cs",
          "role": "ViewModel"
        },
        {
          "language": "CSharp",
          "filePath": "Services/UserService.cs",
          "role": "Service"
        },
        {
          "language": "SQL",
          "content": "SELECT * FROM Users WHERE IsActive = 1",
          "role": "Query"
        }
      ]
    }
  ]
}
```

### 2. MVVM-Based Chunking (`strategy="mvvm"`)

Analyzes View-ViewModel-Model relationships, perfect for WPF/UWP applications.

**Use Case**: Understanding MVVM architecture compliance and data flow.

**Example**:
```bash
ChunkMultiLanguageCode --path="WpfApp.csproj" --strategy="mvvm" --includeXaml=true
```

**What it analyzes**:
- XAML Views and their data bindings
- Corresponding ViewModels and their properties
- Model classes referenced by ViewModels
- Code-behind files and event handlers

### 3. Data Access Chunking (`strategy="dataaccess"`)

Focuses on database interaction patterns and SQL usage.

**Use Case**: Analyzing data access patterns, SQL query optimization, and repository implementations.

**Example**:
```bash
ChunkMultiLanguageCode --path="DataLayer.csproj" --strategy="dataaccess" --includeSql=true
```

**What it analyzes**:
- Repository pattern implementations
- Entity Framework contexts and configurations
- Dapper query patterns
- Raw SQL queries and stored procedure calls

### 4. Component-Based Chunking (`strategy="component"`)

Groups reusable UI components with their code-behind.

**Use Case**: Analyzing custom controls and reusable UI components.

**Example**:
```bash
ChunkMultiLanguageCode --path="ControlLibrary.csproj" --strategy="component" --includeXaml=true
```

## SQL Analysis

### Supported SQL Patterns

The SQL extractor recognizes various patterns of SQL usage in C# code:

#### 1. String Literal SQL
```csharp
var sql = "SELECT * FROM Users WHERE Id = @id";
var users = connection.Query<User>(sql, new { id = userId });
```

#### 2. Entity Framework Raw SQL
```csharp
var users = context.Users
    .FromSqlRaw("SELECT * FROM Users WHERE LastLogin > {0}", DateTime.Now.AddDays(-30))
    .ToList();
```

#### 3. Dapper Queries
```csharp
var users = connection.Query<User>(
    "SELECT Id, Name, Email FROM Users WHERE IsActive = @active",
    new { active = true }
);
```

#### 4. Stored Procedure Calls
```csharp
var result = context.Database.ExecuteSqlRaw(
    "EXEC GetUsersByRole @roleName",
    new SqlParameter("@roleName", "Admin")
);
```

### SQL Analysis Example

```bash
ExtractSqlFromCode --filePath="Repositories/UserRepository.cs"
```

**Output**:
```json
{
  "queries": [
    {
      "content": "SELECT * FROM Users WHERE IsActive = @active",
      "type": "Select",
      "framework": "Dapper",
      "tables": ["Users"],
      "parameters": ["@active"],
      "location": {
        "filePath": "Repositories/UserRepository.cs",
        "line": 25,
        "column": 15
      }
    }
  ],
  "operationCounts": {
    "select": 5,
    "insert": 2,
    "update": 3,
    "delete": 1
  },
  "referencedTables": ["Users", "Roles", "UserRoles"]
}
```

## XAML Analysis

### XAML Structure Analysis

Analyze XAML files for UI structure, data bindings, and resource usage:

```bash
AnalyzeXamlFile --filePath="Views/MainWindow.xaml"
```

**What it analyzes**:
- UI element hierarchy and nesting depth
- Data binding expressions and their targets
- Resource references (styles, templates, converters)
- Event handler mappings
- Named elements for code-behind access

### Example XAML Analysis Output

```json
{
  "elements": [
    {
      "name": "UserListGrid",
      "type": "DataGrid",
      "nestingLevel": 2,
      "hasDataBinding": true
    }
  ],
  "bindings": [
    {
      "targetProperty": "ItemsSource",
      "bindingPath": "Users",
      "bindingMode": "OneWay",
      "element": "UserListGrid"
    }
  ],
  "resources": [
    {
      "key": "UserItemTemplate",
      "type": "DataTemplate",
      "targetType": "User"
    }
  ],
  "eventHandlers": [
    {
      "event": "SelectionChanged",
      "handler": "UserGrid_SelectionChanged",
      "element": "UserListGrid"
    }
  ]
}
```

## MVVM Pattern Analysis

### Project-Wide MVVM Analysis

Analyze MVVM relationships across an entire project:

```bash
AnalyzeMvvmRelationships --projectPath="WpfApp.csproj"
```

**What it analyzes**:
- View-ViewModel naming conventions and relationships
- Data binding compliance and patterns
- Command pattern usage
- Property change notification implementation
- Model-ViewModel relationships

### Example MVVM Analysis Output

```json
{
  "mvvmRelationships": [
    {
      "viewFile": "Views/UserListView.xaml",
      "viewModelFile": "ViewModels/UserListViewModel.cs",
      "modelFiles": ["Models/User.cs", "Models/Role.cs"],
      "bindingCompliance": {
        "hasDataContext": true,
        "usesCommands": true,
        "implementsINotifyPropertyChanged": true
      },
      "dataBindings": [
        {
          "property": "Users",
          "bindingPath": "Users",
          "isValidBinding": true
        }
      ]
    }
  ],
  "complianceMetrics": {
    "mvvmCompliantViews": 8,
    "totalViews": 10,
    "compliancePercentage": 80
  }
}
```

## Real-World Examples

### Example 1: E-Commerce Application Analysis

**Scenario**: Analyzing a WPF e-commerce application with product catalog, shopping cart, and order management.

```bash
# Analyze the entire application by features
ChunkMultiLanguageCode --path="ECommerceApp.csproj" --strategy="feature" --includeXaml=true --includeSql=true

# Focus on data access patterns
ChunkMultiLanguageCode --path="ECommerceApp.csproj" --strategy="dataaccess" --includeSql=true

# Analyze MVVM compliance
AnalyzeMvvmRelationships --projectPath="ECommerceApp.csproj"
```

**Expected Results**:
- Feature chunks for Product Catalog, Shopping Cart, Order Management
- Data access chunks showing repository patterns and SQL queries
- MVVM analysis showing View-ViewModel relationships

### Example 2: Line-of-Business Application

**Scenario**: Analyzing a complex LOB application with multiple modules and data sources.

```bash
# Analyze by business modules
ChunkMultiLanguageCode --path="LOBApp.csproj" --strategy="feature" --includeXaml=true --includeSql=true

# Extract all SQL for database optimization review
ExtractSqlFromCode --filePath="DataAccess/CustomerRepository.cs"
ExtractSqlFromCode --filePath="DataAccess/OrderRepository.cs"
ExtractSqlFromCode --filePath="DataAccess/ProductRepository.cs"
```

### Example 3: Custom Control Library

**Scenario**: Analyzing a reusable control library for UI components.

```bash
# Analyze reusable components
ChunkMultiLanguageCode --path="ControlLibrary.csproj" --strategy="component" --includeXaml=true

# Analyze individual complex controls
AnalyzeXamlFile --filePath="Controls/AdvancedDataGrid.xaml"
AnalyzeXamlFile --filePath="Controls/CustomDatePicker.xaml"
```

## Best Practices

### 1. Choosing the Right Strategy

- **Feature**: Use for understanding complete business functionality
- **MVVM**: Use for architectural analysis and compliance checking
- **DataAccess**: Use for database optimization and query analysis
- **Component**: Use for UI component libraries and reusable controls

### 2. Incremental Analysis

Start with broad analysis and drill down:

```bash
# 1. Start with feature-based overview
ChunkMultiLanguageCode --path="MyApp.csproj" --strategy="feature" --includeXaml=true --includeSql=true

# 2. Focus on specific areas of interest
AnalyzeMvvmRelationships --projectPath="MyApp.csproj"

# 3. Drill down to specific files
AnalyzeXamlFile --filePath="Views/ComplexView.xaml"
ExtractSqlFromCode --filePath="Repositories/CriticalRepository.cs"
```

### 3. Performance Considerations

- Use `includeDependencies=false` for faster analysis when relationships aren't needed
- Analyze specific files rather than entire projects for quick checks
- Consider chunking strategies based on your analysis goals

### 4. Integration with Development Workflow

- Use SQL extraction to identify queries that need optimization
- Use MVVM analysis to ensure architectural compliance
- Use feature chunking to understand impact of changes across languages

## Troubleshooting

### Common Issues

#### 1. XAML Parsing Errors
**Problem**: XAML files fail to parse
**Solution**: Ensure XAML files are well-formed XML and use supported XAML features

#### 2. SQL Not Detected
**Problem**: SQL queries in code are not being found
**Solution**: Ensure SQL is in string literals or recognized ORM patterns (EF, Dapper)

#### 3. MVVM Relationships Not Found
**Problem**: View-ViewModel relationships are not detected
**Solution**: Follow naming conventions (View/ViewModel suffixes) or ensure proper file organization

#### 4. Performance Issues
**Problem**: Analysis takes too long on large projects
**Solution**: Use specific file analysis or disable dependency analysis

### Debug Tips

1. **Enable verbose logging**: Check console output for detailed analysis information
2. **Test with simple files**: Start with minimal examples to verify functionality
3. **Check file paths**: Ensure all file paths are accessible and correctly formatted
4. **Verify project structure**: Ensure project files are valid and can be loaded by MSBuild

## Advanced Usage

### Custom Analysis Workflows

Combine multiple tools for comprehensive analysis:

```bash
# 1. Extract project metadata
ExtractProjectMetadata --projectPath="MyApp.csproj"

# 2. Analyze symbol relationships with multi-language support
ExtractSymbolGraph --path="MyApp.csproj" --scope="project" --includeXaml=true --includeSql=true

# 3. Perform multi-language chunking
ChunkMultiLanguageCode --path="MyApp.csproj" --strategy="feature" --includeXaml=true --includeSql=true

# 4. Analyze specific patterns
AnalyzeMvvmRelationships --projectPath="MyApp.csproj"
```

### Integration with CI/CD

Use the tools in automated workflows:

```yaml
# Example GitHub Actions step
- name: Analyze Code Architecture
  run: |
    dotnet run --project RoslynMCP -- AnalyzeMvvmRelationships --projectPath="src/MyApp.csproj" > mvvm-analysis.json
    dotnet run --project RoslynMCP -- ExtractSqlFromCode --filePath="src/DataAccess/Repository.cs" > sql-analysis.json
```

## Conclusion

The multi-language analysis capabilities of the Roslyn MCP Server provide powerful insights into modern .NET applications. By understanding how C#, XAML, and SQL work together, you can:

- Improve architectural compliance
- Optimize database interactions
- Understand feature boundaries
- Maintain code quality across languages

Use this guide as a reference for implementing comprehensive code analysis in your development workflow.
