using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using RoslynRuntime.Models;

namespace RoslynRuntime.Services;

public class PatternDetector
{
    public List<DesignPattern> DetectPatternsAsync(SyntaxNode root, string filePath)
    {
        var patterns = new List<DesignPattern>();

        // Detect various design patterns
        patterns.AddRange(DetectSingletonPattern(root, filePath));
        patterns.AddRange(DetectFactoryPattern(root, filePath));
        patterns.AddRange(DetectObserverPattern(root, filePath));
        patterns.AddRange(DetectStrategyPattern(root, filePath));
        patterns.AddRange(DetectDecoratorPattern(root, filePath));
        patterns.AddRange(DetectBuilderPattern(root, filePath));
        patterns.AddRange(DetectRepositoryPattern(root, filePath));
        patterns.AddRange(DetectMVCPattern(root, filePath));

        return patterns;
    }

    private List<DesignPattern> DetectSingletonPattern(SyntaxNode root, string filePath)
    {
        var patterns = new List<DesignPattern>();
        var classes = root.DescendantNodes().OfType<ClassDeclarationSyntax>();

        foreach (var classDecl in classes)
        {
            var confidence = 0.0;
            var evidence = new Dictionary<string, object>();
            var locations = new List<Models.Location>();

            // Check for private constructor
            var constructors = classDecl.Members.OfType<ConstructorDeclarationSyntax>();
            var hasPrivateConstructor = constructors.Any(c => 
                c.Modifiers.Any(m => m.IsKind(SyntaxKind.PrivateKeyword)));

            if (hasPrivateConstructor)
            {
                confidence += 0.3;
                evidence["HasPrivateConstructor"] = true;
            }

            // Check for static instance field/property
            var fields = classDecl.Members.OfType<FieldDeclarationSyntax>();
            var properties = classDecl.Members.OfType<PropertyDeclarationSyntax>();

            var hasStaticInstance = fields.Any(f => 
                f.Modifiers.Any(m => m.IsKind(SyntaxKind.StaticKeyword)) &&
                f.Declaration.Type.ToString().Contains(classDecl.Identifier.ValueText)) ||
                properties.Any(p => 
                    p.Modifiers.Any(m => m.IsKind(SyntaxKind.StaticKeyword)) &&
                    p.Type.ToString().Contains(classDecl.Identifier.ValueText));

            if (hasStaticInstance)
            {
                confidence += 0.4;
                evidence["HasStaticInstance"] = true;
            }

            // Check for getInstance method or similar
            var methods = classDecl.Members.OfType<MethodDeclarationSyntax>();
            var hasGetInstanceMethod = methods.Any(m => 
                m.Modifiers.Any(mod => mod.IsKind(SyntaxKind.StaticKeyword)) &&
                (m.Identifier.ValueText.ToLower().Contains("instance") || 
                 m.Identifier.ValueText.ToLower().Contains("getinstance")));

            if (hasGetInstanceMethod)
            {
                confidence += 0.3;
                evidence["HasGetInstanceMethod"] = true;
            }

            if (confidence >= 0.6)
            {
                locations.Add(GetLocation(classDecl, filePath));
                patterns.Add(new DesignPattern
                {
                    Name = "Singleton",
                    Description = $"Class '{classDecl.Identifier.ValueText}' appears to implement the Singleton pattern.",
                    Locations = locations,
                    Confidence = confidence,
                    Evidence = evidence
                });
            }
        }

        return patterns;
    }

