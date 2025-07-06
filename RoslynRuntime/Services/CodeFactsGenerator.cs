using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using RoslynRuntime.Models;
using System.Text;

namespace RoslynRuntime.Services;

public class CodeFactsGenerator
{
    public async Task<CodeFacts> GenerateCodeFactsAsync(string filePath, string format = "json", bool includeDescriptions = true)
    {
        var codeFacts = new CodeFacts
        {
            AnalysisTime = DateTime.UtcNow,
            FilePath = filePath
        };

        try
        {
            var sourceCode = await File.ReadAllTextAsync(filePath);
            var syntaxTree = CSharpSyntaxTree.ParseText(sourceCode, path: filePath);
            var root = await syntaxTree.GetRootAsync();

            // Generate facts about code elements
            codeFacts.Facts = GenerateFacts(root, filePath);
            
            // Generate summaries
            if (includeDescriptions)
            {
                codeFacts.Summaries = GenerateSummaries(root);
            }
            
            // Generate API contracts
            codeFacts.Contracts = GenerateApiContracts(root, filePath);
            
            // Generate documentation
            if (includeDescriptions)
            {
                codeFacts.Documentation = GenerateDocumentation(root, filePath);
            }

            return codeFacts;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to generate code facts: {ex.Message}", ex);
        }
    }

    private List<CodeFact> GenerateFacts(SyntaxNode root, string filePath)
    {
        var facts = new List<CodeFact>();

        // Generate facts about classes
        facts.AddRange(GenerateClassFacts(root, filePath));
        
        // Generate facts about methods
        facts.AddRange(GenerateMethodFacts(root, filePath));
        
        // Generate facts about properties
        facts.AddRange(GeneratePropertyFacts(root, filePath));
        
        // Generate facts about relationships
        facts.AddRange(GenerateRelationshipFacts(root, filePath));

        return facts;
    }

    private List<CodeFact> GenerateClassFacts(SyntaxNode root, string filePath)
    {
        var facts = new List<CodeFact>();
        var classes = root.DescendantNodes().OfType<ClassDeclarationSyntax>();

        foreach (var classDecl in classes)
        {
            var className = classDecl.Identifier.ValueText;
            var location = GetLocation(classDecl, filePath);

            // Basic class facts
            facts.Add(new CodeFact
            {
                Type = "Class",
                Subject = className,
                Predicate = "is_a",
                Object = "class",
                Confidence = 1.0,
                Location = location,
                Context = new Dictionary<string, object>
                {
                    ["AccessModifier"] = GetAccessModifier(classDecl.Modifiers),
                    ["IsAbstract"] = classDecl.Modifiers.Any(m => m.IsKind(SyntaxKind.AbstractKeyword)),
                    ["IsSealed"] = classDecl.Modifiers.Any(m => m.IsKind(SyntaxKind.SealedKeyword)),
                    ["IsStatic"] = classDecl.Modifiers.Any(m => m.IsKind(SyntaxKind.StaticKeyword))
                }
            });

            // Inheritance facts
            if (classDecl.BaseList != null)
            {
                foreach (var baseType in classDecl.BaseList.Types)
                {
                    var baseTypeName = baseType.Type.ToString();
                    facts.Add(new CodeFact
                    {
                        Type = "Class",
                        Subject = className,
                        Predicate = "inherits_from",
                        Object = baseTypeName,
                        Confidence = 0.9,
                        Location = location,
                        Context = new Dictionary<string, object>
                        {
                            ["InheritanceType"] = DetermineInheritanceType(baseTypeName)
                        }
                    });
                }
            }

            // Member count facts
            var methodCount = classDecl.Members.OfType<MethodDeclarationSyntax>().Count();
            var propertyCount = classDecl.Members.OfType<PropertyDeclarationSyntax>().Count();
            var fieldCount = classDecl.Members.OfType<FieldDeclarationSyntax>().Count();

            facts.Add(new CodeFact
            {
                Type = "Class",
                Subject = className,
                Predicate = "has_method_count",
                Object = methodCount.ToString(),
                Confidence = 1.0,
                Location = location
            });

            facts.Add(new CodeFact
            {
                Type = "Class",
                Subject = className,
                Predicate = "has_property_count",
                Object = propertyCount.ToString(),
                Confidence = 1.0,
                Location = location
            });

            // Responsibility facts based on naming patterns
            var responsibility = DetermineClassResponsibility(className);
            if (!string.IsNullOrEmpty(responsibility))
            {
                facts.Add(new CodeFact
                {
                    Type = "Class",
                    Subject = className,
                    Predicate = "has_responsibility",
                    Object = responsibility,
                    Confidence = 0.7,
                    Location = location
                });
            }
        }

        return facts;
    }

