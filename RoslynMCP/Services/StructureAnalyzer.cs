using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using RoslynMCP.Models;

namespace RoslynMCP.Services;

public class StructureAnalyzer
{
    private readonly PatternDetector _patternDetector;
    private readonly MetricsCalculator _metricsCalculator;

    public StructureAnalyzer()
    {
        _patternDetector = new PatternDetector();
        _metricsCalculator = new MetricsCalculator();
    }

    public async Task<StructureAnalysis> AnalyzeStructureAsync(string filePath, bool detectPatterns = true, bool calculateMetrics = true)
    {
        var analysis = new StructureAnalysis
        {
            AnalysisTime = DateTime.UtcNow,
            FilePath = filePath
        };

        try
        {
            var sourceCode = await File.ReadAllTextAsync(filePath);
            var syntaxTree = CSharpSyntaxTree.ParseText(sourceCode, path: filePath);
            var root = await syntaxTree.GetRootAsync();

            if (detectPatterns)
            {
                analysis.DetectedPatterns = _patternDetector.DetectPatternsAsync(root, filePath);
            }

            if (calculateMetrics)
            {
                analysis.Metrics = _metricsCalculator.CalculateMetrics(root, filePath);
            }

            analysis.CodeSmells = DetectCodeSmells(root, filePath);
            analysis.Dependencies = AnalyzeDependencies(root, filePath);
            analysis.Insights = GenerateInsights(analysis);

            return analysis;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to analyze code structure: {ex.Message}", ex);
        }
    }

    private List<CodeSmell> DetectCodeSmells(SyntaxNode root, string filePath)
    {
        var codeSmells = new List<CodeSmell>();

        // Detect long methods
        var methods = root.DescendantNodes().OfType<MethodDeclarationSyntax>();
        foreach (var method in methods)
        {
            var lineCount = method.GetLocation().GetLineSpan().EndLinePosition.Line - 
                           method.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
            
            if (lineCount > 50)
            {
                codeSmells.Add(new CodeSmell
                {
                    Name = "Long Method",
                    Description = $"Method '{method.Identifier.ValueText}' has {lineCount} lines, which exceeds the recommended limit of 50 lines.",
                    Severity = lineCount > 100 ? "High" : "Medium",
                    Location = GetLocation(method, filePath),
                    Recommendation = "Consider breaking this method into smaller, more focused methods.",
                    Details = new Dictionary<string, object>
                    {
                        ["LineCount"] = lineCount,
                        ["MethodName"] = method.Identifier.ValueText,
                        ["Threshold"] = 50
                    }
                });
            }
        }

        // Detect large classes
        var classes = root.DescendantNodes().OfType<ClassDeclarationSyntax>();
        foreach (var classDecl in classes)
        {
            var memberCount = classDecl.Members.Count;
            var lineCount = classDecl.GetLocation().GetLineSpan().EndLinePosition.Line - 
                           classDecl.GetLocation().GetLineSpan().StartLinePosition.Line + 1;

            if (memberCount > 20 || lineCount > 500)
            {
                codeSmells.Add(new CodeSmell
                {
                    Name = "Large Class",
                    Description = $"Class '{classDecl.Identifier.ValueText}' has {memberCount} members and {lineCount} lines.",
                    Severity = (memberCount > 30 || lineCount > 1000) ? "High" : "Medium",
                    Location = GetLocation(classDecl, filePath),
                    Recommendation = "Consider splitting this class into smaller, more cohesive classes following the Single Responsibility Principle.",
                    Details = new Dictionary<string, object>
                    {
                        ["MemberCount"] = memberCount,
                        ["LineCount"] = lineCount,
                        ["ClassName"] = classDecl.Identifier.ValueText
                    }
                });
            }
        }

        // Detect high cyclomatic complexity
        foreach (var method in methods)
        {
            var complexity = CalculateCyclomaticComplexity(method);
            if (complexity > 10)
            {
                codeSmells.Add(new CodeSmell
                {
                    Name = "High Cyclomatic Complexity",
                    Description = $"Method '{method.Identifier.ValueText}' has cyclomatic complexity of {complexity}.",
                    Severity = complexity > 20 ? "Critical" : complexity > 15 ? "High" : "Medium",
                    Location = GetLocation(method, filePath),
                    Recommendation = "Reduce complexity by extracting methods, using polymorphism, or simplifying conditional logic.",
                    Details = new Dictionary<string, object>
                    {
                        ["CyclomaticComplexity"] = complexity,
                        ["MethodName"] = method.Identifier.ValueText,
                        ["Threshold"] = 10
                    }
                });
            }
        }

        // Detect duplicate code patterns
        var duplicates = DetectDuplicateCode(methods);
        codeSmells.AddRange(duplicates);

        // Detect naming violations
        var namingViolations = DetectNamingViolations(root, filePath);
        codeSmells.AddRange(namingViolations);

        return codeSmells;
    }

