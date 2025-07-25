﻿using System.Collections.Immutable;
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
using RoslynRuntime.Services;
using RoslynRuntime.Services.Logging;
using RoslynRuntime;
using Serilog;

class Program
{
    static async Task Main(string[] args)
    {
        var builder = Host.CreateApplicationBuilder(args);

        // Configure Serilog
        builder.Services.AddSerilog((services, configuration) =>
        {
            configuration
                .ReadFrom.Configuration(builder.Configuration)
                .AddRoslynEnrichment()
                .WriteTo.Console(outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
                .WriteTo.File(
                    path: "logs/roslyn-mcp-.log",
                    rollingInterval: Serilog.RollingInterval.Day,
                    retainedFileCountLimit: 7,
                    outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} {Level:u3}] [{SourceContext}] {Message:lj}{NewLine}{Exception}");
        });

        // Register logging services
        builder.Services.AddRoslynLogging();

        builder.Services
            .AddMcpServer()
            .WithStdioServerTransport()
            .WithToolsFromAssembly();

        await builder.Build().RunAsync();
    }
}


[McpServerToolType]
public static class RoslynTools
{
    // Static logger for use in static methods
    private static readonly ILoggerFactory LoggerFactory = Microsoft.Extensions.Logging.LoggerFactory.Create(builder =>
        builder.AddConsole().SetMinimumLevel(LogLevel.Warning));
    private static readonly Microsoft.Extensions.Logging.ILogger Logger = LoggerFactory.CreateLogger("RoslynTools");

    [McpServerTool, Description("Validates a C# file using Roslyn and runs code analyzers. Accepts either a relative or absolute file path.")]
    public static async Task<string> ValidateFile(
        [Description("The path to the C# file to validate")] string filePath,
        [Description("Run analyzers (default: true)")] bool runAnalyzers = true)
    {
        var startTime = DateTime.UtcNow;
        try
        {
            // Log the received file path for debugging
            Logger.LogDebug("ValidateFile called with path: '{FilePath}', runAnalyzers: {RunAnalyzers}", filePath, runAnalyzers);

            // Check if the input is null or empty
            if (string.IsNullOrWhiteSpace(filePath))
            {
                Logger.LogError("File path is null or empty");
                return "Error: File path cannot be empty.";
            }

            // Handle Windows paths with backslashes
            // First, escape any backslashes for JSON serialization
            string normalizedPath = filePath.Replace("\\", "/");
            Logger.LogDebug("Normalized path: '{NormalizedPath}'", normalizedPath);

            // Resolve relative paths
            string fullPath;
            if (!Path.IsPathRooted(normalizedPath))
            {
                fullPath = Path.GetFullPath(normalizedPath);
                Logger.LogDebug("Resolved relative path to: '{FullPath}'", fullPath);
            }
            else
            {
                fullPath = normalizedPath;
            }

            // Ensure the path is in the correct format for file system operations
            string systemPath = Path.GetFullPath(fullPath);
            Logger.LogDebug("System path for file operations: '{SystemPath}'", systemPath);

            // Check if the file exists
            if (!File.Exists(systemPath))
            {
                Logger.LogError("File does not exist: '{SystemPath}'", systemPath);
                return $"Error: File {systemPath} does not exist.";
            }

            Logger.LogDebug("File exists: '{SystemPath}'", systemPath);

            // Find the containing project
            Logger.LogDebug("Searching for containing project...");
            string projectPath = await RoslynHelpers.FindContainingProjectAsync(systemPath, Logger);
            if (string.IsNullOrEmpty(projectPath))
            {
                Logger.LogError("Could not find a project containing this file: '{FilePath}'", systemPath);
                return "Error: Couldn't find a project containing this file.";
            }

            Logger.LogInformation("Found containing project: '{ProjectPath}' for file '{FilePath}'", projectPath, systemPath);

            // Use a StringWriter to capture the output
            Logger.LogDebug("Validating file in project context...");
            var outputWriter = new StringWriter();
            await RoslynHelpers.ValidateFileInProjectContextAsync(systemPath, projectPath, outputWriter, runAnalyzers, Logger);
            string result = outputWriter.ToString();
            
            var duration = DateTime.UtcNow - startTime;
            Logger.LogInformation("Validation completed for file '{FilePath}' in {Duration} ms", 
                systemPath, duration.TotalMilliseconds);

            return result;
        }
        catch (Exception ex)
        {
            var duration = DateTime.UtcNow - startTime;
            Logger.LogError(ex, "ERROR in ValidateFile for '{FilePath}' after {Duration} ms: {Message}", 
                filePath, duration.TotalMilliseconds, ex.Message);

            return $"Error processing file: {ex.Message}";
        }
    }

