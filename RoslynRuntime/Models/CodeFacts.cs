namespace RoslynRuntime.Models;

public class CodeFacts
{
    public List<CodeFact> Facts { get; set; } = new();
    public Dictionary<string, string> Summaries { get; set; } = new();
    public List<ApiContract> Contracts { get; set; } = new();
    public CodeDocumentation Documentation { get; set; } = new();
    public DateTime AnalysisTime { get; set; }
    public string FilePath { get; set; } = string.Empty;
}

public class CodeFact
{
    public string Type { get; set; } = string.Empty; // Method, Class, Property, etc.
    public string Subject { get; set; } = string.Empty;
    public string Predicate { get; set; } = string.Empty;
    public string Object { get; set; } = string.Empty;
    public double Confidence { get; set; }
    public Location Location { get; set; } = new();
    public Dictionary<string, object> Context { get; set; } = new();
}

public class ApiContract
{
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty; // Method, Property, Class, etc.
    public List<string> Preconditions { get; set; } = new();
    public List<string> Postconditions { get; set; } = new();
    public List<string> SideEffects { get; set; } = new();
    public List<Parameter> Parameters { get; set; } = new();
    public ReturnValue? ReturnValue { get; set; }
    public List<string> Exceptions { get; set; } = new();
    public Location Location { get; set; } = new();
    public string Description { get; set; } = string.Empty;
}

public class Parameter
{
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public bool IsOptional { get; set; }
    public string? DefaultValue { get; set; }
    public List<string> Constraints { get; set; } = new();
}

public class ReturnValue
{
    public string Type { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public List<string> PossibleValues { get; set; } = new();
}

public class CodeDocumentation
{
    public List<ClassDocumentation> Classes { get; set; } = new();
    public List<MethodDocumentation> Methods { get; set; } = new();
    public List<PropertyDocumentation> Properties { get; set; } = new();
    public string OverallSummary { get; set; } = string.Empty;
    public List<string> KeyConcepts { get; set; } = new();
    public List<UsageExample> Examples { get; set; } = new();
}

public class ClassDocumentation
{
    public string Name { get; set; } = string.Empty;
    public string Purpose { get; set; } = string.Empty;
    public string Responsibilities { get; set; } = string.Empty;
    public List<string> Collaborators { get; set; } = new();
    public Location Location { get; set; } = new();
    public string Complexity { get; set; } = string.Empty;
    public List<string> DesignPatterns { get; set; } = new();
}

public class MethodDocumentation
{
    public string Name { get; set; } = string.Empty;
    public string ClassName { get; set; } = string.Empty;
    public string Purpose { get; set; } = string.Empty;
    public string Algorithm { get; set; } = string.Empty;
    public string Behavior { get; set; } = string.Empty;
    public Location Location { get; set; } = new();
    public int CyclomaticComplexity { get; set; }
    public List<string> BusinessRules { get; set; } = new();
}

public class PropertyDocumentation
{
    public string Name { get; set; } = string.Empty;
    public string ClassName { get; set; } = string.Empty;
    public string Purpose { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public bool IsReadOnly { get; set; }
    public bool IsWriteOnly { get; set; }
    public Location Location { get; set; } = new();
    public List<string> ValidationRules { get; set; } = new();
}

public class UsageExample
{
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string Context { get; set; } = string.Empty;
}
