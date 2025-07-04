using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FindSymbols;
using Microsoft.CodeAnalysis.MSBuild;
using RoslynMCP.Models;
using RoslynMCP.Utils;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Diagnostics;

namespace RoslynMCP.Services;

/// <summary>
/// Core semantic analyzer that builds a unified semantic graph of an entire solution
/// using Roslyn's SymbolFinder APIs for accurate cross-project symbol resolution
/// </summary>
public class SemanticSolutionAnalyzer
{
    private readonly MSBuildWorkspace _workspace;
    private readonly ConcurrentDictionary<ISymbol, string> _symbolToIdMap = new(SymbolEqualityComparer.Default);
    private readonly ConcurrentDictionary<string, SemanticSymbolNode> _nodes = new();
    private readonly ConcurrentBag<SemanticRelationship> _relationships = new();
    private readonly ArchitecturalRoleClassifier _roleClassifier = new();
    private readonly FeatureBoundaryDetector _featureDetector = new();

    public SemanticSolutionAnalyzer(MSBuildWorkspace workspace)
    {
        _workspace = workspace;
    }

    /// <summary>
    /// Analyzes an entire solution and builds a unified semantic graph
    /// </summary>
    public async Task<UnifiedSemanticGraph> AnalyzeSolutionAsync(Solution solution, CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var metadata = new SemanticGraphMetadata
        {
            SolutionPath = solution.FilePath ?? "Unknown",
            AnalyzedAt = DateTime.UtcNow,
            RoslynVersion = typeof(Solution).Assembly.GetName().Version?.ToString() ?? "Unknown"
        };

        try
        {
            // Phase 1: Discover all symbols across the solution
            await DiscoverSymbolsAsync(solution, cancellationToken);

            // Phase 2: Analyze relationships using SymbolFinder
            await AnalyzeRelationshipsAsync(solution, cancellationToken);

            // Phase 3: Classify architectural roles
            await ClassifyArchitecturalRolesAsync(solution, cancellationToken);

            // Phase 4: Detect feature boundaries
            await DetectFeatureBoundariesAsync(solution, cancellationToken);

            // Phase 5: Build cross-project dependencies
            var crossProjectDeps = await AnalyzeCrossProjectDependenciesAsync(solution, cancellationToken);

            // Phase 6: Build project information
            var projectInfo = await BuildProjectInformationAsync(solution, cancellationToken);

            stopwatch.Stop();
            metadata.AnalysisDuration = stopwatch.Elapsed;
            metadata.TotalProjects = solution.Projects.Count();
            metadata.TotalSymbols = _nodes.Count;
            metadata.TotalRelationships = _relationships.Count;

            // Calculate statistics
            CalculateStatistics(metadata);

            return new UnifiedSemanticGraph
            {
                Nodes = new Dictionary<string, SemanticSymbolNode>(_nodes),
                Relationships = _relationships.ToList(),
                Metadata = metadata,
                ProjectInfo = projectInfo,
                CrossProjectDependencies = crossProjectDeps
            };
        }
        catch (Exception ex)
        {
            metadata.Errors.Add($"Analysis failed: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// Phase 1: Discover all symbols across the solution using SymbolFinder
    /// </summary>
    private async Task DiscoverSymbolsAsync(Solution solution, CancellationToken cancellationToken)
    {
        var tasks = solution.Projects.Select(async project =>
        {
            try
            {
                var compilation = await project.GetCompilationAsync(cancellationToken);
                if (compilation == null) return;

                // Get all symbols in the project
                var symbols = await SymbolFinder.FindDeclarationsAsync(project, "", true, cancellationToken);
                
                await ProcessSymbolsInParallel(symbols, project, compilation, cancellationToken);
            }
            catch (Exception ex)
            {
                // Log error but continue with other projects
                Console.WriteLine($"Error processing project {project.Name}: {ex.Message}");
            }
        });

        await Task.WhenAll(tasks);
    }

    /// <summary>
    /// Process symbols in parallel for better performance
    /// </summary>
    private async Task ProcessSymbolsInParallel(IEnumerable<ISymbol> symbols, Project project, Compilation compilation, CancellationToken cancellationToken)
    {
        var semaphore = new SemaphoreSlim(Environment.ProcessorCount * 2);
        var tasks = symbols.Select(async symbol =>
        {
            await semaphore.WaitAsync(cancellationToken);
            try
            {
                await ProcessSymbolAsync(symbol, project, compilation, cancellationToken);
            }
            finally
            {
                semaphore.Release();
            }
        });

        await Task.WhenAll(tasks);
    }

    /// <summary>
    /// Process individual symbol and create semantic node
    /// </summary>
    private async Task ProcessSymbolAsync(ISymbol symbol, Project project, Compilation compilation, CancellationToken cancellationToken)
    {
        if (ShouldSkipSymbol(symbol)) return;

        var symbolId = GenerateSymbolId(symbol);
        _symbolToIdMap.TryAdd(symbol, symbolId);

        var node = await CreateSemanticSymbolNodeAsync(symbol, project, compilation, cancellationToken);
        _nodes.TryAdd(symbolId, node);
    }

    /// <summary>
    /// Phase 2: Analyze relationships using SymbolFinder APIs
    /// </summary>
    private async Task AnalyzeRelationshipsAsync(Solution solution, CancellationToken cancellationToken)
    {
        var tasks = _nodes.Values.Select(async node =>
        {
            try
            {
                var symbol = await FindSymbolByIdAsync(node.Id, solution, cancellationToken);
                if (symbol != null)
                {
                    await AnalyzeSymbolRelationshipsAsync(symbol, solution, cancellationToken);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error analyzing relationships for {node.Name}: {ex.Message}");
            }
        });

        await Task.WhenAll(tasks);
    }

    /// <summary>
    /// Analyze relationships for a specific symbol using SymbolFinder
    /// </summary>
    private async Task AnalyzeSymbolRelationshipsAsync(ISymbol symbol, Solution solution, CancellationToken cancellationToken)
    {
        // Find all references to this symbol
        var references = await SymbolFinder.FindReferencesAsync(symbol, solution, cancellationToken);
        await ProcessReferencesAsync(symbol, references, cancellationToken);

        // Find implementations if it's an interface or abstract member
        if (symbol.IsAbstract || (symbol.Kind == Microsoft.CodeAnalysis.SymbolKind.NamedType && symbol is INamedTypeSymbol namedTypeSymbol && namedTypeSymbol.TypeKind == TypeKind.Interface))
        {
            IEnumerable<ISymbol> implementations;
            if (symbol is INamedTypeSymbol namedTypeForImpl)
            {
                implementations = await SymbolFinder.FindImplementationsAsync(namedTypeForImpl, solution, true, cancellationToken: cancellationToken);
            }
            else
            {
                implementations = ImmutableArray<ISymbol>.Empty;
            }
            await ProcessImplementationsAsync(symbol, implementations, cancellationToken);
        }

        // Find derived types if it's a class
        if (symbol is INamedTypeSymbol namedType && namedType.TypeKind == TypeKind.Class)
        {
            var derivedTypes = await SymbolFinder.FindDerivedClassesAsync(namedType, solution, ImmutableHashSet<Project>.Empty, cancellationToken);
            await ProcessDerivedTypesAsync(symbol, derivedTypes, cancellationToken);
        }

        // Analyze type-specific relationships
        await AnalyzeTypeSpecificRelationshipsAsync(symbol, solution, cancellationToken);
    }

    /// <summary>
    /// Process symbol references and create relationships
    /// </summary>
    private async Task ProcessReferencesAsync(ISymbol symbol, IEnumerable<ReferencedSymbol> references, CancellationToken cancellationToken)
    {
        var sourceId = _symbolToIdMap.GetValueOrDefault(symbol);
        if (string.IsNullOrEmpty(sourceId)) return;

        foreach (var referencedSymbol in references)
        {
            foreach (var location in referencedSymbol.Locations)
            {
                var referenceSymbol = await GetSymbolAtLocationAsync(location, cancellationToken);
                if (referenceSymbol != null)
                {
                    var targetId = _symbolToIdMap.GetValueOrDefault(referenceSymbol);
                    if (!string.IsNullOrEmpty(targetId) && targetId != sourceId)
                    {
                        var relationship = CreateSemanticRelationship(
                            targetId, sourceId, 
                            DetermineRelationshipType(referenceSymbol, symbol, location),
                            location.Location);
                        
                        _relationships.Add(relationship);
                    }
                }
            }
        }
    }

    /// <summary>
    /// Process implementations and create inheritance relationships
    /// </summary>
    private async Task ProcessImplementationsAsync(ISymbol symbol, IEnumerable<ISymbol> implementations, CancellationToken cancellationToken)
    {
        var sourceId = _symbolToIdMap.GetValueOrDefault(symbol);
        if (string.IsNullOrEmpty(sourceId)) return;

        foreach (var implementation in implementations)
        {
            var targetId = _symbolToIdMap.GetValueOrDefault(implementation);
            if (!string.IsNullOrEmpty(targetId))
            {
                var relationshipType = symbol.Kind == Microsoft.CodeAnalysis.SymbolKind.NamedType && 
                                     ((INamedTypeSymbol)symbol).TypeKind == TypeKind.Interface
                    ? SemanticRelationshipType.Implementation
                    : SemanticRelationshipType.MethodImplementation;

                var relationship = CreateSemanticRelationship(targetId, sourceId, relationshipType);
                _relationships.Add(relationship);
            }
        }
    }

    /// <summary>
    /// Process derived types and create inheritance relationships
    /// </summary>
    private async Task ProcessDerivedTypesAsync(ISymbol symbol, IEnumerable<INamedTypeSymbol> derivedTypes, CancellationToken cancellationToken)
    {
        var sourceId = _symbolToIdMap.GetValueOrDefault(symbol);
        if (string.IsNullOrEmpty(sourceId)) return;

        foreach (var derivedType in derivedTypes)
        {
            var targetId = _symbolToIdMap.GetValueOrDefault(derivedType);
            if (!string.IsNullOrEmpty(targetId))
            {
                var relationship = CreateSemanticRelationship(targetId, sourceId, SemanticRelationshipType.Inheritance);
                _relationships.Add(relationship);
            }
        }
    }

    /// <summary>
    /// Analyze type-specific relationships (composition, aggregation, etc.)
    /// </summary>
    private async Task AnalyzeTypeSpecificRelationshipsAsync(ISymbol symbol, Solution solution, CancellationToken cancellationToken)
    {
        switch (symbol.Kind)
        {
            case Microsoft.CodeAnalysis.SymbolKind.NamedType:
                await AnalyzeNamedTypeRelationshipsAsync((INamedTypeSymbol)symbol, solution, cancellationToken);
                break;
            case Microsoft.CodeAnalysis.SymbolKind.Method:
                await AnalyzeMethodRelationshipsAsync((IMethodSymbol)symbol, solution, cancellationToken);
                break;
            case Microsoft.CodeAnalysis.SymbolKind.Property:
                await AnalyzePropertyRelationshipsAsync((IPropertySymbol)symbol, solution, cancellationToken);
                break;
            case Microsoft.CodeAnalysis.SymbolKind.Field:
                await AnalyzeFieldRelationshipsAsync((IFieldSymbol)symbol, solution, cancellationToken);
                break;
        }
    }

    /// <summary>
    /// Analyze relationships for named types (classes, interfaces, etc.)
    /// </summary>
    private async Task AnalyzeNamedTypeRelationshipsAsync(INamedTypeSymbol namedType, Solution solution, CancellationToken cancellationToken)
    {
        var sourceId = _symbolToIdMap.GetValueOrDefault(namedType);
        if (string.IsNullOrEmpty(sourceId)) return;

        // Analyze base type relationships
        if (namedType.BaseType != null)
        {
            var baseTypeId = _symbolToIdMap.GetValueOrDefault(namedType.BaseType);
            if (!string.IsNullOrEmpty(baseTypeId))
            {
                var relationship = CreateSemanticRelationship(sourceId, baseTypeId, SemanticRelationshipType.Inheritance);
                _relationships.Add(relationship);
            }
        }

        // Analyze interface implementations
        foreach (var interfaceType in namedType.Interfaces)
        {
            var interfaceId = _symbolToIdMap.GetValueOrDefault(interfaceType);
            if (!string.IsNullOrEmpty(interfaceId))
            {
                var relationship = CreateSemanticRelationship(sourceId, interfaceId, SemanticRelationshipType.Implementation);
                _relationships.Add(relationship);
            }
        }

        // Analyze composition relationships through fields and properties
        var members = namedType.GetMembers();
        foreach (var member in members)
        {
            if (member is IFieldSymbol field && !field.IsStatic)
            {
                await AnalyzeCompositionRelationshipAsync(sourceId, field.Type, SemanticRelationshipType.Composition, cancellationToken);
            }
            else if (member is IPropertySymbol property && !property.IsStatic)
            {
                await AnalyzeCompositionRelationshipAsync(sourceId, property.Type, SemanticRelationshipType.Composition, cancellationToken);
            }
        }
    }

    /// <summary>
    /// Analyze method relationships (calls, overrides, etc.)
    /// </summary>
    private async Task AnalyzeMethodRelationshipsAsync(IMethodSymbol method, Solution solution, CancellationToken cancellationToken)
    {
        var sourceId = _symbolToIdMap.GetValueOrDefault(method);
        if (string.IsNullOrEmpty(sourceId)) return;

        // Analyze override relationships
        if (method.OverriddenMethod != null)
        {
            var overriddenId = _symbolToIdMap.GetValueOrDefault(method.OverriddenMethod);
            if (!string.IsNullOrEmpty(overriddenId))
            {
                var relationship = CreateSemanticRelationship(sourceId, overriddenId, SemanticRelationshipType.MethodOverride);
                _relationships.Add(relationship);
            }
        }

        // Analyze parameter type relationships
        foreach (var parameter in method.Parameters)
        {
            await AnalyzeCompositionRelationshipAsync(sourceId, parameter.Type, SemanticRelationshipType.Association, cancellationToken);
        }

        // Analyze return type relationship
        if (!method.ReturnsVoid)
        {
            await AnalyzeCompositionRelationshipAsync(sourceId, method.ReturnType, SemanticRelationshipType.Association, cancellationToken);
        }
    }

    /// <summary>
    /// Analyze property relationships
    /// </summary>
    private async Task AnalyzePropertyRelationshipsAsync(IPropertySymbol property, Solution solution, CancellationToken cancellationToken)
    {
        var sourceId = _symbolToIdMap.GetValueOrDefault(property);
        if (string.IsNullOrEmpty(sourceId)) return;

        await AnalyzeCompositionRelationshipAsync(sourceId, property.Type, SemanticRelationshipType.Association, cancellationToken);
    }

    /// <summary>
    /// Analyze field relationships
    /// </summary>
    private async Task AnalyzeFieldRelationshipsAsync(IFieldSymbol field, Solution solution, CancellationToken cancellationToken)
    {
        var sourceId = _symbolToIdMap.GetValueOrDefault(field);
        if (string.IsNullOrEmpty(sourceId)) return;

        var relationshipType = field.IsReadOnly ? SemanticRelationshipType.Composition : SemanticRelationshipType.Aggregation;
        await AnalyzeCompositionRelationshipAsync(sourceId, field.Type, relationshipType, cancellationToken);
    }

    /// <summary>
    /// Analyze composition/aggregation relationships
    /// </summary>
    private async Task AnalyzeCompositionRelationshipAsync(string sourceId, ITypeSymbol targetType, SemanticRelationshipType relationshipType, CancellationToken cancellationToken)
    {
        var targetId = _symbolToIdMap.GetValueOrDefault(targetType);
        if (!string.IsNullOrEmpty(targetId) && targetId != sourceId)
        {
            var relationship = CreateSemanticRelationship(sourceId, targetId, relationshipType);
            _relationships.Add(relationship);
        }
    }

    /// <summary>
    /// Phase 3: Classify architectural roles for symbols
    /// </summary>
    private async Task ClassifyArchitecturalRolesAsync(Solution solution, CancellationToken cancellationToken)
    {
        var tasks = _nodes.Values.Select(async node =>
        {
            try
            {
                var symbol = await FindSymbolByIdAsync(node.Id, solution, cancellationToken);
                if (symbol != null)
                {
                    node.Role = await _roleClassifier.ClassifySymbolRoleAsync(symbol, solution, cancellationToken);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error classifying role for {node.Name}: {ex.Message}");
            }
        });

        await Task.WhenAll(tasks);
    }

    /// <summary>
    /// Phase 4: Detect feature boundaries
    /// </summary>
    private async Task DetectFeatureBoundariesAsync(Solution solution, CancellationToken cancellationToken)
    {
        var featureBoundaries = await _featureDetector.DetectFeatureBoundariesAsync(_nodes.Values, _relationships, cancellationToken);
        
        foreach (var (featureName, symbolIds) in featureBoundaries)
        {
            foreach (var symbolId in symbolIds)
            {
                if (_nodes.TryGetValue(symbolId, out var node))
                {
                    node.FeatureBoundary = featureName;
                }
            }
        }
    }

    /// <summary>
    /// Phase 5: Analyze cross-project dependencies
    /// </summary>
    private async Task<List<CrossProjectDependency>> AnalyzeCrossProjectDependenciesAsync(Solution solution, CancellationToken cancellationToken)
    {
        var dependencies = new List<CrossProjectDependency>();
        var projectPairs = solution.Projects
            .SelectMany(p1 => solution.Projects.Where(p2 => p1.Id != p2.Id), (p1, p2) => new { Source = p1, Target = p2 })
            .ToList();

        foreach (var pair in projectPairs)
        {
            var crossProjectRefs = _relationships
                .Where(r => 
                {
                    var sourceNode = _nodes.GetValueOrDefault(r.SourceSymbolId);
                    var targetNode = _nodes.GetValueOrDefault(r.TargetSymbolId);
                    return sourceNode?.ProjectId == pair.Source.Id.ToString() && 
                           targetNode?.ProjectId == pair.Target.Id.ToString();
                })
                .ToList();

            if (crossProjectRefs.Any())
            {
                var dependency = new CrossProjectDependency
                {
                    SourceProjectId = pair.Source.Id.ToString(),
                    TargetProjectId = pair.Target.Id.ToString(),
                    DependencyType = "ProjectReference",
                    SharedSymbols = crossProjectRefs.Select(r => r.TargetSymbolId).Distinct().ToList(),
                    ReferenceCount = crossProjectRefs.Count,
                    CouplingStrength = CalculateCouplingStrength(crossProjectRefs)
                };

                dependencies.Add(dependency);
            }
        }

        return dependencies;
    }

    /// <summary>
    /// Phase 6: Build project information
    /// </summary>
    private async Task<Dictionary<string, ProjectSemanticInfo>> BuildProjectInformationAsync(Solution solution, CancellationToken cancellationToken)
    {
        var projectInfo = new Dictionary<string, ProjectSemanticInfo>();

        foreach (var project in solution.Projects)
        {
            var projectNodes = _nodes.Values.Where(n => n.ProjectId == project.Id.ToString()).ToList();
            var projectRelationships = _relationships.Where(r => 
                projectNodes.Any(n => n.Id == r.SourceSymbolId) || 
                projectNodes.Any(n => n.Id == r.TargetSymbolId)).ToList();

            var info = new ProjectSemanticInfo
            {
                ProjectId = project.Id.ToString(),
                ProjectName = project.Name,
                ProjectPath = project.FilePath ?? "Unknown",
                TargetFramework = project.CompilationOptions?.Platform.ToString() ?? "Unknown",
                Languages = project.Documents.Select(d => d.Project.Language).Distinct().ToList(),
                Dependencies = project.ProjectReferences.Select(pr => pr.ProjectId.ToString()).ToList(),
                RoleDistribution = projectNodes.GroupBy(n => n.Role).ToDictionary(g => g.Key, g => g.Count()),
                FeatureBoundaries = projectNodes.Select(n => n.FeatureBoundary).Where(f => f != null).Distinct().ToList()!,
                Metrics = new ProjectMetrics
                {
                    TotalSymbols = projectNodes.Count,
                    TotalRelationships = projectRelationships.Count,
                    CrossProjectReferences = projectRelationships.Count(r => r.IsCrossProject),
                    AverageComplexity = projectNodes.Any() ? projectNodes.Average(n => n.Metrics.CyclomaticComplexity) : 0.0,
                    LanguageDistribution = projectNodes
                        .Where(n => n.Location?.File != null)
                        .GroupBy(n => GetLanguageFromPath(n.Location.File))
                        .ToDictionary(g => g.Key, g => g.Count())
                }
            };

            projectInfo[project.Id.ToString()] = info;
        }

        return projectInfo;
    }

    #region Helper Methods

    /// <summary>
    /// Create a semantic symbol node from a Roslyn symbol
    /// </summary>
    private async Task<SemanticSymbolNode> CreateSemanticSymbolNodeAsync(ISymbol symbol, Project project, Compilation compilation, CancellationToken cancellationToken)
    {
        var location = symbol.Locations.FirstOrDefault();

        var node = new SemanticSymbolNode
        {
            Id = GenerateSymbolId(symbol),
            Name = symbol.Name,
            FullName = symbol.ToDisplayString(),
            Kind = symbol.Kind,
            TypeName = GetTypeName(symbol),
            Location = LocationConverter.ToSymbolLocation(location),
            Accessibility = symbol.DeclaredAccessibility,
            Modifiers = GetModifiers(symbol),
            ProjectId = project.Id.ToString(),
            AssemblyName = project.AssemblyName ?? "Unknown",
            Interfaces = GetInterfaces(symbol),
            BaseType = GetBaseType(symbol),
            GenericTypeParameters = GetGenericTypeParameters(symbol),
            Metrics = await CalculateSemanticMetricsAsync(symbol, compilation, cancellationToken)
        };

        return node;
    }

    /// <summary>
    /// Create a semantic relationship
    /// </summary>
    private SemanticRelationship CreateSemanticRelationship(string sourceId, string targetId, SemanticRelationshipType type, Microsoft.CodeAnalysis.Location? location = null)
    {
        var relationship = new SemanticRelationship
        {
            SourceSymbolId = sourceId,
            TargetSymbolId = targetId,
            Type = type,
            Label = type.ToString(),
            Location = LocationConverter.ToSymbolLocation(location),
            IsCrossProject = IsCrossProjectRelationship(sourceId, targetId),
            IsCrossLanguage = IsCrossLanguageRelationship(sourceId, targetId)
        };

        return relationship;
    }

    /// <summary>
    /// Generate unique identifier for symbol
    /// </summary>
    private string GenerateSymbolId(ISymbol symbol)
    {
        return $"{symbol.ContainingAssembly?.Name ?? "Unknown"}::{symbol.ToDisplayString()}";
    }

    /// <summary>
    /// Determine if we should skip analyzing this symbol
    /// </summary>
    private bool ShouldSkipSymbol(ISymbol symbol)
    {
        return symbol.IsImplicitlyDeclared ||
               symbol.Kind == Microsoft.CodeAnalysis.SymbolKind.Namespace ||
               symbol.ContainingAssembly?.Name?.StartsWith("System") == true ||
               symbol.ContainingAssembly?.Name?.StartsWith("Microsoft") == true;
    }

    /// <summary>
    /// Calculate semantic metrics for a symbol
    /// </summary>
    private async Task<SemanticMetrics> CalculateSemanticMetricsAsync(ISymbol symbol, Compilation compilation, CancellationToken cancellationToken)
    {
        var metrics = new SemanticMetrics();

        if (symbol is INamedTypeSymbol namedType)
        {
            metrics.NumberOfMethods = namedType.GetMembers().OfType<IMethodSymbol>().Count();
            metrics.NumberOfProperties = namedType.GetMembers().OfType<IPropertySymbol>().Count();
            metrics.NumberOfFields = namedType.GetMembers().OfType<IFieldSymbol>().Count();
            metrics.DepthOfInheritance = CalculateDepthOfInheritance(namedType);
        }
        else if (symbol is IMethodSymbol method)
        {
            metrics.NumberOfParameters = method.Parameters.Length;
            // TODO: Calculate cyclomatic complexity by analyzing method body
        }

        return metrics;
    }

    /// <summary>
    /// Get type name for symbol
    /// </summary>
    private string GetTypeName(ISymbol symbol)
    {
        return symbol switch
        {
            INamedTypeSymbol namedType => namedType.TypeKind.ToString(),
            IMethodSymbol => "Method",
            IPropertySymbol => "Property",
            IFieldSymbol => "Field",
            IEventSymbol => "Event",
            _ => symbol.Kind.ToString()
        };
    }

    /// <summary>
    /// Get modifiers for symbol
    /// </summary>
    private List<string> GetModifiers(ISymbol symbol)
    {
        var modifiers = new List<string>();
        
        if (symbol.IsStatic) modifiers.Add("static");
        if (symbol.IsAbstract) modifiers.Add("abstract");
        if (symbol.IsVirtual) modifiers.Add("virtual");
        if (symbol.IsOverride) modifiers.Add("override");
        if (symbol.IsSealed) modifiers.Add("sealed");
        if (symbol.IsExtern) modifiers.Add("extern");

        return modifiers;
    }

    /// <summary>
    /// Get interfaces implemented by symbol
    /// </summary>
    private List<string> GetInterfaces(ISymbol symbol)
    {
        if (symbol is INamedTypeSymbol namedType)
        {
            return namedType.Interfaces.Select(i => i.ToDisplayString()).ToList();
        }
        return new List<string>();
    }

    /// <summary>
    /// Get base type of symbol
    /// </summary>
    private string? GetBaseType(ISymbol symbol)
    {
        if (symbol is INamedTypeSymbol namedType && namedType.BaseType != null)
        {
            return namedType.BaseType.ToDisplayString();
        }
        return null;
    }

    /// <summary>
    /// Get generic type parameters
    /// </summary>
    private List<string> GetGenericTypeParameters(ISymbol symbol)
    {
        return symbol switch
        {
            INamedTypeSymbol namedType => namedType.TypeParameters.Select(tp => tp.Name).ToList(),
            IMethodSymbol method => method.TypeParameters.Select(tp => tp.Name).ToList(),
            _ => new List<string>()
        };
    }

    /// <summary>
    /// Calculate depth of inheritance for a type
    /// </summary>
    private int CalculateDepthOfInheritance(INamedTypeSymbol type)
    {
        int depth = 0;
        var current = type.BaseType;
        
        while (current != null && current.SpecialType != SpecialType.System_Object)
        {
            depth++;
            current = current.BaseType;
        }
        
        return depth;
    }

    /// <summary>
    /// Determine relationship type based on context
    /// </summary>
    private SemanticRelationshipType DetermineRelationshipType(ISymbol source, ISymbol target, ReferenceLocation location)
    {
        // This is a simplified implementation - in practice, you'd analyze the syntax context
        return SemanticRelationshipType.Association;
    }

    /// <summary>
    /// Check if relationship crosses project boundaries
    /// </summary>
    private bool IsCrossProjectRelationship(string sourceId, string targetId)
    {
        var sourceNode = _nodes.GetValueOrDefault(sourceId);
        var targetNode = _nodes.GetValueOrDefault(targetId);
        return sourceNode?.ProjectId != targetNode?.ProjectId;
    }

    /// <summary>
    /// Check if relationship crosses language boundaries
    /// </summary>
    private bool IsCrossLanguageRelationship(string sourceId, string targetId)
    {
        var sourceNode = _nodes.GetValueOrDefault(sourceId);
        var targetNode = _nodes.GetValueOrDefault(targetId);
        
        if (sourceNode?.Location?.File == null || targetNode?.Location?.File == null)
            return false;
            
        var sourceLanguage = GetLanguageFromPath(sourceNode.Location.File);
        var targetLanguage = GetLanguageFromPath(targetNode.Location.File);
        
        return sourceLanguage != targetLanguage;
    }

    /// <summary>
    /// Get language from file path
    /// </summary>
    private string GetLanguageFromPath(string filePath)
    {
        var extension = Path.GetExtension(filePath).ToLowerInvariant();
        return extension switch
        {
            ".cs" => "C#",
            ".xaml" => "XAML",
            ".sql" => "SQL",
            ".vb" => "VB.NET",
            ".fs" => "F#",
            _ => "Unknown"
        };
    }

    /// <summary>
    /// Calculate coupling strength between projects
    /// </summary>
    private double CalculateCouplingStrength(List<SemanticRelationship> relationships)
    {
        if (!relationships.Any()) return 0.0;
        
        // Simple calculation based on number and types of relationships
        var weights = new Dictionary<SemanticRelationshipType, double>
        {
            { SemanticRelationshipType.Inheritance, 1.0 },
            { SemanticRelationshipType.Implementation, 0.9 },
            { SemanticRelationshipType.Composition, 0.8 },
            { SemanticRelationshipType.MethodCall, 0.6 },
            { SemanticRelationshipType.PropertyAccess, 0.5 },
            { SemanticRelationshipType.Association, 0.3 }
        };

        var totalWeight = relationships.Sum(r => weights.GetValueOrDefault(r.Type, 0.1));
        return Math.Min(totalWeight / relationships.Count, 1.0);
    }

    /// <summary>
    /// Calculate statistics for the semantic graph metadata
    /// </summary>
    private void CalculateStatistics(SemanticGraphMetadata metadata)
    {
        metadata.SymbolDistribution = _nodes.Values
            .GroupBy(n => n.Kind)
            .ToDictionary(g => g.Key, g => g.Count());

        metadata.RelationshipDistribution = _relationships
            .GroupBy(r => r.Type)
            .ToDictionary(g => g.Key, g => g.Count());

        metadata.RoleDistribution = _nodes.Values
            .GroupBy(n => n.Role)
            .ToDictionary(g => g.Key, g => g.Count());

        metadata.CrossProjectRelationships = _relationships.Count(r => r.IsCrossProject);
        metadata.CrossLanguageRelationships = _relationships.Count(r => r.IsCrossLanguage);
    }

    /// <summary>
    /// Find symbol by ID in the solution
    /// </summary>
    private async Task<ISymbol?> FindSymbolByIdAsync(string symbolId, Solution solution, CancellationToken cancellationToken)
    {
        return _symbolToIdMap.FirstOrDefault(kvp => string.Equals(kvp.Value, symbolId, StringComparison.Ordinal)).Key;
    }

    /// <summary>
    /// Get symbol at a specific location
    /// </summary>
    private async Task<ISymbol?> GetSymbolAtLocationAsync(ReferenceLocation location, CancellationToken cancellationToken)
    {
        var document = location.Document;
        if (document == null) return null;

        var semanticModel = await document.GetSemanticModelAsync(cancellationToken);
        if (semanticModel == null) return null;

        var syntaxTree = await document.GetSyntaxTreeAsync(cancellationToken);
        if (syntaxTree == null) return null;

        var root = await syntaxTree.GetRootAsync(cancellationToken);
        var node = root.FindNode(location.Location.SourceSpan);

        return semanticModel.GetSymbolInfo(node, cancellationToken).Symbol;
    }

    #endregion
}

/// <summary>
/// Classifies architectural roles for symbols based on semantic analysis
/// </summary>
public class ArchitecturalRoleClassifier
{
    public async Task<ArchitecturalRole> ClassifySymbolRoleAsync(ISymbol symbol, Solution solution, CancellationToken cancellationToken)
    {
        if (symbol is not INamedTypeSymbol namedType)
            return ArchitecturalRole.Unknown;

        var typeName = namedType.Name.ToLowerInvariant();
        var namespaceName = namedType.ContainingNamespace?.ToDisplayString().ToLowerInvariant() ?? "";
        var fileName = symbol.Locations.FirstOrDefault()?.SourceTree?.FilePath ?? "";

        // UI Layer classification
        if (IsUIComponent(typeName, namespaceName, fileName))
        {
            if (typeName.EndsWith("viewmodel") || namespaceName.Contains("viewmodel"))
                return ArchitecturalRole.ViewModel;
            if (typeName.EndsWith("view") || fileName.EndsWith(".xaml.cs"))
                return ArchitecturalRole.View;
            if (typeName.EndsWith("window"))
                return ArchitecturalRole.Window;
            if (typeName.EndsWith("page"))
                return ArchitecturalRole.Page;
            if (typeName.EndsWith("usercontrol"))
                return ArchitecturalRole.UserControl;
            if (fileName.EndsWith(".xaml.cs"))
                return ArchitecturalRole.CodeBehind;
        }

        // Business Layer classification
        if (IsBusinessComponent(typeName, namespaceName))
        {
            if (typeName.EndsWith("service") || namespaceName.Contains("service"))
                return ArchitecturalRole.Service;
            if (typeName.EndsWith("entity") || namespaceName.Contains("entities"))
                return ArchitecturalRole.Entity;
            if (namespaceName.Contains("domain"))
                return ArchitecturalRole.Domain;
            if (typeName.EndsWith("valueobject"))
                return ArchitecturalRole.ValueObject;
            return ArchitecturalRole.BusinessLogic;
        }

        // Data Layer classification
        if (IsDataComponent(typeName, namespaceName))
        {
            if (typeName.EndsWith("repository") || namespaceName.Contains("repository"))
                return ArchitecturalRole.Repository;
            if (typeName.EndsWith("context") || typeName.EndsWith("dbcontext"))
                return ArchitecturalRole.DbContext;
            if (namespaceName.Contains("data"))
                return ArchitecturalRole.DataAccess;
            return ArchitecturalRole.DataModel;
        }

        // Infrastructure classification
        if (IsInfrastructureComponent(typeName, namespaceName))
        {
            if (typeName.EndsWith("controller"))
                return ArchitecturalRole.Controller;
            if (typeName.EndsWith("apicontroller"))
                return ArchitecturalRole.ApiController;
            if (typeName.EndsWith("middleware"))
                return ArchitecturalRole.Middleware;
            if (typeName.EndsWith("configuration") || typeName.EndsWith("config"))
                return ArchitecturalRole.Configuration;
        }

        // Framework patterns
        if (namedType.TypeKind == TypeKind.Interface)
            return ArchitecturalRole.Interface;
        if (namedType.IsAbstract)
            return ArchitecturalRole.AbstractClass;
        if (typeName.EndsWith("factory"))
            return ArchitecturalRole.Factory;
        if (typeName.EndsWith("builder"))
            return ArchitecturalRole.Builder;

        // Cross-cutting concerns
        if (IsCrossCuttingConcern(typeName, namespaceName))
        {
            if (typeName.EndsWith("helper"))
                return ArchitecturalRole.Helper;
            if (typeName.EndsWith("utility") || typeName.EndsWith("util"))
                return ArchitecturalRole.Utility;
            if (typeName.EndsWith("extension"))
                return ArchitecturalRole.Extension;
            if (typeName.EndsWith("attribute"))
                return ArchitecturalRole.Attribute;
        }

        return ArchitecturalRole.Unknown;
    }

    private bool IsUIComponent(string typeName, string namespaceName, string fileName)
    {
        return namespaceName.Contains("ui") || 
               namespaceName.Contains("view") || 
               namespaceName.Contains("presentation") ||
               fileName.Contains("Views") ||
               fileName.Contains("ViewModels") ||
               fileName.EndsWith(".xaml.cs");
    }

    private bool IsBusinessComponent(string typeName, string namespaceName)
    {
        return namespaceName.Contains("business") ||
               namespaceName.Contains("domain") ||
               namespaceName.Contains("service") ||
               namespaceName.Contains("logic") ||
               namespaceName.Contains("core");
    }

    private bool IsDataComponent(string typeName, string namespaceName)
    {
        return namespaceName.Contains("data") ||
               namespaceName.Contains("repository") ||
               namespaceName.Contains("persistence") ||
               namespaceName.Contains("storage") ||
               namespaceName.Contains("dal");
    }

    private bool IsInfrastructureComponent(string typeName, string namespaceName)
    {
        return namespaceName.Contains("infrastructure") ||
               namespaceName.Contains("api") ||
               namespaceName.Contains("web") ||
               namespaceName.Contains("controller") ||
               namespaceName.Contains("middleware");
    }

    private bool IsCrossCuttingConcern(string typeName, string namespaceName)
    {
        return namespaceName.Contains("common") ||
               namespaceName.Contains("shared") ||
               namespaceName.Contains("utility") ||
               namespaceName.Contains("helper") ||
               namespaceName.Contains("extension");
    }
}

/// <summary>
/// Detects feature boundaries based on symbol relationships and clustering
/// </summary>
public class FeatureBoundaryDetector
{
    public async Task<Dictionary<string, List<string>>> DetectFeatureBoundariesAsync(
        IEnumerable<SemanticSymbolNode> nodes, 
        IEnumerable<SemanticRelationship> relationships, 
        CancellationToken cancellationToken)
    {
        var featureBoundaries = new Dictionary<string, List<string>>();
        var nodeList = nodes.ToList();
        var relationshipList = relationships.ToList();

        // Group by namespace as initial feature boundary
        var namespaceGroups = nodeList
            .GroupBy(n => GetFeatureFromNamespace(GetNamespaceFromLocation(n.Location)))
            .Where(g => !string.IsNullOrEmpty(g.Key))
            .ToDictionary(g => g.Key, g => g.Select(n => n.Id).ToList());

        foreach (var group in namespaceGroups)
        {
            featureBoundaries[group.Key] = group.Value;
        }

        // Refine boundaries using clustering based on relationships
        await RefineFeatureBoundariesAsync(featureBoundaries, relationshipList, cancellationToken);

        return featureBoundaries;
    }

    private string GetFeatureFromNamespace(string namespaceName)
    {
        if (string.IsNullOrEmpty(namespaceName)) return "Unknown";

        var parts = namespaceName.Split('.');
        if (parts.Length >= 3)
        {
            // Assume format like Company.Product.Feature
            return parts[2];
        }
        else if (parts.Length >= 2)
        {
            return parts[1];
        }
        
        return parts[0];
    }

    private async Task RefineFeatureBoundariesAsync(
        Dictionary<string, List<string>> boundaries, 
        List<SemanticRelationship> relationships, 
        CancellationToken cancellationToken)
    {
        // Simple refinement: merge features with high coupling
        var featureNames = boundaries.Keys.ToList();
        
        for (int i = 0; i < featureNames.Count; i++)
        {
            for (int j = i + 1; j < featureNames.Count; j++)
            {
                var feature1 = featureNames[i];
                var feature2 = featureNames[j];
                
                var coupling = CalculateFeatureCoupling(boundaries[feature1], boundaries[feature2], relationships);
                
                // If coupling is high, consider merging
                if (coupling > 0.7)
                {
                    var mergedName = $"{feature1}_{feature2}";
                    boundaries[mergedName] = boundaries[feature1].Concat(boundaries[feature2]).ToList();
                    boundaries.Remove(feature1);
                    boundaries.Remove(feature2);
                    break;
                }
            }
        }
    }

    private double CalculateFeatureCoupling(List<string> feature1Symbols, List<string> feature2Symbols, List<SemanticRelationship> relationships)
    {
        var crossFeatureRelationships = relationships.Count(r =>
            (feature1Symbols.Contains(r.SourceSymbolId) && feature2Symbols.Contains(r.TargetSymbolId)) ||
            (feature2Symbols.Contains(r.SourceSymbolId) && feature1Symbols.Contains(r.TargetSymbolId)));

        var totalPossibleRelationships = feature1Symbols.Count * feature2Symbols.Count;
        
        return totalPossibleRelationships > 0 ? (double)crossFeatureRelationships / totalPossibleRelationships : 0.0;
    }

    private string GetNamespaceFromLocation(SymbolLocation? location)
    {
        if (location?.File == null) return "Unknown";
        
        // Try to extract namespace from the file path or use a default approach
        var filePath = location.File;
        if (string.IsNullOrEmpty(filePath)) return "Unknown";
        
        // Simple heuristic: extract from file path structure
        var pathParts = filePath.Replace('\\', '/').Split('/');
        if (pathParts.Length >= 2)
        {
            // Look for common .NET project structure patterns
            var relevantParts = pathParts.Where(p => 
                !string.IsNullOrEmpty(p) && 
                !p.Equals("bin", StringComparison.OrdinalIgnoreCase) &&
                !p.Equals("obj", StringComparison.OrdinalIgnoreCase) &&
                !p.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) &&
                !p.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)).ToArray();
            
            if (relevantParts.Length >= 2)
            {
                // Take the last 2-3 meaningful parts as namespace
                var namespaceParts = relevantParts.TakeLast(Math.Min(3, relevantParts.Length));
                return string.Join(".", namespaceParts);
            }
        }
        
        return "Unknown";
    }
}