    private List<CodeFact> GenerateMethodFacts(SyntaxNode root, string filePath)
    {
        var facts = new List<CodeFact>();
        var methods = root.DescendantNodes().OfType<MethodDeclarationSyntax>();

        foreach (var method in methods)
        {
            var className = method.Ancestors().OfType<ClassDeclarationSyntax>().FirstOrDefault()?.Identifier.ValueText ?? "Unknown";
            var methodName = method.Identifier.ValueText;
            var fullMethodName = $"{className}.{methodName}";
            var location = GetLocation(method, filePath);

            // Basic method facts
            facts.Add(new CodeFact
            {
                Type = "Method",
                Subject = fullMethodName,
                Predicate = "is_a",
                Object = "method",
                Confidence = 1.0,
                Location = location,
                Context = new Dictionary<string, object>
                {
                    ["AccessModifier"] = GetAccessModifier(method.Modifiers),
                    ["IsStatic"] = method.Modifiers.Any(m => m.IsKind(SyntaxKind.StaticKeyword)),
                    ["IsAsync"] = method.Modifiers.Any(m => m.IsKind(SyntaxKind.AsyncKeyword)),
                    ["ReturnType"] = method.ReturnType.ToString()
                }
            });

            // Parameter facts
            foreach (var parameter in method.ParameterList.Parameters)
            {
                facts.Add(new CodeFact
                {
                    Type = "Method",
                    Subject = fullMethodName,
                    Predicate = "has_parameter",
                    Object = $"{parameter.Type} {parameter.Identifier.ValueText}",
                    Confidence = 1.0,
                    Location = location,
                    Context = new Dictionary<string, object>
                    {
                        ["ParameterName"] = parameter.Identifier.ValueText,
                        ["ParameterType"] = parameter.Type?.ToString() ?? "unknown"
                    }
                });
            }

            // Behavior facts based on method content
            var behaviors = AnalyzeMethodBehavior(method);
            foreach (var behavior in behaviors)
            {
                facts.Add(new CodeFact
                {
                    Type = "Method",
                    Subject = fullMethodName,
                    Predicate = "exhibits_behavior",
                    Object = behavior,
                    Confidence = 0.8,
                    Location = location
                });
            }

            // Complexity facts
            var complexity = CalculateMethodComplexity(method);
            facts.Add(new CodeFact
            {
                Type = "Method",
                Subject = fullMethodName,
                Predicate = "has_complexity",
                Object = complexity.ToString(),
                Confidence = 1.0,
                Location = location,
                Context = new Dictionary<string, object>
                {
                    ["ComplexityLevel"] = GetComplexityLevel(complexity)
                }
            });
        }

        return facts;
    }

