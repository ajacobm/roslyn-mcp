using System.Collections.Immutable;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.MSBuild;
using RoslynRuntime.Models;

namespace RoslynRuntime.Services
{
    public class SymbolGraphExtractor
    {
        private readonly MSBuildWorkspace _workspace;
        private readonly Dictionary<ISymbol, string> _symbolToIdMap = new(SymbolEqualityComparer.Default);
        private readonly Dictionary<string, SymbolNode> _nodes = [];
        private readonly List<SymbolEdge> _edges = [];
        private readonly HashSet<string> _processedFiles = [];
        private int _edgeIdCounter = 0;

        public SymbolGraphExtractor(MSBuildWorkspace workspace)
        {
            _workspace = workspace;
        }

        public async Task<SymbolGraph> ExtractSymbolGraphAsync(
            string path,
            string scope,
            bool includeInheritance,
            bool includeMethodCalls,
            bool includeFieldAccess,
            bool includeNamespaces,
            int maxDepth)
        {
            var options = new SymbolGraphOptions
            {
                Scope = scope,
                IncludeInheritance = includeInheritance,
                IncludeMethodCalls = includeMethodCalls,
                IncludeFieldAccess = includeFieldAccess,
                IncludeNamespaces = includeNamespaces,
                MaxDepth = maxDepth
            };

            // Clear previous state
            _symbolToIdMap.Clear();
            _nodes.Clear();
            _edges.Clear();
            _processedFiles.Clear();
            _edgeIdCounter = 0;

            try
            {
                switch (scope.ToLowerInvariant())
                {
                    case "file":
                        await ProcessSingleFile(path, options);
                        break;
                    case "project":
                        await ProcessProject(path, options);
                        break;
                    case "solution":
                        await ProcessSolution(path, options);
                        break;
                    default:
                        throw new ArgumentException($"Invalid scope: {scope}. Valid values are 'file', 'project', 'solution'");
                }

                // Calculate metrics and statistics
                CalculateGraphStatistics();

                // Create the final graph
                var graph = new SymbolGraph
                {
                    Nodes = _nodes.Values.ToList(),
                    Edges = _edges,
                    Metadata = CreateMetadata(path, scope),
                    Statistics = CreateStatistics(),
                    DatabaseFormats = CreateDatabaseFormats()
                };

                return graph;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Error extracting symbol graph: {ex.Message}", ex);
            }
        }

        private async Task ProcessSingleFile(string filePath, SymbolGraphOptions options)
        {
            if (!File.Exists(filePath))
                throw new FileNotFoundException($"File not found: {filePath}");

            // Find containing project
            var projectPath = FindContainingProjectAsync(filePath);
            if (string.IsNullOrEmpty(projectPath))
                throw new InvalidOperationException("Could not find containing project for file");

            var project = await _workspace.OpenProjectAsync(projectPath);
            var document = project.Documents.FirstOrDefault(d => 
                string.Equals(d.FilePath, filePath, StringComparison.OrdinalIgnoreCase));

            if (document == null)
                throw new InvalidOperationException("File not found in project");

            await ProcessDocument(document, options);
        }

        private async Task ProcessProject(string path, SymbolGraphOptions options)
        {
            string projectPath;
            
            if (path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
            {
                projectPath = path;
            }
            else
            {
                projectPath = FindContainingProjectAsync(path);
                if (string.IsNullOrEmpty(projectPath))
                    throw new InvalidOperationException("Could not find project file");
            }

            var project = await _workspace.OpenProjectAsync(projectPath);
            
            foreach (var document in project.Documents)
            {
                if (document.FilePath?.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) == true)
                {
                    await ProcessDocument(document, options);
                }
            }
        }

