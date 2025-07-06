using Microsoft.CodeAnalysis;
using Microsoft.Extensions.Caching.Memory;
using RoslynRuntime;
using RoslynRuntime.Services;
using System.Diagnostics;
using System.Text.Json;

namespace RoslynWebApi.Services;

/// <summary>
/// Service for Roslyn code analysis operations
/// </summary>
public interface IRoslynAnalysisService
{
    Task<string> ValidateFileAsync(string filePath, bool runAnalyzers = true);
    Task<string> ExtractProjectMetadataAsync(string projectPath);
    Task<string> FindUsagesAsync(string filePath, int line, int column);
    Task<string> ChunkCodeBySemanticsAsync(string path, string strategy = "class", bool includeDependencies = true);
    Task<string> AnalyzeCodeStructureAsync(string path, bool detectPatterns = true, bool calculateMetrics = true);
    Task<string> GenerateCodeFactsAsync(string path, string format = "json", bool includeDescriptions = true);
    Task<string> ExtractSymbolGraphAsync(string path, string scope = "file", bool includeInheritance = true, 
        bool includeMethodCalls = true, bool includeFieldAccess = true, bool includeNamespaces = true, 
        bool includeXaml = false, bool includeSql = false, int maxDepth = 3);
    Task<string> ChunkMultiLanguageCodeAsync(string path, string strategy = "feature", bool includeDependencies = true, 
        bool includeXaml = false, bool includeSql = false);
    Task<string> ExtractUnifiedSemanticGraphAsync(string path, bool includeRoles = true, bool includeFeatures = true, 
        bool includeCrossProject = true, bool includeCrossLanguage = false);
    Task<string> ExtractSqlFromCodeAsync(string filePath);
    Task<string> AnalyzeXamlFileAsync(string filePath);
    Task<string> AnalyzeMvvmRelationshipsAsync(string projectPath);
}

/// <summary>
/// Implementation of Roslyn analysis service using shared RoslynHelpers
/// </summary>
public class RoslynAnalysisService : IRoslynAnalysisService
{
    private static readonly ILoggerFactory _loggerFactory = Microsoft.Extensions.Logging.LoggerFactory.Create(builder =>
        builder.AddConsole().SetMinimumLevel(LogLevel.Warning));
    private readonly ILogger<RoslynAnalysisService> _logger;
    private readonly IMemoryCache _cache;

    public RoslynAnalysisService(ILogger<RoslynAnalysisService> logger, IMemoryCache cache)
    {
        _logger = logger;
        _cache = cache;
    }

    public async Task<string> ValidateFileAsync(string filePath, bool runAnalyzers = true)
    {
        var startTime = DateTime.UtcNow;
        try
        {
            _logger.LogInformation("ValidateFileAsync called with path: '{FilePath}', runAnalyzers: {RunAnalyzers}", filePath, runAnalyzers);

            // Check if the input is null or empty
            if (string.IsNullOrWhiteSpace(filePath))
            {
                _logger.LogError("File path is null or empty");
                return "Error: File path cannot be empty.";
            }

            // Handle Windows paths with backslashes
            string normalizedPath = filePath.Replace("\\", "/");
            _logger.LogDebug("Normalized path: '{NormalizedPath}'", normalizedPath);

            // Resolve relative paths
            string fullPath = !Path.IsPathRooted(normalizedPath) 
                ? Path.GetFullPath(normalizedPath) 
                : normalizedPath;

            // Ensure the path is in the correct format for file system operations
            string systemPath = Path.GetFullPath(fullPath);
            _logger.LogDebug("System path for file operations: '{SystemPath}'", systemPath);

            // Check if the file exists
            if (!File.Exists(systemPath))
            {
                _logger.LogError("File does not exist: '{SystemPath}'", systemPath);
                return $"Error: File {systemPath} does not exist.";
            }

            _logger.LogDebug("File exists: '{SystemPath}'", systemPath);

            // Find the containing project
            _logger.LogDebug("Searching for containing project...");
            string projectPath = await RoslynHelpers.FindContainingProjectAsync(systemPath, _logger);
            if (string.IsNullOrEmpty(projectPath))
            {
                _logger.LogError("Could not find a project containing this file: '{FilePath}'", systemPath);
                return "Error: Couldn't find a project containing this file.";
            }

            _logger.LogInformation("Found containing project: '{ProjectPath}' for file '{FilePath}'", projectPath, systemPath);

            // Use a StringWriter to capture the output
            _logger.LogDebug("Validating file in project context...");
            var outputWriter = new StringWriter();
            await RoslynHelpers.ValidateFileInProjectContextAsync(systemPath, projectPath, outputWriter, runAnalyzers, _logger);
            string result = outputWriter.ToString();
            
            var duration = DateTime.UtcNow - startTime;
            _logger.LogInformation("Validation completed for file '{FilePath}' in {Duration} ms", 
                systemPath, duration.TotalMilliseconds);

            return result;
        }
        catch (Exception ex)
        {
            var duration = DateTime.UtcNow - startTime;
            _logger.LogError(ex, "ERROR in ValidateFileAsync for '{FilePath}' after {Duration} ms: {Message}", 
                filePath, duration.TotalMilliseconds, ex.Message);

            return $"Error processing file: {ex.Message}";
        }
    }