    private List<CodeFact> GeneratePropertyFacts(SyntaxNode root, string filePath)
    {
        var facts = new List<CodeFact>();
        var properties = root.DescendantNodes().OfType<PropertyDeclarationSyntax>();

        foreach (var property in properties)
        {
            var className = property.Ancestors().OfType<ClassDeclarationSyntax>().FirstOrDefault()?.Identifier.ValueText ?? "Unknown";
            var propertyName = property.Identifier.ValueText;
            var fullPropertyName = $"{className}.{propertyName}";
            var location = GetLocation(property, filePath);

            // Basic property facts
            facts.Add(new CodeFact
            {
                Type = "Property",
                Subject = fullPropertyName,
                Predicate = "is_a",
                Object = "property",
                Confidence = 1.0,
                Location = location,
                Context = new Dictionary<string, object>
                {
                    ["PropertyType"] = property.Type.ToString(),
                    ["AccessModifier"] = GetAccessModifier(property.Modifiers),
                    ["IsStatic"] = property.Modifiers.Any(m => m.IsKind(SyntaxKind.StaticKeyword))
                }
            });

            // Accessor facts
            if (property.AccessorList != null)
            {
                var hasGetter = property.AccessorList.Accessors.Any(a => a.IsKind(SyntaxKind.GetAccessorDeclaration));
                var hasSetter = property.AccessorList.Accessors.Any(a => a.IsKind(SyntaxKind.SetAccessorDeclaration));

                if (hasGetter)
                {
                    facts.Add(new CodeFact
                    {
                        Type = "Property",
                        Subject = fullPropertyName,
                        Predicate = "has_getter",
                        Object = "true",
                        Confidence = 1.0,
                        Location = location
                    });
                }

                if (hasSetter)
                {
                    facts.Add(new CodeFact
                    {
                        Type = "Property",
                        Subject = fullPropertyName,
                        Predicate = "has_setter",
                        Object = "true",
                        Confidence = 1.0,
                        Location = location
                    });
                }

                // Auto-property fact
                var isAutoProperty = property.AccessorList.Accessors.All(a => a.Body == null && a.ExpressionBody == null);
                if (isAutoProperty)
                {
                    facts.Add(new CodeFact
                    {
                        Type = "Property",
                        Subject = fullPropertyName,
                        Predicate = "is_auto_property",
                        Object = "true",
                        Confidence = 1.0,
                        Location = location
                    });
                }
            }
        }

        return facts;
    }

    private List<CodeFact> GenerateRelationshipFacts(SyntaxNode root, string filePath)
    {
        var facts = new List<CodeFact>();
        var classes = root.DescendantNodes().OfType<ClassDeclarationSyntax>();

        foreach (var classDecl in classes)
        {
            var className = classDecl.Identifier.ValueText;
            var location = GetLocation(classDecl, filePath);

            // Find usage relationships
            var identifiers = classDecl.DescendantNodes().OfType<IdentifierNameSyntax>();
            var referencedTypes = identifiers
                .Select(id => id.Identifier.ValueText)
                .Where(name => classes.Any(c => c.Identifier.ValueText == name && c.Identifier.ValueText != className))
                .Distinct();

            foreach (var referencedType in referencedTypes)
            {
                facts.Add(new CodeFact
                {
                    Type = "Relationship",
                    Subject = className,
                    Predicate = "uses",
                    Object = referencedType,
                    Confidence = 0.8,
                    Location = location,
                    Context = new Dictionary<string, object>
                    {
                        ["RelationshipType"] = "Usage"
                    }
                });
            }

            // Find composition relationships (fields of other class types)
            var fields = classDecl.Members.OfType<FieldDeclarationSyntax>();
            foreach (var field in fields)
            {
                var fieldType = field.Declaration.Type.ToString();
                if (classes.Any(c => c.Identifier.ValueText == fieldType))
                {
                    facts.Add(new CodeFact
                    {
                        Type = "Relationship",
                        Subject = className,
                        Predicate = "has_composition_with",
                        Object = fieldType,
                        Confidence = 0.9,
                        Location = location,
                        Context = new Dictionary<string, object>
                        {
                            ["RelationshipType"] = "Composition"
                        }
                    });
                }
            }
        }

        return facts;
    }

    private Dictionary<string, string> GenerateSummaries(SyntaxNode root)
    {
        var summaries = new Dictionary<string, string>();
        var classes = root.DescendantNodes().OfType<ClassDeclarationSyntax>();

        foreach (var classDecl in classes)
        {
            var className = classDecl.Identifier.ValueText;
            var summary = GenerateClassSummary(classDecl);
            summaries[className] = summary;

            // Generate method summaries
            var methods = classDecl.Members.OfType<MethodDeclarationSyntax>();
            foreach (var method in methods)
            {
                var methodName = $"{className}.{method.Identifier.ValueText}";
                var methodSummary = GenerateMethodSummary(method);
                summaries[methodName] = methodSummary;
            }
        }

        return summaries;
    }