        private async Task ProcessSolution(string path, SymbolGraphOptions options)
        {
            string solutionPath;
            
            if (path.EndsWith(".sln", StringComparison.OrdinalIgnoreCase))
            {
                solutionPath = path;
            }
            else
            {
                // Try to find solution file in directory hierarchy
                var directory = new DirectoryInfo(Path.GetDirectoryName(path) ?? ".");
                while (directory != null)
                {
                    var solutionFiles = directory.GetFiles("*.sln");
                    if (solutionFiles.Length > 0)
                    {
                        solutionPath = solutionFiles[0].FullName;
                        break;
                    }
                    directory = directory.Parent;
                }
                
                if (directory == null)
                    throw new InvalidOperationException("Could not find solution file");
                
                solutionPath = directory.GetFiles("*.sln")[0].FullName;
            }

            var solution = await _workspace.OpenSolutionAsync(solutionPath);
            
            foreach (var project in solution.Projects)
            {
                foreach (var document in project.Documents)
                {
                    if (document.FilePath?.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) == true)
                    {
                        await ProcessDocument(document, options);
                    }
                }
            }
        }

        private async Task ProcessDocument(Document document, SymbolGraphOptions options)
        {
            if (document.FilePath != null && _processedFiles.Contains(document.FilePath))
                return;

            var syntaxTree = await document.GetSyntaxTreeAsync();
            var semanticModel = await document.GetSemanticModelAsync();
            
            if (syntaxTree == null || semanticModel == null)
                return;

            var root = await syntaxTree.GetRootAsync();
            
            // Process all symbols in the document
            await ProcessSyntaxNode(root, semanticModel, options);
            
            if (document.FilePath != null)
                _processedFiles.Add(document.FilePath);
        }

        private async Task ProcessSyntaxNode(SyntaxNode node, SemanticModel semanticModel, SymbolGraphOptions options)
        {
            // Process current node
            var symbol = semanticModel.GetDeclaredSymbol(node);
            if (symbol != null)
            {
                ProcessSymbol(symbol, semanticModel, options);
            }

            // Process member access and method calls
            if (options.IncludeMethodCalls || options.IncludeFieldAccess)
            {
                ProcessExpressions(node, semanticModel, options);
            }

            // Recursively process child nodes
            foreach (var child in node.ChildNodes())
            {
                await ProcessSyntaxNode(child, semanticModel, options);
            }
        }

        private void ProcessSymbol(ISymbol symbol, SemanticModel semanticModel, SymbolGraphOptions options)
        {
            var symbolId = GenerateSymbolId(symbol);
            
            if (_nodes.ContainsKey(symbolId))
                return;

            var node = CreateSymbolNode(symbol);
            _nodes[symbolId] = node;
            _symbolToIdMap[symbol] = symbolId;

            // Extract relationships
            ExtractRelationships(symbol, semanticModel, options);
        }

        private void ProcessExpressions(SyntaxNode node, SemanticModel semanticModel, SymbolGraphOptions options)
        {
            // Process method calls
            if (options.IncludeMethodCalls)
            {
                var invocations = node.DescendantNodes().OfType<InvocationExpressionSyntax>();
                foreach (var invocation in invocations)
                {
                    ProcessMethodCall(invocation, semanticModel);
                }
            }

            // Process member access
            if (options.IncludeFieldAccess)
            {
                var memberAccess = node.DescendantNodes().OfType<MemberAccessExpressionSyntax>();
                foreach (var access in memberAccess)
                {
                    ProcessMemberAccess(access, semanticModel);
                }
            }
        }

        private void ProcessMethodCall(InvocationExpressionSyntax invocation, SemanticModel semanticModel)
        {
            var symbolInfo = semanticModel.GetSymbolInfo(invocation);
            if (symbolInfo.Symbol is IMethodSymbol targetMethod)
            {
                var containingMethod = semanticModel.GetEnclosingSymbol(invocation.SpanStart) as IMethodSymbol;
                if (containingMethod != null)
                {
                    var sourceId = GetOrCreateSymbolId(containingMethod);
                    var targetId = GetOrCreateSymbolId(targetMethod);
                    
                    CreateSymbolEdge(sourceId, targetId, RelationshipType.MethodCall, 
                        CreateLocation(invocation, semanticModel.SyntaxTree));
                }
            }
        }

