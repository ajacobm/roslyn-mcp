# Changelog

All notable changes to the Roslyn MCP Server will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [2.0.0] - 2025-01-04

### Added - Multi-Language Analysis Support

#### New Models
- **SqlMetadata.cs**: Comprehensive SQL query metadata including query types, tables, parameters, and framework detection
- **XamlMetadata.cs**: XAML structure analysis including UI elements, data bindings, resources, and event handlers
- **MultiLanguageChunk.cs**: Cross-language code chunks that span C#, XAML, and SQL

#### New Services
- **SqlExtractor.cs**: Extracts and analyzes SQL queries from C# code
  - Supports string literal SQL, Entity Framework, Dapper, and stored procedure patterns
  - Detects CRUD operations and referenced database tables
  - Identifies query parameters and framework usage
- **XamlAnalyzer.cs**: Analyzes XAML files for UI structure and MVVM patterns
  - Parses UI element hierarchy and nesting depth
  - Extracts data binding expressions and targets
  - Identifies resource usage (styles, templates, converters)
  - Maps event handlers to code-behind methods
- **MultiLanguageChunker.cs**: Groups related code across multiple languages
  - Feature-based chunking spanning C#, XAML, and SQL
  - MVVM pattern analysis and relationship detection
  - Data access pattern grouping
  - Component-based analysis for reusable UI elements

#### New MCP Tools
- **ChunkMultiLanguageCode**: Break down code into cross-language chunks
  - Supports 4 strategies: feature, mvvm, dataaccess, component
  - Optional XAML and SQL analysis inclusion
  - Cross-language relationship detection
- **ExtractSqlFromCode**: Extract SQL queries and database operations from C# files
  - Comprehensive query analysis and classification
  - Framework detection (EF, Dapper, ADO.NET)
  - Table and parameter identification
- **AnalyzeXamlFile**: Analyze XAML files for UI structure and data bindings
  - Element hierarchy analysis
  - Data binding validation and mapping
  - Resource usage tracking
  - Event handler identification
- **AnalyzeMvvmRelationships**: Analyze MVVM patterns across entire projects
  - View-ViewModel-Model relationship mapping
  - MVVM compliance checking
  - Data binding validation
  - Architectural pattern detection

#### Enhanced Existing Tools
- **ExtractSymbolGraph**: Added optional XAML and SQL analysis parameters
  - `includeXaml`: Include XAML-to-code relationships
  - `includeSql`: Include SQL query relationships

#### Documentation
- **MULTI_LANGUAGE_GUIDE.md**: Comprehensive guide for multi-language analysis
  - Detailed examples for each chunking strategy
  - Real-world usage scenarios
  - Best practices and troubleshooting
  - Integration with development workflows
- **ASSUMPTIONS.md**: Critical assumptions and naming conventions documentation
  - File naming conventions for MVVM pattern detection
  - SQL detection patterns and requirements
  - XAML structure assumptions
  - Directory structure expectations
  - Breaking changes and workarounds
  - Validation checklist for accurate analysis
- **Updated README.md**: Added multi-language capabilities overview
  - New tool descriptions and examples
  - Supported technologies and frameworks
  - Updated technical details

#### Dependencies
- **Portable.Xaml 0.26.0**: Cross-platform XAML parsing support
- **Microsoft.SqlServer.TransactSql.ScriptDom 161.8905.0**: SQL parsing and analysis

### Changed
- Project title updated to "Roslyn Multi-Language Analysis MCP Server"
- Enhanced project description to reflect multi-language capabilities
- Updated tool descriptions to include new multi-language features

### Technical Details
- All new services follow established patterns for error handling and logging
- Multi-language analysis is opt-in via tool parameters
- Backward compatibility maintained for all existing functionality
- Cross-platform support maintained (Windows, macOS, Linux)

### Supported Analysis Patterns
- **MVVM Architecture**: Complete View-ViewModel-Model relationship analysis
- **Repository Pattern**: Data access layer analysis with SQL extraction
- **Feature Modules**: Cross-language feature boundary detection
- **UI Components**: Reusable XAML component analysis
- **Data Access Patterns**: Entity Framework, Dapper, and ADO.NET analysis

## [1.0.0] - 2024-12-XX

### Added
- Initial release with core C# analysis capabilities
- **ValidateFile**: C# file validation with Roslyn
- **ExtractProjectMetadata**: Project metadata extraction
- **FindUsages**: Symbol reference finding
- **ChunkCodeBySemantics**: C# code chunking
- **AnalyzeCodeStructure**: Architectural pattern analysis
- **GenerateCodeFacts**: Code documentation generation
- **ExtractSymbolGraph**: Symbol relationship analysis
- Docker support with multi-stage builds
- MSBuild integration for project context
- Code analyzer support

### Technical Foundation
- Microsoft.CodeAnalysis libraries integration
- MSBuildWorkspace for project loading
- Standard diagnostic analyzer support
- JSON output formatting
- Error handling and logging