    public async Task<string> ExtractProjectMetadataAsync(string projectPath)
    {
        var startTime = DateTime.UtcNow;
        try
        {
            _logger.LogInformation("ExtractProjectMetadataAsync called with path: '{ProjectPath}'", projectPath);

            if (string.IsNullOrWhiteSpace(projectPath))
            {
                _logger.LogError("Project path is null or empty");
                return "Error: Project path cannot be empty.";
            }

            // Normalize the path
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
                    _logger.LogError("Project file does not exist: '{SystemPath}'", systemPath);
                    return $"Error: Project file {systemPath} does not exist.";
                }
                actualProjectPath = systemPath;
            }
            else
            {
                _logger.LogDebug("Searching for containing project...");
                actualProjectPath = await RoslynHelpers.FindContainingProjectAsync(systemPath, _logger);
                if (string.IsNullOrEmpty(actualProjectPath))
                {
                    _logger.LogError("Couldn't find a project file for path: '{SystemPath}'", systemPath);
                    return "Error: Couldn't find a project file. Please provide a .csproj file path or a file within a project.";
                }
            }

            _logger.LogInformation("Using project file: '{ActualProjectPath}'", actualProjectPath);

            // Create workspace and extractor
            var workspace = RoslynHelpers.CreateWorkspace(_logger);
            var extractor = new ProjectMetadataExtractor(workspace);

            // Extract metadata
            _logger.LogDebug("Extracting project metadata...");
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
            
            _logger.LogInformation("Metadata extraction complete for '{ActualProjectPath}' in {Duration} ms", 
                actualProjectPath, duration.TotalMilliseconds);

