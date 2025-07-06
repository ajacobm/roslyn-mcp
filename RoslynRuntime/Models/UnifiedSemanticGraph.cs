using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FindSymbols;
using System.Collections.Immutable;

namespace RoslynRuntime.Models;

/// <summary>
/// Represents a unified semantic graph of an entire solution with cross-project symbol relationships
/// </summary>
public class UnifiedSemanticGraph
{
    public Dictionary<string, SemanticSymbolNode> Nodes { get; set; } = new();
    public List<SemanticRelationship> Relationships { get; set; } = new();
    public SemanticGraphMetadata Metadata { get; set; } = new();
    public Dictionary<string, ProjectSemanticInfo> ProjectInfo { get; set; } = new();
    public List<CrossProjectDependency> CrossProjectDependencies { get; set; } = new();

    public UnifiedSemanticGraph() { }

    public UnifiedSemanticGraph(Dictionary<string, SemanticSymbolNode> nodes)
    {
        Nodes = nodes;
    }

    /// <summary>
    /// Find all symbols that depend on the given symbol
    /// </summary>
    public IEnumerable<SemanticSymbolNode> GetDependents(string symbolId)
    {
        return Relationships
            .Where(r => r.TargetSymbolId == symbolId)
            .Select(r => Nodes.GetValueOrDefault(r.SourceSymbolId))
            .Where(n => n != null)!;
    }

    /// <summary>
    /// Find all symbols that the given symbol depends on
    /// </summary>
    public IEnumerable<SemanticSymbolNode> GetDependencies(string symbolId)
    {
        return Relationships
            .Where(r => r.SourceSymbolId == symbolId)
            .Select(r => Nodes.GetValueOrDefault(r.TargetSymbolId))
            .Where(n => n != null)!;
    }

    /// <summary>
    /// Get all symbols within a specific feature boundary
    /// </summary>
    public IEnumerable<SemanticSymbolNode> GetFeatureSymbols(string featureName)
    {
        return Nodes.Values.Where(n => n.FeatureBoundary == featureName);
    }

    /// <summary>
    /// Find the shortest dependency path between two symbols
    /// </summary>
    public List<SemanticSymbolNode>? FindDependencyPath(string fromSymbolId, string toSymbolId)
    {
        var visited = new HashSet<string>();
        var queue = new Queue<(string symbolId, List<string> path)>();
        queue.Enqueue((fromSymbolId, new List<string> { fromSymbolId }));

        while (queue.Count > 0)
        {
            var (currentId, path) = queue.Dequeue();
            
            if (currentId == toSymbolId)
            {
                return path.Select(id => Nodes[id]).ToList();
            }

            if (visited.Contains(currentId))
                continue;

            visited.Add(currentId);

            var dependencies = GetDependencies(currentId);
            foreach (var dep in dependencies)
            {
                if (!visited.Contains(dep.Id))
                {
                    var newPath = new List<string>(path) { dep.Id };
                    queue.Enqueue((dep.Id, newPath));
                }
            }
        }

        return null; // No path found
    }
}

/// <summary>
/// Represents a symbol node in the semantic graph with rich metadata
/// </summary>
public class SemanticSymbolNode
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public SymbolKind Kind { get; set; }
    public string TypeName { get; set; } = string.Empty;
    public SymbolLocation? Location { get; set; } // Use custom SymbolLocation for graph compatibility
    public Accessibility Accessibility { get; set; }
    public List<string> Modifiers { get; set; } = new();
    
    // Semantic-specific properties
    public string ProjectId { get; set; } = string.Empty;
    public string AssemblyName { get; set; } = string.Empty;
    public string? FeatureBoundary { get; set; }
    public ArchitecturalRole Role { get; set; } = ArchitecturalRole.Unknown;
    public List<string> Interfaces { get; set; } = new();
    public string? BaseType { get; set; }
    public List<string> GenericTypeParameters { get; set; } = new();
    
    // Cross-language information
    public Dictionary<string, object> LanguageSpecificData { get; set; } = new();
    
    // Metrics and analysis data
    public SemanticMetrics Metrics { get; set; } = new();
    public List<string> Tags { get; set; } = new(); // For categorization
    
    // Reference information
    public int IncomingReferenceCount { get; set; }
    public int OutgoingReferenceCount { get; set; }
    public List<string> ReferencedBy { get; set; } = new();
    public List<string> References { get; set; } = new();
}

/// <summary>
/// Represents a semantic relationship between symbols
/// </summary>
public class SemanticRelationship
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string SourceSymbolId { get; set; } = string.Empty;
    public string TargetSymbolId { get; set; } = string.Empty;
    public SemanticRelationshipType Type { get; set; }
    public string Label { get; set; } = string.Empty;
    public SymbolLocation? Location { get; set; } // Use custom SymbolLocation for graph compatibility
    public double Strength { get; set; } = 1.0; // Relationship strength (0.0 - 1.0)
    public Dictionary<string, object> Properties { get; set; } = new();
    
    // Context information
    public string? ContextMethod { get; set; } // Method where relationship occurs
    public string? ContextType { get; set; } // Type where relationship occurs
    public bool IsCrossProject { get; set; }
    public bool IsCrossLanguage { get; set; }
}