        private void ProcessMemberAccess(MemberAccessExpressionSyntax memberAccess, SemanticModel semanticModel)
        {
            var symbolInfo = semanticModel.GetSymbolInfo(memberAccess);
            if (symbolInfo.Symbol != null)
            {
                var containingSymbol = semanticModel.GetEnclosingSymbol(memberAccess.SpanStart);
                if (containingSymbol != null)
                {
                    var sourceId = GetOrCreateSymbolId(containingSymbol);
                    var targetId = GetOrCreateSymbolId(symbolInfo.Symbol);
                    
                    var relationshipType = symbolInfo.Symbol.Kind switch
                    {
                        Microsoft.CodeAnalysis.SymbolKind.Field => RelationshipType.FieldAccess,
                        Microsoft.CodeAnalysis.SymbolKind.Property => RelationshipType.PropertyAccess,
                        Microsoft.CodeAnalysis.SymbolKind.Event => RelationshipType.EventSubscription,
                        _ => RelationshipType.Dependency
                    };
                    
                    CreateSymbolEdge(sourceId, targetId, relationshipType, 
                        CreateLocation(memberAccess, semanticModel.SyntaxTree));
                }
            }
        }

        private void ExtractRelationships(ISymbol symbol, SemanticModel semanticModel, SymbolGraphOptions options)
        {
            if (options.IncludeInheritance && symbol is INamedTypeSymbol typeSymbol)
            {
                ExtractInheritanceRelationships(typeSymbol);
            }

            if (options.IncludeNamespaces)
            {
                ExtractNamespaceRelationships(symbol);
            }

            // Extract composition relationships
            ExtractCompositionRelationships(symbol);
        }

        private void ExtractInheritanceRelationships(INamedTypeSymbol typeSymbol)
        {
            var sourceId = GetOrCreateSymbolId(typeSymbol);

            // Base type
            if (typeSymbol.BaseType != null && typeSymbol.BaseType.SpecialType != SpecialType.System_Object)
            {
                var targetId = GetOrCreateSymbolId(typeSymbol.BaseType);
                CreateSymbolEdge(sourceId, targetId, RelationshipType.Inheritance, null);
            }

            // Interfaces
            foreach (var interfaceType in typeSymbol.Interfaces)
            {
                var targetId = GetOrCreateSymbolId(interfaceType);
                CreateSymbolEdge(sourceId, targetId, RelationshipType.Implementation, null);
            }
        }

        private void ExtractNamespaceRelationships(ISymbol symbol)
        {
            if (symbol.ContainingNamespace != null && !symbol.ContainingNamespace.IsGlobalNamespace)
            {
                var sourceId = GetOrCreateSymbolId(symbol);
                var namespaceId = GetOrCreateSymbolId(symbol.ContainingNamespace);
                CreateSymbolEdge(sourceId, namespaceId, RelationshipType.Namespace, null);
            }
        }

        private void ExtractCompositionRelationships(ISymbol symbol)
        {
            var sourceId = GetOrCreateSymbolId(symbol);

            switch (symbol)
            {
                case IFieldSymbol fieldSymbol:
                    var fieldTargetId = GetOrCreateSymbolId(fieldSymbol.Type);
                    CreateSymbolEdge(sourceId, fieldTargetId, RelationshipType.Composition, null);
                    break;
                    
                case IPropertySymbol propertySymbol:
                    var propertyTargetId = GetOrCreateSymbolId(propertySymbol.Type);
                    CreateSymbolEdge(sourceId, propertyTargetId, RelationshipType.Composition, null);
                    break;
                    
                case IMethodSymbol methodSymbol:
                    // Return type
                    if (methodSymbol.ReturnType.SpecialType != SpecialType.System_Void)
                    {
                        var returnTypeId = GetOrCreateSymbolId(methodSymbol.ReturnType);
                        CreateSymbolEdge(sourceId, returnTypeId, RelationshipType.Dependency, null);
                    }
                    
                    // Parameters
                    foreach (var parameter in methodSymbol.Parameters)
                    {
                        var parameterTypeId = GetOrCreateSymbolId(parameter.Type);
                        CreateSymbolEdge(sourceId, parameterTypeId, RelationshipType.Dependency, null);
                    }
                    break;
            }
        }

