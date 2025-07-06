using System.Collections.Immutable;
using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.MSBuild;
using Microsoft.Extensions.Logging;

namespace RoslynRuntime
{
    /// <summary>
    /// Shared helper methods for Roslyn workspace and project operations
    /// </summary>
    public static class RoslynHelpers
    {
        /// <summary>
        /// Finds the containing project file for a given source file path
        /// </summary>
        /// <param name="filePath">Path to the source file</param>
        /// <param name="logger">Optional logger for diagnostic messages</param>
        /// <returns>Path to the containing project file, or empty string if not found</returns>
        public static async Task<string> FindContainingProjectAsync(string filePath, ILogger? logger = null)
        {
            // Start from the directory containing the file and go up until we find a .csproj file
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            {
                return string.Empty;
            }
            DirectoryInfo? directory = new FileInfo(filePath).Directory;

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
                    EnsureCSharpLanguageServicesRegistered(workspace, logger);

                    // Add event handler for workspace failures
                    workspace.WorkspaceFailed += (sender, args) =>
                    {
                        logger?.LogWarning("Workspace warning: {Message}", args.Diagnostic.Message);
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
                        logger?.LogError(ex, "Error opening project: {Message}", ex.Message);
                    }
                }

                directory = directory.Parent;
            }

            return string.Empty;
        }

        /// <summary>
        /// Validates a file in its project context, including syntax, semantic, and analyzer checks
        /// </summary>
        /// <param name="filePath">Path to the file to validate</param>
        /// <param name="projectPath">Path to the containing project</param>
        /// <param name="writer">TextWriter to output validation results</param>
        /// <param name="runAnalyzers">Whether to run code analyzers</param>
        /// <param name="logger">Optional logger for diagnostic messages</param>
        public static async Task ValidateFileInProjectContextAsync(string filePath, string projectPath,
            TextWriter writer, bool runAnalyzers = true, ILogger? logger = null)
        {
            // Use the provided TextWriter or fallback to Console.Out for backward compatibility
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
                EnsureCSharpLanguageServicesRegistered(workspace, logger);

                // Add event handler for workspace failures
                workspace.WorkspaceFailed += (sender, args) =>
                {
                    logger?.LogWarning("Workspace warning: {Message}", args.Diagnostic.Message);
                    writer.WriteLine($"Workspace warning: {args.Diagnostic.Message}");
                };

                // Load the project
                logger?.LogInformation("Loading project: {ProjectPath}", projectPath);
                writer.WriteLine($"Loading project: {projectPath}");
                var project = await workspace.OpenProjectAsync(projectPath);
                logger?.LogInformation("Project loaded successfully: {ProjectName}", project.Name);
                writer.WriteLine($"Project loaded successfully: {project.Name}");

                // Find the document in the project
                var document = project.Documents
                    .FirstOrDefault(d => string.Equals(d.FilePath, filePath, StringComparison.OrdinalIgnoreCase));

                if (document == null)
                {
                    logger?.LogError("File not found in the project documents: {FilePath}", filePath);
                    writer.WriteLine("Error: File not found in the project documents.");
                    writer.WriteLine("All project documents:");
                    foreach (var doc in project.Documents)
                    {
                        writer.WriteLine($"  - {doc.FilePath}");
                    }

                    return;
                }

                logger?.LogDebug("Document found: {DocumentName}", document.Name);
                writer.WriteLine($"Document found: {document.Name}");

                // Parse syntax tree
                var syntaxTree = await document.GetSyntaxTreeAsync();
                var syntaxDiagnostics = syntaxTree?.GetDiagnostics();

                if (syntaxDiagnostics != null && syntaxDiagnostics.Any())
                {
                    logger?.LogWarning("Found {Count} syntax errors in {FilePath}", syntaxDiagnostics.Count(), filePath);
                    writer.WriteLine("Syntax errors found:");
                    foreach (var diagnostic in syntaxDiagnostics)
                    {
                        var location = diagnostic.Location.GetLineSpan();
                        writer.WriteLine($"Line {location.StartLinePosition.Line + 1}: {diagnostic.GetMessage()}");
                    }
                }
                else
                {
                    logger?.LogDebug("No syntax errors found in {FilePath}", filePath);
                    writer.WriteLine("No syntax errors found.");
                }

                // Get the semantic model for deeper analysis
                var semanticModel = await document.GetSemanticModelAsync();
                var semanticDiagnostics = semanticModel?.GetDiagnostics();

                if (semanticDiagnostics != null && semanticDiagnostics.Value.Any())
                {
                    logger?.LogWarning("Found {Count} semantic errors in {FilePath}", semanticDiagnostics.Value.Count(), filePath);
                    writer.WriteLine("\nSemantic errors found:");
                    foreach (var diagnostic in semanticDiagnostics)
                    {
                        var location = diagnostic.Location.GetLineSpan();
                        writer.WriteLine($"Line {location.StartLinePosition.Line + 1}: {diagnostic.GetMessage()}");
                    }
                }
                else
                {
                    logger?.LogDebug("No semantic errors found in {FilePath}", filePath);
                    writer.WriteLine("No semantic errors found.");
                }

                // Check compilation for the entire project to validate references
                var compilation = await project.GetCompilationAsync();
                
                if (compilation == null)
                {
                    logger?.LogError("Unable to get compilation for the project: {ProjectPath}", projectPath);
                    writer.WriteLine("Error: Unable to get compilation for the project.");
                    return;
                }
                
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
                    logger?.LogInformation("Running code analyzers for {FilePath}", filePath);
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
                                        var analyzer = Activator.CreateInstance(analyzerType) as DiagnosticAnalyzer;
                                        if (analyzer == null)
                                        {
                                            logger?.LogWarning("{AnalyzerType} is not a valid DiagnosticAnalyzer", analyzerType.FullName);
                                            writer.WriteLine($"Warning: {analyzerType.FullName} is not a valid DiagnosticAnalyzer.");
                                            continue;
                                        }
                                        analyzers.Add(analyzer);
                                    }
                                    catch (Exception ex)
                                    {
                                        logger?.LogError(ex, "Error creating analyzer instance for {AnalyzerType}", analyzerType.FullName);
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                logger?.LogError(ex, "Error loading analyzer assembly: {AnalyzerPath}", analyzerPath);
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
                                    // Check if the assembly is a Roslyn analyzer assembly
                                    if (assembly.IsDynamic || assembly.Location == null)
                                        continue;

                                    var analyzerTypes = assembly.GetTypes()
                                        .Where(t => !t.IsAbstract && typeof(DiagnosticAnalyzer).IsAssignableFrom(t));
                                        
                                    foreach (var analyzerType in analyzerTypes)
                                    {
                                        try
                                        {
                                            var analyzer = Activator.CreateInstance(analyzerType) as DiagnosticAnalyzer;
                                            if (analyzer == null)
                                            {
                                                logger?.LogWarning("{AnalyzerType} is not a valid DiagnosticAnalyzer", analyzerType.FullName);
                                                writer.WriteLine($"Warning: {analyzerType.FullName} is not a valid DiagnosticAnalyzer.");
                                                continue;
                                            }
                                            analyzers.Add(analyzer);
                                        }
                                        catch (Exception ex)
                                        {
                                            logger?.LogError(ex, "Error creating analyzer instance for {AnalyzerType}", analyzerType.FullName);
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
                            logger?.LogInformation("Found {AnalyzerCount} analyzers", analyzers.Count);
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
                            logger?.LogWarning("No analyzers found");
                            writer.WriteLine("No analyzers found");
                        }
                    }
                    catch (Exception ex)
                    {
                        logger?.LogError(ex, "Error running analyzers for {FilePath}", filePath);
                        writer.WriteLine($"Error running analyzers: {ex.Message}");
                    }
                }
                
                // Combine compilation diagnostics and analyzer diagnostics
                var allDiagnostics = compilationDiagnostics.Concat(analyzerDiagnostics);

                if (allDiagnostics.Any())
                {
                    logger?.LogInformation("Found {DiagnosticCount} compilation and analyzer diagnostics for {FilePath}", 
                        allDiagnostics.Count(), filePath);
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
                    logger?.LogInformation("File compiles successfully with no analyzer warnings: {FilePath}", filePath);
                    writer.WriteLine("File compiles successfully in project context with no analyzer warnings.");
                }
            }
            catch (Exception ex)
            {
                logger?.LogError(ex, "Error validating file: {FilePath}", filePath);
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
        /// <param name="workspace">The MSBuild workspace to configure</param>
        /// <param name="logger">Optional logger for diagnostic messages</param>
        public static void EnsureCSharpLanguageServicesRegistered(MSBuildWorkspace workspace, ILogger? logger = null)
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
                    var languageServicesType = languageServices?.GetType();

                    if (languageServicesType == null)
                    {
                        logger?.LogWarning("Unable to retrieve language services type");
                        return;
                    }
                    
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
                            logger?.LogDebug("Successfully registered C# language service explicitly");
                        }
                    }
                }

                // Force load the CSharp assembly to ensure its language services are available
                RuntimeHelpers.RunClassConstructor(typeof(CSharpSyntaxTree).TypeHandle);
            }
            catch (Exception ex)
            {
                logger?.LogWarning(ex, "Error while registering language services: {Message}", ex.Message);
                // Continue execution as the standard registration might still work
            }
        }

        /// <summary>
        /// Creates a properly configured MSBuildWorkspace for Roslyn operations
        /// </summary>
        /// <param name="logger">Optional logger for diagnostic messages</param>
        /// <returns>Configured MSBuildWorkspace</returns>
        public static MSBuildWorkspace CreateWorkspace(ILogger? logger = null)
        {
            var properties = new Dictionary<string, string>
            {
                { "AlwaysUseNETSdkDefaults", "true" },
                { "DesignTimeBuild", "true" }
            };

            var workspace = MSBuildWorkspace.Create(properties);
            EnsureCSharpLanguageServicesRegistered(workspace, logger);

            workspace.WorkspaceFailed += (sender, args) =>
            {
                logger?.LogWarning("Workspace warning: {Message}", args.Diagnostic.Message);
            };

            return workspace;
        }
    }
}