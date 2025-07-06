using System.Text.Json.Serialization;

namespace RoslynRuntime.Models;

/// <summary>
/// Root metadata container for a .NET project
/// </summary>
public class ProjectMetadata
{
    [JsonPropertyName("project")]
    public ProjectInfo Project { get; set; } = new();

    [JsonPropertyName("assemblies")]
    public List<AssemblyInfo> Assemblies { get; set; } = new();

    [JsonPropertyName("namespaces")]
    public List<NamespaceInfo> Namespaces { get; set; } = new();

    [JsonPropertyName("types")]
    public List<TypeInfo> Types { get; set; } = new();

    [JsonPropertyName("members")]
    public List<MemberInfo> Members { get; set; } = new();

    [JsonPropertyName("dependencies")]
    public List<DependencyInfo> Dependencies { get; set; } = new();

    [JsonPropertyName("extractionMetadata")]
    public ExtractionMetadata ExtractionMetadata { get; set; } = new();
}

/// <summary>
/// Basic project information
/// </summary>
public class ProjectInfo
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("filePath")]
    public string FilePath { get; set; } = string.Empty;

    [JsonPropertyName("targetFramework")]
    public string TargetFramework { get; set; } = string.Empty;

    [JsonPropertyName("outputType")]
    public string OutputType { get; set; } = string.Empty;

    [JsonPropertyName("assemblyName")]
    public string AssemblyName { get; set; } = string.Empty;

    [JsonPropertyName("documentCount")]
    public int DocumentCount { get; set; }

    [JsonPropertyName("language")]
    public string Language { get; set; } = string.Empty;
}

/// <summary>
/// Assembly-level information
/// </summary>
public class AssemblyInfo
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("version")]
    public string Version { get; set; } = string.Empty;

    [JsonPropertyName("location")]
    public string Location { get; set; } = string.Empty;

    [JsonPropertyName("isReferenceAssembly")]
    public bool IsReferenceAssembly { get; set; }

    [JsonPropertyName("globalAliases")]
    public List<string> GlobalAliases { get; set; } = new();
}

/// <summary>
/// Namespace organization information
/// </summary>
public class NamespaceInfo
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("fullName")]
    public string FullName { get; set; } = string.Empty;

    [JsonPropertyName("isGlobal")]
    public bool IsGlobal { get; set; }

    [JsonPropertyName("typeCount")]
    public int TypeCount { get; set; }

    [JsonPropertyName("nestedNamespaces")]
    public List<string> NestedNamespaces { get; set; } = new();

    [JsonPropertyName("containingAssembly")]
    public string ContainingAssembly { get; set; } = string.Empty;
}

/// <summary>
/// Type-level metadata (classes, interfaces, structs, enums, delegates)
/// </summary>
public class TypeInfo
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("fullName")]
    public string FullName { get; set; } = string.Empty;

    [JsonPropertyName("kind")]
    public string Kind { get; set; } = string.Empty; // Class, Interface, Struct, Enum, Delegate

    [JsonPropertyName("accessibility")]
    public string Accessibility { get; set; } = string.Empty;

    [JsonPropertyName("isAbstract")]
    public bool IsAbstract { get; set; }

    [JsonPropertyName("isSealed")]
    public bool IsSealed { get; set; }

    [JsonPropertyName("isStatic")]
    public bool IsStatic { get; set; }

    [JsonPropertyName("isGeneric")]
    public bool IsGeneric { get; set; }

    [JsonPropertyName("namespace")]
    public string Namespace { get; set; } = string.Empty;

    [JsonPropertyName("containingAssembly")]
    public string ContainingAssembly { get; set; } = string.Empty;

    [JsonPropertyName("baseType")]
    public string? BaseType { get; set; }

    [JsonPropertyName("interfaces")]
    public List<string> Interfaces { get; set; } = new();

    [JsonPropertyName("genericParameters")]
    public List<GenericParameterInfo> GenericParameters { get; set; } = new();

    [JsonPropertyName("attributes")]
    public List<AttributeInfo> Attributes { get; set; } = new();

    [JsonPropertyName("memberCount")]
    public int MemberCount { get; set; }

    [JsonPropertyName("documentation")]
    public DocumentationInfo? Documentation { get; set; }

    [JsonPropertyName("sourceLocation")]
    public SourceLocationInfo? SourceLocation { get; set; }
}