    private List<CodeSmell> DetectDuplicateCode(IEnumerable<MethodDeclarationSyntax> methods)
    {
        var codeSmells = new List<CodeSmell>();
        var methodGroups = new Dictionary<string, List<MethodDeclarationSyntax>>();

        // Group methods by similar structure (simplified approach)
        foreach (var method in methods)
        {
            var signature = GetMethodSignature(method);
            if (!methodGroups.ContainsKey(signature))
                methodGroups[signature] = new List<MethodDeclarationSyntax>();
            
            methodGroups[signature].Add(method);
        }

        foreach (var group in methodGroups.Where(g => g.Value.Count > 1))
        {
            foreach (var method in group.Value)
            {
                codeSmells.Add(new CodeSmell
                {
                    Name = "Duplicate Code",
                    Description = $"Method '{method.Identifier.ValueText}' appears to have similar structure to other methods.",
                    Severity = "Medium",
                    Location = GetLocation(method, ""),
                    Recommendation = "Consider extracting common functionality into a shared method or using inheritance/composition.",
                    Details = new Dictionary<string, object>
                    {
                        ["MethodName"] = method.Identifier.ValueText,
                        ["SimilarMethods"] = group.Value.Count - 1
                    }
                });
            }
        }

        return codeSmells;
    }

    private List<CodeSmell> DetectNamingViolations(SyntaxNode root, string filePath)
    {
        var codeSmells = new List<CodeSmell>();

        // Check class naming conventions
        var classes = root.DescendantNodes().OfType<ClassDeclarationSyntax>();
        foreach (var classDecl in classes)
        {
            var className = classDecl.Identifier.ValueText;
            if (!char.IsUpper(className[0]))
            {
                codeSmells.Add(new CodeSmell
                {
                    Name = "Naming Convention Violation",
                    Description = $"Class '{className}' should start with an uppercase letter (PascalCase).",
                    Severity = "Low",
                    Location = GetLocation(classDecl, filePath),
                    Recommendation = "Use PascalCase for class names.",
                    Details = new Dictionary<string, object>
                    {
                        ["ViolationType"] = "ClassNaming",
                        ["ClassName"] = className
                    }
                });
            }
        }

        // Check method naming conventions
        var methods = root.DescendantNodes().OfType<MethodDeclarationSyntax>();
        foreach (var method in methods)
        {
            var methodName = method.Identifier.ValueText;
            if (!char.IsUpper(methodName[0]))
            {
                codeSmells.Add(new CodeSmell
                {
                    Name = "Naming Convention Violation",
                    Description = $"Method '{methodName}' should start with an uppercase letter (PascalCase).",
                    Severity = "Low",
                    Location = GetLocation(method, filePath),
                    Recommendation = "Use PascalCase for method names.",
                    Details = new Dictionary<string, object>
                    {
                        ["ViolationType"] = "MethodNaming",
                        ["MethodName"] = methodName
                    }
                });
            }
        }

        return codeSmells;
    }

