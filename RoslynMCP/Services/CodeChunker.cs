using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using RoslynMCP.Models;
using System.Text;

namespace RoslynMCP.Services;

public class CodeChunker
{
    public async Task<ChunkingResult> ChunkCodeAsync(string filePath, string strategy = "class", bool includeDependencies = true)
    {
        var result = new ChunkingResult
        {
            Metadata = new ChunkingMetadata
            {
                Strategy = strategy,
                AnalysisTime = DateTime.UtcNow,
                FilePath = filePath
            }
        };

        try
        {
            var sourceCode = await File.ReadAllTextAsync(filePath);
            var syntaxTree = CSharpSyntaxTree.ParseText(sourceCode, path: filePath);
            var root = await syntaxTree.GetRootAsync();

            switch (strategy.ToLowerInvariant())
            {
                case "class":
                    result.Chunks = ChunkByClass(root, filePath);
                    break;
                case "method":
                    result.Chunks = ChunkByMethod(root, filePath);
                    break;
                case "namespace":
                    result.Chunks = ChunkByNamespace(root, filePath);
                    break;
                case "feature":
                    result.Chunks = ChunkByFeature(root, filePath);
                    break;
                default:
                    result.Chunks = ChunkByClass(root, filePath);
                    break;
            }

            if (includeDependencies)
            {
                result.Relationships = AnalyzeDependencies(result.Chunks, root);
            }

            // Update metadata
            result.Metadata.TotalChunks = result.Chunks.Count;
            result.Metadata.ChunkTypeDistribution = result.Chunks
                .GroupBy(c => c.Type)
                .ToDictionary(g => g.Key, g => g.Count());

            return result;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to chunk code: {ex.Message}", ex);
        }
    }

    private List<CodeChunk> ChunkByClass(SyntaxNode root, string filePath)
    {
        var chunks = new List<CodeChunk>();
        var classes = root.DescendantNodes().OfType<ClassDeclarationSyntax>();

        foreach (var classDecl in classes)
        {
            var chunk = new CodeChunk
            {
                Id = Guid.NewGuid().ToString(),
                Type = "Class",
                Name = classDecl.Identifier.ValueText,
                Content = classDecl.ToString(),
                Location = GetLocation(classDecl, filePath),
                ComplexityScore = CalculateComplexity(classDecl),
                Metadata = ExtractClassMetadata(classDecl)
            };

            chunks.Add(chunk);
        }

        // Also include interfaces, structs, enums
        var interfaces = root.DescendantNodes().OfType<InterfaceDeclarationSyntax>();
        foreach (var interfaceDecl in interfaces)
        {
            chunks.Add(new CodeChunk
            {
                Id = Guid.NewGuid().ToString(),
                Type = "Interface",
                Name = interfaceDecl.Identifier.ValueText,
                Content = interfaceDecl.ToString(),
                Location = GetLocation(interfaceDecl, filePath),
                ComplexityScore = CalculateComplexity(interfaceDecl),
                Metadata = ExtractInterfaceMetadata(interfaceDecl)
            });
        }

        return chunks;
    }

    private List<CodeChunk> ChunkByMethod(SyntaxNode root, string filePath)
    {
        var chunks = new List<CodeChunk>();
        var methods = root.DescendantNodes().OfType<MethodDeclarationSyntax>();

        foreach (var method in methods)
        {
            var className = method.Ancestors().OfType<ClassDeclarationSyntax>().FirstOrDefault()?.Identifier.ValueText ?? "Unknown";
            
            var chunk = new CodeChunk
            {
                Id = Guid.NewGuid().ToString(),
                Type = "Method",
                Name = $"{className}.{method.Identifier.ValueText}",
                Content = method.ToString(),
                Location = GetLocation(method, filePath),
                ComplexityScore = CalculateComplexity(method),
                Metadata = ExtractMethodMetadata(method, className)
            };

            chunks.Add(chunk);
        }

        // Also include properties and constructors
        var properties = root.DescendantNodes().OfType<PropertyDeclarationSyntax>();
        foreach (var property in properties)
        {
            var className = property.Ancestors().OfType<ClassDeclarationSyntax>().FirstOrDefault()?.Identifier.ValueText ?? "Unknown";
            
            chunks.Add(new CodeChunk
            {
                Id = Guid.NewGuid().ToString(),
                Type = "Property",
                Name = $"{className}.{property.Identifier.ValueText}",
                Content = property.ToString(),
                Location = GetLocation(property, filePath),
                ComplexityScore = CalculateComplexity(property),
                Metadata = ExtractPropertyMetadata(property, className)
            });
        }

        return chunks;
    }