    private List<ApiContract> GenerateApiContracts(SyntaxNode root, string filePath)
    {
        var contracts = new List<ApiContract>();
        var methods = root.DescendantNodes().OfType<MethodDeclarationSyntax>();

        foreach (var method in methods)
        {
            var className = method.Ancestors().OfType<ClassDeclarationSyntax>().FirstOrDefault()?.Identifier.ValueText ?? "Unknown";
            var methodName = method.Identifier.ValueText;
            var fullMethodName = $"{className}.{methodName}";

            var contract = new ApiContract
            {
                Name = fullMethodName,
                Type = "Method",
                Location = GetLocation(method, filePath),
                Description = GenerateMethodDescription(method)
            };

            // Extract parameters
            foreach (var param in method.ParameterList.Parameters)
            {
                contract.Parameters.Add(new Parameter
                {
                    Name = param.Identifier.ValueText,
                    Type = param.Type?.ToString() ?? "unknown",
                    Description = $"Parameter of type {param.Type}",
                    IsOptional = param.Default != null
                });
            }

            // Extract return value
            if (method.ReturnType.ToString() != "void")
            {
                contract.ReturnValue = new ReturnValue
                {
                    Type = method.ReturnType.ToString(),
                    Description = $"Returns a value of type {method.ReturnType}"
                };
            }

            // Analyze preconditions and postconditions
            contract.Preconditions = AnalyzePreconditions(method);
            contract.Postconditions = AnalyzePostconditions(method);
            contract.SideEffects = AnalyzeSideEffects(method);
            contract.Exceptions = AnalyzeExceptions(method);

            contracts.Add(contract);
        }

        return contracts;
    }

    private CodeDocumentation GenerateDocumentation(SyntaxNode root, string filePath)
    {
        var documentation = new CodeDocumentation();
        var classes = root.DescendantNodes().OfType<ClassDeclarationSyntax>();

        // Generate class documentation
        foreach (var classDecl in classes)
        {
            documentation.Classes.Add(new ClassDocumentation
            {
                Name = classDecl.Identifier.ValueText,
                Purpose = DetermineClassPurpose(classDecl),
                Responsibilities = DetermineClassResponsibilities(classDecl),
                Collaborators = FindClassCollaborators(classDecl, root),
                Location = GetLocation(classDecl, filePath),
                Complexity = GetComplexityLevel(CalculateClassComplexity(classDecl)),
                DesignPatterns = DetectClassPatterns(classDecl)
            });
        }

        // Generate method documentation
        var methods = root.DescendantNodes().OfType<MethodDeclarationSyntax>();
        foreach (var method in methods)
        {
            var className = method.Ancestors().OfType<ClassDeclarationSyntax>().FirstOrDefault()?.Identifier.ValueText ?? "Unknown";
            
            documentation.Methods.Add(new MethodDocumentation
            {
                Name = method.Identifier.ValueText,
                ClassName = className,
                Purpose = DetermineMethodPurpose(method),
                Algorithm = AnalyzeMethodAlgorithm(method),
                Behavior = AnalyzeMethodBehaviorDescription(method),
                Location = GetLocation(method, filePath),
                CyclomaticComplexity = CalculateMethodComplexity(method),
                BusinessRules = ExtractBusinessRules(method)
            });
        }

        // Generate property documentation
        var properties = root.DescendantNodes().OfType<PropertyDeclarationSyntax>();
        foreach (var property in properties)
        {
            var className = property.Ancestors().OfType<ClassDeclarationSyntax>().FirstOrDefault()?.Identifier.ValueText ?? "Unknown";
            
            documentation.Properties.Add(new PropertyDocumentation
            {
                Name = property.Identifier.ValueText,
                ClassName = className,
                Purpose = DeterminePropertyPurpose(property),
                Type = property.Type.ToString(),
                IsReadOnly = property.AccessorList?.Accessors.Any(a => a.IsKind(SyntaxKind.SetAccessorDeclaration)) != true,
                IsWriteOnly = property.AccessorList?.Accessors.Any(a => a.IsKind(SyntaxKind.GetAccessorDeclaration)) != true,
                Location = GetLocation(property, filePath),
                ValidationRules = ExtractValidationRules(property)
            });
        }

        // Generate overall summary
        documentation.OverallSummary = GenerateOverallSummary(root);
        documentation.KeyConcepts = ExtractKeyConcepts(root);
        documentation.Examples = GenerateUsageExamples(root);

        return documentation;
    }

