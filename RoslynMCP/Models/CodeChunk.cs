using Microsoft.CodeAnalysis;

namespace RoslynMCP.Models;

public class CodeChunk
{
    public string Id { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty; // Class, Method, Property, etc.
    public string Name { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public Location Location { get; set; } = new();
    public List<string> Dependencies { get; set; } = new();
    public Dictionary<string, object> Metadata { get; set; } = new();
    public int ComplexityScore { get; set; }
}

public class ChunkingResult
{
    public List<CodeChunk> Chunks { get; set; } = new();
    public Dictionary<string, List<string>> Relationships { get; set; } = new();
    public ChunkingMetadata Metadata { get; set; } = new();
}

public class ChunkingMetadata
{
    public string Strategy { get; set; } = string.Empty;
    public int TotalChunks { get; set; }
    public DateTime AnalysisTime { get; set; }
    public string FilePath { get; set; } = string.Empty;
    public Dictionary<string, int> ChunkTypeDistribution { get; set; } = new();
}

public class Location
{
    public string FilePath { get; set; } = string.Empty;
    public int StartLine { get; set; }
    public int EndLine { get; set; }
    public int StartColumn { get; set; }
    public int EndColumn { get; set; }
}