/// <summary>
/// Member-level metadata (methods, properties, fields, events)
/// </summary>
public class MemberInfo
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("fullName")]
    public string FullName { get; set; } = string.Empty;

    [JsonPropertyName("kind")]
    public string Kind { get; set; } = string.Empty; // Method, Property, Field, Event, Constructor

    [JsonPropertyName("accessibility")]
    public string Accessibility { get; set; } = string.Empty;

    [JsonPropertyName("isStatic")]
    public bool IsStatic { get; set; }

    [JsonPropertyName("isAbstract")]
    public bool IsAbstract { get; set; }

    [JsonPropertyName("isVirtual")]
    public bool IsVirtual { get; set; }

    [JsonPropertyName("isOverride")]
    public bool IsOverride { get; set; }

    [JsonPropertyName("containingType")]
    public string ContainingType { get; set; } = string.Empty;

    [JsonPropertyName("returnType")]
    public string? ReturnType { get; set; }

    [JsonPropertyName("parameters")]
    public List<ParameterInfo> Parameters { get; set; } = new();

    [JsonPropertyName("attributes")]
    public List<AttributeInfo> Attributes { get; set; } = new();

    [JsonPropertyName("documentation")]
    public DocumentationInfo? Documentation { get; set; }

    [JsonPropertyName("sourceLocation")]
    public SourceLocationInfo? SourceLocation { get; set; }

    // Method-specific properties
    [JsonPropertyName("isAsync")]
    public bool? IsAsync { get; set; }

    [JsonPropertyName("isExtensionMethod")]
    public bool? IsExtensionMethod { get; set; }

    // Property-specific properties
    [JsonPropertyName("hasGetter")]
    public bool? HasGetter { get; set; }

    [JsonPropertyName("hasSetter")]
    public bool? HasSetter { get; set; }

    // Field-specific properties
    [JsonPropertyName("isReadOnly")]
    public bool? IsReadOnly { get; set; }

    [JsonPropertyName("isConst")]
    public bool? IsConst { get; set; }

    [JsonPropertyName("constantValue")]
    public object? ConstantValue { get; set; }
}

/// <summary>
/// Parameter information for methods and constructors
/// </summary>
public class ParameterInfo
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("isOptional")]
    public bool IsOptional { get; set; }

    [JsonPropertyName("hasDefaultValue")]
    public bool HasDefaultValue { get; set; }

    [JsonPropertyName("defaultValue")]
    public object? DefaultValue { get; set; }

    [JsonPropertyName("refKind")]
    public string RefKind { get; set; } = string.Empty; // None, Ref, Out, In

    [JsonPropertyName("isParams")]
    public bool IsParams { get; set; }

    [JsonPropertyName("attributes")]
    public List<AttributeInfo> Attributes { get; set; } = new();
}

/// <summary>
/// Generic parameter information
/// </summary>
public class GenericParameterInfo
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("variance")]
    public string Variance { get; set; } = string.Empty; // None, In, Out

    [JsonPropertyName("constraints")]
    public List<string> Constraints { get; set; } = new();

    [JsonPropertyName("hasReferenceTypeConstraint")]
    public bool HasReferenceTypeConstraint { get; set; }

    [JsonPropertyName("hasValueTypeConstraint")]
    public bool HasValueTypeConstraint { get; set; }

    [JsonPropertyName("hasConstructorConstraint")]
    public bool HasConstructorConstraint { get; set; }
}

/// <summary>
/// Attribute information
/// </summary>
public class AttributeInfo
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("fullName")]
    public string FullName { get; set; } = string.Empty;

    [JsonPropertyName("arguments")]
    public List<AttributeArgumentInfo> Arguments { get; set; } = new();
}

/// <summary>
/// Attribute argument information
/// </summary>
public class AttributeArgumentInfo
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("value")]
    public object? Value { get; set; }

    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;
}

/// <summary>
/// Documentation information extracted from XML docs and comments
/// </summary>
public class DocumentationInfo
{
    [JsonPropertyName("summary")]
    public string? Summary { get; set; }

    [JsonPropertyName("remarks")]
    public string? Remarks { get; set; }

    [JsonPropertyName("returns")]
    public string? Returns { get; set; }

    [JsonPropertyName("parameters")]
    public Dictionary<string, string> Parameters { get; set; } = new();

    [JsonPropertyName("exceptions")]
    public Dictionary<string, string> Exceptions { get; set; } = new();

    [JsonPropertyName("examples")]
    public List<string> Examples { get; set; } = new();

    [JsonPropertyName("seeAlso")]
    public List<string> SeeAlso { get; set; } = new();
}

/// <summary>
/// Source code location information
/// </summary>
public class SourceLocationInfo
{
    [JsonPropertyName("filePath")]
    public string FilePath { get; set; } = string.Empty;

    [JsonPropertyName("startLine")]
    public int StartLine { get; set; }

    [JsonPropertyName("startColumn")]
    public int StartColumn { get; set; }

    [JsonPropertyName("endLine")]
    public int EndLine { get; set; }

    [JsonPropertyName("endColumn")]
    public int EndColumn { get; set; }
}

/// <summary>
/// Dependency information
/// </summary>
public class DependencyInfo
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("version")]
    public string Version { get; set; } = string.Empty;

    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty; // PackageReference, ProjectReference, FrameworkReference

    [JsonPropertyName("isImplicit")]
    public bool IsImplicit { get; set; }
}

/// <summary>
/// Metadata about the extraction process
/// </summary>
public class ExtractionMetadata
{
    [JsonPropertyName("extractedAt")]
    public DateTime ExtractedAt { get; set; } = DateTime.UtcNow;

    [JsonPropertyName("extractorVersion")]
    public string ExtractorVersion { get; set; } = "1.0.0";

    [JsonPropertyName("roslynVersion")]
    public string RoslynVersion { get; set; } = string.Empty;

    [JsonPropertyName("processingTimeMs")]
    public long ProcessingTimeMs { get; set; }

    [JsonPropertyName("totalSymbols")]
    public int TotalSymbols { get; set; }

    [JsonPropertyName("errors")]
    public List<string> Errors { get; set; } = new();

    [JsonPropertyName("warnings")]
    public List<string> Warnings { get; set; } = new();
}