    private List<CodeChunk> ChunkByNamespace(SyntaxNode root, string filePath)
    {
        var chunks = new List<CodeChunk>();
        var namespaces = root.DescendantNodes().OfType<NamespaceDeclarationSyntax>();

        foreach (var ns in namespaces)
        {
            var chunk = new CodeChunk
            {
                Id = Guid.NewGuid().ToString(),
                Type = "Namespace",
                Name = ns.Name.ToString(),
                Content = ns.ToString(),
                Location = GetLocation(ns, filePath),
                ComplexityScore = CalculateComplexity(ns),
                Metadata = ExtractNamespaceMetadata(ns)
            };

            chunks.Add(chunk);
        }

        // Handle file-scoped namespaces
        var fileScopedNamespaces = root.DescendantNodes().OfType<FileScopedNamespaceDeclarationSyntax>();
        foreach (var ns in fileScopedNamespaces)
        {
            chunks.Add(new CodeChunk
            {
                Id = Guid.NewGuid().ToString(),
                Type = "FileScopedNamespace",
                Name = ns.Name.ToString(),
                Content = ns.ToString(),
                Location = GetLocation(ns, filePath),
                ComplexityScore = CalculateComplexity(ns),
                Metadata = ExtractNamespaceMetadata(ns)
            });
        }

        return chunks;
    }

    private List<CodeChunk> ChunkByFeature(SyntaxNode root, string filePath)
    {
        // Feature-based chunking groups related functionality together
        var chunks = new List<CodeChunk>();
        var classes = root.DescendantNodes().OfType<ClassDeclarationSyntax>();

        // Group classes by common patterns (e.g., Controller, Service, Repository)
        var featureGroups = classes.GroupBy(c => DetermineFeatureGroup(c.Identifier.ValueText));

        foreach (var group in featureGroups)
        {
            var combinedContent = new StringBuilder();
            var locations = new List<Models.Location>();
            var totalComplexity = 0;

            foreach (var classDecl in group)
            {
                combinedContent.AppendLine(classDecl.ToString());
                locations.Add(GetLocation(classDecl, filePath));
                totalComplexity += CalculateComplexity(classDecl);
            }

            var chunk = new CodeChunk
            {
                Id = Guid.NewGuid().ToString(),
                Type = "Feature",
                Name = group.Key,
                Content = combinedContent.ToString(),
                Location = locations.FirstOrDefault() ?? new Models.Location(),
                ComplexityScore = totalComplexity,
                Metadata = new Dictionary<string, object>
                {
                    ["ClassCount"] = group.Count(),
                    ["FeatureType"] = group.Key,
                    ["Locations"] = locations
                }
            };

            chunks.Add(chunk);
        }

        return chunks;
    }

    private string DetermineFeatureGroup(string className)
    {
        if (className.EndsWith("Controller")) return "Controllers";
        if (className.EndsWith("Service")) return "Services";
        if (className.EndsWith("Repository")) return "Repositories";
        if (className.EndsWith("Model") || className.EndsWith("Entity")) return "Models";
        if (className.EndsWith("Helper") || className.EndsWith("Utility")) return "Utilities";
        if (className.EndsWith("Exception")) return "Exceptions";
        if (className.EndsWith("Attribute")) return "Attributes";
        if (className.EndsWith("Extension")) return "Extensions";
        
        return "Core";
    }