    // Helper methods for analysis
    private string DetermineInheritanceType(string baseTypeName)
    {
        if (baseTypeName.StartsWith("I") && char.IsUpper(baseTypeName[1]))
            return "Interface";
        return "Class";
    }

    private string DetermineClassResponsibility(string className)
    {
        if (className.EndsWith("Controller")) return "Web Controller";
        if (className.EndsWith("Service")) return "Business Service";
        if (className.EndsWith("Repository")) return "Data Repository";
        if (className.EndsWith("Model") || className.EndsWith("Entity")) return "Data Model";
        if (className.EndsWith("Helper")) return "Utility Helper";
        if (className.EndsWith("Factory")) return "Object Factory";
        if (className.EndsWith("Builder")) return "Object Builder";
        if (className.EndsWith("Manager")) return "Resource Manager";
        return "";
    }

    private List<string> AnalyzeMethodBehavior(MethodDeclarationSyntax method)
    {
        var behaviors = new List<string>();
        var methodBody = method.Body?.ToString() ?? method.ExpressionBody?.ToString() ?? "";

        if (methodBody.Contains("return")) behaviors.Add("Returns value");
        if (methodBody.Contains("throw")) behaviors.Add("Throws exceptions");
        if (methodBody.Contains("await")) behaviors.Add("Asynchronous operation");
        if (methodBody.Contains("foreach") || methodBody.Contains("for")) behaviors.Add("Iterates over collection");
        if (methodBody.Contains("if")) behaviors.Add("Conditional logic");
        if (methodBody.Contains("try")) behaviors.Add("Error handling");
        if (methodBody.Contains("new ")) behaviors.Add("Creates objects");

        return behaviors;
    }

    private int CalculateMethodComplexity(MethodDeclarationSyntax method)
    {
        var complexity = 1;
        var controlFlowNodes = method.DescendantNodes().Where(n =>
            n is IfStatementSyntax ||
            n is WhileStatementSyntax ||
            n is ForStatementSyntax ||
            n is ForEachStatementSyntax ||
            n is SwitchStatementSyntax ||
            n is ConditionalExpressionSyntax ||
            n is CatchClauseSyntax);

        return complexity + controlFlowNodes.Count();
    }

    private int CalculateClassComplexity(ClassDeclarationSyntax classDecl)
    {
        var methods = classDecl.Members.OfType<MethodDeclarationSyntax>();
        return methods.Sum(m => CalculateMethodComplexity(m));
    }

    private string GetComplexityLevel(int complexity)
    {
        if (complexity <= 5) return "Low";
        if (complexity <= 10) return "Medium";
        if (complexity <= 20) return "High";
        return "Very High";
    }

    private string GenerateClassSummary(ClassDeclarationSyntax classDecl)
    {
        var className = classDecl.Identifier.ValueText;
        var methodCount = classDecl.Members.OfType<MethodDeclarationSyntax>().Count();
        var propertyCount = classDecl.Members.OfType<PropertyDeclarationSyntax>().Count();
        var responsibility = DetermineClassResponsibility(className);

        var summary = new StringBuilder();
        summary.Append($"Class {className}");
        
        if (!string.IsNullOrEmpty(responsibility))
            summary.Append($" serves as a {responsibility}");
        
        summary.Append($" with {methodCount} methods and {propertyCount} properties.");

        if (classDecl.BaseList != null)
        {
            var baseTypes = classDecl.BaseList.Types.Select(t => t.Type.ToString());
            summary.Append($" It extends/implements: {string.Join(", ", baseTypes)}.");
        }

        return summary.ToString();
    }