    [McpServerTool, Description("Extract comprehensive metadata from a .NET project including types, members, namespaces, and dependencies. Returns structured JSON data suitable for embedding and semantic search.")]
    public static async Task<string> ExtractProjectMetadata(
        [Description("Path to the .csproj file or a file within the project")] string projectPath)
    {
        var startTime = DateTime.UtcNow;
        try
        {
            Logger.LogDebug("ExtractProjectMetadata called with path: '{ProjectPath}'", projectPath);

            // Check if the input is null or empty
            if (string.IsNullOrWhiteSpace(projectPath))
            {
                Logger.LogError("Project path is null or empty");
                return "Error: Project path cannot be empty.";
            }

            // Normalize the path
            string normalizedPath = projectPath.Replace("\\", "/");
            string systemPath = !Path.IsPathRooted(normalizedPath)
                ? Path.GetFullPath(normalizedPath)
                : Path.GetFullPath(normalizedPath);

            Logger.LogDebug("System path: '{SystemPath}'", systemPath);

            // Determine if this is a project file or a source file
            string actualProjectPath;
            if (systemPath.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
            {
                // It's already a project file
                if (!File.Exists(systemPath))
                {
                    Logger.LogError("Project file does not exist: '{SystemPath}'", systemPath);
                    return $"Error: Project file {systemPath} does not exist.";
                }
                actualProjectPath = systemPath;
            }
            else
            {
                // It might be a source file, find the containing project
                Logger.LogDebug("Searching for containing project...");
                actualProjectPath = await RoslynHelpers.FindContainingProjectAsync(systemPath, Logger);
                if (string.IsNullOrEmpty(actualProjectPath))
                {
                    Logger.LogError("Couldn't find a project file for path: '{SystemPath}'", systemPath);
                    return "Error: Couldn't find a project file. Please provide a .csproj file path or a file within a project.";
                }
            }

            Logger.LogInformation("Using project file: '{ActualProjectPath}'", actualProjectPath);

            // Create workspace and extractor
            var workspace = RoslynHelpers.CreateWorkspace(Logger);
            var extractor = new ProjectMetadataExtractor(workspace);

            // Extract metadata
            Logger.LogDebug("Extracting project metadata...");
            var metadata = await extractor.ExtractAsync(actualProjectPath);

            // Serialize to JSON with proper formatting
            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
            };

            var jsonResult = JsonSerializer.Serialize(metadata, options);
            var duration = DateTime.UtcNow - startTime;
            
            // Log some metadata about what was extracted
            Logger.LogInformation("Metadata extraction complete for '{ActualProjectPath}' in {Duration} ms", 
                actualProjectPath, duration.TotalMilliseconds);

            return jsonResult;
        }
        catch (Exception ex)
        {
            var duration = DateTime.UtcNow - startTime;
            Logger.LogError(ex, "ERROR in ExtractProjectMetadata for '{ProjectPath}' after {Duration} ms: {Message}", 
                projectPath, duration.TotalMilliseconds, ex.Message);

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

            string projectPath = await RoslynHelpers.FindContainingProjectAsync(systemPath, Logger);
            if (string.IsNullOrEmpty(projectPath))
            {
                return "Error: Couldn't find a project containing this file.";
            }

            var workspace = RoslynHelpers.CreateWorkspace(Logger);
            var project = await workspace.OpenProjectAsync(projectPath);
            var document = project.Documents.FirstOrDefault(d =>
                string.Equals(d.FilePath, systemPath, StringComparison.OrdinalIgnoreCase));

            if (document == null) return "Error: File not found in project.";

            var sourceText = await document.GetTextAsync();
            var position = sourceText.Lines[line - 1].Start + (column - 1);

            var semanticModel = await document.GetSemanticModelAsync();
            if (semanticModel == null)
            {
                return "Error: Unable to get semantic model for the document.";
            }
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
                case Microsoft.CodeAnalysis.SymbolKind.Method:
                    var methodSymbol = (IMethodSymbol)symbol;
                    results.AppendLine($"- **Return Type**: {methodSymbol.ReturnType.ToDisplayString()}");
                    results.AppendLine($"- **Is Extension Method**: {methodSymbol.IsExtensionMethod}");
                    results.AppendLine($"- **Parameter Count**: {methodSymbol.Parameters.Length}");
                    break;
                case Microsoft.CodeAnalysis.SymbolKind.Property:
                    var propertySymbol = (IPropertySymbol)symbol;
                    results.AppendLine($"- **Property Type**: {propertySymbol.Type.ToDisplayString()}");
                    results.AppendLine($"- **Has Getter**: {propertySymbol.GetMethod != null}");
                    results.AppendLine($"- **Has Setter**: {propertySymbol.SetMethod != null}");
                    break;
                case Microsoft.CodeAnalysis.SymbolKind.Field:
                    var fieldSymbol = (IFieldSymbol)symbol;
                    results.AppendLine($"- **Field Type**: {fieldSymbol.Type.ToDisplayString()}");
                    results.AppendLine($"- **Is Const**: {fieldSymbol.IsConst}");
                    results.AppendLine($"- **Is Static**: {fieldSymbol.IsStatic}");
                    break;
                case Microsoft.CodeAnalysis.SymbolKind.Event:
                    var eventSymbol = (IEventSymbol)symbol;
                    results.AppendLine($"- **Event Type**: {eventSymbol.Type.ToDisplayString()}");
                    break;
                case Microsoft.CodeAnalysis.SymbolKind.Parameter:
                    var parameterSymbol = (IParameterSymbol)symbol;
                    results.AppendLine($"- **Parameter Type**: {parameterSymbol.Type.ToDisplayString()}");
                    results.AppendLine($"- **Is Optional**: {parameterSymbol.IsOptional}");
                    break;
                case Microsoft.CodeAnalysis.SymbolKind.Local:
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



    [McpServerTool, Description("Break down C# code into semantically meaningful chunks for analysis.")]
    public static async Task<string> ChunkCodeBySemantics(
        [Description("Path to the C# file or project")] string path,
        [Description("Chunking strategy: 'class', 'method', 'feature', 'namespace'")] string strategy = "class",
        [Description("Include dependency relationships")] bool includeDependencies = true)
    {
        try
        {
            Logger.LogDebug("ChunkCodeBySemantics called with path: '{Path}', strategy: '{Strategy}'", path, strategy);

            // Normalize file path
            string normalizedPath = path.Replace("\\", "/");
            string systemPath = !Path.IsPathRooted(normalizedPath)
                ? Path.GetFullPath(normalizedPath)
                : Path.GetFullPath(normalizedPath);

            if (!File.Exists(systemPath))
            {
                Logger.LogError("File does not exist: '{SystemPath}'", systemPath);
                return $"Error: File {systemPath} does not exist.";
            }

            // Create chunker service
            var chunker = new CodeChunker();
            
            // Perform chunking
            var result = await chunker.ChunkCodeAsync(systemPath, strategy, includeDependencies);

            // Serialize to JSON
            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
            };

            Logger.LogInformation("Code chunking completed for '{Path}' with strategy '{Strategy}'", systemPath, strategy);
            return JsonSerializer.Serialize(result, options);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "ERROR in ChunkCodeBySemantics for '{Path}': {Message}", path, ex.Message);
            return $"Error chunking code: {ex.Message}";
        }
    }