        private string GetOrCreateSymbolId(ISymbol symbol)
        {
            if (_symbolToIdMap.TryGetValue(symbol, out var existingId))
                return existingId;

            var symbolId = GenerateSymbolId(symbol);
            _symbolToIdMap[symbol] = symbolId;
            
            if (!_nodes.ContainsKey(symbolId))
            {
                var node = CreateSymbolNode(symbol);
                _nodes[symbolId] = node;
            }
            
            return symbolId;
        }

        private string GenerateSymbolId(ISymbol symbol)
        {
            return symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        }

        private SymbolNode CreateSymbolNode(ISymbol symbol)
        {
            var location = CreateLocation(symbol);
            var metrics = CalculateSymbolMetrics(symbol);
            
            return new SymbolNode
            {
                Id = GenerateSymbolId(symbol),
                Name = symbol.Name,
                FullName = symbol.ToDisplayString(),
                Kind = symbol.Kind,
                TypeName = GetSymbolTypeName(symbol),
                Location = location,
                Accessibility = ConvertAccessibility(symbol.DeclaredAccessibility),
                Modifiers = GetSymbolModifiers(symbol),
                Properties = GetSymbolProperties(symbol),
                Metrics = metrics
            };
        }

        private SymbolEdge CreateSymbolEdge(string sourceId, string targetId, RelationshipType type, SymbolLocation? location)
        {
            var edgeId = $"edge_{++_edgeIdCounter}";
            var edge = new SymbolEdge
            {
                Id = edgeId,
                SourceId = sourceId,
                TargetId = targetId,
                Type = type,
                Label = GetRelationshipLabel(type),
                Location = location,
                Properties = new Dictionary<string, object>()
            };
            
            _edges.Add(edge);
            return edge;
        }

        private SymbolLocation? CreateLocation(ISymbol symbol)
        {
            var syntaxRef = symbol.DeclaringSyntaxReferences.FirstOrDefault();
            if (syntaxRef == null) return null;

            var lineSpan = syntaxRef.GetSyntax().GetLocation().GetLineSpan();
            return new SymbolLocation
            {
                File = syntaxRef.SyntaxTree.FilePath ?? "",
                Line = lineSpan.StartLinePosition.Line + 1,
                Column = lineSpan.StartLinePosition.Character + 1,
                EndLine = lineSpan.EndLinePosition.Line + 1,
                EndColumn = lineSpan.EndLinePosition.Character + 1
            };
        }

        private SymbolLocation CreateLocation(SyntaxNode node, SyntaxTree syntaxTree)
        {
            var lineSpan = node.GetLocation().GetLineSpan();
            return new SymbolLocation
            {
                File = syntaxTree.FilePath ?? "",
                Line = lineSpan.StartLinePosition.Line + 1,
                Column = lineSpan.StartLinePosition.Character + 1,
                EndLine = lineSpan.EndLinePosition.Line + 1,
                EndColumn = lineSpan.EndLinePosition.Character + 1
            };
        }

        private SymbolMetrics CalculateSymbolMetrics(ISymbol symbol)
        {
            var metrics = new SymbolMetrics();

            switch (symbol)
            {
                case IMethodSymbol methodSymbol:
                    metrics.NumberOfParameters = methodSymbol.Parameters.Length;
                    metrics.CyclomaticComplexity = CalculateCyclomaticComplexity(methodSymbol);
                    break;
                    
                case INamedTypeSymbol typeSymbol:
                    metrics.NumberOfMethods = typeSymbol.GetMembers().OfType<IMethodSymbol>().Count();
                    metrics.NumberOfProperties = typeSymbol.GetMembers().OfType<IPropertySymbol>().Count();
                    metrics.NumberOfFields = typeSymbol.GetMembers().OfType<IFieldSymbol>().Count();
                    break;
            }

            return metrics;
        }

