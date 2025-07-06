using System.Diagnostics;
using System.Text.Json;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.MSBuild;
using RoslynRuntime.Models;

namespace RoslynRuntime.Services;

/// <summary>
/// Service responsible for extracting comprehensive metadata from .NET projects
/// </summary>
public class ProjectMetadataExtractor
{
    private readonly MSBuildWorkspace _workspace;
    private readonly List<string> _errors = new();
    private readonly List<string> _warnings = new();

    public ProjectMetadataExtractor(MSBuildWorkspace workspace)
    {
        _workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
    }

    /// <summary>
    /// Extracts comprehensive metadata from a project
    /// </summary>
    /// <param name="projectPath">Path to the .csproj file</param>
    /// <returns>Complete project metadata</returns>
    public async Task<ProjectMetadata> ExtractAsync(string projectPath)
    {
        var stopwatch = Stopwatch.StartNew();
        _errors.Clear();
        _warnings.Clear();

        try
        {
            var project = await _workspace.OpenProjectAsync(projectPath);
            var compilation = await project.GetCompilationAsync();

            if (compilation == null)
            {
                throw new InvalidOperationException("Failed to get compilation for project");
            }

            var metadata = new ProjectMetadata
            {
                Project = ExtractProjectInfo(project),
                Assemblies = ExtractAssemblyInfo(compilation),
                Namespaces = ExtractNamespaceInfo(compilation),
                Types = await ExtractTypeInfoAsync(compilation),
                Members = await ExtractMemberInfoAsync(compilation),
                Dependencies = ExtractDependencyInfo(project),
                ExtractionMetadata = CreateExtractionMetadata(stopwatch.ElapsedMilliseconds, compilation)
            };

            return metadata;
        }
        catch (Exception ex)
        {
            _errors.Add($"Failed to extract project metadata: {ex.Message}");
            return new ProjectMetadata
            {
                ExtractionMetadata = CreateExtractionMetadata(stopwatch.ElapsedMilliseconds, null)
            };
        }
    }

    private Models.ProjectInfo ExtractProjectInfo(Project project)
    {
        try
        {
            return new Models.ProjectInfo
            {
                Name = project.Name,
                FilePath = project.FilePath ?? string.Empty,
                TargetFramework = GetProjectProperty(project, "TargetFramework") ?? string.Empty,
                OutputType = GetProjectProperty(project, "OutputType") ?? string.Empty,
                AssemblyName = project.AssemblyName ?? project.Name,
                DocumentCount = project.Documents.Count(),
                Language = project.Language
            };
        }
        catch (Exception ex)
        {
            _errors.Add($"Error extracting project info: {ex.Message}");
            return new Models.ProjectInfo { Name = project.Name };
        }
    }

