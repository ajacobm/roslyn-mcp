using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using RoslynRuntime.Models;

namespace RoslynRuntime.Services;

public class MetricsCalculator
{
    public List<CodeMetric> CalculateMetrics(SyntaxNode root, string filePath)
    {
        var metrics = new List<CodeMetric>();

        // Calculate complexity metrics
        metrics.AddRange(CalculateComplexityMetrics(root, filePath));
        
        // Calculate maintainability metrics
        metrics.AddRange(CalculateMaintainabilityMetrics(root, filePath));
        
        // Calculate size metrics
        metrics.AddRange(CalculateSizeMetrics(root, filePath));
        
        // Calculate coupling metrics
        metrics.AddRange(CalculateCouplingMetrics(root, filePath));
        
        // Calculate cohesion metrics
        metrics.AddRange(CalculateCohesionMetrics(root, filePath));

        return metrics;
    }

    private List<CodeMetric> CalculateComplexityMetrics(SyntaxNode root, string filePath)
    {
        var metrics = new List<CodeMetric>();
        var methods = root.DescendantNodes().OfType<MethodDeclarationSyntax>();

        foreach (var method in methods)
        {
            var className = method.Ancestors().OfType<ClassDeclarationSyntax>().FirstOrDefault()?.Identifier.ValueText ?? "Unknown";
            var methodName = $"{className}.{method.Identifier.ValueText}";

            // Cyclomatic Complexity
            var cyclomaticComplexity = CalculateCyclomaticComplexity(method);
            metrics.Add(new CodeMetric
            {
                Name = "Cyclomatic Complexity",
                Category = "Complexity",
                Value = cyclomaticComplexity,
                Unit = "paths",
                Description = $"Number of linearly independent paths through {methodName}",
                Location = GetLocation(method, filePath)
            });

            // Cognitive Complexity
            var cognitiveComplexity = CalculateCognitiveComplexity(method);
            metrics.Add(new CodeMetric
            {
                Name = "Cognitive Complexity",
                Category = "Complexity",
                Value = cognitiveComplexity,
                Unit = "points",
                Description = $"Cognitive complexity of {methodName}",
                Location = GetLocation(method, filePath)
            });

            // Nesting Depth
            var nestingDepth = CalculateNestingDepth(method);
            metrics.Add(new CodeMetric
            {
                Name = "Nesting Depth",
                Category = "Complexity",
                Value = nestingDepth,
                Unit = "levels",
                Description = $"Maximum nesting depth in {methodName}",
                Location = GetLocation(method, filePath)
            });
        }

        // Overall file complexity
        var overallCyclomaticComplexity = methods.Sum(m => CalculateCyclomaticComplexity(m));
        metrics.Add(new CodeMetric
        {
            Name = "Total Cyclomatic Complexity",
            Category = "Complexity",
            Value = overallCyclomaticComplexity,
            Unit = "paths",
            Description = "Sum of cyclomatic complexity for all methods in the file",
            Location = null
        });

        return metrics;
    }

    private List<CodeMetric> CalculateMaintainabilityMetrics(SyntaxNode root, string filePath)
    {
        var metrics = new List<CodeMetric>();

        // Maintainability Index (simplified version)
        var maintainabilityIndex = CalculateMaintainabilityIndex(root);
        metrics.Add(new CodeMetric
        {
            Name = "Maintainability Index",
            Category = "Maintainability",
            Value = maintainabilityIndex,
            Unit = "index",
            Description = "Overall maintainability score (0-100, higher is better)",
            Location = null
        });

        // Comment Density
        var commentDensity = CalculateCommentDensity(root);
        metrics.Add(new CodeMetric
        {
            Name = "Comment Density",
            Category = "Maintainability",
            Value = commentDensity,
            Unit = "percentage",
            Description = "Percentage of lines that are comments",
            Location = null
        });

        // Technical Debt Ratio (simplified)
        var technicalDebtRatio = CalculateTechnicalDebtRatio(root);
        metrics.Add(new CodeMetric
        {
            Name = "Technical Debt Ratio",
            Category = "Maintainability",
            Value = technicalDebtRatio,
            Unit = "percentage",
            Description = "Estimated technical debt as percentage of total development time",
            Location = null
        });

        return metrics;
    }

