using System.Text.Json.Serialization;

namespace RoslynMCP.Models;

public class XamlMetadata
{
    public List<XamlElement> Elements { get; set; } = new();
    public List<XamlBinding> Bindings { get; set; } = new();
    public List<XamlResource> Resources { get; set; } = new();
    public List<XamlEventHandler> EventHandlers { get; set; } = new();
    public XamlAnalysisMetadata Metadata { get; set; } = new();
    public Dictionary<string, object> Statistics { get; set; } = new();
}

public class XamlElement
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string? ParentId { get; set; }
    public List<string> ChildIds { get; set; } = new();
    public Dictionary<string, string> Attributes { get; set; } = new();
    public XamlLocation Location { get; set; } = new();
    public List<XamlBinding> ElementBindings { get; set; } = new();
    public List<XamlEventHandler> ElementEventHandlers { get; set; } = new();
    public Dictionary<string, object> Properties { get; set; } = new();
}

public class XamlBinding
{
    public string Id { get; set; } = string.Empty;
    public string ElementId { get; set; } = string.Empty;
    public string Property { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public XamlBindingMode Mode { get; set; }
    public string? Source { get; set; }
    public string? Converter { get; set; }
    public XamlLocation Location { get; set; } = new();
    public Dictionary<string, object> Properties { get; set; } = new();
}

public class XamlResource
{
    public string Id { get; set; } = string.Empty;
    public string Key { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public XamlResourceScope Scope { get; set; }
    public XamlLocation Location { get; set; } = new();
    public List<string> UsedByElements { get; set; } = new();
    public Dictionary<string, object> Properties { get; set; } = new();
}

public class XamlEventHandler
{
    public string Id { get; set; } = string.Empty;
    public string ElementId { get; set; } = string.Empty;
    public string EventName { get; set; } = string.Empty;
    public string HandlerName { get; set; } = string.Empty;
    public XamlLocation Location { get; set; } = new();
    public string? CodeBehindMethod { get; set; }
    public Dictionary<string, object> Properties { get; set; } = new();
}

public class XamlLocation
{
    public string FilePath { get; set; } = string.Empty;
    public int Line { get; set; }
    public int Column { get; set; }
    public int EndLine { get; set; }
    public int EndColumn { get; set; }
}

public class XamlAnalysisMetadata
{
    public DateTime AnalyzedAt { get; set; }
    public int TotalElements { get; set; }
    public int TotalBindings { get; set; }
    public int TotalResources { get; set; }
    public int TotalEventHandlers { get; set; }
    public Dictionary<string, int> ElementTypeDistribution { get; set; } = new();
    public Dictionary<string, int> BindingModeDistribution { get; set; } = new();
    public Dictionary<string, int> ResourceTypeDistribution { get; set; } = new();
    public List<string> ProcessedFiles { get; set; } = new();
    public int MaxNestingDepth { get; set; }
}

public class MvvmRelationships
{
    public List<ViewViewModelMapping> ViewViewModelMappings { get; set; } = new();
    public List<ViewModelModelMapping> ViewModelModelMappings { get; set; } = new();
    public MvvmAnalysisMetadata Metadata { get; set; } = new();
}

public class ViewViewModelMapping
{
    public string Id { get; set; } = string.Empty;
    public string ViewFilePath { get; set; } = string.Empty;
    public string ViewModelClassName { get; set; } = string.Empty;
    public string ViewModelFilePath { get; set; } = string.Empty;
    public MvvmMappingType MappingType { get; set; }
    public List<string> SharedProperties { get; set; } = new();
    public List<string> SharedCommands { get; set; } = new();
    public Dictionary<string, object> Properties { get; set; } = new();
}

public class ViewModelModelMapping
{
    public string Id { get; set; } = string.Empty;
    public string ViewModelClassName { get; set; } = string.Empty;
    public string ModelClassName { get; set; } = string.Empty;
    public string ModelFilePath { get; set; } = string.Empty;
    public MvvmMappingType MappingType { get; set; }
    public List<string> SharedProperties { get; set; } = new();
    public Dictionary<string, object> Properties { get; set; } = new();
}

public class MvvmAnalysisMetadata
{
    public DateTime AnalyzedAt { get; set; }
    public int TotalViewViewModelMappings { get; set; }
    public int TotalViewModelModelMappings { get; set; }
    public Dictionary<string, int> MappingTypeDistribution { get; set; } = new();
    public List<string> ProcessedFiles { get; set; } = new();
}

public enum XamlBindingMode
{
    OneWay,
    TwoWay,
    OneTime,
    OneWayToSource,
    Default,
    Unknown
}

public enum XamlResourceScope
{
    Application,
    Window,
    UserControl,
    Local,
    Unknown
}

public enum MvvmMappingType
{
    DataContext,
    Binding,
    Command,
    Property,
    Event,
    Unknown
}
