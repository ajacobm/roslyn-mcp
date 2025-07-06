using System.Text.Json.Serialization;

namespace RoslynRuntime.Models;

public class MultiLanguageChunk : CodeChunk
{
    public List<LanguageComponent> Components { get; set; } = new();
    public MultiLanguageChunkType ChunkType { get; set; }
    public Dictionary<string, object> CrossLanguageMetadata { get; set; } = new();
}

public class LanguageComponent
{
    public string Id { get; set; } = string.Empty;
    public string Language { get; set; } = string.Empty; // "CSharp", "XAML", "SQL"
    public string FilePath { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public SymbolLocation Location { get; set; } = new();
    public ComponentRole Role { get; set; }
    public Dictionary<string, object> LanguageSpecificMetadata { get; set; } = new();
}

public class MultiLanguageChunkingResult : ChunkingResult
{
    public List<MultiLanguageChunk> MultiLanguageChunks { get; set; } = new();
    public Dictionary<string, List<string>> CrossLanguageRelationships { get; set; } = new();
    public MultiLanguageChunkingMetadata MultiLanguageMetadata { get; set; } = new();
}

public class MultiLanguageChunkingMetadata : ChunkingMetadata
{
    public Dictionary<string, int> LanguageDistribution { get; set; } = new();
    public new Dictionary<string, int> ChunkTypeDistribution { get; set; } = new();
    public int CrossLanguageRelationships { get; set; }
    public List<string> SupportedLanguages { get; set; } = new();
}

public enum MultiLanguageChunkType
{
    Feature,        // Complete feature spanning multiple languages
    DataAccess,     // Database-related code (C# + SQL)
    UIFeature,      // UI-related code (XAML + C# + potentially SQL)
    MvvmPattern,    // MVVM pattern (View + ViewModel + Model)
    Service,        // Service layer (C# + potentially SQL)
    Repository,     // Repository pattern (C# + SQL)
    Controller,     // Web controller (C# + potentially SQL)
    Component,      // Reusable component (XAML + C#)
    Utility,        // Utility/helper code
    Configuration,  // Configuration-related code
    Unknown
}

public enum ComponentRole
{
    View,           // XAML view
    ViewModel,      // C# ViewModel
    Model,          // C# Model/Entity
    CodeBehind,     // C# code-behind
    DataAccess,     // C# data access code
    Query,          // SQL query
    Service,        // C# service
    Controller,     // C# controller
    Repository,     // C# repository
    Configuration,  // Configuration code
    Utility,        // Utility/helper
    Unknown
}