    private List<CodeMetric> CalculateSizeMetrics(SyntaxNode root, string filePath)
    {
        var metrics = new List<CodeMetric>();

        // Lines of Code
        var linesOfCode = CalculateLinesOfCode(root);
        metrics.Add(new CodeMetric
        {
            Name = "Lines of Code",
            Category = "Size",
            Value = linesOfCode,
            Unit = "lines",
            Description = "Total number of lines of code (excluding comments and blank lines)",
            Location = null
        });

        // Number of Classes
        var classCount = root.DescendantNodes().OfType<ClassDeclarationSyntax>().Count();
        metrics.Add(new CodeMetric
        {
            Name = "Class Count",
            Category = "Size",
            Value = classCount,
            Unit = "classes",
            Description = "Total number of classes",
            Location = null
        });

        // Number of Methods
        var methodCount = root.DescendantNodes().OfType<MethodDeclarationSyntax>().Count();
        metrics.Add(new CodeMetric
        {
            Name = "Method Count",
            Category = "Size",
            Value = methodCount,
            Unit = "methods",
            Description = "Total number of methods",
            Location = null
        });

        // Number of Properties
        var propertyCount = root.DescendantNodes().OfType<PropertyDeclarationSyntax>().Count();
        metrics.Add(new CodeMetric
        {
            Name = "Property Count",
            Category = "Size",
            Value = propertyCount,
            Unit = "properties",
            Description = "Total number of properties",
            Location = null
        });

        // Average Method Length
        var methods = root.DescendantNodes().OfType<MethodDeclarationSyntax>();
        if (methods.Any())
        {
            var averageMethodLength = methods.Average(m => CalculateMethodLength(m));
            metrics.Add(new CodeMetric
            {
                Name = "Average Method Length",
                Category = "Size",
                Value = averageMethodLength,
                Unit = "lines",
                Description = "Average number of lines per method",
                Location = null
            });
        }

        return metrics;
    }

    private List<CodeMetric> CalculateCouplingMetrics(SyntaxNode root, string filePath)
    {
        var metrics = new List<CodeMetric>();
        var classes = root.DescendantNodes().OfType<ClassDeclarationSyntax>();

        foreach (var classDecl in classes)
        {
            var className = classDecl.Identifier.ValueText;

            // Afferent Coupling (Ca) - classes that depend on this class
            var afferentCoupling = CalculateAfferentCoupling(classDecl, root);
            metrics.Add(new CodeMetric
            {
                Name = "Afferent Coupling",
                Category = "Coupling",
                Value = afferentCoupling,
                Unit = "dependencies",
                Description = $"Number of classes that depend on {className}",
                Location = GetLocation(classDecl, filePath)
            });

            // Efferent Coupling (Ce) - classes this class depends on
            var efferentCoupling = CalculateEfferentCoupling(classDecl, root);
            metrics.Add(new CodeMetric
            {
                Name = "Efferent Coupling",
                Category = "Coupling",
                Value = efferentCoupling,
                Unit = "dependencies",
                Description = $"Number of classes that {className} depends on",
                Location = GetLocation(classDecl, filePath)
            });

            // Instability (I = Ce / (Ca + Ce))
            var totalCoupling = afferentCoupling + efferentCoupling;
            var instability = totalCoupling > 0 ? (double)efferentCoupling / totalCoupling : 0;
            metrics.Add(new CodeMetric
            {
                Name = "Instability",
                Category = "Coupling",
                Value = Math.Round(instability, 2),
                Unit = "ratio",
                Description = $"Instability metric for {className} (0=stable, 1=unstable)",
                Location = GetLocation(classDecl, filePath)
            });
        }

        return metrics;
    }