    private List<DesignPattern> DetectFactoryPattern(SyntaxNode root, string filePath)
    {
        var patterns = new List<DesignPattern>();
        var classes = root.DescendantNodes().OfType<ClassDeclarationSyntax>();

        foreach (var classDecl in classes)
        {
            var className = classDecl.Identifier.ValueText;
            var confidence = 0.0;
            var evidence = new Dictionary<string, object>();

            // Check if class name contains "Factory"
            if (className.Contains("Factory"))
            {
                confidence += 0.4;
                evidence["NameContainsFactory"] = true;
            }

            // Check for creation methods
            var methods = classDecl.Members.OfType<MethodDeclarationSyntax>();
            var creationMethods = methods.Where(m => 
                m.Identifier.ValueText.ToLower().Contains("create") ||
                m.Identifier.ValueText.ToLower().Contains("make") ||
                m.Identifier.ValueText.ToLower().Contains("build") ||
                m.Identifier.ValueText.ToLower().Contains("new")).ToList();

            if (creationMethods.Any())
            {
                confidence += 0.3;
                evidence["HasCreationMethods"] = creationMethods.Count;
            }

            // Check if methods return different types (Abstract Factory)
            var returnTypes = creationMethods.Select(m => m.ReturnType.ToString()).Distinct().ToList();
            if (returnTypes.Count > 1)
            {
                confidence += 0.3;
                evidence["ReturnsMultipleTypes"] = returnTypes.Count;
            }

            if (confidence >= 0.6)
            {
                patterns.Add(new DesignPattern
                {
                    Name = returnTypes.Count > 1 ? "Abstract Factory" : "Factory Method",
                    Description = $"Class '{className}' appears to implement a Factory pattern.",
                    Locations = new List<Models.Location> { GetLocation(classDecl, filePath) },
                    Confidence = confidence,
                    Evidence = evidence
                });
            }
        }

        return patterns;
    }

    private List<DesignPattern> DetectObserverPattern(SyntaxNode root, string filePath)
    {
        var patterns = new List<DesignPattern>();
        var classes = root.DescendantNodes().OfType<ClassDeclarationSyntax>();

        foreach (var classDecl in classes)
        {
            var confidence = 0.0;
            var evidence = new Dictionary<string, object>();

            // Check for event declarations
            var events = classDecl.Members.OfType<EventFieldDeclarationSyntax>();
            if (events.Any())
            {
                confidence += 0.5;
                evidence["HasEvents"] = events.Count();
            }

            // Check for observer-like methods
            var methods = classDecl.Members.OfType<MethodDeclarationSyntax>();
            var observerMethods = methods.Where(m => 
                m.Identifier.ValueText.ToLower().Contains("notify") ||
                m.Identifier.ValueText.ToLower().Contains("update") ||
                m.Identifier.ValueText.ToLower().Contains("subscribe") ||
                m.Identifier.ValueText.ToLower().Contains("unsubscribe")).ToList();

            if (observerMethods.Any())
            {
                confidence += 0.3;
                evidence["HasObserverMethods"] = observerMethods.Count;
            }

            // Check for collection of observers
            var fields = classDecl.Members.OfType<FieldDeclarationSyntax>();
            var observerCollections = fields.Where(f => 
                f.Declaration.Type.ToString().Contains("List") ||
                f.Declaration.Type.ToString().Contains("Collection") ||
                f.Declaration.Type.ToString().Contains("Observer")).ToList();

            if (observerCollections.Any())
            {
                confidence += 0.2;
                evidence["HasObserverCollections"] = observerCollections.Count;
            }

            if (confidence >= 0.6)
            {
                patterns.Add(new DesignPattern
                {
                    Name = "Observer",
                    Description = $"Class '{classDecl.Identifier.ValueText}' appears to implement the Observer pattern.",
                    Locations = new List<Models.Location> { GetLocation(classDecl, filePath) },
                    Confidence = confidence,
                    Evidence = evidence
                });
            }
        }

        return patterns;
    }

    private List<DesignPattern> DetectStrategyPattern(SyntaxNode root, string filePath)
    {
        var patterns = new List<DesignPattern>();
        var interfaces = root.DescendantNodes().OfType<InterfaceDeclarationSyntax>();

        foreach (var interfaceDecl in interfaces)
        {
            var interfaceName = interfaceDecl.Identifier.ValueText;
            var confidence = 0.0;
            var evidence = new Dictionary<string, object>();

            // Check if interface name suggests strategy
            if (interfaceName.Contains("Strategy") || interfaceName.Contains("Algorithm"))
            {
                confidence += 0.4;
                evidence["NameSuggestsStrategy"] = true;
            }

            // Find classes implementing this interface
            var classes = root.DescendantNodes().OfType<ClassDeclarationSyntax>();
            var implementingClasses = classes.Where(c => 
                c.BaseList?.Types.Any(t => t.Type.ToString() == interfaceName) == true).ToList();

            if (implementingClasses.Count >= 2)
            {
                confidence += 0.4;
                evidence["MultipleImplementations"] = implementingClasses.Count;
            }

            // Check for context class that uses the strategy
            var contextClasses = classes.Where(c => 
                c.Members.OfType<FieldDeclarationSyntax>().Any(f => 
                    f.Declaration.Type.ToString() == interfaceName) ||
                c.Members.OfType<PropertyDeclarationSyntax>().Any(p => 
                    p.Type.ToString() == interfaceName)).ToList();

            if (contextClasses.Any())
            {
                confidence += 0.2;
                evidence["HasContextClasses"] = contextClasses.Count;
            }

            if (confidence >= 0.6)
            {
                var locations = new List<Models.Location> { GetLocation(interfaceDecl, filePath) };
                locations.AddRange(implementingClasses.Select(c => GetLocation(c, filePath)));

                patterns.Add(new DesignPattern
                {
                    Name = "Strategy",
                    Description = $"Interface '{interfaceName}' and its implementations appear to follow the Strategy pattern.",
                    Locations = locations,
                    Confidence = confidence,
                    Evidence = evidence
                });
            }
        }

        return patterns;
    }

