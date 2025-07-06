using System.ComponentModel;
using System.Text.Json.Serialization;
using Microsoft.CodeAnalysis;

namespace RoslynRuntime.Models
{
    public class SymbolGraph
    {
        public List<SymbolNode> Nodes { get; set; } = new();
        public List<SymbolEdge> Edges { get; set; } = new();
        public GraphMetadata Metadata { get; set; } = new();
        public Dictionary<string, object> Statistics { get; set; } = new();
        public GraphDatabaseFormats? DatabaseFormats { get; set; }
    }

    public class SymbolNode
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public SymbolKind Kind { get; set; }
        public string TypeName { get; set; } = string.Empty;
        public SymbolLocation? Location { get; set; }
        public AccessibilityLevel Accessibility { get; set; }
        public List<string> Modifiers { get; set; } = new();
        public Dictionary<string, object> Properties { get; set; } = new();
        public SymbolMetrics Metrics { get; set; } = new();
    }

    public class SymbolEdge
    {
        public string Id { get; set; } = string.Empty;
        public string SourceId { get; set; } = string.Empty;
        public string TargetId { get; set; } = string.Empty;
        public RelationshipType Type { get; set; }
        public string Label { get; set; } = string.Empty;
        public Dictionary<string, object> Properties { get; set; } = new();
        public SymbolLocation? Location { get; set; }
    }

    public enum RelationshipType
    {
        Inheritance,
        Implementation,
        Composition,
        Aggregation,
        MethodCall,
        FieldAccess,
        PropertyAccess,
        EventSubscription,
        GenericConstraint,
        Namespace,
        Assembly,
        Dependency,
        Override,
        Instantiation
    }

    public class SymbolMetrics
    {
        public int CyclomaticComplexity { get; set; }
        public int LinesOfCode { get; set; }
        public int NumberOfParameters { get; set; }
        public int NumberOfMethods { get; set; }
        public int NumberOfProperties { get; set; }
        public int NumberOfFields { get; set; }
        public int IncomingReferences { get; set; }
        public int OutgoingReferences { get; set; }
    }

    public class GraphMetadata
    {
        public string Scope { get; set; } = string.Empty;
        public string RootPath { get; set; } = string.Empty;
        public DateTime GeneratedAt { get; set; }
        public int TotalNodes { get; set; }
        public int TotalEdges { get; set; }
        public Dictionary<string, int> NodeTypeDistribution { get; set; } = new();
        public Dictionary<string, int> EdgeTypeDistribution { get; set; } = new();
        public List<string> ProcessedFiles { get; set; } = new();
    }

    public enum AccessibilityLevel
    {
        Public,
        Private,
        Protected,
        Internal,
        ProtectedInternal,
        PrivateProtected
    }

    public class SymbolLocation
    {
        public string File { get; set; } = string.Empty;
        public int Line { get; set; }
        public int Column { get; set; }
        public int EndLine { get; set; }
        public int EndColumn { get; set; }
    }

    // Graph Database Integration Models
    public class GraphDatabaseFormats
    {
        public CypherStatements? CypherStatements { get; set; }
        public BulkImportFormat? BulkImport { get; set; }
    }

    public class CypherStatements
    {
        public List<string> Nodes { get; set; } = new();
        public List<string> Relationships { get; set; } = new();
    }

    public class BulkImportFormat
    {
        public List<BulkImportNode> Nodes { get; set; } = new();
        public List<BulkImportRelationship> Relationships { get; set; } = new();
    }

    public class BulkImportNode
    {
        public List<string> Labels { get; set; } = new();
        public Dictionary<string, object> Properties { get; set; } = new();
    }

    public class BulkImportRelationship
    {
        public string Type { get; set; } = string.Empty;
        public string StartNodeId { get; set; } = string.Empty;
        public string EndNodeId { get; set; } = string.Empty;
        public Dictionary<string, object> Properties { get; set; } = new();
    }

}