    private string GenerateMethodSummary(MethodDeclarationSyntax method)
    {
        var methodName = method.Identifier.ValueText;
        var returnType = method.ReturnType.ToString();
        var paramCount = method.ParameterList.Parameters.Count;
        var complexity = CalculateMethodComplexity(method);

        var summary = new StringBuilder();
        summary.Append($"Method {methodName}");
        
        if (returnType != "void")
            summary.Append($" returns {returnType}");
        
        summary.Append($" and takes {paramCount} parameter(s).");
        summary.Append($" Complexity level: {GetComplexityLevel(complexity)}.");

        var behaviors = AnalyzeMethodBehavior(method);
        if (behaviors.Any())
        {
            summary.Append($" Behaviors: {string.Join(", ", behaviors)}.");
        }

        return summary.ToString();
    }

    private string GenerateMethodDescription(MethodDeclarationSyntax method)
    {
        var purpose = DetermineMethodPurpose(method);
        return !string.IsNullOrEmpty(purpose) ? purpose : $"Method {method.Identifier.ValueText} performs operations as defined in its implementation.";
    }

    private string DetermineMethodPurpose(MethodDeclarationSyntax method)
    {
        var methodName = method.Identifier.ValueText.ToLower();
        
        if (methodName.StartsWith("get")) return "Retrieves data or values";
        if (methodName.StartsWith("set")) return "Sets or updates data";
        if (methodName.StartsWith("create")) return "Creates new instances or data";
        if (methodName.StartsWith("delete") || methodName.StartsWith("remove")) return "Deletes or removes data";
        if (methodName.StartsWith("update")) return "Updates existing data";
        if (methodName.StartsWith("validate")) return "Validates data or conditions";
        if (methodName.StartsWith("calculate")) return "Performs calculations";
        if (methodName.StartsWith("process")) return "Processes data or operations";
        if (methodName.StartsWith("handle")) return "Handles events or operations";
        if (methodName.StartsWith("initialize") || methodName.StartsWith("init")) return "Initializes components or data";
        
        return "";
    }

    private List<string> AnalyzePreconditions(MethodDeclarationSyntax method)
    {
        var preconditions = new List<string>();
        
        // Look for parameter validation
        var methodBody = method.Body?.ToString() ?? "";
        if (methodBody.Contains("ArgumentNullException")) preconditions.Add("Parameters must not be null");
        if (methodBody.Contains("ArgumentException")) preconditions.Add("Parameters must be valid");
        if (methodBody.Contains("if") && methodBody.Contains("throw")) preconditions.Add("Input validation required");
        
        return preconditions;
    }

    private List<string> AnalyzePostconditions(MethodDeclarationSyntax method)
    {
        var postconditions = new List<string>();
        
        if (method.ReturnType.ToString() != "void")
        {
            postconditions.Add($"Returns a value of type {method.ReturnType}");
        }
        
        return postconditions;
    }

    private List<string> AnalyzeSideEffects(MethodDeclarationSyntax method)
    {
        var sideEffects = new List<string>();
        var methodBody = method.Body?.ToString() ?? "";
        
        if (methodBody.Contains("Console.Write")) sideEffects.Add("Writes to console");
        if (methodBody.Contains("File.")) sideEffects.Add("File system operations");
        if (methodBody.Contains("Database") || methodBody.Contains("Sql")) sideEffects.Add("Database operations");
        if (methodBody.Contains("HttpClient") || methodBody.Contains("WebRequest")) sideEffects.Add("Network operations");
        
        return sideEffects;
    }

    private List<string> AnalyzeExceptions(MethodDeclarationSyntax method)
    {
        var exceptions = new List<string>();
        var methodBody = method.Body?.ToString() ?? "";
        
        if (methodBody.Contains("ArgumentNullException")) exceptions.Add("ArgumentNullException");
        if (methodBody.Contains("ArgumentException")) exceptions.Add("ArgumentException");
        if (methodBody.Contains("InvalidOperationException")) exceptions.Add("InvalidOperationException");
        if (methodBody.Contains("NotImplementedException")) exceptions.Add("NotImplementedException");
        
        return exceptions;
    }

    private string DetermineClassPurpose(ClassDeclarationSyntax classDecl)
    {
        var responsibility = DetermineClassResponsibility(classDecl.Identifier.ValueText);
        return !string.IsNullOrEmpty(responsibility) ? $"This class serves as a {responsibility}" : "General purpose class";
    }