    private Dictionary<string, List<string>> AnalyzeDependencies(List<CodeChunk> chunks, SyntaxNode root)
    {
        var relationships = new Dictionary<string, List<string>>();

        foreach (var chunk in chunks)
        {
            relationships[chunk.Id] = new List<string>();
            
            // Find dependencies based on type references, method calls, etc.
            var dependencies = FindDependenciesInChunk(chunk, chunks, root);
            relationships[chunk.Id].AddRange(dependencies);
        }

        return relationships;
    }

    private List<string> FindDependenciesInChunk(CodeChunk chunk, List<CodeChunk> allChunks, SyntaxNode root)
    {
        var dependencies = new List<string>();
        
        // Parse the chunk content to find references to other chunks
        var chunkTree = CSharpSyntaxTree.ParseText(chunk.Content);
        var chunkRoot = chunkTree.GetRoot();

        // Find type references
        var identifiers = chunkRoot.DescendantNodes().OfType<IdentifierNameSyntax>();
        
        foreach (var identifier in identifiers)
        {
            var referencedChunk = allChunks.FirstOrDefault(c => 
                c.Id != chunk.Id && 
                (c.Name == identifier.Identifier.ValueText || 
                 c.Name.EndsWith($".{identifier.Identifier.ValueText}")));
            
            if (referencedChunk != null && !dependencies.Contains(referencedChunk.Id))
            {
                dependencies.Add(referencedChunk.Id);
            }
        }

        return dependencies;
    }

    private Models.Location GetLocation(SyntaxNode node, string filePath)
    {
        var span = node.GetLocation().GetLineSpan();
        return new Models.Location
        {
            FilePath = filePath,
            StartLine = span.StartLinePosition.Line + 1,
            EndLine = span.EndLinePosition.Line + 1,
            StartColumn = span.StartLinePosition.Character + 1,
            EndColumn = span.EndLinePosition.Character + 1
        };
    }

    private int CalculateComplexity(SyntaxNode node)
    {
        // Simple complexity calculation based on control flow statements
        var complexity = 1; // Base complexity

        var controlFlowNodes = node.DescendantNodes().Where(n =>
            n is IfStatementSyntax ||
            n is WhileStatementSyntax ||
            n is ForStatementSyntax ||
            n is ForEachStatementSyntax ||
            n is SwitchStatementSyntax ||
            n is ConditionalExpressionSyntax ||
            n is CatchClauseSyntax);

        complexity += controlFlowNodes.Count();

        return complexity;
    }

    private Dictionary<string, object> ExtractClassMetadata(ClassDeclarationSyntax classDecl)
    {
        return new Dictionary<string, object>
        {
            ["IsAbstract"] = classDecl.Modifiers.Any(m => m.IsKind(SyntaxKind.AbstractKeyword)),
            ["IsSealed"] = classDecl.Modifiers.Any(m => m.IsKind(SyntaxKind.SealedKeyword)),
            ["IsStatic"] = classDecl.Modifiers.Any(m => m.IsKind(SyntaxKind.StaticKeyword)),
            ["AccessModifier"] = GetAccessModifier(classDecl.Modifiers),
            ["BaseTypes"] = classDecl.BaseList?.Types.Select(t => t.ToString()).ToList() ?? new List<string>(),
            ["MethodCount"] = classDecl.Members.OfType<MethodDeclarationSyntax>().Count(),
            ["PropertyCount"] = classDecl.Members.OfType<PropertyDeclarationSyntax>().Count(),
            ["FieldCount"] = classDecl.Members.OfType<FieldDeclarationSyntax>().Count()
        };
    }