    private List<DesignPattern> DetectDecoratorPattern(SyntaxNode root, string filePath)
    {
        var patterns = new List<DesignPattern>();
        var classes = root.DescendantNodes().OfType<ClassDeclarationSyntax>();

        foreach (var classDecl in classes)
        {
            var className = classDecl.Identifier.ValueText;
            var confidence = 0.0;
            var evidence = new Dictionary<string, object>();

            // Check if class name contains "Decorator"
            if (className.Contains("Decorator"))
            {
                confidence += 0.3;
                evidence["NameContainsDecorator"] = true;
            }

            // Check for composition (has a field of the same interface it implements)
            var baseTypes = classDecl.BaseList?.Types.Select(t => t.Type.ToString()).ToList() ?? new List<string>();
            var fields = classDecl.Members.OfType<FieldDeclarationSyntax>();

            foreach (var baseType in baseTypes)
            {
                if (fields.Any(f => f.Declaration.Type.ToString() == baseType))
                {
                    confidence += 0.4;
                    evidence["HasComposition"] = true;
                    break;
                }
            }

            // Check for constructor that takes the component
            var constructors = classDecl.Members.OfType<ConstructorDeclarationSyntax>();
            foreach (var constructor in constructors)
            {
                foreach (var baseType in baseTypes)
                {
                    if (constructor.ParameterList.Parameters.Any(p => p.Type?.ToString() == baseType))
                    {
                        confidence += 0.3;
                        evidence["ConstructorTakesComponent"] = true;
                        break;
                    }
                }
            }

            if (confidence >= 0.6)
            {
                patterns.Add(new DesignPattern
                {
                    Name = "Decorator",
                    Description = $"Class '{className}' appears to implement the Decorator pattern.",
                    Locations = new List<Models.Location> { GetLocation(classDecl, filePath) },
                    Confidence = confidence,
                    Evidence = evidence
                });
            }
        }

        return patterns;
    }

    private List<DesignPattern> DetectBuilderPattern(SyntaxNode root, string filePath)
    {
        var patterns = new List<DesignPattern>();
        var classes = root.DescendantNodes().OfType<ClassDeclarationSyntax>();

        foreach (var classDecl in classes)
        {
            var className = classDecl.Identifier.ValueText;
            var confidence = 0.0;
            var evidence = new Dictionary<string, object>();

            // Check if class name contains "Builder"
            if (className.Contains("Builder"))
            {
                confidence += 0.4;
                evidence["NameContainsBuilder"] = true;
            }

            // Check for fluent interface methods (methods returning 'this' or the builder type)
            var methods = classDecl.Members.OfType<MethodDeclarationSyntax>();
            var fluentMethods = methods.Where(m => 
                m.ReturnType.ToString() == className ||
                m.ReturnType.ToString() == "this" ||
                m.Body?.Statements.OfType<ReturnStatementSyntax>().Any(r => 
                    r.Expression?.ToString() == "this") == true).ToList();

            if (fluentMethods.Count >= 2)
            {
                confidence += 0.3;
                evidence["HasFluentMethods"] = fluentMethods.Count;
            }

            // Check for Build method
            var buildMethod = methods.FirstOrDefault(m => 
                m.Identifier.ValueText.ToLower() == "build" ||
                m.Identifier.ValueText.ToLower() == "create");

            if (buildMethod != null)
            {
                confidence += 0.3;
                evidence["HasBuildMethod"] = true;
            }

            if (confidence >= 0.6)
            {
                patterns.Add(new DesignPattern
                {
                    Name = "Builder",
                    Description = $"Class '{className}' appears to implement the Builder pattern.",
                    Locations = new List<Models.Location> { GetLocation(classDecl, filePath) },
                    Confidence = confidence,
                    Evidence = evidence
                });
            }
        }

        return patterns;
    }

