using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace RoslynWebApi.Models;

/// <summary>
/// Request model for file validation operations
/// </summary>
public class ValidateFileRequest
{
    /// <summary>
    /// Path to the C# file to validate
    /// </summary>
    [Required]
    public string FilePath { get; set; } = string.Empty;

    /// <summary>
    /// Whether to run code analyzers (default: true)
    /// </summary>
    public bool RunAnalyzers { get; set; } = true;
}

/// <summary>
/// Request model for project metadata extraction
/// </summary>
public class ExtractProjectMetadataRequest
{
    /// <summary>
    /// Path to the .csproj file or a file within the project
    /// </summary>
    [Required]
    public string ProjectPath { get; set; } = string.Empty;
}

/// <summary>
/// Request model for finding symbol usages
/// </summary>
public class FindUsagesRequest
{
    /// <summary>
    /// Path to the file containing the symbol
    /// </summary>
    [Required]
    public string FilePath { get; set; } = string.Empty;

    /// <summary>
    /// Line number (1-based)
    /// </summary>
    [Range(1, int.MaxValue)]
    public int Line { get; set; }

    /// <summary>
    /// Column number (1-based)
    /// </summary>
    [Range(1, int.MaxValue)]
    public int Column { get; set; }
}

/// <summary>
/// Request model for code chunking operations
/// </summary>
public class ChunkCodeRequest
{
    /// <summary>
    /// Path to the C# file or project
    /// </summary>
    [Required]
    public string Path { get; set; } = string.Empty;

    /// <summary>
    /// Chunking strategy: 'class', 'method', 'feature', 'namespace'
    /// </summary>
    public string Strategy { get; set; } = "class";

    /// <summary>
    /// Include dependency relationships
    /// </summary>
    public bool IncludeDependencies { get; set; } = true;
}

/// <summary>
/// Request model for code structure analysis
/// </summary>
public class AnalyzeStructureRequest
{
    /// <summary>
    /// Path to the C# file or project
    /// </summary>
    [Required]
    public string Path { get; set; } = string.Empty;

    /// <summary>
    /// Include design pattern detection
    /// </summary>
    public bool DetectPatterns { get; set; } = true;

    /// <summary>
    /// Calculate complexity metrics
    /// </summary>
    public bool CalculateMetrics { get; set; } = true;
}

/// <summary>
/// Request model for generating code facts
/// </summary>
public class GenerateCodeFactsRequest
{
    /// <summary>
    /// Path to the C# file or project
    /// </summary>
    [Required]
    public string Path { get; set; } = string.Empty;

    /// <summary>
    /// Output format: 'json', 'markdown', 'text'
    /// </summary>
    public string Format { get; set; } = "json";

    /// <summary>
    /// Include natural language descriptions
    /// </summary>
    public bool IncludeDescriptions { get; set; } = true;
}

/// <summary>
/// Request model for symbol graph extraction
/// </summary>
public class ExtractSymbolGraphRequest
{
    /// <summary>
    /// Path to the C# file or project
    /// </summary>
    [Required]
    public string Path { get; set; } = string.Empty;

    /// <summary>
    /// Graph scope: 'file', 'project', 'solution'
    /// </summary>
    public string Scope { get; set; } = "file";

    /// <summary>
    /// Include inheritance relationships
    /// </summary>
    public bool IncludeInheritance { get; set; } = true;

    /// <summary>
    /// Include method call relationships
    /// </summary>
    public bool IncludeMethodCalls { get; set; } = true;

    /// <summary>
    /// Include field/property access relationships
    /// </summary>
    public bool IncludeFieldAccess { get; set; } = true;

    /// <summary>
    /// Include namespace relationships
    /// </summary>
    public bool IncludeNamespaces { get; set; } = true;

    /// <summary>
    /// Include XAML analysis
    /// </summary>
    public bool IncludeXaml { get; set; } = false;

    /// <summary>
    /// Include SQL analysis
    /// </summary>
    public bool IncludeSql { get; set; } = false;

    /// <summary>
    /// Maximum depth for relationship traversal
    /// </summary>
    [Range(1, 10)]
    public int MaxDepth { get; set; } = 3;
}

/// <summary>
/// Request model for multi-language code chunking
/// </summary>
public class ChunkMultiLanguageCodeRequest
{
    /// <summary>
    /// Path to the C# file or project
    /// </summary>
    [Required]
    public string Path { get; set; } = string.Empty;

    /// <summary>
    /// Chunking strategy: 'feature', 'dataaccess', 'mvvm', 'component'
    /// </summary>
    public string Strategy { get; set; } = "feature";

    /// <summary>
    /// Include dependency relationships
    /// </summary>
    public bool IncludeDependencies { get; set; } = true;

    /// <summary>
    /// Include XAML analysis
    /// </summary>
    public bool IncludeXaml { get; set; } = false;

    /// <summary>
    /// Include SQL analysis
    /// </summary>
    public bool IncludeSql { get; set; } = false;
}

/// <summary>
/// Request model for unified semantic graph extraction
/// </summary>
public class ExtractUnifiedSemanticGraphRequest
{
    /// <summary>
    /// Path to the solution file (.sln) or project file (.csproj)
    /// </summary>
    [Required]
    public string Path { get; set; } = string.Empty;

    /// <summary>
    /// Include architectural role classification
    /// </summary>
    public bool IncludeRoles { get; set; } = true;

    /// <summary>
    /// Include feature boundary detection
    /// </summary>
    public bool IncludeFeatures { get; set; } = true;

    /// <summary>
    /// Include cross-project dependencies
    /// </summary>
    public bool IncludeCrossProject { get; set; } = true;

    /// <summary>
    /// Include cross-language relationships
    /// </summary>
    public bool IncludeCrossLanguage { get; set; } = false;
}

/// <summary>
/// Request model for SQL extraction from code
/// </summary>
public class ExtractSqlRequest
{
    /// <summary>
    /// Path to the C# file
    /// </summary>
    [Required]
    public string FilePath { get; set; } = string.Empty;
}

/// <summary>
/// Request model for XAML file analysis
/// </summary>
public class AnalyzeXamlRequest
{
    /// <summary>
    /// Path to the XAML file
    /// </summary>
    [Required]
    public string FilePath { get; set; } = string.Empty;
}

/// <summary>
/// Request model for MVVM relationship analysis
/// </summary>
public class AnalyzeMvvmRequest
{
    /// <summary>
    /// Path to the project file or a file within the project
    /// </summary>
    [Required]
    public string ProjectPath { get; set; } = string.Empty;
}
