# Code Analysis Assumptions and Conventions

This document outlines the assumptions and conventions that the Roslyn MCP Server makes when analyzing multi-language .NET projects. Understanding these assumptions is critical for ensuring accurate analysis results.

## Table of Contents

1. [File Naming Conventions](#file-naming-conventions)
2. [Directory Structure Assumptions](#directory-structure-assumptions)
3. [MVVM Pattern Assumptions](#mvvm-pattern-assumptions)
4. [SQL Detection Assumptions](#sql-detection-assumptions)
5. [XAML Structure Assumptions](#xaml-structure-assumptions)
6. [Project Structure Assumptions](#project-structure-assumptions)
7. [Code Pattern Assumptions](#code-pattern-assumptions)
8. [Breaking Changes and Workarounds](#breaking-changes-and-workarounds)

## File Naming Conventions

### C# File Naming

#### ViewModel Detection
- **Assumption**: ViewModels end with `ViewModel` or `VM` suffix
- **Examples**: `UserListViewModel.cs`, `ProductVM.cs`
- **Breaking Case**: `UserListPresenter.cs`, `UserListController.cs`
- **Impact**: MVVM relationship detection will fail

#### Model Detection
- **Assumption**: Models end with `Model` or `Entity` suffix
- **Examples**: `UserModel.cs`, `ProductEntity.cs`, `Customer.cs` (if referenced in ViewModel)
- **Breaking Case**: `UserData.cs`, `ProductInfo.cs`
- **Impact**: Model-ViewModel relationships may not be detected

#### Service/Repository Detection
- **Assumption**: Data access classes contain specific keywords
- **Keywords**: `Repository`, `Service`, `Context`, `DataAccess`, `Dal`, `Dao`
- **Examples**: `UserRepository.cs`, `DataService.cs`, `ApplicationDbContext.cs`
- **Breaking Case**: `UserStore.cs`, `DataManager.cs`
- **Impact**: Data access chunking may miss relevant files

#### Code-Behind Detection
- **Assumption**: Code-behind files follow `.xaml.cs` pattern
- **Examples**: `MainWindow.xaml.cs`, `UserControl.xaml.cs`
- **Breaking Case**: Custom naming schemes
- **Impact**: View-CodeBehind relationships will not be detected

### XAML File Naming

#### View-ViewModel Relationship
- **Assumption**: XAML view name + "ViewModel" = corresponding ViewModel class
- **Examples**: 
  - `UserListView.xaml` → `UserListViewModel.cs`
  - `ProductEdit.xaml` → `ProductEditViewModel.cs`
- **Breaking Case**: 
  - `UserListView.xaml` → `UserListPresenter.cs`
  - `ProductEdit.xaml` → `ProductEditController.cs`
- **Impact**: MVVM relationship detection will fail

#### Component vs. Window/Page Detection
- **Assumption**: Reusable components don't contain `<Window>` or `<Page>` root elements
- **Examples**: 
  - Component: `<UserControl>`, `<Grid>`, `<StackPanel>`
  - Not Component: `<Window>`, `<Page>`
- **Breaking Case**: Custom root elements or unconventional structures
- **Impact**: Component-based chunking may misclassify files

## Directory Structure Assumptions

### Feature-Based Organization
- **Assumption**: Features are organized by directory structure
- **Expected Structure**:
  ```
  Features/
  ├── UserManagement/
  │   ├── Views/
  │   ├── ViewModels/
  │   └── Models/
  └── ProductCatalog/
      ├── Views/
      ├── ViewModels/
      └── Models/
  ```
- **Breaking Case**: Flat structure or technology-based organization
- **Impact**: Feature-based chunking may group unrelated files

### Technology-Based Folders
- **Assumption**: Standard folder names indicate file types
- **Expected Folders**: `Views`, `ViewModels`, `Models`, `Controllers`, `Services`, `Features`
- **Breaking Case**: `Presenters`, `Screens`, `Pages`, `Components`
- **Impact**: Feature detection may fail to properly categorize files

## MVVM Pattern Assumptions

### DataContext Binding
- **Assumption**: ViewModels are bound via DataContext in XAML
- **Expected Pattern**: `DataContext="{Binding SomeViewModel}"`
- **Breaking Case**: ViewModels set in code-behind or dependency injection
- **Impact**: View-ViewModel relationships will not be detected

### Property Naming
- **Assumption**: Binding paths in XAML match ViewModel property names exactly
- **Expected Pattern**: `{Binding UserName}` matches `public string UserName { get; set; }`
- **Breaking Case**: Property name transformations or computed bindings
- **Impact**: Shared property analysis may be incomplete

### Command Pattern
- **Assumption**: Commands follow `ICommand` interface pattern
- **Expected Pattern**: `public ICommand SaveCommand { get; set; }`
- **Breaking Case**: Custom command implementations or method-based commands
- **Impact**: Command detection may miss non-standard implementations

### Model References in ViewModels
- **Assumption**: Models are referenced by type name in ViewModel code
- **Expected Pattern**: `UserModel user` or `ProductEntity product`
- **Breaking Case**: Generic collections, interfaces, or dynamic types
- **Impact**: ViewModel-Model relationships may not be detected

## SQL Detection Assumptions

### String Literal SQL
- **Assumption**: SQL queries are in string literals starting with SQL keywords
- **Expected Pattern**: `var sql = "SELECT * FROM Users";`
- **Breaking Case**: 
  - Concatenated strings: `"SELECT * " + "FROM Users"`
  - String interpolation: `$"SELECT * FROM {tableName}"`
- **Impact**: SQL queries may not be detected

### Entity Framework Patterns
- **Assumption**: EF uses specific method names for raw SQL
- **Expected Methods**: `FromSqlRaw`, `FromSqlInterpolated`, `ExecuteSqlRaw`, `ExecuteSqlInterpolated`
- **Breaking Case**: Custom extension methods or older EF versions
- **Impact**: EF SQL queries may not be detected

### Dapper Patterns
- **Assumption**: Dapper uses standard method names
- **Expected Methods**: `Query`, `QueryAsync`, `Execute`, `ExecuteAsync`, `QueryFirst`, etc.
- **Breaking Case**: Custom Dapper extensions or wrapped methods
- **Impact**: Dapper queries may not be detected

### ADO.NET Patterns
- **Assumption**: SQL is assigned to `CommandText` property
- **Expected Pattern**: `command.CommandText = "SELECT * FROM Users"`
- **Breaking Case**: Dynamic command building or stored procedure calls
- **Impact**: ADO.NET SQL may not be detected

### SQL Syntax Requirements
- **Assumption**: SQL follows standard T-SQL syntax
- **Expected**: Valid T-SQL that can be parsed by SqlServer.TransactSql.ScriptDom
- **Breaking Case**: Database-specific syntax (MySQL, PostgreSQL, Oracle)
- **Impact**: SQL parsing may fail, falling back to regex patterns

## XAML Structure Assumptions

### Element Naming
- **Assumption**: Important elements have `Name` or `x:Name` attributes
- **Expected Pattern**: `<Button Name="SaveButton" />` or `<Button x:Name="SaveButton" />`
- **Breaking Case**: Elements without names or custom naming schemes
- **Impact**: Element identification may be incomplete

### Binding Syntax
- **Assumption**: Data bindings follow standard WPF syntax
- **Expected Pattern**: `{Binding PropertyName, Mode=TwoWay}`
- **Breaking Case**: Custom markup extensions or non-standard binding syntax
- **Impact**: Binding analysis may miss custom patterns

### Event Handler Naming
- **Assumption**: Event handlers follow standard naming patterns
- **Expected Suffixes**: `Click`, `Changed`, `Loaded`, `Unloaded`, `MouseEnter`, etc.
- **Breaking Case**: Custom event names or non-standard suffixes
- **Impact**: Event handler detection may be incomplete

### Resource Dictionary Structure
- **Assumption**: Resources are defined in standard resource sections
- **Expected Pattern**: `<Window.Resources>`, `<UserControl.Resources>`, `<ResourceDictionary>`
- **Breaking Case**: Merged dictionaries or external resource files
- **Impact**: Resource analysis may be incomplete

## Project Structure Assumptions

### Project File Location
- **Assumption**: `.csproj` files are in the root of the analysis scope
- **Expected**: Analysis starts from project file or finds containing project
- **Breaking Case**: Solution-level analysis or projects in subdirectories
- **Impact**: Project context may not be established correctly

### File Extensions
- **Assumption**: Standard file extensions are used
- **Expected**: `.cs` for C#, `.xaml` for XAML, `.csproj` for projects
- **Breaking Case**: Custom extensions or preprocessed files
- **Impact**: Files may not be included in analysis

## Code Pattern Assumptions

### Class Declaration Patterns
- **Assumption**: Classes follow standard C# declaration syntax
- **Expected Pattern**: `public class UserViewModel : INotifyPropertyChanged`
- **Breaking Case**: Partial classes, nested classes, or unconventional declarations
- **Impact**: Class detection and role assignment may fail

### Method Naming for Data Access
- **Assumption**: Data access methods contain SQL or database-related keywords
- **Expected Patterns**: Methods containing `SqlConnection`, `DbContext`, `IRepository`
- **Breaking Case**: Abstracted data access or custom naming conventions
- **Impact**: Data access role assignment may be incorrect

### Namespace Conventions
- **Assumption**: Namespaces reflect project structure and purpose
- **Expected Pattern**: `MyApp.ViewModels`, `MyApp.Models`, `MyApp.Services`
- **Breaking Case**: Flat namespaces or non-descriptive naming
- **Impact**: Feature grouping may be less accurate

## Breaking Changes and Workarounds

### When Conventions Don't Match

#### Alternative ViewModel Naming
**Problem**: ViewModels named `*Presenter` or `*Controller`
**Workaround**: 
1. Rename files to follow `*ViewModel` convention, or
2. Use explicit DataContext binding in XAML, or
3. Manually specify relationships in analysis configuration

#### Non-Standard Directory Structure
**Problem**: Technology-first organization (all ViewModels in one folder)
**Workaround**:
1. Reorganize into feature-based structure, or
2. Use namespace-based feature detection, or
3. Rely on file content analysis rather than structure

#### Custom SQL Patterns
**Problem**: SQL in configuration files or custom query builders
**Workaround**:
1. Extract SQL to string constants, or
2. Add comments with SQL content for detection, or
3. Use standard ORM patterns where possible

#### Non-Standard XAML Patterns
**Problem**: Custom markup extensions or binding patterns
**Workaround**:
1. Use standard WPF binding syntax where possible, or
2. Add explicit element names for important UI elements, or
3. Document custom patterns for manual analysis

### Configuration Options

Some assumptions can be overridden through analysis parameters:

```csharp
// Disable dependency analysis if naming conventions don't match
ChunkMultiLanguageCode(path, includeDependencies: false)

// Focus on specific file types
ExtractSqlFromCode(filePath) // Analyze individual files
AnalyzeXamlFile(filePath)    // Analyze individual XAML files

// Use different chunking strategies
ChunkMultiLanguageCode(path, strategy: "component") // Less dependent on naming
```

### Best Practices for Compatibility

1. **Follow Standard Conventions**: Use established naming patterns when possible
2. **Explicit Naming**: Use descriptive file and class names that indicate purpose
3. **Standard Patterns**: Follow MVVM, Repository, and other established patterns
4. **Documentation**: Document deviations from standard conventions
5. **Incremental Analysis**: Start with individual file analysis before project-wide analysis

## Impact Assessment

### High Impact Assumptions
- ViewModel naming conventions (breaks MVVM analysis)
- SQL string literal patterns (breaks SQL detection)
- XAML binding syntax (breaks binding analysis)

### Medium Impact Assumptions
- Directory structure (affects feature grouping)
- Model naming conventions (affects relationship detection)
- Event handler patterns (affects UI analysis)

### Low Impact Assumptions
- Namespace conventions (affects categorization)
- Comment patterns (affects documentation)
- File organization (affects performance)

## Validation Checklist

Before running multi-language analysis, verify:

- [ ] ViewModels end with `ViewModel` or `VM`
- [ ] Models end with `Model` or `Entity`
- [ ] XAML files have corresponding `*.xaml.cs` code-behind files
- [ ] SQL queries are in string literals or standard ORM patterns
- [ ] XAML uses standard WPF binding syntax
- [ ] Important UI elements have `Name` or `x:Name` attributes
- [ ] Project follows feature-based or standard directory structure
- [ ] Data access classes contain recognizable keywords

Following these conventions will ensure the most accurate and comprehensive analysis results.