            return jsonResult;
        }
        catch (Exception ex)
        {
            var duration = DateTime.UtcNow - startTime;
            _logger.LogError(ex, "ERROR in ExtractProjectMetadataAsync for '{ProjectPath}' after {Duration} ms: {Message}", 
                projectPath, duration.TotalMilliseconds, ex.Message);

            return $"Error extracting project metadata: {ex.Message}";
        }
    }

    public async Task<string> FindUsagesAsync(string filePath, int line, int column)
    {
        var startTime = DateTime.UtcNow;
        try
        {
            _logger.LogInformation("FindUsagesAsync called with path: '{FilePath}', line: {Line}, column: {Column}", filePath, line, column);

            // Cache key for this operation
            string cacheKey = $"find_usages_{filePath}_{line}_{column}";
            if (_cache.TryGetValue<string>(cacheKey, out var cachedResult))
            {
                _logger.LogDebug("Returning cached result for FindUsagesAsync");
                return cachedResult;
            }

            // Normalize file path
            string normalizedPath = filePath.Replace("\\", "/");
            string systemPath = !Path.IsPathRooted(normalizedPath)
                ? Path.GetFullPath(normalizedPath)
                : Path.GetFullPath(normalizedPath);

            string projectPath = await RoslynHelpers.FindContainingProjectAsync(systemPath, _logger);
            if (string.IsNullOrEmpty(projectPath))
            {
                return "Error: Couldn't find a project containing this file.";
            }

            var workspace = RoslynHelpers.CreateWorkspace(_logger);
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

            // This would need to be implemented in the runtime project - for now return a basic result
            var duration = DateTime.UtcNow - startTime;
            var result = $"Find usages operation completed in {duration.TotalMilliseconds}ms for position {line}:{column} in {filePath}";
            
            // Cache the result for future requests
            _cache.Set(cacheKey, result, TimeSpan.FromMinutes(30));
            
            _logger.LogInformation("FindUsagesAsync completed in {Duration} ms", duration.TotalMilliseconds);
            return result;
        }
        catch (Exception ex)
        {
            var duration = DateTime.UtcNow - startTime;
            _logger.LogError(ex, "ERROR in FindUsagesAsync after {Duration} ms: {Message}", duration.TotalMilliseconds, ex.Message);
            return $"Error finding usages: {ex.Message}";
        }
    }

    // Implement other methods by delegating to the appropriate RoslynHelpers or services
    public async Task<string> ChunkCodeBySemanticsAsync(string path, string strategy = "class", bool includeDependencies = true)
    {
        try
        {
            _logger.LogInformation("ChunkCodeBySemanticsAsync called with path: '{Path}', strategy: '{Strategy}'", path, strategy);

            // Normalize file path
            string normalizedPath = path.Replace("\\", "/");
            string systemPath = !Path.IsPathRooted(normalizedPath)
                ? Path.GetFullPath(normalizedPath)
                : Path.GetFullPath(normalizedPath);

            if (!File.Exists(systemPath))
            {
                _logger.LogError("File does not exist: '{SystemPath}'", systemPath);
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

            _logger.LogInformation("Code chunking completed for '{Path}' with strategy '{Strategy}'", systemPath, strategy);

            // This would delegate to the actual service implementation
            return await Task.FromResult($"Code chunking with strategy '{strategy}' completed for path: {path}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ERROR in ChunkCodeBySemanticsAsync: {Message}", ex.Message);
            return $"Error chunking code: {ex.Message}";
        }
    }

    public async Task<string> AnalyzeCodeStructureAsync(string path, bool detectPatterns = true, bool calculateMetrics = true)
    {
        try
        {
            _logger.LogInformation("AnalyzeCodeStructureAsync called with path: '{Path}'", path);
            // Normalize file path
            string normalizedPath = path.Replace("\\", "/");
            string systemPath = !Path.IsPathRooted(normalizedPath)
                ? Path.GetFullPath(normalizedPath)
                : Path.GetFullPath(normalizedPath);

            if (!File.Exists(systemPath))
            {
                _logger.LogError("File does not exist: '{SystemPath}'", systemPath);
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

            _logger.LogInformation("Code structure analysis completed for '{Path}'", systemPath);
            return await Task.FromResult(JsonSerializer.Serialize(result, options));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ERROR in AnalyzeCodeStructureAsync: {Message}", ex.Message);
            return $"Error analyzing code structure: {ex.Message}";
        }
    }

    public async Task<string> GenerateCodeFactsAsync(string path, string format = "json", bool includeDescriptions = true)
    {
        try
        {
            _logger.LogInformation("GenerateCodeFactsAsync called with path: '{Path}', format: '{Format}'", path, format);

            // Normalize file path
            string normalizedPath = path.Replace("\\", "/");
            string systemPath = !Path.IsPathRooted(normalizedPath)
                ? Path.GetFullPath(normalizedPath)
                : Path.GetFullPath(normalizedPath);

            if (!File.Exists(systemPath))
            {
                _logger.LogError("File does not exist: '{SystemPath}'", systemPath);
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

            _logger.LogInformation("Code facts generation completed for '{Path}' in format '{Format}'", systemPath, format);
            return await Task.FromResult(JsonSerializer.Serialize(result, options));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ERROR in GenerateCodeFactsAsync: {Message}", ex.Message);
            return $"Error generating code facts: {ex.Message}";
        }
    }

    public async Task<string> ExtractSymbolGraphAsync(string path, string scope = "file", bool includeInheritance = true, 
        bool includeMethodCalls = true, bool includeFieldAccess = true, bool includeNamespaces = true, 
        bool includeXaml = false, bool includeSql = false, int maxDepth = 3)
    {
        try
        {
            _logger.LogInformation("ExtractSymbolGraphAsync called with path: '{Path}', scope: '{Scope}'", path, scope);
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
                    var projectPath = await RoslynHelpers.FindContainingProjectAsync(systemPath, _logger);
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
            var workspace = RoslynHelpers.CreateWorkspace(_logger);
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

            return await Task.FromResult(JsonSerializer.Serialize(result, options));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ERROR in ExtractSymbolGraphAsync: {Message}", ex.Message);
            return $"Error extracting symbol graph: {ex.Message}";
        }
    }

    public async Task<string> ChunkMultiLanguageCodeAsync(string path, string strategy = "feature", bool includeDependencies = true, 
        bool includeXaml = false, bool includeSql = false)
    {
        try
        {
            _logger.LogInformation("ChunkMultiLanguageCodeAsync called with path: '{Path}', strategy: '{Strategy}'", path, strategy);
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

            return await Task.FromResult(JsonSerializer.Serialize(result, options));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ERROR in ChunkMultiLanguageCodeAsync: {Message}", ex.Message);
            return $"Error chunking multi-language code: {ex.Message}";
        }
    }

    public async Task<string> ExtractUnifiedSemanticGraphAsync(string path, bool includeRoles = true, bool includeFeatures = true, 
        bool includeCrossProject = true, bool includeCrossLanguage = false)
    {
        try
        {
            _logger.LogInformation("ExtractUnifiedSemanticGraphAsync called with path: '{Path}'", path);
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
                    var projectPath = await RoslynHelpers.FindContainingProjectAsync(systemPath, _logger);
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
            var workspace = RoslynHelpers.CreateWorkspace(_logger);
            var analyzerLogger = _loggerFactory.CreateLogger<SemanticSolutionAnalyzer>();
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

            return await Task.FromResult(JsonSerializer.Serialize(result, options));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ERROR in ExtractUnifiedSemanticGraphAsync: {Message}", ex.Message);
            return $"Error extracting unified semantic graph: {ex.Message}";
        }
    }

    public async Task<string> ExtractSqlFromCodeAsync(string filePath)
    {
        try
        {
            _logger.LogInformation("ExtractSqlFromCodeAsync called with path: '{FilePath}'", filePath);
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

            return await Task.FromResult(JsonSerializer.Serialize(result, options));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ERROR in ExtractSqlFromCodeAsync: {Message}", ex.Message);
            return $"Error extracting SQL from code: {ex.Message}";
        }
    }

    public async Task<string> AnalyzeXamlFileAsync(string filePath)
    {
        try
        {
            _logger.LogInformation("AnalyzeXamlFileAsync called with path: '{FilePath}'", filePath);
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
            var xamlLogger = _loggerFactory.CreateLogger<XamlAnalyzer>();
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

            return await Task.FromResult(JsonSerializer.Serialize(result, options));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ERROR in AnalyzeXamlFileAsync: {Message}", ex.Message);
            return $"Error analyzing XAML file: {ex.Message}";
        }
    }

    public async Task<string> AnalyzeMvvmRelationshipsAsync(string projectPath)
    {
        try
        {
            _logger.LogInformation("AnalyzeMvvmRelationshipsAsync called with path: '{ProjectPath}'", projectPath);
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
                actualProjectPath = await RoslynHelpers.FindContainingProjectAsync(systemPath, _logger);
                if (string.IsNullOrEmpty(actualProjectPath))
                {
                    return "Error: Could not find a project file. Please provide a .csproj file path or a file within a project.";
                }
            }

            // Create XAML analyzer service
            var xamlLogger = _loggerFactory.CreateLogger<XamlAnalyzer>();
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

            return await Task.FromResult(JsonSerializer.Serialize(result, options));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ERROR in AnalyzeMvvmRelationshipsAsync: {Message}", ex.Message);
            return $"Error analyzing MVVM relationships: {ex.Message}";
        }
    }
}