    private List<AssemblyInfo> ExtractAssemblyInfo(Compilation compilation)
    {
        var assemblies = new List<AssemblyInfo>();

        try
        {
            // Add the main assembly
            assemblies.Add(new AssemblyInfo
            {
                Name = compilation.AssemblyName ?? "Unknown",
                Version = compilation.Assembly.Identity.Version.ToString(),
                Location = string.Empty,
                IsReferenceAssembly = false,
                GlobalAliases = new List<string>()
            });

            // Add referenced assemblies
            foreach (var reference in compilation.References)
            {
                try
                {
                    var assemblySymbol = compilation.GetAssemblyOrModuleSymbol(reference) as IAssemblySymbol;
                    if (assemblySymbol != null)
                    {
                        assemblies.Add(new AssemblyInfo
                        {
                            Name = assemblySymbol.Name,
                            Version = assemblySymbol.Identity.Version.ToString(),
                            Location = reference.Display ?? string.Empty,
                            IsReferenceAssembly = true,
                            GlobalAliases = assemblySymbol.GlobalNamespace.GetNamespaceMembers()
                                .Select(ns => ns.Name)
                                .Take(10) // Limit to avoid excessive data
                                .ToList()
                        });
                    }
                }
                catch (Exception ex)
                {
                    _warnings.Add($"Error processing assembly reference: {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            _errors.Add($"Error extracting assembly info: {ex.Message}");
        }

        return assemblies;
    }

    private List<NamespaceInfo> ExtractNamespaceInfo(Compilation compilation)
    {
        var namespaces = new List<NamespaceInfo>();
        var processedNamespaces = new HashSet<string>();

        try
        {
            ExtractNamespaceInfoRecursive(compilation.GlobalNamespace, namespaces, processedNamespaces, compilation.AssemblyName ?? "Unknown");
        }
        catch (Exception ex)
        {
            _errors.Add($"Error extracting namespace info: {ex.Message}");
        }

        return namespaces;
    }

    private void ExtractNamespaceInfoRecursive(INamespaceSymbol namespaceSymbol, List<NamespaceInfo> namespaces, HashSet<string> processed, string assemblyName)
    {
        var fullName = namespaceSymbol.ToDisplayString();
        
        if (processed.Contains(fullName))
            return;

        processed.Add(fullName);

        try
        {
            var namespaceInfo = new NamespaceInfo
            {
                Name = namespaceSymbol.Name,
                FullName = fullName,
                IsGlobal = namespaceSymbol.IsGlobalNamespace,
                TypeCount = namespaceSymbol.GetTypeMembers().Length,
                NestedNamespaces = namespaceSymbol.GetNamespaceMembers().Select(ns => ns.Name).ToList(),
                ContainingAssembly = assemblyName
            };

            namespaces.Add(namespaceInfo);

            // Process nested namespaces
            foreach (var nestedNamespace in namespaceSymbol.GetNamespaceMembers())
            {
                ExtractNamespaceInfoRecursive(nestedNamespace, namespaces, processed, assemblyName);
            }
        }
        catch (Exception ex)
        {
            _warnings.Add($"Error processing namespace {fullName}: {ex.Message}");
        }
    }

    private async Task<List<Models.TypeInfo>> ExtractTypeInfoAsync(Compilation compilation)
    {
        var types = new List<Models.TypeInfo>();

        try
        {
            await ExtractTypesFromNamespaceAsync(compilation.GlobalNamespace, types, compilation);
        }
        catch (Exception ex)
        {
            _errors.Add($"Error extracting type info: {ex.Message}");
        }

        return types;
    }

    private async Task ExtractTypesFromNamespaceAsync(INamespaceSymbol namespaceSymbol, List<Models.TypeInfo> types, Compilation compilation)
    {
        // Process types in current namespace
        foreach (var typeSymbol in namespaceSymbol.GetTypeMembers())
        {
            try
            {
                var typeInfo = CreateTypeInfoAsync(typeSymbol, compilation);
                types.Add(typeInfo);
            }
            catch (Exception ex)
            {
                _warnings.Add($"Error processing type {typeSymbol.Name}: {ex.Message}");
            }
        }

        // Process nested namespaces
        foreach (var nestedNamespace in namespaceSymbol.GetNamespaceMembers())
        {
            await ExtractTypesFromNamespaceAsync(nestedNamespace, types, compilation);
        }
    }

    private Models.TypeInfo CreateTypeInfoAsync(INamedTypeSymbol typeSymbol, Compilation compilation)
    {
        var typeInfo = new Models.TypeInfo
        {
            Name = typeSymbol.Name,
            FullName = typeSymbol.ToDisplayString(),
            Kind = typeSymbol.TypeKind.ToString(),
            Accessibility = typeSymbol.DeclaredAccessibility.ToString(),
            IsAbstract = typeSymbol.IsAbstract,
            IsSealed = typeSymbol.IsSealed,
            IsStatic = typeSymbol.IsStatic,
            IsGeneric = typeSymbol.IsGenericType,
            Namespace = typeSymbol.ContainingNamespace?.ToDisplayString() ?? string.Empty,
            ContainingAssembly = typeSymbol.ContainingAssembly?.Name ?? string.Empty,
            BaseType = typeSymbol.BaseType?.ToDisplayString(),
            Interfaces = typeSymbol.Interfaces.Select(i => i.ToDisplayString()).ToList(),
            GenericParameters = ExtractGenericParameters(typeSymbol),
            Attributes = ExtractAttributes(typeSymbol),
            MemberCount = typeSymbol.GetMembers().Length
        };

        // Extract documentation and source location
        typeInfo.Documentation = ExtractDocumentationAsync(typeSymbol);
        typeInfo.SourceLocation = ExtractSourceLocation(typeSymbol);

        return typeInfo;
    }

    private async Task<List<MemberInfo>> ExtractMemberInfoAsync(Compilation compilation)
    {
        var members = new List<MemberInfo>();

        try
        {
            await ExtractMembersFromNamespaceAsync(compilation.GlobalNamespace, members, compilation);
        }
        catch (Exception ex)
        {
            _errors.Add($"Error extracting member info: {ex.Message}");
        }

        return members;
    }

    private async Task ExtractMembersFromNamespaceAsync(INamespaceSymbol namespaceSymbol, List<MemberInfo> members, Compilation compilation)
    {
        // Process types in current namespace
        foreach (var typeSymbol in namespaceSymbol.GetTypeMembers())
        {
            try
            {
                ExtractMembersFromTypeAsync(typeSymbol, members, compilation);
            }
            catch (Exception ex)
            {
                _warnings.Add($"Error processing members of type {typeSymbol.Name}: {ex.Message}");
            }
        }

        // Process nested namespaces
        foreach (var nestedNamespace in namespaceSymbol.GetNamespaceMembers())
        {
            await ExtractMembersFromNamespaceAsync(nestedNamespace, members, compilation);
        }
    }

    private void ExtractMembersFromTypeAsync(INamedTypeSymbol typeSymbol, List<MemberInfo> members, Compilation compilation)
    {
        foreach (var memberSymbol in typeSymbol.GetMembers())
        {
            try
            {
                var memberInfo = CreateMemberInfoAsync(memberSymbol, compilation);
                if (memberInfo != null)
                {
                    members.Add(memberInfo);
                }
            }
            catch (Exception ex)
            {
                _warnings.Add($"Error processing member {memberSymbol.Name}: {ex.Message}");
            }
        }
    }

    private MemberInfo? CreateMemberInfoAsync(ISymbol memberSymbol, Compilation compilation)
    {
        var memberInfo = new MemberInfo
        {
            Name = memberSymbol.Name,
            FullName = memberSymbol.ToDisplayString(),
            Kind = memberSymbol.Kind.ToString(),
            Accessibility = memberSymbol.DeclaredAccessibility.ToString(),
            IsStatic = memberSymbol.IsStatic,
            IsAbstract = memberSymbol.IsAbstract,
            IsVirtual = memberSymbol.IsVirtual,
            IsOverride = memberSymbol.IsOverride,
            ContainingType = memberSymbol.ContainingType?.ToDisplayString() ?? string.Empty,
            Attributes = ExtractAttributes(memberSymbol)
        };

        // Set member-specific properties based on symbol type
        switch (memberSymbol)
        {
            case IMethodSymbol methodSymbol:
                SetMethodProperties(memberInfo, methodSymbol);
                break;
            case IPropertySymbol propertySymbol:
                SetPropertyProperties(memberInfo, propertySymbol);
                break;
            case IFieldSymbol fieldSymbol:
                SetFieldProperties(memberInfo, fieldSymbol);
                break;
            case IEventSymbol eventSymbol:
                SetEventProperties(memberInfo, eventSymbol);
                break;
            default:
                // Skip unsupported member types
                return null;
        }

        // Extract documentation and source location
        memberInfo.Documentation = ExtractDocumentationAsync(memberSymbol);
        memberInfo.SourceLocation = ExtractSourceLocation(memberSymbol);

        return memberInfo;
    }

    private void SetMethodProperties(MemberInfo memberInfo, IMethodSymbol methodSymbol)
    {
        memberInfo.ReturnType = methodSymbol.ReturnType.ToDisplayString();
        memberInfo.Parameters = ExtractParameters(methodSymbol);
        memberInfo.IsAsync = methodSymbol.IsAsync;
        memberInfo.IsExtensionMethod = methodSymbol.IsExtensionMethod;
    }

    private void SetPropertyProperties(MemberInfo memberInfo, IPropertySymbol propertySymbol)
    {
        memberInfo.ReturnType = propertySymbol.Type.ToDisplayString();
        memberInfo.HasGetter = propertySymbol.GetMethod != null;
        memberInfo.HasSetter = propertySymbol.SetMethod != null;
        memberInfo.Parameters = ExtractParameters(propertySymbol);
    }

    private void SetFieldProperties(MemberInfo memberInfo, IFieldSymbol fieldSymbol)
    {
        memberInfo.ReturnType = fieldSymbol.Type.ToDisplayString();
        memberInfo.IsReadOnly = fieldSymbol.IsReadOnly;
        memberInfo.IsConst = fieldSymbol.IsConst;
        if (fieldSymbol.HasConstantValue)
        {
            memberInfo.ConstantValue = fieldSymbol.ConstantValue;
        }
    }

    private void SetEventProperties(MemberInfo memberInfo, IEventSymbol eventSymbol)
    {
        memberInfo.ReturnType = eventSymbol.Type.ToDisplayString();
    }

    private List<ParameterInfo> ExtractParameters(IMethodSymbol methodSymbol)
    {
        return methodSymbol.Parameters.Select(p => new ParameterInfo
        {
            Name = p.Name,
            Type = p.Type.ToDisplayString(),
            IsOptional = p.IsOptional,
            HasDefaultValue = p.HasExplicitDefaultValue,
            DefaultValue = p.HasExplicitDefaultValue ? p.ExplicitDefaultValue : null,
            RefKind = p.RefKind.ToString(),
            IsParams = p.IsParams,
            Attributes = ExtractAttributes(p)
        }).ToList();
    }

    private List<ParameterInfo> ExtractParameters(IPropertySymbol propertySymbol)
    {
        return propertySymbol.Parameters.Select(p => new ParameterInfo
        {
            Name = p.Name,
            Type = p.Type.ToDisplayString(),
            IsOptional = p.IsOptional,
            HasDefaultValue = p.HasExplicitDefaultValue,
            DefaultValue = p.HasExplicitDefaultValue ? p.ExplicitDefaultValue : null,
            RefKind = p.RefKind.ToString(),
            IsParams = p.IsParams,
            Attributes = ExtractAttributes(p)
        }).ToList();
    }

    private List<GenericParameterInfo> ExtractGenericParameters(INamedTypeSymbol typeSymbol)
    {
        return typeSymbol.TypeParameters.Select(tp => new GenericParameterInfo
        {
            Name = tp.Name,
            Variance = tp.Variance.ToString(),
            Constraints = tp.ConstraintTypes.Select(ct => ct.ToDisplayString()).ToList(),
            HasReferenceTypeConstraint = tp.HasReferenceTypeConstraint,
            HasValueTypeConstraint = tp.HasValueTypeConstraint,
            HasConstructorConstraint = tp.HasConstructorConstraint
        }).ToList();
    }

    private List<AttributeInfo> ExtractAttributes(ISymbol symbol)
    {
        return symbol.GetAttributes().Select(attr => new AttributeInfo
        {
            Name = attr.AttributeClass?.Name ?? "Unknown",
            FullName = attr.AttributeClass?.ToDisplayString() ?? "Unknown",
            Arguments = ExtractAttributeArguments(attr)
        }).ToList();
    }

    private List<AttributeArgumentInfo> ExtractAttributeArguments(AttributeData attribute)
    {
        var arguments = new List<AttributeArgumentInfo>();

        // Constructor arguments
        for (int i = 0; i < attribute.ConstructorArguments.Length; i++)
        {
            var arg = attribute.ConstructorArguments[i];
            arguments.Add(new AttributeArgumentInfo
            {
                Name = null, // Constructor arguments don't have names
                Value = arg.Value,
                Type = arg.Type?.ToDisplayString() ?? "Unknown"
            });
        }

        // Named arguments
        foreach (var namedArg in attribute.NamedArguments)
        {
            arguments.Add(new AttributeArgumentInfo
            {
                Name = namedArg.Key,
                Value = namedArg.Value.Value,
                Type = namedArg.Value.Type?.ToDisplayString() ?? "Unknown"
            });
        }

        return arguments;
    }

    private DocumentationInfo? ExtractDocumentationAsync(ISymbol symbol)
    {
        try
        {
            var xmlDoc = symbol.GetDocumentationCommentXml();
            if (string.IsNullOrWhiteSpace(xmlDoc))
                return null;

            var doc = XDocument.Parse($"<root>{xmlDoc}</root>");
            var docInfo = new DocumentationInfo();

            // Extract summary
            var summaryElement = doc.Descendants("summary").FirstOrDefault();
            if (summaryElement != null)
            {
                docInfo.Summary = summaryElement.Value.Trim();
            }

            // Extract remarks
            var remarksElement = doc.Descendants("remarks").FirstOrDefault();
            if (remarksElement != null)
            {
                docInfo.Remarks = remarksElement.Value.Trim();
            }

            // Extract returns
            var returnsElement = doc.Descendants("returns").FirstOrDefault();
            if (returnsElement != null)
            {
                docInfo.Returns = returnsElement.Value.Trim();
            }

            // Extract parameters
            foreach (var paramElement in doc.Descendants("param"))
            {
                var nameAttr = paramElement.Attribute("name");
                if (nameAttr != null)
                {
                    docInfo.Parameters[nameAttr.Value] = paramElement.Value.Trim();
                }
            }

            // Extract exceptions
            foreach (var exceptionElement in doc.Descendants("exception"))
            {
                var crefAttr = exceptionElement.Attribute("cref");
                if (crefAttr != null)
                {
                    docInfo.Exceptions[crefAttr.Value] = exceptionElement.Value.Trim();
                }
            }

            // Extract examples
            foreach (var exampleElement in doc.Descendants("example"))
            {
                docInfo.Examples.Add(exampleElement.Value.Trim());
            }

            // Extract see also references
            foreach (var seeAlsoElement in doc.Descendants("seealso"))
            {
                var crefAttr = seeAlsoElement.Attribute("cref");
                if (crefAttr != null)
                {
                    docInfo.SeeAlso.Add(crefAttr.Value);
                }
            }

            return docInfo;
        }
        catch (Exception ex)
        {
            _warnings.Add($"Error extracting documentation for {symbol.Name}: {ex.Message}");
            return null;
        }
    }

    private SourceLocationInfo? ExtractSourceLocation(ISymbol symbol)
    {
        try
        {
            var location = symbol.Locations.FirstOrDefault(l => l.IsInSource);
            if (location == null)
                return null;

            var lineSpan = location.GetLineSpan();
            return new SourceLocationInfo
            {
                FilePath = location.SourceTree?.FilePath ?? string.Empty,
                StartLine = lineSpan.StartLinePosition.Line + 1,
                StartColumn = lineSpan.StartLinePosition.Character + 1,
                EndLine = lineSpan.EndLinePosition.Line + 1,
                EndColumn = lineSpan.EndLinePosition.Character + 1
            };
        }
        catch (Exception ex)
        {
            _warnings.Add($"Error extracting source location for {symbol.Name}: {ex.Message}");
            return null;
        }
    }

    private List<DependencyInfo> ExtractDependencyInfo(Project project)
    {
        var dependencies = new List<DependencyInfo>();

        try
        {
            // Extract from project references
            foreach (var projectRef in project.ProjectReferences)
            {
                dependencies.Add(new DependencyInfo
                {
                    Name = Path.GetFileNameWithoutExtension(projectRef.ProjectId.ToString()),
                    Version = "Unknown",
                    Type = "ProjectReference",
                    IsImplicit = false
                });
            }

            // Extract from metadata references
            foreach (var metadataRef in project.MetadataReferences)
            {
                var display = metadataRef.Display;
                if (!string.IsNullOrEmpty(display))
                {
                    var name = Path.GetFileNameWithoutExtension(display);
                    dependencies.Add(new DependencyInfo
                    {
                        Name = name,
                        Version = "Unknown",
                        Type = "MetadataReference",
                        IsImplicit = true
                    });
                }
            }
        }
        catch (Exception ex)
        {
            _errors.Add($"Error extracting dependency info: {ex.Message}");
        }

        return dependencies;
    }

    private ExtractionMetadata CreateExtractionMetadata(long processingTimeMs, Compilation? compilation)
    {
        return new ExtractionMetadata
        {
            ExtractedAt = DateTime.UtcNow,
            ExtractorVersion = "1.0.0",
            RoslynVersion = typeof(Compilation).Assembly.GetName().Version?.ToString() ?? "Unknown",
            ProcessingTimeMs = processingTimeMs,
            TotalSymbols = compilation?.GetSymbolsWithName(_ => true).Count() ?? 0,
            Errors = new List<string>(_errors),
            Warnings = new List<string>(_warnings)
        };
    }

    private string? GetProjectProperty(Project project, string propertyName)
    {
        try
        {
            // This is a simplified approach - in a real implementation,
            // you might want to parse the project file directly
            return null;
        }
        catch
        {
            return null;
        }
    }
}