    private DependencyGraph AnalyzeDependencies(SyntaxNode root, string filePath)
    {
        var graph = new DependencyGraph();
        var nodeMap = new Dictionary<string, DependencyNode>();

        // Create nodes for classes and interfaces
        var types = root.DescendantNodes().Where(n => 
            n is ClassDeclarationSyntax || 
            n is InterfaceDeclarationSyntax || 
            n is StructDeclarationSyntax);

        foreach (var type in types)
        {
            var name = GetTypeName(type);
            var node = new DependencyNode
            {
                Id = Guid.NewGuid().ToString(),
                Name = name,
                Type = type.GetType().Name.Replace("DeclarationSyntax", ""),
                Location = GetLocation(type, filePath)
            };

            nodeMap[name] = node;
            graph.Nodes.Add(node);
        }

        // Analyze dependencies between types
        foreach (var type in types)
        {
            var typeName = GetTypeName(type);
            var fromNode = nodeMap[typeName];

            // Find inheritance relationships
            if (type is ClassDeclarationSyntax classDecl && classDecl.BaseList != null)
            {
                foreach (var baseType in classDecl.BaseList.Types)
                {
                    var baseTypeName = baseType.Type.ToString();
                    if (nodeMap.ContainsKey(baseTypeName))
                    {
                        var toNode = nodeMap[baseTypeName];
                        graph.Edges.Add(new DependencyEdge
                        {
                            FromId = fromNode.Id,
                            ToId = toNode.Id,
                            DependencyType = "Inheritance",
                            Weight = 3
                        });
                        toNode.IncomingDependencies++;
                        fromNode.OutgoingDependencies++;
                    }
                }
            }

            // Find usage relationships
            var identifiers = type.DescendantNodes().OfType<IdentifierNameSyntax>();
            foreach (var identifier in identifiers)
            {
                var referencedType = identifier.Identifier.ValueText;
                if (nodeMap.ContainsKey(referencedType) && referencedType != typeName)
                {
                    var toNode = nodeMap[referencedType];
                    var existingEdge = graph.Edges.FirstOrDefault(e => 
                        e.FromId == fromNode.Id && e.ToId == toNode.Id && e.DependencyType == "Usage");
                    
                    if (existingEdge == null)
                    {
                        graph.Edges.Add(new DependencyEdge
                        {
                            FromId = fromNode.Id,
                            ToId = toNode.Id,
                            DependencyType = "Usage",
                            Weight = 1
                        });
                        toNode.IncomingDependencies++;
                        fromNode.OutgoingDependencies++;
                    }
                    else
                    {
                        existingEdge.Weight++;
                    }
                }
            }
        }

        // Detect circular dependencies
        graph.CircularDependencies = DetectCircularDependencies(graph);

        // Calculate coupling metrics
        graph.CouplingMetrics = CalculateCouplingMetrics(graph);

        return graph;
    }

    private List<CircularDependency> DetectCircularDependencies(DependencyGraph graph)
    {
        var circularDependencies = new List<CircularDependency>();
        var visited = new HashSet<string>();
        var recursionStack = new HashSet<string>();

        foreach (var node in graph.Nodes)
        {
            if (!visited.Contains(node.Id))
            {
                var cycle = DetectCycleFromNode(node.Id, graph, visited, recursionStack, new List<string>());
                if (cycle != null)
                {
                    circularDependencies.Add(new CircularDependency
                    {
                        NodeIds = cycle,
                        Description = $"Circular dependency detected involving {cycle.Count} components",
                        Severity = cycle.Count > 3 ? "High" : "Medium"
                    });
                }
            }
        }

        return circularDependencies;
    }

    private List<string>? DetectCycleFromNode(string nodeId, DependencyGraph graph, HashSet<string> visited, 
        HashSet<string> recursionStack, List<string> currentPath)
    {
        visited.Add(nodeId);
        recursionStack.Add(nodeId);
        currentPath.Add(nodeId);

        var outgoingEdges = graph.Edges.Where(e => e.FromId == nodeId);
        foreach (var edge in outgoingEdges)
        {
            if (!visited.Contains(edge.ToId))
            {
                var cycle = DetectCycleFromNode(edge.ToId, graph, visited, recursionStack, new List<string>(currentPath));
                if (cycle != null) return cycle;
            }
            else if (recursionStack.Contains(edge.ToId))
            {
                // Found a cycle
                var cycleStart = currentPath.IndexOf(edge.ToId);
                return currentPath.Skip(cycleStart).ToList();
            }
        }

        recursionStack.Remove(nodeId);
        return null;
    }

    private Dictionary<string, int> CalculateCouplingMetrics(DependencyGraph graph)
    {
        return new Dictionary<string, int>
        {
            ["TotalNodes"] = graph.Nodes.Count,
            ["TotalEdges"] = graph.Edges.Count,
            ["AverageIncomingDependencies"] = graph.Nodes.Count > 0 ? (int)graph.Nodes.Average(n => n.IncomingDependencies) : 0,
            ["AverageOutgoingDependencies"] = graph.Nodes.Count > 0 ? (int)graph.Nodes.Average(n => n.OutgoingDependencies) : 0,
            ["MaxIncomingDependencies"] = graph.Nodes.Count > 0 ? graph.Nodes.Max(n => n.IncomingDependencies) : 0,
            ["MaxOutgoingDependencies"] = graph.Nodes.Count > 0 ? graph.Nodes.Max(n => n.OutgoingDependencies) : 0,
            ["CircularDependencyCount"] = graph.CircularDependencies.Count
        };
    }

