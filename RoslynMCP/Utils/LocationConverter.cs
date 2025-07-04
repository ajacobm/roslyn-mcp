using Microsoft.CodeAnalysis;
using RoslynMCP.Models;

namespace RoslynMCP.Utils;

/// <summary>
/// Utility class for converting between Roslyn native Location types and custom SymbolLocation types
/// </summary>
public static class LocationConverter
{
    /// <summary>
    /// Convert Roslyn's Location to custom SymbolLocation for graph database compatibility
    /// </summary>
    public static SymbolLocation? ToSymbolLocation(Microsoft.CodeAnalysis.Location? location)
    {
        if (location == null || location.Kind != Microsoft.CodeAnalysis.LocationKind.SourceFile)
            return null;

        var lineSpan = location.GetLineSpan();
        
        return new SymbolLocation
        {
            File = location.SourceTree?.FilePath ?? "Unknown",
            Line = lineSpan.StartLinePosition.Line + 1, // Convert to 1-based
            Column = lineSpan.StartLinePosition.Character + 1, // Convert to 1-based  
            EndLine = lineSpan.EndLinePosition.Line + 1,
            EndColumn = lineSpan.EndLinePosition.Character + 1
        };
    }

    /// <summary>
    /// Convert custom SymbolLocation to a descriptive string for debugging
    /// </summary>
    public static string ToLocationString(SymbolLocation? location)
    {
        if (location == null)
            return "Unknown location";
            
        return $"{location.File}({location.Line},{location.Column})";
    }
    
    /// <summary>
    /// Create a minimal SymbolLocation for cases where we only have basic info
    /// </summary>
    public static SymbolLocation CreateMinimalLocation(string filePath, int line = 1, int column = 1)
    {
        return new SymbolLocation
        {
            File = filePath,
            Line = line,
            Column = column,
            EndLine = line,
            EndColumn = column
        };
    }
    
    /// <summary>
    /// Convert custom Location to SymbolLocation
    /// </summary>
    public static SymbolLocation ToSymbolLocation(Models.Location location)
    {
        return new SymbolLocation
        {
            File = location.FilePath,
            Line = location.StartLine,
            Column = location.StartColumn,
            EndLine = location.EndLine,
            EndColumn = location.EndColumn
        };
    }
    
    /// <summary>
    /// Convert SymbolLocation to custom Location
    /// </summary>
    public static Models.Location ToLocation(SymbolLocation symbolLocation)
    {
        return new Models.Location
        {
            FilePath = symbolLocation.File,
            StartLine = symbolLocation.Line,
            StartColumn = symbolLocation.Column,
            EndLine = symbolLocation.EndLine,
            EndColumn = symbolLocation.EndColumn
        };
    }
}