    private List<CodeMetric> CalculateCohesionMetrics(SyntaxNode root, string filePath)
    {
        var metrics = new List<CodeMetric>();
        var classes = root.DescendantNodes().OfType<ClassDeclarationSyntax>();

        foreach (var classDecl in classes)
        {
            var className = classDecl.Identifier.ValueText;

            // LCOM (Lack of Cohesion of Methods) - simplified version
            var lcom = CalculateLCOM(classDecl);
            metrics.Add(new CodeMetric
            {
                Name = "LCOM",
                Category = "Cohesion",
                Value = lcom,
                Unit = "score",
                Description = $"Lack of Cohesion of Methods for {className} (lower is better)",
                Location = GetLocation(classDecl, filePath)
            });

            // Method-Field Interaction Ratio
            var methodFieldRatio = CalculateMethodFieldInteractionRatio(classDecl);
            metrics.Add(new CodeMetric
            {
                Name = "Method-Field Interaction Ratio",
                Category = "Cohesion",
                Value = Math.Round(methodFieldRatio, 2),
                Unit = "ratio",
                Description = $"Average number of fields accessed per method in {className}",
                Location = GetLocation(classDecl, filePath)
            });
        }

        return metrics;
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

    private int CalculateCognitiveComplexity(MethodDeclarationSyntax method)
    {
        var complexity = 0;
        var nestingLevel = 0;

        foreach (var node in method.DescendantNodes())
        {
            switch (node)
            {
                case IfStatementSyntax:
                case WhileStatementSyntax:
                case ForStatementSyntax:
                case ForEachStatementSyntax:
                case SwitchStatementSyntax:
                    complexity += 1 + nestingLevel;
                    nestingLevel++;
                    break;
                case ConditionalExpressionSyntax:
                    complexity += 1;
                    break;
                case CatchClauseSyntax:
                    complexity += 1 + nestingLevel;
                    break;
            }

            // Handle logical operators
            if (node is BinaryExpressionSyntax binary)
            {
                if (binary.OperatorToken.IsKind(SyntaxKind.AmpersandAmpersandToken) ||
                    binary.OperatorToken.IsKind(SyntaxKind.BarBarToken))
                {
                    complexity += 1;
                }
            }
        }

        return complexity;
    }

    private int CalculateNestingDepth(MethodDeclarationSyntax method)
    {
        var maxDepth = 0;
        var currentDepth = 0;

        foreach (var node in method.DescendantNodes())
        {
            if (IsNestingNode(node))
            {
                currentDepth++;
                maxDepth = Math.Max(maxDepth, currentDepth);
            }
            else if (IsClosingNode(node))
            {
                currentDepth = Math.Max(0, currentDepth - 1);
            }
        }

        return maxDepth;
    }

    private bool IsNestingNode(SyntaxNode node)
    {
        return node is IfStatementSyntax ||
               node is WhileStatementSyntax ||
               node is ForStatementSyntax ||
               node is ForEachStatementSyntax ||
               node is SwitchStatementSyntax ||
               node is TryStatementSyntax ||
               node is BlockSyntax;
    }

    private bool IsClosingNode(SyntaxNode node)
    {
        // This is a simplified approach - in practice, you'd need more sophisticated tracking
        return false;
    }

    private double CalculateMaintainabilityIndex(SyntaxNode root)
    {
        var methods = root.DescendantNodes().OfType<MethodDeclarationSyntax>();
        if (!methods.Any()) return 100;

        var avgCyclomaticComplexity = methods.Average(m => CalculateCyclomaticComplexity(m));
        var linesOfCode = CalculateLinesOfCode(root);
        var commentDensity = CalculateCommentDensity(root);

        // Simplified maintainability index calculation
        var maintainabilityIndex = 171 - 5.2 * Math.Log(avgCyclomaticComplexity) - 0.23 * avgCyclomaticComplexity - 16.2 * Math.Log(linesOfCode) + 50 * Math.Sin(Math.Sqrt(2.4 * commentDensity));

        return Math.Max(0, Math.Min(100, maintainabilityIndex));
    }

    private double CalculateCommentDensity(SyntaxNode root)
    {
        var totalLines = root.GetLocation().GetLineSpan().EndLinePosition.Line + 1;
        var commentLines = root.DescendantTrivia().Count(t => 
            t.IsKind(SyntaxKind.SingleLineCommentTrivia) ||
            t.IsKind(SyntaxKind.MultiLineCommentTrivia) ||
            t.IsKind(SyntaxKind.SingleLineDocumentationCommentTrivia) ||
            t.IsKind(SyntaxKind.MultiLineDocumentationCommentTrivia));

        return totalLines > 0 ? (double)commentLines / totalLines * 100 : 0;
    }

    private double CalculateTechnicalDebtRatio(SyntaxNode root)
    {
        var methods = root.DescendantNodes().OfType<MethodDeclarationSyntax>();
        var totalComplexity = methods.Sum(m => CalculateCyclomaticComplexity(m));
        var methodCount = methods.Count();

        if (methodCount == 0) return 0;

        var avgComplexity = (double)totalComplexity / methodCount;
        
        // Simplified technical debt calculation based on complexity
        var debtRatio = Math.Max(0, (avgComplexity - 5) * 2); // Assume debt starts accumulating after complexity of 5
        
        return Math.Min(100, debtRatio);
    }

    private int CalculateLinesOfCode(SyntaxNode root)
    {
        var lines = root.ToString().Split('\n');
        var codeLines = lines.Count(line => 
            !string.IsNullOrWhiteSpace(line) && 
            !line.Trim().StartsWith("//") && 
            !line.Trim().StartsWith("/*") && 
            !line.Trim().StartsWith("*"));

        return codeLines;
    }

    private int CalculateMethodLength(MethodDeclarationSyntax method)
    {
        var span = method.GetLocation().GetLineSpan();
        return span.EndLinePosition.Line - span.StartLinePosition.Line + 1;
    }

    private int CalculateAfferentCoupling(ClassDeclarationSyntax targetClass, SyntaxNode root)
    {
        var targetClassName = targetClass.Identifier.ValueText;
        var otherClasses = root.DescendantNodes().OfType<ClassDeclarationSyntax>()
            .Where(c => c.Identifier.ValueText != targetClassName);

        var afferentCount = 0;

        foreach (var otherClass in otherClasses)
        {
            var identifiers = otherClass.DescendantNodes().OfType<IdentifierNameSyntax>();
            if (identifiers.Any(id => id.Identifier.ValueText == targetClassName))
            {
                afferentCount++;
            }
        }

        return afferentCount;
    }

    private int CalculateEfferentCoupling(ClassDeclarationSyntax sourceClass, SyntaxNode root)
    {
        var sourceClassName = sourceClass.Identifier.ValueText;
        var allClassNames = root.DescendantNodes().OfType<ClassDeclarationSyntax>()
            .Select(c => c.Identifier.ValueText)
            .Where(name => name != sourceClassName)
            .ToHashSet();

        var identifiers = sourceClass.DescendantNodes().OfType<IdentifierNameSyntax>();
        var referencedClasses = identifiers
            .Where(id => allClassNames.Contains(id.Identifier.ValueText))
            .Select(id => id.Identifier.ValueText)
            .Distinct()
            .Count();

        return referencedClasses;
    }

    private int CalculateLCOM(ClassDeclarationSyntax classDecl)
    {
        var methods = classDecl.Members.OfType<MethodDeclarationSyntax>().ToList();
        var fields = classDecl.Members.OfType<FieldDeclarationSyntax>()
            .SelectMany(f => f.Declaration.Variables.Select(v => v.Identifier.ValueText))
            .ToList();

        if (methods.Count <= 1 || fields.Count == 0) return 0;

        var methodFieldAccess = new Dictionary<string, HashSet<string>>();

        foreach (var method in methods)
        {
            var methodName = method.Identifier.ValueText;
            methodFieldAccess[methodName] = new HashSet<string>();

            var identifiers = method.DescendantNodes().OfType<IdentifierNameSyntax>();
            foreach (var identifier in identifiers)
            {
                if (fields.Contains(identifier.Identifier.ValueText))
                {
                    methodFieldAccess[methodName].Add(identifier.Identifier.ValueText);
                }
            }
        }

        // Calculate LCOM as number of method pairs that don't share fields
        var totalPairs = 0;
        var unrelatedPairs = 0;

        for (int i = 0; i < methods.Count; i++)
        {
            for (int j = i + 1; j < methods.Count; j++)
            {
                totalPairs++;
                var method1 = methods[i].Identifier.ValueText;
                var method2 = methods[j].Identifier.ValueText;

                if (!methodFieldAccess[method1].Intersect(methodFieldAccess[method2]).Any())
                {
                    unrelatedPairs++;
                }
            }
        }

        return totalPairs > 0 ? (unrelatedPairs * 100) / totalPairs : 0;
    }

    private double CalculateMethodFieldInteractionRatio(ClassDeclarationSyntax classDecl)
    {
        var methods = classDecl.Members.OfType<MethodDeclarationSyntax>().ToList();
        var fields = classDecl.Members.OfType<FieldDeclarationSyntax>()
            .SelectMany(f => f.Declaration.Variables.Select(v => v.Identifier.ValueText))
            .ToList();

        if (methods.Count == 0 || fields.Count == 0) return 0;

        var totalFieldAccesses = 0;

        foreach (var method in methods)
        {
            var identifiers = method.DescendantNodes().OfType<IdentifierNameSyntax>();
            var fieldAccesses = identifiers.Count(id => fields.Contains(id.Identifier.ValueText));
            totalFieldAccesses += fieldAccesses;
        }

        return (double)totalFieldAccesses / methods.Count;
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
}