    private ArchitecturalInsights GenerateInsights(StructureAnalysis analysis)
    {
        var insights = new ArchitecturalInsights();

        // Calculate overall complexity
        var complexityMetrics = analysis.Metrics.Where(m => m.Category == "Complexity").ToList();
        insights.OverallComplexity = complexityMetrics.Count > 0 ? complexityMetrics.Average(m => m.Value) : 0;

        // Calculate maintainability
        var maintainabilityFactors = new[]
        {
            analysis.CodeSmells.Count(cs => cs.Severity == "High" || cs.Severity == "Critical"),
            analysis.Dependencies.CircularDependencies.Count,
            analysis.Dependencies.CouplingMetrics.GetValueOrDefault("MaxIncomingDependencies", 0)
        };
        
        insights.Maintainability = Math.Max(0, 100 - maintainabilityFactors.Sum() * 5);

        // Calculate testability
        var testabilityFactors = analysis.Dependencies.Nodes.Count(n => n.IncomingDependencies > 5);
        insights.Testability = Math.Max(0, 100 - testabilityFactors * 10);

        // Generate recommendations
        insights.Recommendations = GenerateRecommendations(analysis);

        // Quality metrics
        insights.QualityMetrics = new Dictionary<string, object>
        {
            ["CodeSmellCount"] = analysis.CodeSmells.Count,
            ["HighSeveritySmells"] = analysis.CodeSmells.Count(cs => cs.Severity == "High" || cs.Severity == "Critical"),
            ["CircularDependencies"] = analysis.Dependencies.CircularDependencies.Count,
            ["AverageCyclomaticComplexity"] = complexityMetrics.Count > 0 ? complexityMetrics.Average(m => m.Value) : 0,
            ["TotalClasses"] = analysis.Dependencies.Nodes.Count(n => n.Type == "Class"),
            ["TotalInterfaces"] = analysis.Dependencies.Nodes.Count(n => n.Type == "Interface")
        };

        return insights;
    }

    private List<string> GenerateRecommendations(StructureAnalysis analysis)
    {
        var recommendations = new List<string>();

        if (analysis.CodeSmells.Any(cs => cs.Name == "Large Class"))
        {
            recommendations.Add("Consider breaking down large classes into smaller, more focused classes following the Single Responsibility Principle.");
        }

        if (analysis.CodeSmells.Any(cs => cs.Name == "High Cyclomatic Complexity"))
        {
            recommendations.Add("Reduce method complexity by extracting smaller methods and simplifying conditional logic.");
        }

        if (analysis.Dependencies.CircularDependencies.Any())
        {
            recommendations.Add("Resolve circular dependencies by introducing abstractions or restructuring the dependency relationships.");
        }

        if (analysis.Dependencies.CouplingMetrics.GetValueOrDefault("MaxIncomingDependencies", 0) > 10)
        {
            recommendations.Add("Reduce coupling by using dependency injection and interface segregation.");
        }

        if (analysis.DetectedPatterns.Any(p => p.Name == "Singleton" && p.Confidence > 0.8))
        {
            recommendations.Add("Consider whether Singleton patterns are necessary, as they can make testing difficult.");
        }

        return recommendations;
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

    private int CalculateCyclomaticComplexity(MethodDeclarationSyntax method)
    {
        var complexity = 1; // Base complexity

        var controlFlowNodes = method.DescendantNodes().Where(n =>
            n is IfStatementSyntax ||
            n is WhileStatementSyntax ||
            n is ForStatementSyntax ||
            n is ForEachStatementSyntax ||
            n is SwitchStatementSyntax ||
            n is ConditionalExpressionSyntax ||
            n is CatchClauseSyntax ||
            n is CaseSwitchLabelSyntax);

        complexity += controlFlowNodes.Count();

        // Add complexity for logical operators
        var logicalOperators = method.DescendantTokens().Where(t =>
            t.IsKind(SyntaxKind.AmpersandAmpersandToken) ||
            t.IsKind(SyntaxKind.BarBarToken));

        complexity += logicalOperators.Count();

        return complexity;
    }

    private string GetMethodSignature(MethodDeclarationSyntax method)
    {
        // Simplified signature for duplicate detection
        var parameterTypes = method.ParameterList.Parameters.Select(p => p.Type?.ToString() ?? "").ToList();
        return $"{method.ReturnType}({string.Join(",", parameterTypes)})";
    }

    private string GetTypeName(SyntaxNode type)
    {
        return type switch
        {
            ClassDeclarationSyntax classDecl => classDecl.Identifier.ValueText,
            InterfaceDeclarationSyntax interfaceDecl => interfaceDecl.Identifier.ValueText,
            StructDeclarationSyntax structDecl => structDecl.Identifier.ValueText,
            _ => "Unknown"
        };
    }
}