        private int CalculateCyclomaticComplexity(IMethodSymbol methodSymbol)
        {
            // Simplified complexity calculation
            // In a full implementation, this would analyze the method body
            return 1; // Base complexity
        }

        private SymbolKind ConvertSymbolKind(Microsoft.CodeAnalysis.SymbolKind kind)
        {
            // Direct use of Roslyn's SymbolKind - no conversion needed
            return kind;
        }

        private AccessibilityLevel ConvertAccessibility(Accessibility accessibility)
        {
            return accessibility switch
            {
                Accessibility.Public => AccessibilityLevel.Public,
                Accessibility.Private => AccessibilityLevel.Private,
                Accessibility.Protected => AccessibilityLevel.Protected,
                Accessibility.Internal => AccessibilityLevel.Internal,
                Accessibility.ProtectedOrInternal => AccessibilityLevel.ProtectedInternal,
                Accessibility.ProtectedAndInternal => AccessibilityLevel.PrivateProtected,
                _ => AccessibilityLevel.Private
            };
        }

        private string GetSymbolTypeName(ISymbol symbol)
        {
            return symbol switch
            {
                INamedTypeSymbol namedType => namedType.TypeKind.ToString().ToLowerInvariant(),
                IMethodSymbol => "method",
                IPropertySymbol => "property",
                IFieldSymbol => "field",
                IEventSymbol => "event",
                IParameterSymbol => "parameter",
                ILocalSymbol => "local",
                INamespaceSymbol => "namespace",
                _ => symbol.Kind.ToString().ToLowerInvariant()
            };
        }

        private List<string> GetSymbolModifiers(ISymbol symbol)
        {
            var modifiers = new List<string>();
            
            if (symbol.IsStatic) modifiers.Add("static");
            if (symbol.IsAbstract) modifiers.Add("abstract");
            if (symbol.IsVirtual) modifiers.Add("virtual");
            if (symbol.IsSealed) modifiers.Add("sealed");
            if (symbol.IsOverride) modifiers.Add("override");
            
            return modifiers;
        }

        private Dictionary<string, object> GetSymbolProperties(ISymbol symbol)
        {
            var properties = new Dictionary<string, object>
            {
                ["isStatic"] = symbol.IsStatic,
                ["isAbstract"] = symbol.IsAbstract,
                ["isVirtual"] = symbol.IsVirtual,
                ["isSealed"] = symbol.IsSealed,
                ["isOverride"] = symbol.IsOverride
            };

            switch (symbol)
            {
                case INamedTypeSymbol namedType:
                    properties["typeKind"] = namedType.TypeKind.ToString();
                    properties["isGeneric"] = namedType.IsGenericType;
                    properties["arity"] = namedType.Arity;
                    break;
                    
                case IMethodSymbol method:
                    properties["isExtensionMethod"] = method.IsExtensionMethod;
                    properties["isAsync"] = method.IsAsync;
                    properties["isGeneric"] = method.IsGenericMethod;
                    break;
                    
                case IPropertySymbol property:
                    properties["hasGetter"] = property.GetMethod != null;
                    properties["hasSetter"] = property.SetMethod != null;
                    properties["isIndexer"] = property.IsIndexer;
                    break;
                    
                case IFieldSymbol field:
                    properties["isConst"] = field.IsConst;
                    properties["isReadOnly"] = field.IsReadOnly;
                    properties["isVolatile"] = field.IsVolatile;
                    break;
            }

            return properties;
        }