    [McpServerTool, Description("Analyze code structure, patterns, and architectural metrics.")]
    public static async Task<string> AnalyzeCodeStructure(
        [Description("Path to the C# file or project")] string path,
        [Description("Include design pattern detection")] bool detectPatterns = true,
        [Description("Calculate complexity metrics")] bool calculateMetrics = true)
    {
        try
        {
            Logger.LogDebug("AnalyzeCodeStructure called with path: '{Path}'", path);

            // Normalize file path
            string normalizedPath = path.Replace("\\", "/");
            string systemPath = !Path.IsPathRooted(normalizedPath)
                ? Path.GetFullPath(normalizedPath)
                : Path.GetFullPath(normalizedPath);

            if (!File.Exists(systemPath))
            {
                Logger.LogError("File does not exist: '{SystemPath}'", systemPath);
                return $"Error: File {systemPath} does not exist.";
            }

            // Create structure analyzer service
            var analyzer = new StructureAnalyzer();
            
            // Perform analysis
            var result = await analyzer.AnalyzeStructureAsync(systemPath, detectPatterns, calculateMetrics);

            // Serialize to JSON
            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
            };

            Logger.LogInformation("Code structure analysis completed for '{Path}'", systemPath);
            return JsonSerializer.Serialize(result, options);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "ERROR in AnalyzeCodeStructure for '{Path}': {Message}", path, ex.Message);
            return $"Error analyzing code structure: {ex.Message}";
        }
    }

    [McpServerTool, Description("Generate factual information about code for documentation and analysis.")]
    public static async Task<string> GenerateCodeFacts(
        [Description("Path to the C# file or project")] string path,
        [Description("Output format: 'json', 'markdown', 'text'")] string format = "json",
        [Description("Include natural language descriptions")] bool includeDescriptions = true)
    {
        try
        {
            Logger.LogDebug("GenerateCodeFacts called with path: '{Path}', format: '{Format}'", path, format);

            // Normalize file path
            string normalizedPath = path.Replace("\\", "/");
            string systemPath = !Path.IsPathRooted(normalizedPath)
                ? Path.GetFullPath(normalizedPath)
                : Path.GetFullPath(normalizedPath);

            if (!File.Exists(systemPath))
            {
                Logger.LogError("File does not exist: '{SystemPath}'", systemPath);
                return $"Error: File {systemPath} does not exist.";
            }

            // Create code facts generator service
            var generator = new CodeFactsGenerator();
            
            // Generate facts
            var result = await generator.GenerateCodeFactsAsync(systemPath, format, includeDescriptions);

            // Serialize to JSON
            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
            };

            Logger.LogInformation("Code facts generation completed for '{Path}' in format '{Format}'", systemPath, format);
            return JsonSerializer.Serialize(result, options);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "ERROR in GenerateCodeFacts for '{Path}': {Message}", path, ex.Message);
            return $"Error generating code facts: {ex.Message}";
        }
    }

    [McpServerTool, Description("Extract a comprehensive symbol graph showing relationships between types, methods, and other code elements.")]
    public static async Task<string> ExtractSymbolGraph(
        [Description("Path to the C# file or project")] string path,
        [Description("Graph scope: 'file', 'project', 'solution'")] string scope = "file",
        [Description("Include inheritance relationships")] bool includeInheritance = true,
        [Description("Include method call relationships")] bool includeMethodCalls = true,
        [Description("Include field/property access relationships")] bool includeFieldAccess = true,
        [Description("Include namespace relationships")] bool includeNamespaces = true,
        [Description("Include XAML analysis")] bool includeXaml = false,
        [Description("Include SQL analysis")] bool includeSql = false,
        [Description("Maximum depth for relationship traversal")] int maxDepth = 3)
    {
        try
        {
            Logger.LogDebug("ExtractSymbolGraph called with path: '{Path}', scope: '{Scope}'", path, scope);

            // Normalize file path
            string normalizedPath = path.Replace("\\", "/");
            string systemPath = !Path.IsPathRooted(normalizedPath)
                ? Path.GetFullPath(normalizedPath)
                : Path.GetFullPath(normalizedPath);

            // Validate scope parameter
            var validScopes = new[] { "file", "project", "solution" };
            if (!validScopes.Contains(scope.ToLowerInvariant()))
            {
                return $"Error: Invalid scope '{scope}'. Valid values are: {string.Join(", ", validScopes)}";
            }

            // For file scope, check if file exists
            if (scope.ToLowerInvariant() == "file" && !File.Exists(systemPath))
            {
                return $"Error: File {systemPath} does not exist.";
            }

            // For project scope, check if it's a project file or find containing project
            if (scope.ToLowerInvariant() == "project")
            {
                if (!systemPath.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
                {
                    // Try to find containing project
                    var projectPath = await RoslynHelpers.FindContainingProjectAsync(systemPath, Logger);
                    if (string.IsNullOrEmpty(projectPath))
                    {
                        return "Error: Could not find a project file. Please provide a .csproj file path or a file within a project.";
                    }
                    systemPath = projectPath;
                }
                else if (!File.Exists(systemPath))
                {
                    return $"Error: Project file {systemPath} does not exist.";
                }
            }

            // For solution scope, check if it's a solution file or find containing solution
            if (scope.ToLowerInvariant() == "solution")
            {
                if (!systemPath.EndsWith(".sln", StringComparison.OrdinalIgnoreCase))
                {
                    // Try to find solution file in directory hierarchy
                    var directory = new DirectoryInfo(Path.GetDirectoryName(systemPath) ?? ".");
                    string? solutionPath = null;
                    
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
                    
                    if (solutionPath == null)
                    {
                        return "Error: Could not find a solution file. Please provide a .sln file path or ensure you're within a solution directory.";
                    }
                    systemPath = solutionPath;
                }
                else if (!File.Exists(systemPath))
                {
                    return $"Error: Solution file {systemPath} does not exist.";
                }
            }

            // Create workspace and symbol graph extractor
            var workspace = RoslynHelpers.CreateWorkspace(Logger);
            var extractor = new SymbolGraphExtractor(workspace);
            
            // Extract symbol graph
            var result = await extractor.ExtractSymbolGraphAsync(
                systemPath, 
                scope, 
                includeInheritance, 
                includeMethodCalls, 
                includeFieldAccess, 
                includeNamespaces, 
                maxDepth);

            // Serialize to JSON
            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
            };

            return JsonSerializer.Serialize(result, options);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "ERROR in ExtractSymbolGraph for '{Path}': {Message}", path, ex.Message);
            return $"Error extracting symbol graph: {ex.Message}";
        }
    }

    [McpServerTool, Description("Break down multi-language code into semantically meaningful chunks spanning C#, XAML, and SQL.")]
    public static async Task<string> ChunkMultiLanguageCode(
        [Description("Path to the C# file or project")] string path,
        [Description("Chunking strategy: 'feature', 'dataaccess', 'mvvm', 'component'")] string strategy = "feature",
        [Description("Include dependency relationships")] bool includeDependencies = true,
        [Description("Include XAML analysis")] bool includeXaml = false,
        [Description("Include SQL analysis")] bool includeSql = false)
    {
        try
        {
            Logger.LogDebug("ChunkMultiLanguageCode called with path: '{Path}', strategy: '{Strategy}'", path, strategy);

            // Normalize file path
            string normalizedPath = path.Replace("\\", "/");
            string systemPath = !Path.IsPathRooted(normalizedPath)
                ? Path.GetFullPath(normalizedPath)
                : Path.GetFullPath(normalizedPath);

            // Validate strategy parameter
            var validStrategies = new[] { "feature", "dataaccess", "mvvm", "component" };
            if (!validStrategies.Contains(strategy.ToLowerInvariant()))
            {
                return $"Error: Invalid strategy '{strategy}'. Valid values are: {string.Join(", ", validStrategies)}";
            }

            // Create multi-language chunker service
            var chunker = new MultiLanguageChunker();
            
            // Perform chunking
            var result = await chunker.ChunkMultiLanguageCodeAsync(systemPath, strategy, includeDependencies, includeXaml, includeSql);

            // Serialize to JSON
            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
            };

            return JsonSerializer.Serialize(result, options);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "ERROR in ChunkMultiLanguageCode for '{Path}': {Message}", path, ex.Message);
            return $"Error chunking multi-language code: {ex.Message}";
        }
    }

    [McpServerTool, Description("Extract a unified semantic graph of an entire solution using Roslyn's SymbolFinder APIs for accurate cross-project symbol resolution.")]
    public static async Task<string> ExtractUnifiedSemanticGraph(
        [Description("Path to the solution file (.sln) or project file (.csproj)")] string path,
        [Description("Include architectural role classification")] bool includeRoles = true,
        [Description("Include feature boundary detection")] bool includeFeatures = true,
        [Description("Include cross-project dependencies")] bool includeCrossProject = true,
        [Description("Include cross-language relationships")] bool includeCrossLanguage = false)
    {
        try
        {
            Logger.LogDebug("ExtractUnifiedSemanticGraph called with path: '{Path}'", path);

            // Normalize file path
            string normalizedPath = path.Replace("\\", "/");
            string systemPath = !Path.IsPathRooted(normalizedPath)
                ? Path.GetFullPath(normalizedPath)
                : Path.GetFullPath(normalizedPath);

            // Determine if this is a solution or project file
            bool isSolutionFile = systemPath.EndsWith(".sln", StringComparison.OrdinalIgnoreCase);
            bool isProjectFile = systemPath.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase);

            if (!isSolutionFile && !isProjectFile)
            {
                // Try to find solution or project file
                var directory = new DirectoryInfo(Path.GetDirectoryName(systemPath) ?? ".");
                string? foundPath = null;
                
                // First try to find solution file
                while (directory != null)
                {
                    var solutionFiles = directory.GetFiles("*.sln");
                    if (solutionFiles.Length > 0)
                    {
                        foundPath = solutionFiles[0].FullName;
                        isSolutionFile = true;
                        break;
                    }
                    directory = directory.Parent;
                }
                
                // If no solution found, try to find project file
                if (foundPath == null)
                {
                    var projectPath = await RoslynHelpers.FindContainingProjectAsync(systemPath, Logger);
                    if (!string.IsNullOrEmpty(projectPath))
                    {
                        foundPath = projectPath;
                        isProjectFile = true;
                    }
                }
                
                if (foundPath == null)
                {
                    return "Error: Could not find a solution (.sln) or project (.csproj) file. Please provide a valid solution or project file path.";
                }
                
                systemPath = foundPath;
            }

            if (!File.Exists(systemPath))
            {
                return $"Error: File {systemPath} does not exist.";
            }

            // Create workspace and semantic analyzer
            var workspace = RoslynHelpers.CreateWorkspace(Logger);
            var analyzerLogger = LoggerFactory.CreateLogger<SemanticSolutionAnalyzer>();
            var analyzer = new SemanticSolutionAnalyzer(workspace, analyzerLogger);
            
            Solution solution;
            if (isSolutionFile)
            {
                // Open solution
                solution = await workspace.OpenSolutionAsync(systemPath);
            }
            else
            {
                // Open project and get its solution
                var project = await workspace.OpenProjectAsync(systemPath);
                solution = project.Solution;
            }

            // Extract unified semantic graph
            var result = await analyzer.AnalyzeSolutionAsync(solution);

            // Serialize to JSON
            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
            };

            return JsonSerializer.Serialize(result, options);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "ERROR in ExtractUnifiedSemanticGraph for '{Path}': {Message}", path, ex.Message);
            Console.Error.WriteLine($"Stack trace: {ex.StackTrace}");
            if (ex.InnerException != null)
            {
                Console.Error.WriteLine($"Inner exception: {ex.InnerException.Message}");
                Console.Error.WriteLine($"Inner stack trace: {ex.InnerException.StackTrace}");
            }
            return $"Error extracting unified semantic graph: {ex.Message}";
        }
    }

    [McpServerTool, Description("Extract SQL queries and database operations from C# code.")]
    public static async Task<string> ExtractSqlFromCode(
        [Description("Path to the C# file")] string filePath)
    {
        try
        {
            Logger.LogDebug("ExtractSqlFromCode called with path: '{FilePath}'", filePath);

            // Normalize file path
            string normalizedPath = filePath.Replace("\\", "/");
            string systemPath = !Path.IsPathRooted(normalizedPath)
                ? Path.GetFullPath(normalizedPath)
                : Path.GetFullPath(normalizedPath);

            if (!File.Exists(systemPath))
            {
                return $"Error: File {systemPath} does not exist.";
            }

            // Create SQL extractor service
            var extractor = new SqlExtractor();
            
            // Extract SQL metadata
            var result = await extractor.ExtractSqlFromFileAsync(systemPath);

            // Serialize to JSON
            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
            };

            return JsonSerializer.Serialize(result, options);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "ERROR in ExtractSqlFromCode for '{FilePath}': {Message}", filePath, ex.Message);
            return $"Error extracting SQL from code: {ex.Message}";
        }
    }

    [McpServerTool, Description("Analyze XAML files for UI structure, data bindings, and resources.")]
    public static async Task<string> AnalyzeXamlFile(
        [Description("Path to the XAML file")] string filePath)
    {
        try
        {
            Logger.LogDebug("AnalyzeXamlFile called with path: '{FilePath}'", filePath);

            // Normalize file path
            string normalizedPath = filePath.Replace("\\", "/");
            string systemPath = !Path.IsPathRooted(normalizedPath)
                ? Path.GetFullPath(normalizedPath)
                : Path.GetFullPath(normalizedPath);

            if (!File.Exists(systemPath))
            {
                return $"Error: File {systemPath} does not exist.";
            }

            if (!systemPath.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase))
            {
                return $"Error: File {systemPath} is not a XAML file.";
            }

            // Create XAML analyzer service
            var xamlLogger = LoggerFactory.CreateLogger<XamlAnalyzer>();
            var analyzer = new XamlAnalyzer(xamlLogger);
            
            // Analyze XAML file
            var result = await analyzer.AnalyzeXamlFileAsync(systemPath);

            // Serialize to JSON
            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
            };

            return JsonSerializer.Serialize(result, options);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "ERROR in AnalyzeXamlFile for '{FilePath}': {Message}", filePath, ex.Message);
            return $"Error analyzing XAML file: {ex.Message}";
        }
    }

    [McpServerTool, Description("Analyze MVVM relationships between Views, ViewModels, and Models in a project.")]
    public static async Task<string> AnalyzeMvvmRelationships(
        [Description("Path to the project file or a file within the project")] string projectPath)
    {
        try
        {
            Logger.LogDebug("AnalyzeMvvmRelationships called with path: '{ProjectPath}'", projectPath);

            // Normalize file path
            string normalizedPath = projectPath.Replace("\\", "/");
            string systemPath = !Path.IsPathRooted(normalizedPath)
                ? Path.GetFullPath(normalizedPath)
                : Path.GetFullPath(normalizedPath);

            // Determine if this is a project file or a source file
            string actualProjectPath;
            if (systemPath.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
            {
                if (!File.Exists(systemPath))
                {
                    return $"Error: Project file {systemPath} does not exist.";
                }
                actualProjectPath = systemPath;
            }
            else
            {
                // Try to find containing project
                actualProjectPath = await RoslynHelpers.FindContainingProjectAsync(systemPath, Logger);
                if (string.IsNullOrEmpty(actualProjectPath))
                {
                    return "Error: Could not find a project file. Please provide a .csproj file path or a file within a project.";
                }
            }

            // Create XAML analyzer service
            var xamlLogger = LoggerFactory.CreateLogger<XamlAnalyzer>();
            var analyzer = new XamlAnalyzer(xamlLogger);
            
            // Analyze MVVM relationships
            var result = await analyzer.AnalyzeMvvmRelationshipsAsync(actualProjectPath);

            // Serialize to JSON
            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
            };

            return JsonSerializer.Serialize(result, options);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "ERROR in AnalyzeMvvmRelationships for '{ProjectPath}': {Message}", projectPath, ex.Message);
            return $"Error analyzing MVVM relationships: {ex.Message}";
        }
    }
}

[McpServerToolType]
public static class EchoTool
{
    [McpServerTool, Description("Echoes the message back to the client.")]
    public static string Echo(string message) => $"hello {message}";
}
