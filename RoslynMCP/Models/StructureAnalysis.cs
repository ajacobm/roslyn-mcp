namespace RoslynMCP.Models;

public class StructureAnalysis
{
    public List<DesignPattern> DetectedPatterns { get; set; } = new();
    public List<CodeMetric> Metrics { get; set; } = new();
    public List<CodeSmell> CodeSmells { get; set; } = new();
    public DependencyGraph Dependencies { get; set; } = new();
    public ArchitecturalInsights Insights { get; set; } = new();
    public DateTime AnalysisTime { get; set; }
    public string FilePath { get; set; } = string.Empty;
}

public class DesignPattern
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public List<Location> Locations { get; set; } = new();
    public double Confidence { get; set; }
    public Dictionary<string, object> Evidence { get; set; } = new();
}

public class CodeMetric
{
    public string Name { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty; // Complexity, Maintainability, etc.
    public double Value { get; set; }
    public string Unit { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public Location? Location { get; set; }
}

public class CodeSmell
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Severity { get; set; } = string.Empty; // Low, Medium, High, Critical
    public Location Location { get; set; } = new();
    public string Recommendation { get; set; } = string.Empty;
    public Dictionary<string, object> Details { get; set; } = new();
}

public class DependencyGraph
{
    public List<DependencyNode> Nodes { get; set; } = new();
    public List<DependencyEdge> Edges { get; set; } = new();
    public List<CircularDependency> CircularDependencies { get; set; } = new();
    public Dictionary<string, int> CouplingMetrics { get; set; } = new();
}

public class DependencyNode
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty; // Class, Interface, Namespace, etc.
    public Location Location { get; set; } = new();
    public int IncomingDependencies { get; set; }
    public int OutgoingDependencies { get; set; }
}

public class DependencyEdge
{
    public string FromId { get; set; } = string.Empty;
    public string ToId { get; set; } = string.Empty;
    public string DependencyType { get; set; } = string.Empty; // Inheritance, Composition, Usage, etc.
    public int Weight { get; set; } = 1;
}

public class CircularDependency
{
    public List<string> NodeIds { get; set; } = new();
    public string Description { get; set; } = string.Empty;
    public string Severity { get; set; } = string.Empty;
}

public class ArchitecturalInsights
{
    public double OverallComplexity { get; set; }
    public double Maintainability { get; set; }
    public double Testability { get; set; }
    public List<string> Recommendations { get; set; } = new();
    public Dictionary<string, object> QualityMetrics { get; set; } = new();
}