        private string GetRelationshipLabel(RelationshipType type)
        {
            return type switch
            {
                RelationshipType.Inheritance => "inherits from",
                RelationshipType.Implementation => "implements",
                RelationshipType.Composition => "has",
                RelationshipType.Aggregation => "contains",
                RelationshipType.MethodCall => "calls",
                RelationshipType.FieldAccess => "accesses field",
                RelationshipType.PropertyAccess => "accesses property",
                RelationshipType.EventSubscription => "subscribes to",
                RelationshipType.GenericConstraint => "constrained by",
                RelationshipType.Namespace => "in namespace",
                RelationshipType.Assembly => "in assembly",
                RelationshipType.Dependency => "depends on",
                RelationshipType.Override => "overrides",
                RelationshipType.Instantiation => "instantiates",
                _ => type.ToString().ToLowerInvariant()
            };
        }

        private void CalculateGraphStatistics()
        {
            // Calculate incoming and outgoing references for each node
            var incomingCounts = new Dictionary<string, int>();
            var outgoingCounts = new Dictionary<string, int>();

            foreach (var edge in _edges)
            {
                outgoingCounts[edge.SourceId] = outgoingCounts.GetValueOrDefault(edge.SourceId) + 1;
                incomingCounts[edge.TargetId] = incomingCounts.GetValueOrDefault(edge.TargetId) + 1;
            }

            foreach (var node in _nodes.Values)
            {
                node.Metrics.IncomingReferences = incomingCounts.GetValueOrDefault(node.Id);
                node.Metrics.OutgoingReferences = outgoingCounts.GetValueOrDefault(node.Id);
            }
        }

        private GraphMetadata CreateMetadata(string rootPath, string scope)
        {
            var nodeTypeDistribution = _nodes.Values
                .GroupBy(n => n.Kind.ToString())
                .ToDictionary(g => g.Key, g => g.Count());

            var edgeTypeDistribution = _edges
                .GroupBy(e => e.Type.ToString())
                .ToDictionary(g => g.Key, g => g.Count());

            return new GraphMetadata
            {
                Scope = scope,
                RootPath = rootPath,
                GeneratedAt = DateTime.UtcNow,
                TotalNodes = _nodes.Count,
                TotalEdges = _edges.Count,
                NodeTypeDistribution = nodeTypeDistribution,
                EdgeTypeDistribution = edgeTypeDistribution,
                ProcessedFiles = _processedFiles.ToList()
            };
        }

        private Dictionary<string, object> CreateStatistics()
        {
            var stats = new Dictionary<string, object>
            {
                ["totalSymbols"] = _nodes.Count,
                ["totalRelationships"] = _edges.Count,
                ["filesProcessed"] = _processedFiles.Count,
                ["averageComplexity"] = _nodes.Values.Average(n => n.Metrics.CyclomaticComplexity),
                ["maxIncomingReferences"] = _nodes.Values.Max(n => n.Metrics.IncomingReferences),
                ["maxOutgoingReferences"] = _nodes.Values.Max(n => n.Metrics.OutgoingReferences)
            };

            return stats;
        }

        private GraphDatabaseFormats CreateDatabaseFormats()
        {
            var cypherStatements = new CypherStatements();
            var bulkImport = new BulkImportFormat();

            // Generate Cypher statements for nodes
            foreach (var node in _nodes.Values)
            {
                var labels = new List<string> { "Symbol", node.Kind.ToString() };
                var properties = CreateCypherProperties(node);
                
                var cypher = $"CREATE (n:{string.Join(":", labels)} {properties})";
                cypherStatements.Nodes.Add(cypher);
                
                // Add to bulk import format
                bulkImport.Nodes.Add(new BulkImportNode
                {
                    Labels = labels,
                    Properties = CreateBulkImportProperties(node)
                });
            }

            // Generate Cypher statements for relationships
            foreach (var edge in _edges)
            {
                var relationshipType = edge.Type.ToString().ToUpperInvariant();
                var properties = edge.Properties.Any() ? CreateCypherProperties(edge.Properties) : "";
                
                var cypher = $"MATCH (a:Symbol {{id: '{edge.SourceId}'}}), (b:Symbol {{id: '{edge.TargetId}'}}) " +
                           $"CREATE (a)-[:{relationshipType} {properties}]->(b)";
                cypherStatements.Relationships.Add(cypher);
                
                // Add to bulk import format
                bulkImport.Relationships.Add(new BulkImportRelationship
                {
                    Type = relationshipType,
                    StartNodeId = edge.SourceId,
                    EndNodeId = edge.TargetId,
                    Properties = edge.Properties
                });
            }

            return new GraphDatabaseFormats
            {
                CypherStatements = cypherStatements,
                BulkImport = bulkImport
            };
        }

