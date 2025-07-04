using System.Collections.Immutable;
using System.ComponentModel;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.FindSymbols;
using Microsoft.CodeAnalysis.MSBuild;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using RoslynMCP.Models;
using RoslynMCP.Services;

class Program
{
    static async Task Main(string[] args)
    {
        var builder = Host.CreateApplicationBuilder(args);
        builder.Logging.AddConsole(consoleLogOptions =>
        {
            // Configure all logs to go to stderr
            consoleLogOptions.LogToStandardErrorThreshold = LogLevel.Trace;
        });
        builder.Services
            .AddMcpServer()
            .WithStdioServerTransport()
            .WithToolsFromAssembly();
        await builder.Build().RunAsync();
    }

    public static async Task<string> FindContainingProjectAsync(string filePath)
    {
        // Start from the directory containing the file and go up until we find a .csproj file
        DirectoryInfo directory = new FileInfo(filePath).Directory;

        while (directory != null)
        {
            var projectFiles = directory.GetFiles("*.csproj");
            if (projectFiles.Length > 0)
            {
                // Create MSBuildWorkspace with proper configuration
                var properties = new Dictionary<string, string>
                {
                    // Set MSBuild property to help locate the SDK
                    { "AlwaysUseNETSdkDefaults", "true" },
                    // Add any other properties needed
                    { "DesignTimeBuild", "true" }
                };

                var workspace = MSBuildWorkspace.Create(properties);

                // Ensure C# language services are registered
                EnsureCSharpLanguageServicesRegistered(workspace);

                // Add event handler for workspace failures
                workspace.WorkspaceFailed += (sender, args) =>
                {
                    Console.WriteLine($"Workspace warning: {args.Diagnostic.Message}");
                };

                try
                {
                    var project = await workspace.OpenProjectAsync(projectFiles[0].FullName);

                    var documents = project.Documents
                        .Where(d => string.Equals(d.FilePath, filePath, StringComparison.OrdinalIgnoreCase))
                        .ToList();

                    if (documents.Any())
                    {
                        return projectFiles[0].FullName;
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error opening project: {ex.Message}");
                    if (ex.InnerException != null)
                    {
                        Console.WriteLine($"Inner exception: {ex.InnerException.Message}");
                    }
                }
            }

            directory = directory.Parent;
        }

        return string.Empty;
    }

    public static async Task ValidateFileInProjectContextAsync(string filePath, string projectPath,
        TextWriter writer = null, bool runAnalyzers = true)
    {
        // Use the provided TextWriter or fallback to Console.Out
        writer ??= Console.Out;

        try
        {
            // Create MSBuildWorkspace with proper configuration
            var properties = new Dictionary<string, string>
            {
                // Set MSBuild property to help locate the SDK
                { "AlwaysUseNETSdkDefaults", "true" },
                // Make sure language services are properly registered
                { "DesignTimeBuild", "true" }
            };

            var workspace = MSBuildWorkspace.Create(properties);

            // Ensure C# language services are registered
            EnsureCSharpLanguageServicesRegistered(workspace);

            // Add event handler for workspace failures
            workspace.WorkspaceFailed += (sender, args) =>
            {
                writer.WriteLine($"Workspace warning: {args.Diagnostic.Message}");
            };

            // Load the project
            writer.WriteLine($"Loading project: {projectPath}");
            var project = await workspace.OpenProjectAsync(projectPath);
            writer.WriteLine($"Project loaded successfully: {project.Name}");

            // Find the document in the project
            var document = project.Documents
                .FirstOrDefault(d => string.Equals(d.FilePath, filePath, StringComparison.OrdinalIgnoreCase));

            if (document == null)
            {
                writer.WriteLine("Error: File not found in the project documents.");
                writer.WriteLine("All project documents:");
                foreach (var doc in project.Documents)
                {
                    writer.WriteLine($"  - {doc.FilePath}");
                }

                return;
            }

            writer.WriteLine($"Document found: {document.Name}");

            // Parse syntax tree
            var syntaxTree = await document.GetSyntaxTreeAsync();
            var syntaxDiagnostics = syntaxTree.GetDiagnostics();

            if (syntaxDiagnostics.Any())
            {
                writer.WriteLine("Syntax errors found:");
                foreach (var diagnostic in syntaxDiagnostics)
                {
                    var location = diagnostic.Location.GetLineSpan();
                    writer.WriteLine($"Line {location.StartLinePosition.Line + 1}: {diagnostic.GetMessage()}");
                }
            }
            else
            {
                writer.WriteLine("No syntax errors found.");
            }

            // Get the semantic model for deeper analysis
            var semanticModel = await document.GetSemanticModelAsync();
            var semanticDiagnostics = semanticModel.GetDiagnostics();

            if (semanticDiagnostics.Any())
            {
                writer.WriteLine("\nSemantic errors found:");
                foreach (var diagnostic in semanticDiagnostics)
                {
                    var location = diagnostic.Location.GetLineSpan();
                    writer.WriteLine($"Line {location.StartLinePosition.Line + 1}: {diagnostic.GetMessage()}");
                }
            }
            else
            {
                writer.WriteLine("No semantic errors found.");
            }

            // Check compilation for the entire project to validate references
            var compilation = await project.GetCompilationAsync();
            
            // Get compilation diagnostics for the file
            var compilationDiagnostics = compilation.GetDiagnostics()
                .Where(d => d.Location.SourceTree != null &&
                            string.Equals(d.Location.SourceTree.FilePath, filePath,
                                StringComparison.OrdinalIgnoreCase))
                .ToList();
            
            // If analyzers are requested, run them and get their diagnostics
            IEnumerable<Diagnostic> analyzerDiagnostics = Array.Empty<Diagnostic>();
            if (runAnalyzers)
            {
                writer.WriteLine("\nRunning code analyzers...");
                
                try
                {
                    // Get the analyzer assembly paths
                    var analyzerAssemblies = new List<string>();
                    
                    // Try to find the analyzer assemblies in the NuGet packages
                    var nugetPackagesPath = Environment.GetEnvironmentVariable("NUGET_PACKAGES") 
                        ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nuget", "packages");
                    
                    // Microsoft.CodeAnalysis.NetAnalyzers
                    var netAnalyzersPath = Path.Combine(nugetPackagesPath, "microsoft.codeanalysis.analyzers", "3.11.0", 
                        "analyzers", "dotnet", "cs", "Microsoft.CodeAnalysis.Analyzers.dll");
                    if (File.Exists(netAnalyzersPath))
                        analyzerAssemblies.Add(netAnalyzersPath);
                    
                    var csharpAnalyzersPath = Path.Combine(nugetPackagesPath, "microsoft.codeanalysis.analyzers", "3.11.0", 
                        "analyzers", "dotnet", "cs", "Microsoft.CodeAnalysis.CSharp.Analyzers.dll");
                    if (File.Exists(csharpAnalyzersPath))
                        analyzerAssemblies.Add(csharpAnalyzersPath);
                    
                    // Load the analyzers
                    var analyzers = new List<DiagnosticAnalyzer>();
                    foreach (var analyzerPath in analyzerAssemblies)
                    {
                        try
                        {
                            var analyzerAssembly = Assembly.LoadFrom(analyzerPath);
                            var analyzerTypes = analyzerAssembly.GetTypes()
                                .Where(t => !t.IsAbstract && typeof(DiagnosticAnalyzer).IsAssignableFrom(t));
                            
                            foreach (var analyzerType in analyzerTypes)
                            {
                                try
                                {
                                    var analyzer = (DiagnosticAnalyzer)Activator.CreateInstance(analyzerType);
                                    analyzers.Add(analyzer);
                                }
                                catch (Exception ex)
                                {
                                    Console.Error.WriteLine($"Error creating analyzer instance: {ex.Message}");
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            Console.Error.WriteLine($"Error loading analyzer assembly: {ex.Message}");
                        }
                    }
                    
                    // If no analyzers were found in the NuGet packages, try to use the ones that are already loaded
                    if (!analyzers.Any())
                    {
                        var loadedAssemblies = AppDomain.CurrentDomain.GetAssemblies();
                        foreach (var assembly in loadedAssemblies)
                        {
                            try
                            {
                                if (assembly.FullName.Contains("Microsoft.CodeAnalysis") && assembly.FullName.Contains("Analyzers"))
                                {
                                    var analyzerTypes = assembly.GetTypes()
                                        .Where(t => !t.IsAbstract && typeof(DiagnosticAnalyzer).IsAssignableFrom(t));
                                    
                                    foreach (var analyzerType in analyzerTypes)
                                    {
                                        try
                                        {
                                            var analyzer = (DiagnosticAnalyzer)Activator.CreateInstance(analyzerType);
                                            analyzers.Add(analyzer);
                                        }
                                        catch (Exception ex)
                                        {
                                            Console.Error.WriteLine($"Error creating analyzer instance: {ex.Message}");
                                        }
                                    }
                                }
                            }
                            catch (Exception)
                            {
                                // Ignore errors when trying to get types from dynamic assemblies
                            }
                        }
                    }
                    
                    // Add the analyzers to the compilation
                    if (analyzers.Any())
                    {
                        writer.WriteLine($"Found {analyzers.Count} analyzers");
                        
                        // Create a CompilationWithAnalyzers object
                        var compilationWithAnalyzers = compilation.WithAnalyzers(
                            ImmutableArray.CreateRange(analyzers));
                        
                        // Get the diagnostics from the analyzers
                        var allAnalyzerDiagnostics = await compilationWithAnalyzers.GetAnalyzerDiagnosticsAsync();
                        
                        // Filter to only include diagnostics for the current file
                        analyzerDiagnostics = allAnalyzerDiagnostics.Where(d => 
                            d.Location.SourceTree != null &&
                            string.Equals(d.Location.SourceTree.FilePath, filePath, StringComparison.OrdinalIgnoreCase));
                    }
                    else
                    {
                        writer.WriteLine("No analyzers found");
                    }
                }
                catch (Exception ex)
                {
                    writer.WriteLine($"Error running analyzers: {ex.Message}");
                }
            }
            
            // Combine compilation diagnostics and analyzer diagnostics
            var allDiagnostics = compilationDiagnostics.Concat(analyzerDiagnostics);

            if (allDiagnostics.Any())
            {
                writer.WriteLine("\nCompilation and analyzer diagnostics:");
                foreach (var diagnostic in allDiagnostics.OrderBy(d => d.Severity))
                {
                    var location = diagnostic.Location.GetLineSpan();
                    var severity = diagnostic.Severity.ToString();
                    writer.WriteLine($"[{severity}] Line {location.StartLinePosition.Line + 1}: {diagnostic.Id} - {diagnostic.GetMessage()}");
                }
            }
            else
            {
                writer.WriteLine("File compiles successfully in project context with no analyzer warnings.");
            }
        }
        catch (Exception ex)
        {
            writer.WriteLine($"Error validating file: {ex.Message}");
            if (ex.InnerException != null)
            {
                writer.WriteLine($"Inner exception: {ex.InnerException.Message}");
            }

            writer.WriteLine($"Stack trace: {ex.StackTrace}");
        }
    }

    /// <summary>
    /// Ensures that C# language services are properly registered with the workspace
    /// </summary>
    public static void EnsureCSharpLanguageServicesRegistered(MSBuildWorkspace workspace)
    {
        try
        {
            // First try to register all language services using reflection
            Type msbuildWorkspaceType = typeof(MSBuildWorkspace);
            var registerMethod = msbuildWorkspaceType.GetMethod("RegisterLanguageServices",
                BindingFlags.NonPublic | BindingFlags.Instance);

            registerMethod?.Invoke(workspace, null);

            // Explicitly register C# language service if available
            // This ensures the C# language is supported
            var languageServicesField = msbuildWorkspaceType.GetField("_languageServices",
                BindingFlags.NonPublic | BindingFlags.Instance);

            if (languageServicesField != null)
            {
                var languageServices = languageServicesField.GetValue(workspace);
                var languageServicesType = languageServices.GetType();

                // Try to find the method to register a language service
                var registerLanguageServiceMethod = languageServicesType
                    .GetMethods(BindingFlags.Public | BindingFlags.Instance)
                    .FirstOrDefault(m => m.Name == "Register" && m.GetParameters().Length >= 1);

                if (registerLanguageServiceMethod != null)
                {
                    // Get the CSharpLanguageService type
                    var csharpLanguageServiceType =
                        Type.GetType(
                            "Microsoft.CodeAnalysis.CSharp.CSharpLanguageService, Microsoft.CodeAnalysis.CSharp");
                    if (csharpLanguageServiceType != null)
                    {
                        // Create an instance of the CSharpLanguageService
                        var csharpLanguageService = Activator.CreateInstance(csharpLanguageServiceType);

                        // Register it with the language services
                        registerLanguageServiceMethod.Invoke(languageServices, new[] { csharpLanguageService });
                        Console.WriteLine("Successfully registered C# language service explicitly");
                    }
                }
            }

            // Force load the CSharp assembly to ensure its language services are available
            RuntimeHelpers.RunClassConstructor(typeof(CSharpSyntaxTree).TypeHandle);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Warning: Error while registering language services: {ex.Message}");
            // Continue execution as the standard registration might still work
        }
    }

    /// <summary>
    /// Adds Microsoft recommended analyzers to the compilation
    /// </summary>
    private static Compilation AddAnalyzersToCompilation(Compilation compilation)
    {
        try
        {
            // Get the analyzer assembly paths
            var analyzerAssemblies = new List<string>();
            
            // Try to find the analyzer assemblies in the NuGet packages
            var nugetPackagesPath = Environment.GetEnvironmentVariable("NUGET_PACKAGES") 
                ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nuget", "packages");
            
            // Microsoft.CodeAnalysis.NetAnalyzers
            var netAnalyzersPath = Path.Combine(nugetPackagesPath, "microsoft.codeanalysis.analyzers", "3.11.0", 
                "analyzers", "dotnet", "cs", "Microsoft.CodeAnalysis.Analyzers.dll");
            if (File.Exists(netAnalyzersPath))
                analyzerAssemblies.Add(netAnalyzersPath);
            
            var csharpAnalyzersPath = Path.Combine(nugetPackagesPath, "microsoft.codeanalysis.analyzers", "3.11.0", 
                "analyzers", "dotnet", "cs", "Microsoft.CodeAnalysis.CSharp.Analyzers.dll");
            if (File.Exists(csharpAnalyzersPath))
                analyzerAssemblies.Add(csharpAnalyzersPath);
            
            // Load the analyzers
            var analyzers = new List<DiagnosticAnalyzer>();
            foreach (var analyzerPath in analyzerAssemblies)
            {
                try
                {
                    var analyzerAssembly = Assembly.LoadFrom(analyzerPath);
                    var analyzerTypes = analyzerAssembly.GetTypes()
                        .Where(t => !t.IsAbstract && typeof(DiagnosticAnalyzer).IsAssignableFrom(t));
                    
                    foreach (var analyzerType in analyzerTypes)
                    {
                        try
                        {
                            var analyzer = (DiagnosticAnalyzer)Activator.CreateInstance(analyzerType);
                            analyzers.Add(analyzer);
                        }
                        catch (Exception ex)
                        {
                            Console.Error.WriteLine($"Error creating analyzer instance: {ex.Message}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"Error loading analyzer assembly: {ex.Message}");
                }
            }
            
            // If no analyzers were found in the NuGet packages, try to use the ones that are already loaded
            if (!analyzers.Any())
            {
                var loadedAssemblies = AppDomain.CurrentDomain.GetAssemblies();
                foreach (var assembly in loadedAssemblies)
                {
                    try
                    {
                        if (assembly.FullName.Contains("Microsoft.CodeAnalysis") && assembly.FullName.Contains("Analyzers"))
                        {
                            var analyzerTypes = assembly.GetTypes()
                                .Where(t => !t.IsAbstract && typeof(DiagnosticAnalyzer).IsAssignableFrom(t));
                            
                            foreach (var analyzerType in analyzerTypes)
                            {
                                try
                                {
                                    var analyzer = (DiagnosticAnalyzer)Activator.CreateInstance(analyzerType);
                                    analyzers.Add(analyzer);
                                }
                                catch (Exception ex)
                                {
                                    Console.Error.WriteLine($"Error creating analyzer instance: {ex.Message}");
                                }
                            }
                        }
                    }
                    catch (Exception)
                    {
                        // Ignore errors when trying to get types from dynamic assemblies
                    }
                }
            }
            
            // Add the analyzers to the compilation
            if (analyzers.Any())
            {
                Console.Error.WriteLine($"Added {analyzers.Count} analyzers to compilation");
                
                // Create a CompilationWithAnalyzers object
                var compilationWithAnalyzers = compilation.WithAnalyzers(
                    ImmutableArray.CreateRange(analyzers));
                
                // Get the diagnostics from the analyzers
                var analyzerDiagnostics = compilationWithAnalyzers.GetAnalyzerDiagnosticsAsync().Result;
                
                // Add the analyzer diagnostics to the compilation diagnostics
                var allDiagnostics = compilation.GetDiagnostics()
                    .Concat(analyzerDiagnostics)
                    .ToImmutableArray();
                
                // Return the original compilation (we've already extracted the diagnostics)
                return compilation;
            }
            
            Console.Error.WriteLine("No analyzers found");
            return compilation;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error adding analyzers: {ex.Message}");
            return compilation;
        }
    }
}


[McpServerToolType]
public static class RoslynTools
{
    [McpServerTool, Description("Validates a C# file using Roslyn and runs code analyzers. Accepts either a relative or absolute file path.")]
    public static async Task<string> ValidateFile(
        [Description("The path to the C# file to validate")] string filePath,
        [Description("Run analyzers (default: true)")] bool runAnalyzers = true)
    {
        try
        {
            // Log the received file path for debugging
            Console.Error.WriteLine($"ValidateFile called with path: '{filePath}'");
            Console.Error.WriteLine($"Path type: {filePath?.GetType().FullName ?? "null"}");
            Console.Error.WriteLine($"Run analyzers: {runAnalyzers}");

            // Check if the input is null or empty
            if (string.IsNullOrWhiteSpace(filePath))
            {
                Console.Error.WriteLine("File path is null or empty");
                return "Error: File path cannot be empty.";
            }

            // Handle Windows paths with backslashes
            // First, escape any backslashes for JSON serialization
            string normalizedPath = filePath.Replace("\\", "/");
            Console.Error.WriteLine($"Normalized path (for JSON): '{normalizedPath}'");

            // Resolve relative paths
            string fullPath;
            if (!Path.IsPathRooted(normalizedPath))
            {
                fullPath = Path.GetFullPath(normalizedPath);
                Console.Error.WriteLine($"Resolved relative path to: '{fullPath}'");
            }
            else
            {
                fullPath = normalizedPath;
            }

            // Ensure the path is in the correct format for file system operations
            string systemPath = Path.GetFullPath(fullPath);
            Console.Error.WriteLine($"System path for file operations: '{systemPath}'");

            // Check if the file exists
            if (!File.Exists(systemPath))
            {
                Console.Error.WriteLine($"File does not exist: '{systemPath}'");
                return $"Error: File {systemPath} does not exist.";
            }

            Console.Error.WriteLine($"File exists: '{systemPath}'");

            // Find the containing project
            Console.Error.WriteLine("Searching for containing project...");
            string projectPath = await Program.FindContainingProjectAsync(systemPath);
            if (string.IsNullOrEmpty(projectPath))
            {
                Console.Error.WriteLine("Could not find a project containing this file");
                return "Error: Couldn't find a project containing this file.";
            }

            Console.Error.WriteLine($"Found containing project: '{projectPath}'");

            // Use a StringWriter to capture the output
            Console.Error.WriteLine("Validating file in project context...");
            var outputWriter = new StringWriter();
            await Program.ValidateFileInProjectContextAsync(systemPath, projectPath, outputWriter, runAnalyzers);
            string result = outputWriter.ToString();
            Console.Error.WriteLine("Validation complete");

            return result;
        }
        catch (Exception ex)
        {
            // Log the exception for debugging
            Console.Error.WriteLine($"ERROR in ValidateFile: {ex.Message}");
            Console.Error.WriteLine($"Exception type: {ex.GetType().FullName}");
            Console.Error.WriteLine($"Stack trace: {ex.StackTrace}");
            if (ex.InnerException != null)
            {
                Console.Error.WriteLine($"Inner exception: {ex.InnerException.Message}");
                Console.Error.WriteLine($"Inner exception type: {ex.InnerException.GetType().FullName}");
                Console.Error.WriteLine($"Inner stack trace: {ex.InnerException.StackTrace}");
            }

            return $"Error processing file: {ex.Message}";
        }
    }

    [McpServerTool, Description("Extract comprehensive metadata from a .NET project including types, members, namespaces, and dependencies. Returns structured JSON data suitable for embedding and semantic search.")]
    public static async Task<string> ExtractProjectMetadata(
        [Description("Path to the .csproj file or a file within the project")] string projectPath)
    {
        try
        {
            Console.Error.WriteLine($"ExtractProjectMetadata called with path: '{projectPath}'");

            // Check if the input is null or empty
            if (string.IsNullOrWhiteSpace(projectPath))
            {
                Console.Error.WriteLine("Project path is null or empty");
                return "Error: Project path cannot be empty.";
            }

            // Normalize the path
            string normalizedPath = projectPath.Replace("\\", "/");
            string systemPath = !Path.IsPathRooted(normalizedPath)
                ? Path.GetFullPath(normalizedPath)
                : Path.GetFullPath(normalizedPath);

            Console.Error.WriteLine($"System path: '{systemPath}'");

            // Determine if this is a project file or a source file
            string actualProjectPath;
            if (systemPath.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
            {
                // It's already a project file
                if (!File.Exists(systemPath))
                {
                    return $"Error: Project file {systemPath} does not exist.";
                }
                actualProjectPath = systemPath;
            }
            else
            {
                // It might be a source file, find the containing project
                Console.Error.WriteLine("Searching for containing project...");
                actualProjectPath = await Program.FindContainingProjectAsync(systemPath);
                if (string.IsNullOrEmpty(actualProjectPath))
                {
                    return "Error: Couldn't find a project file. Please provide a .csproj file path or a file within a project.";
                }
            }

            Console.Error.WriteLine($"Using project file: '{actualProjectPath}'");

            // Create workspace and extractor
            var workspace = CreateWorkspace();
            var extractor = new ProjectMetadataExtractor(workspace);

            // Extract metadata
            Console.Error.WriteLine("Extracting project metadata...");
            var metadata = await extractor.ExtractAsync(actualProjectPath);

            // Serialize to JSON with proper formatting
            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
            };

            var jsonResult = JsonSerializer.Serialize(metadata, options);
            Console.Error.WriteLine("Metadata extraction complete");

            return jsonResult;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"ERROR in ExtractProjectMetadata: {ex.Message}");
            Console.Error.WriteLine($"Exception type: {ex.GetType().FullName}");
            Console.Error.WriteLine($"Stack trace: {ex.StackTrace}");
            if (ex.InnerException != null)
            {
                Console.Error.WriteLine($"Inner exception: {ex.InnerException.Message}");
                Console.Error.WriteLine($"Inner stack trace: {ex.InnerException.StackTrace}");
            }

            return $"Error extracting project metadata: {ex.Message}";
        }
    }

    [McpServerTool, Description("Find all references to a symbol at the specified position.")]
    public static async Task<string> FindUsages(
        [Description("Path to the file")] string filePath,
        [Description("Line number (1-based)")] int line,
        [Description("Column number (1-based)")]
        int column)
    {
        try
        {
            // Normalize file path
            string normalizedPath = filePath.Replace("\\", "/");
            string systemPath = !Path.IsPathRooted(normalizedPath)
                ? Path.GetFullPath(normalizedPath)
                : Path.GetFullPath(normalizedPath);

            string projectPath = await Program.FindContainingProjectAsync(systemPath);
            if (string.IsNullOrEmpty(projectPath))
            {
                return "Error: Couldn't find a project containing this file.";
            }

            var workspace = CreateWorkspace();
            var project = await workspace.OpenProjectAsync(projectPath);
            var document = project.Documents.FirstOrDefault(d =>
                string.Equals(d.FilePath, systemPath, StringComparison.OrdinalIgnoreCase));

            if (document == null) return "Error: File not found in project.";

            var sourceText = await document.GetTextAsync();
            var position = sourceText.Lines[line - 1].Start + (column - 1);

            var semanticModel = await document.GetSemanticModelAsync();
            var symbol = await SymbolFinder.FindSymbolAtPositionAsync(semanticModel, position, workspace);

            if (symbol == null) return "No symbol found at specified position.";

            var references = await SymbolFinder.FindReferencesAsync(symbol, project.Solution);

            var results = new StringBuilder();

            // Add a header with search metadata
            results.AppendLine("# Symbol Usage Analysis");
            results.AppendLine();
            results.AppendLine("## Search Information");
            results.AppendLine($"- **File**: {systemPath}");
            results.AppendLine($"- **Position**: Line {line}, Column {column}");
            results.AppendLine($"- **Project**: {Path.GetFileName(projectPath)}");
            results.AppendLine();

            // Add detailed symbol information
            results.AppendLine("## Symbol Details");
            results.AppendLine($"- **Name**: {symbol.Name}");
            results.AppendLine($"- **Kind**: {symbol.Kind}");
            results.AppendLine($"- **Full Name**: {symbol.ToDisplayString()}");

            // Add containing type and namespace information if available
            if (symbol.ContainingType != null)
            {
                results.AppendLine($"- **Containing Type**: {symbol.ContainingType.ToDisplayString()}");
            }

            if (symbol.ContainingNamespace != null && !symbol.ContainingNamespace.IsGlobalNamespace)
            {
                results.AppendLine($"- **Namespace**: {symbol.ContainingNamespace.ToDisplayString()}");
            }

            // Add accessibility information
            results.AppendLine($"- **Accessibility**: {symbol.DeclaredAccessibility}");

            // Add additional symbol-specific information based on its kind
            switch (symbol.Kind)
            {
                case SymbolKind.Method:
                    var methodSymbol = (IMethodSymbol)symbol;
                    results.AppendLine($"- **Return Type**: {methodSymbol.ReturnType.ToDisplayString()}");
                    results.AppendLine($"- **Is Extension Method**: {methodSymbol.IsExtensionMethod}");
                    results.AppendLine($"- **Parameter Count**: {methodSymbol.Parameters.Length}");
                    break;
                case SymbolKind.Property:
                    var propertySymbol = (IPropertySymbol)symbol;
                    results.AppendLine($"- **Property Type**: {propertySymbol.Type.ToDisplayString()}");
                    results.AppendLine($"- **Has Getter**: {propertySymbol.GetMethod != null}");
                    results.AppendLine($"- **Has Setter**: {propertySymbol.SetMethod != null}");
                    break;
                case SymbolKind.Field:
                    var fieldSymbol = (IFieldSymbol)symbol;
                    results.AppendLine($"- **Field Type**: {fieldSymbol.Type.ToDisplayString()}");
                    results.AppendLine($"- **Is Const**: {fieldSymbol.IsConst}");
                    results.AppendLine($"- **Is Static**: {fieldSymbol.IsStatic}");
                    break;
                case SymbolKind.Event:
                    var eventSymbol = (IEventSymbol)symbol;
                    results.AppendLine($"- **Event Type**: {eventSymbol.Type.ToDisplayString()}");
                    break;
                case SymbolKind.Parameter:
                    var parameterSymbol = (IParameterSymbol)symbol;
                    results.AppendLine($"- **Parameter Type**: {parameterSymbol.Type.ToDisplayString()}");
                    results.AppendLine($"- **Is Optional**: {parameterSymbol.IsOptional}");
                    break;
                case SymbolKind.Local:
                    var localSymbol = (ILocalSymbol)symbol;
                    results.AppendLine($"- **Local Type**: {localSymbol.Type.ToDisplayString()}");
                    results.AppendLine($"- **Is Const**: {localSymbol.IsConst}");
                    break;
            }

            results.AppendLine();

            // Add reference information
            results.AppendLine("## References");
            results.AppendLine(
                $"Found {references.Sum(r => r.Locations.Count())} references in {references.Count()} locations.");
            results.AppendLine();

            int referenceCount = 1;
            foreach (var reference in references)
            {
                results.AppendLine($"### Reference Definition: {reference.Definition.ToDisplayString()}");

                foreach (var location in reference.Locations)
                {
                    var linePosition = location.Location.GetLineSpan();
                    var refLine = linePosition.StartLinePosition.Line + 1;
                    var refColumn = linePosition.StartLinePosition.Character + 1;

                    results.AppendLine(
                        $"#### Reference {referenceCount}: {Path.GetFileName(location.Document.FilePath)}:{refLine}:{refColumn}");
                    results.AppendLine($"- **File**: {location.Document.FilePath}");
                    results.AppendLine($"- **Line**: {refLine}");
                    results.AppendLine($"- **Column**: {refColumn}");

                    // Try to get code snippet context
                    try
                    {
                        var refSourceText = await location.Document.GetTextAsync();
                        var startLine = Math.Max(0, refLine - 3);
                        var endLine = Math.Min(refSourceText.Lines.Count, refLine + 2);

                        results.AppendLine("- **Code Context**:");
                        results.AppendLine("```csharp");

                        for (int i = startLine - 1; i < endLine; i++)
                        {
                            var codeLine = refSourceText.Lines[i];
                            var lineText = codeLine.ToString();
                            var lineNumber = i + 1;

                            // Highlight the reference line
                            if (lineNumber == refLine)
                            {
                                results.AppendLine($"{lineNumber}: > {lineText}");
                            }
                            else
                            {
                                results.AppendLine($"{lineNumber}:   {lineText}");
                            }
                        }

                        results.AppendLine("```");
                    }
                    catch (Exception ex)
                    {
                        results.AppendLine($"- **Code Context**: Unable to retrieve code context. Error: {ex.Message}");
                    }

                    results.AppendLine();
                    referenceCount++;
                }
            }

            // Add a summary section
            results.AppendLine("## Summary");
            results.AppendLine(
                $"Symbol `{symbol.Name}` of type `{symbol.Kind}` has {references.Sum(r => r.Locations.Count())} references across {references.Count()} locations.");

            return results.ToString();
        }
        catch (Exception ex)
        {
            return $"Error finding usages: {ex.Message}\n{ex.StackTrace}";
        }
    }

    private static MSBuildWorkspace CreateWorkspace()
    {
        var properties = new Dictionary<string, string>
        {
            { "AlwaysUseNETSdkDefaults", "true" },
            { "DesignTimeBuild", "true" }
        };

        var workspace = MSBuildWorkspace.Create(properties);
        Program.EnsureCSharpLanguageServicesRegistered(workspace);

        workspace.WorkspaceFailed += (sender, args) =>
        {
            Console.Error.WriteLine($"Workspace warning: {args.Diagnostic.Message}");
        };

        return workspace;
    }
}

[McpServerToolType]
public static class EchoTool
{
    [McpServerTool, Description("Echoes the message back to the client.")]
    public static string Echo(string message) => $"hello {message}";
}