    private string DetermineClassResponsibilities(ClassDeclarationSyntax classDecl)
    {
        var responsibilities = new List<string>();
        var methods = classDecl.Members.OfType<MethodDeclarationSyntax>();
        
        foreach (var method in methods)
        {
            var purpose = DetermineMethodPurpose(method);
            if (!string.IsNullOrEmpty(purpose) && !responsibilities.Contains(purpose))
            {
                responsibilities.Add(purpose);
            }
        }
        
        return string.Join(", ", responsibilities);
    }

    private List<string> FindClassCollaborators(ClassDeclarationSyntax classDecl, SyntaxNode root)
    {
        var collaborators = new List<string>();
        var allClasses = root.DescendantNodes().OfType<ClassDeclarationSyntax>().Select(c => c.Identifier.ValueText).ToList();
        var identifiers = classDecl.DescendantNodes().OfType<IdentifierNameSyntax>();
        
        foreach (var identifier in identifiers)
        {
            if (allClasses.Contains(identifier.Identifier.ValueText) && 
                identifier.Identifier.ValueText != classDecl.Identifier.ValueText)
            {
                if (!collaborators.Contains(identifier.Identifier.ValueText))
                {
                    collaborators.Add(identifier.Identifier.ValueText);
                }
            }
        }
        
        return collaborators;
    }

    private List<string> DetectClassPatterns(ClassDeclarationSyntax classDecl)
    {
        var patterns = new List<string>();
        var className = classDecl.Identifier.ValueText;
        
        if (className.EndsWith("Factory")) patterns.Add("Factory Pattern");
        if (className.EndsWith("Builder")) patterns.Add("Builder Pattern");
        if (className.EndsWith("Singleton")) patterns.Add("Singleton Pattern");
        if (className.EndsWith("Observer")) patterns.Add("Observer Pattern");
        if (className.EndsWith("Strategy")) patterns.Add("Strategy Pattern");
        if (className.EndsWith("Decorator")) patterns.Add("Decorator Pattern");
        
        return patterns;
    }

    private string AnalyzeMethodAlgorithm(MethodDeclarationSyntax method)
    {
        var methodBody = method.Body?.ToString() ?? "";
        
        if (methodBody.Contains("foreach") || methodBody.Contains("for"))
            return "Iterative algorithm";
        if (methodBody.Contains("while"))
            return "Loop-based algorithm";
        if (methodBody.Contains("if") && methodBody.Contains("else"))
            return "Conditional algorithm";
        if (methodBody.Contains("switch"))
            return "Decision-based algorithm";
        if (methodBody.Contains("return") && !methodBody.Contains("if"))
            return "Direct computation";
            
        return "Sequential algorithm";
    }

    private string AnalyzeMethodBehaviorDescription(MethodDeclarationSyntax method)
    {
        var behaviors = AnalyzeMethodBehavior(method);
        return behaviors.Any() ? string.Join(", ", behaviors) : "Standard method behavior";
    }

    private List<string> ExtractBusinessRules(MethodDeclarationSyntax method)
    {
        var rules = new List<string>();
        var methodBody = method.Body?.ToString() ?? "";
        
        // This is a simplified extraction - in practice, you'd use more sophisticated analysis
        if (methodBody.Contains("if") && methodBody.Contains("throw"))
            rules.Add("Input validation rule");
        if (methodBody.Contains("DateTime.Now"))
            rules.Add("Time-dependent rule");
        if (methodBody.Contains("Math."))
            rules.Add("Mathematical calculation rule");
            
        return rules;
    }

    private string DeterminePropertyPurpose(PropertyDeclarationSyntax property)
    {
        var propertyName = property.Identifier.ValueText.ToLower();
        
        if (propertyName.Contains("id")) return "Identifier property";
        if (propertyName.Contains("name")) return "Name property";
        if (propertyName.Contains("count") || propertyName.Contains("length")) return "Count/Size property";
        if (propertyName.Contains("date") || propertyName.Contains("time")) return "Date/Time property";
        if (propertyName.Contains("status") || propertyName.Contains("state")) return "Status property";
        if (propertyName.Contains("config") || propertyName.Contains("setting")) return "Configuration property";
        
        return "Data property";
    }