        private string CreateCypherProperties(SymbolNode node)
        {
            var props = new Dictionary<string, object>
            {
                ["id"] = node.Id,
                ["name"] = node.Name,
                ["fullName"] = node.FullName,
                ["kind"] = node.Kind.ToString(),
                ["typeName"] = node.TypeName,
                ["accessibility"] = node.Accessibility.ToString(),
                ["cyclomaticComplexity"] = node.Metrics.CyclomaticComplexity,
                ["linesOfCode"] = node.Metrics.LinesOfCode,
                ["incomingReferences"] = node.Metrics.IncomingReferences,
                ["outgoingReferences"] = node.Metrics.OutgoingReferences
            };

            if (node.Location != null)
            {
                props["file"] = node.Location.File;
                props["line"] = node.Location.Line;
                props["column"] = node.Location.Column;
            }

            foreach (var prop in node.Properties)
            {
                props[prop.Key] = prop.Value;
            }

            return CreateCypherProperties(props);
        }

        private string CreateCypherProperties(Dictionary<string, object> properties)
        {
            if (!properties.Any()) return "";

            var props = properties.Select(kvp => 
            {
                var value = kvp.Value switch
                {
                    string s => $"'{s.Replace("'", "\\'")}'",
                    bool b => b.ToString().ToLowerInvariant(),
                    null => "null",
                    _ => kvp.Value.ToString()
                };
                return $"{kvp.Key}: {value}";
            });

            return $"{{{string.Join(", ", props)}}}";
        }

        private Dictionary<string, object> CreateBulkImportProperties(SymbolNode node)
        {
            var props = new Dictionary<string, object>
            {
                ["id"] = node.Id,
                ["name"] = node.Name,
                ["fullName"] = node.FullName,
                ["kind"] = node.Kind.ToString(),
                ["typeName"] = node.TypeName,
                ["accessibility"] = node.Accessibility.ToString(),
                ["cyclomaticComplexity"] = node.Metrics.CyclomaticComplexity,
                ["linesOfCode"] = node.Metrics.LinesOfCode,
                ["incomingReferences"] = node.Metrics.IncomingReferences,
                ["outgoingReferences"] = node.Metrics.OutgoingReferences
            };

            if (node.Location != null)
            {
                props["file"] = node.Location.File;
                props["line"] = node.Location.Line;
                props["column"] = node.Location.Column;
            }

            foreach (var prop in node.Properties)
            {
                props[prop.Key] = prop.Value;
            }

            return props;
        }

        private string FindContainingProjectAsync(string filePath)
        {
            // Start from the directory containing the file and go up until we find a .csproj file
            DirectoryInfo? directory = new FileInfo(filePath).Directory;

            while (directory != null)
            {
                var projectFiles = directory.GetFiles("*.csproj");
                if (projectFiles.Length > 0)
                {
                    return projectFiles[0].FullName;
                }

                directory = directory.Parent;
            }

            return string.Empty;
        }
    }

    public class SymbolGraphOptions
    {
        public string Scope { get; set; } = string.Empty;
        public bool IncludeInheritance { get; set; }
        public bool IncludeMethodCalls { get; set; }
        public bool IncludeFieldAccess { get; set; }
        public bool IncludeNamespaces { get; set; }
        public int MaxDepth { get; set; }
    }
}