    private Dictionary<string, object> ExtractInterfaceMetadata(InterfaceDeclarationSyntax interfaceDecl)
    {
        return new Dictionary<string, object>
        {
            ["AccessModifier"] = GetAccessModifier(interfaceDecl.Modifiers),
            ["BaseInterfaces"] = interfaceDecl.BaseList?.Types.Select(t => t.ToString()).ToList() ?? new List<string>(),
            ["MethodCount"] = interfaceDecl.Members.OfType<MethodDeclarationSyntax>().Count(),
            ["PropertyCount"] = interfaceDecl.Members.OfType<PropertyDeclarationSyntax>().Count()
        };
    }

    private Dictionary<string, object> ExtractMethodMetadata(MethodDeclarationSyntax method, string className)
    {
        return new Dictionary<string, object>
        {
            ["ClassName"] = className,
            ["AccessModifier"] = GetAccessModifier(method.Modifiers),
            ["IsStatic"] = method.Modifiers.Any(m => m.IsKind(SyntaxKind.StaticKeyword)),
            ["IsVirtual"] = method.Modifiers.Any(m => m.IsKind(SyntaxKind.VirtualKeyword)),
            ["IsOverride"] = method.Modifiers.Any(m => m.IsKind(SyntaxKind.OverrideKeyword)),
            ["IsAsync"] = method.Modifiers.Any(m => m.IsKind(SyntaxKind.AsyncKeyword)),
            ["ReturnType"] = method.ReturnType.ToString(),
            ["ParameterCount"] = method.ParameterList.Parameters.Count,
            ["Parameters"] = method.ParameterList.Parameters.Select(p => new { Name = p.Identifier.ValueText, Type = p.Type?.ToString() }).ToList()
        };
    }

    private Dictionary<string, object> ExtractPropertyMetadata(PropertyDeclarationSyntax property, string className)
    {
        return new Dictionary<string, object>
        {
            ["ClassName"] = className,
            ["AccessModifier"] = GetAccessModifier(property.Modifiers),
            ["IsStatic"] = property.Modifiers.Any(m => m.IsKind(SyntaxKind.StaticKeyword)),
            ["Type"] = property.Type.ToString(),
            ["HasGetter"] = property.AccessorList?.Accessors.Any(a => a.IsKind(SyntaxKind.GetAccessorDeclaration)) ?? false,
            ["HasSetter"] = property.AccessorList?.Accessors.Any(a => a.IsKind(SyntaxKind.SetAccessorDeclaration)) ?? false,
            ["IsAutoProperty"] = property.AccessorList?.Accessors.All(a => a.Body == null && a.ExpressionBody == null) ?? false
        };
    }

    private Dictionary<string, object> ExtractNamespaceMetadata(SyntaxNode namespaceDecl)
    {
        var members = namespaceDecl.ChildNodes().ToList();
        return new Dictionary<string, object>
        {
            ["ClassCount"] = members.OfType<ClassDeclarationSyntax>().Count(),
            ["InterfaceCount"] = members.OfType<InterfaceDeclarationSyntax>().Count(),
            ["StructCount"] = members.OfType<StructDeclarationSyntax>().Count(),
            ["EnumCount"] = members.OfType<EnumDeclarationSyntax>().Count(),
            ["UsingDirectives"] = namespaceDecl.DescendantNodes().OfType<UsingDirectiveSyntax>().Select(u => u.Name?.ToString()).ToList()
        };
    }

    private string GetAccessModifier(SyntaxTokenList modifiers)
    {
        if (modifiers.Any(m => m.IsKind(SyntaxKind.PublicKeyword))) return "public";
        if (modifiers.Any(m => m.IsKind(SyntaxKind.PrivateKeyword))) return "private";
        if (modifiers.Any(m => m.IsKind(SyntaxKind.ProtectedKeyword))) return "protected";
        if (modifiers.Any(m => m.IsKind(SyntaxKind.InternalKeyword))) return "internal";
        return "internal"; // Default in C#
    }
}