    private List<string> ExtractValidationRules(PropertyDeclarationSyntax property)
    {
        var rules = new List<string>();
        
        // Look for validation attributes (simplified)
        var attributes = property.AttributeLists.SelectMany(al => al.Attributes);
        foreach (var attr in attributes)
        {
            var attrName = attr.Name.ToString();
            if (attrName.Contains("Required")) rules.Add("Required field");
            if (attrName.Contains("Range")) rules.Add("Range validation");
            if (attrName.Contains("StringLength")) rules.Add("String length validation");
            if (attrName.Contains("RegularExpression")) rules.Add("Pattern validation");
        }
        
        return rules;
    }

    private string GenerateOverallSummary(SyntaxNode root)
    {
        var classes = root.DescendantNodes().OfType<ClassDeclarationSyntax>().Count();
        var interfaces = root.DescendantNodes().OfType<InterfaceDeclarationSyntax>().Count();
        var methods = root.DescendantNodes().OfType<MethodDeclarationSyntax>().Count();
        var properties = root.DescendantNodes().OfType<PropertyDeclarationSyntax>().Count();
        
        return $"This code file contains {classes} classes, {interfaces} interfaces, {methods} methods, and {properties} properties. " +
               "It represents a structured C# codebase with various components working together to provide functionality.";
    }

    private List<string> ExtractKeyConcepts(SyntaxNode root)
    {
        var concepts = new List<string>();
        var classes = root.DescendantNodes().OfType<ClassDeclarationSyntax>();
        
        foreach (var classDecl in classes)
        {
            var responsibility = DetermineClassResponsibility(classDecl.Identifier.ValueText);
            if (!string.IsNullOrEmpty(responsibility) && !concepts.Contains(responsibility))
            {
                concepts.Add(responsibility);
            }
        }
        
        // Add technical concepts
        var namespaces = root.DescendantNodes().OfType<UsingDirectiveSyntax>()
            .Select(u => u.Name?.ToString())
            .Where(n => !string.IsNullOrEmpty(n))
            .ToList();
            
        if (namespaces.Any(n => n!.Contains("System.Threading"))) concepts.Add("Concurrency");
        if (namespaces.Any(n => n!.Contains("System.IO"))) concepts.Add("File I/O");
        if (namespaces.Any(n => n!.Contains("System.Net"))) concepts.Add("Networking");
        if (namespaces.Any(n => n!.Contains("System.Data"))) concepts.Add("Data Access");
        
        return concepts;
    }

    private List<UsageExample> GenerateUsageExamples(SyntaxNode root)
    {
        var examples = new List<UsageExample>();
        var classes = root.DescendantNodes().OfType<ClassDeclarationSyntax>();
        
        foreach (var classDecl in classes.Take(3)) // Limit to first 3 classes
        {
            var className = classDecl.Identifier.ValueText;
            var publicMethods = classDecl.Members.OfType<MethodDeclarationSyntax>()
                .Where(m => m.Modifiers.Any(mod => mod.IsKind(SyntaxKind.PublicKeyword)))
                .Take(2);
                
            foreach (var method in publicMethods)
            {
                var paramList = string.Join(", ", method.ParameterList.Parameters.Select(p => $"{p.Type} {p.Identifier.ValueText}"));
                var codeExample = $"var instance = new {className}();\ninstance.{method.Identifier.ValueText}({paramList});";
                
                examples.Add(new UsageExample
                {
                    Title = $"Using {className}.{method.Identifier.ValueText}",
                    Description = $"Example usage of the {method.Identifier.ValueText} method",
                    Code = codeExample,
                    Context = "Method usage example"
                });
            }
        }
        
        return examples;
    }

    private string GetAccessModifier(SyntaxTokenList modifiers)
    {
        if (modifiers.Any(m => m.IsKind(SyntaxKind.PublicKeyword))) return "public";
        if (modifiers.Any(m => m.IsKind(SyntaxKind.PrivateKeyword))) return "private";
        if (modifiers.Any(m => m.IsKind(SyntaxKind.ProtectedKeyword))) return "protected";
        if (modifiers.Any(m => m.IsKind(SyntaxKind.InternalKeyword))) return "internal";
        return "internal"; // Default in C#
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