    private List<DesignPattern> DetectRepositoryPattern(SyntaxNode root, string filePath)
    {
        var patterns = new List<DesignPattern>();
        var classes = root.DescendantNodes().OfType<ClassDeclarationSyntax>();
        var interfaces = root.DescendantNodes().OfType<InterfaceDeclarationSyntax>();

        var repositoryTypes = classes.Concat<SyntaxNode>(interfaces);

        foreach (var type in repositoryTypes)
        {
            var typeName = GetTypeName(type);
            var confidence = 0.0;
            var evidence = new Dictionary<string, object>();

            // Check if name contains "Repository"
            if (typeName.Contains("Repository"))
            {
                confidence += 0.5;
                evidence["NameContainsRepository"] = true;
            }

            // Check for CRUD methods
            var methods = type.DescendantNodes().OfType<MethodDeclarationSyntax>();
            var crudMethods = methods.Where(m => 
                m.Identifier.ValueText.ToLower().Contains("get") ||
                m.Identifier.ValueText.ToLower().Contains("find") ||
                m.Identifier.ValueText.ToLower().Contains("add") ||
                m.Identifier.ValueText.ToLower().Contains("create") ||
                m.Identifier.ValueText.ToLower().Contains("update") ||
                m.Identifier.ValueText.ToLower().Contains("delete") ||
                m.Identifier.ValueText.ToLower().Contains("remove") ||
                m.Identifier.ValueText.ToLower().Contains("save")).ToList();

            if (crudMethods.Count >= 3)
            {
                confidence += 0.3;
                evidence["HasCRUDMethods"] = crudMethods.Count;
            }

            // Check for async methods (common in repositories)
            var asyncMethods = methods.Where(m => 
                m.Modifiers.Any(mod => mod.IsKind(SyntaxKind.AsyncKeyword)) ||
                m.ReturnType.ToString().Contains("Task")).ToList();

            if (asyncMethods.Any())
            {
                confidence += 0.2;
                evidence["HasAsyncMethods"] = asyncMethods.Count;
            }

            if (confidence >= 0.6)
            {
                patterns.Add(new DesignPattern
                {
                    Name = "Repository",
                    Description = $"{(type is ClassDeclarationSyntax ? "Class" : "Interface")} '{typeName}' appears to implement the Repository pattern.",
                    Locations = new List<Models.Location> { GetLocation(type, filePath) },
                    Confidence = confidence,
                    Evidence = evidence
                });
            }
        }

        return patterns;
    }

    private List<DesignPattern> DetectMVCPattern(SyntaxNode root, string filePath)
    {
        var patterns = new List<DesignPattern>();
        var classes = root.DescendantNodes().OfType<ClassDeclarationSyntax>();

        var controllers = classes.Where(c => c.Identifier.ValueText.EndsWith("Controller")).ToList();
        var models = classes.Where(c => c.Identifier.ValueText.EndsWith("Model") || 
                                       c.Identifier.ValueText.EndsWith("Entity")).ToList();
        var views = classes.Where(c => c.Identifier.ValueText.EndsWith("View") || 
                                      c.Identifier.ValueText.EndsWith("ViewModel")).ToList();

        if (controllers.Any() && models.Any())
        {
            var confidence = 0.6;
            var evidence = new Dictionary<string, object>
            {
                ["ControllerCount"] = controllers.Count,
                ["ModelCount"] = models.Count,
                ["ViewCount"] = views.Count
            };

            if (views.Any())
            {
                confidence += 0.2;
            }

            var locations = new List<Models.Location>();
            locations.AddRange(controllers.Select(c => GetLocation(c, filePath)));
            locations.AddRange(models.Select(m => GetLocation(m, filePath)));
            locations.AddRange(views.Select(v => GetLocation(v, filePath)));

            patterns.Add(new DesignPattern
            {
                Name = "Model-View-Controller (MVC)",
                Description = "The code structure suggests an MVC architectural pattern.",
                Locations = locations,
                Confidence = confidence,
                Evidence = evidence
            });
        }

        return patterns;
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