/// <summary>
/// Enhanced relationship types based on semantic analysis
/// </summary>
public enum SemanticRelationshipType
{
    // Type relationships
    Inheritance,
    Implementation,
    Composition,
    Aggregation,
    Association,
    
    // Method relationships
    MethodCall,
    MethodOverride,
    MethodImplementation,
    
    // Property/Field relationships
    PropertyAccess,
    FieldAccess,
    PropertyBinding, // XAML data binding
    
    // Event relationships
    EventSubscription,
    EventPublication,
    
    // Dependency relationships
    DependencyInjection,
    ServiceRegistration,
    ServiceConsumption,
    
    // Data relationships
    DatabaseAccess,
    EntityMapping,
    QueryExecution,
    
    // Architectural relationships
    LayerDependency,
    ModuleDependency,
    FeatureDependency,
    
    // Cross-language relationships
    XamlCodeBehind,
    XamlDataContext,
    SqlQuery,
    LinqToSql,
    
    // Generic relationships
    GenericConstraint,
    TypeParameter,
    Instantiation,
    
    // Namespace relationships
    NamespaceContainment,
    UsingDirective,
    
    // Assembly relationships
    AssemblyReference,
    
    Unknown
}

/// <summary>
/// Architectural role classification for symbols
/// </summary>
public enum ArchitecturalRole
{
    // UI Layer
    View,
    ViewModel,
    CodeBehind,
    UserControl,
    Window,
    Page,
    
    // Business Layer
    Service,
    BusinessLogic,
    Domain,
    Entity,
    ValueObject,
    
    // Data Layer
    Repository,
    DataAccess,
    DbContext,
    DataModel,
    
    // Infrastructure
    Controller,
    ApiController,
    Middleware,
    Configuration,
    
    // Cross-cutting
    Utility,
    Helper,
    Extension,
    Attribute,
    
    // Framework
    Interface,
    AbstractClass,
    Factory,
    Builder,
    
    Unknown
}


/// <summary>
/// Semantic metrics for symbols
/// </summary>
public class SemanticMetrics
{
    public int CyclomaticComplexity { get; set; }
    public int LinesOfCode { get; set; }
    public int NumberOfMethods { get; set; }
    public int NumberOfProperties { get; set; }
    public int NumberOfFields { get; set; }
    public int NumberOfParameters { get; set; }
    public int DepthOfInheritance { get; set; }
    public int NumberOfChildren { get; set; }
    public double CouplingBetweenObjects { get; set; }
    public double LackOfCohesionOfMethods { get; set; }
    public int FanIn { get; set; } // Number of classes that depend on this class
    public int FanOut { get; set; } // Number of classes this class depends on
}

/// <summary>
/// Project-level semantic information
/// </summary>
public class ProjectSemanticInfo
{
    public string ProjectId { get; set; } = string.Empty;
    public string ProjectName { get; set; } = string.Empty;
    public string ProjectPath { get; set; } = string.Empty;
    public string TargetFramework { get; set; } = string.Empty;
    public List<string> Languages { get; set; } = new();
    public List<string> Dependencies { get; set; } = new();
    public Dictionary<ArchitecturalRole, int> RoleDistribution { get; set; } = new();
    public List<string> FeatureBoundaries { get; set; } = new();
    public ProjectMetrics Metrics { get; set; } = new();
}

/// <summary>
/// Cross-project dependency information
/// </summary>
public class CrossProjectDependency
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string SourceProjectId { get; set; } = string.Empty;
    public string TargetProjectId { get; set; } = string.Empty;
    public string DependencyType { get; set; } = string.Empty; // ProjectReference, PackageReference, etc.
    public List<string> SharedSymbols { get; set; } = new();
    public int ReferenceCount { get; set; }
    public double CouplingStrength { get; set; }
}

/// <summary>
/// Project-level metrics
/// </summary>
public class ProjectMetrics
{
    public int TotalSymbols { get; set; }
    public int TotalRelationships { get; set; }
    public int CrossProjectReferences { get; set; }
    public double AverageComplexity { get; set; }
    public double Maintainability { get; set; }
    public Dictionary<string, int> LanguageDistribution { get; set; } = new();
}

/// <summary>
/// Metadata for the entire semantic graph
/// </summary>
public class SemanticGraphMetadata
{
    public string SolutionPath { get; set; } = string.Empty;
    public DateTime AnalyzedAt { get; set; } = DateTime.UtcNow;
    public TimeSpan AnalysisDuration { get; set; }
    public string RoslynVersion { get; set; } = string.Empty;
    public int TotalProjects { get; set; }
    public int TotalSymbols { get; set; }
    public int TotalRelationships { get; set; }
    public int CrossProjectRelationships { get; set; }
    public int CrossLanguageRelationships { get; set; }
    public Dictionary<SymbolKind, int> SymbolDistribution { get; set; } = new();
    public Dictionary<SemanticRelationshipType, int> RelationshipDistribution { get; set; } = new();
    public Dictionary<ArchitecturalRole, int> RoleDistribution { get; set; } = new();
    public List<string> ProcessedFiles { get; set; } = new();
    public List<string> SkippedFiles { get; set; } = new();
    public List<string> Errors { get; set; } = new();
    public Dictionary<string, object> AdditionalMetrics { get; set; } = new();
}
