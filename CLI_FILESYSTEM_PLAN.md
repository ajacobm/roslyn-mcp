# Local CLI & Filesystem Integration Implementation Plan

## Overview
This document details the implementation of enhanced CLI capabilities and local filesystem-based repository analysis for the Roslyn MCP Server, providing robust local development workflows alongside the remote repository features.

## CLI Architecture Design

### Command Structure
```
roslyn-mcp
├── serve              # Start MCP server (existing enhanced with modes)
├── analyze            # Direct analysis commands
│   ├── repository     # Analyze remote repository
│   ├── local          # Analyze local directory
│   ├── project        # Analyze specific project file
│   └── file           # Analyze individual file
├── discover           # Discovery commands  
│   ├── structure      # Discover project structure
│   └── dependencies   # Analyze dependencies
├── watch              # File watching commands
│   ├── directory      # Watch directory for changes
│   └── project        # Watch specific project
└── config             # Configuration management
    ├── show           # Show current configuration  
    ├── set            # Set configuration values
    └── validate       # Validate configuration
```

### Enhanced CLI Implementation
```csharp
public class Program
{
    public static async Task Main(string[] args)
    {
        var app = new CommandApp();
        
        // Configure command types
        app.Configure(config =>
        {
            config.AddCommand<ServeCommand>("serve")
                  .WithDescription("Start the MCP server")
                  .WithExample(new[] { "serve", "--mode", "http", "--port", "3001" });
                  
            config.AddBranch("analyze", analyze =>
            {
                analyze.SetDescription("Analysis commands");
                analyze.AddCommand<AnalyzeRepositoryCommand>("repository")
                       .WithAlias("repo")
                       .WithDescription("Analyze a git repository");
                analyze.AddCommand<AnalyzeLocalCommand>("local")
                       .WithDescription("Analyze a local directory");
                analyze.AddCommand<AnalyzeProjectCommand>("project")
                       .WithDescription("Analyze a specific project file");
                analyze.AddCommand<AnalyzeFileCommand>("file")
                       .WithDescription("Analyze a single C# file");
            });
            
            config.AddBranch("discover", discover =>
            {
                discover.SetDescription("Discovery commands");
                discover.AddCommand<DiscoverStructureCommand>("structure")
                        .WithDescription("Discover project structure");
                discover.AddCommand<DiscoverDependenciesCommand>("dependencies")
                        .WithDescription("Analyze dependencies");
            });
            
            config.AddBranch("watch", watch =>
            {
                watch.SetDescription("File watching commands");
                watch.AddCommand<WatchDirectoryCommand>("directory")
                     .WithDescription("Watch a directory for changes");
                watch.AddCommand<WatchProjectCommand>("project")
                     .WithDescription("Watch a project for changes");
            });
            
            config.AddBranch("config", config =>
            {
                config.SetDescription("Configuration management");
                config.AddCommand<ConfigShowCommand>("show");
                config.AddCommand<ConfigSetCommand>("set");
                config.AddCommand<ConfigValidateCommand>("validate");
            });
        });
        
        await app.RunAsync(args);
    }
}
```

### Command Implementations

#### Local Analysis Command
```csharp
public class AnalyzeLocalCommand : AsyncCommand<AnalyzeLocalCommand.Settings>
{
    public class Settings : CommandSettings
    {
        [Description("Local directory or repository path")]
        [CommandArgument(0, "<path>")]
        public string Path { get; set; } = string.Empty;
        
        [Description("Output format")]
        [CommandOption("-f|--format")]
        [DefaultValue("json")]
        public OutputFormat Format { get; set; } = OutputFormat.Json;
        
        [Description("Output file path")]
        [CommandOption("-o|--output")]
        public string? OutputFile { get; set; }
        
        [Description("Include full project metadata")]
        [CommandOption("--full-metadata")]
        [DefaultValue(true)]
        public bool FullMetadata { get; set; } = true;
        
        [Description("Analyze specific project within directory")]
        [CommandOption("-p|--project")]
        public string? ProjectFilter { get; set; }
        
        [Description("Watch for file changes")]
        [CommandOption("-w|--watch")]
        [DefaultValue(false)]
        public bool WatchMode { get; set; }
        
        [Description("Include analysis of XAML files")]
        [CommandOption("--include-xaml")]
        [DefaultValue(false)]
        public bool IncludeXaml { get; set; }
        
        [Description("Include analysis of SQL in code")]
        [CommandOption("--include-sql")]
        [DefaultValue(false)]
        public bool IncludeSql { get; set; }
    }
    
    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        try
        {
            // Validate path exists
            if (!Directory.Exists(settings.Path) && !File.Exists(settings.Path))
            {
                AnsiConsole.MarkupLine($"[red]Error:[/] Path not found: {settings.Path}");
                return 1;
            }
            
            // Initialize local analyzer
            var analyzer = new LocalRepositoryAnalyzer(GetLogger());
            
            // Show progress
            var analysisResult = await AnsiConsole.Progress()
                .StartAsync(async ctx =>
                {
                    var task = ctx.AddTask("Analyzing local repository");
                    
                    var result = await analyzer.AnalyzeAsync(new LocalAnalysisRequest
                    {
                        Path = Path.GetFullPath(settings.Path),
                        ProjectFilter = settings.ProjectFilter,
                        IncludeFullMetadata = settings.FullMetadata,
                        IncludeXaml = settings.IncludeXaml,
                        IncludeSql = settings.IncludeSql,
                        ProgressCallback = progress =>
                        {
                            task.Value = progress.PercentComplete;
                            task.Description = progress.CurrentOperation;
                        }
                    });
                    
                    task.Value = 100;
                    return result;
                });
            
            // Output results
            var output = FormatOutput(analysisResult, settings.Format);
            
            if (!string.IsNullOrEmpty(settings.OutputFile))
            {
                await File.WriteAllTextAsync(settings.OutputFile, output);
                AnsiConsole.MarkupLine($"[green]Analysis saved to:[/] {settings.OutputFile}");
            }
            else
            {
                Console.WriteLine(output);
            }
            
            // Start watch mode if requested
            if (settings.WatchMode)
            {
                AnsiConsole.MarkupLine("[yellow]Watch mode enabled. Press Ctrl+C to exit.[/]");
                await StartWatchMode(settings.Path, analyzer);
            }
            
            return 0;
        }
        catch (Exception ex)
        {
            AnsiConsole.WriteException(ex);
            return 1;
        }
    }
}
```

#### Project Discovery Command  
```csharp
public class DiscoverStructureCommand : AsyncCommand<DiscoverStructureCommand.Settings>
{
    public class Settings : CommandSettings
    {
        [Description("Directory path to analyze")]
        [CommandArgument(0, "<path>")]
        public string Path { get; set; } = string.Empty;
        
        [Description("Output format")]
        [CommandOption("-f|--format")]
        [DefaultValue("tree")]
        public DiscoveryFormat Format { get; set; } = DiscoveryFormat.Tree;
        
        [Description("Include file sizes")]
        [CommandOption("--sizes")]
        [DefaultValue(false)]
        public bool IncludeSizes { get; set; }
        
        [Description("Include git information")]
        [CommandOption("--git-info")]
        [DefaultValue(true)]
        public bool IncludeGitInfo { get; set; }
        
        [Description("Filter by file extensions")]
        [CommandOption("--filter")]
        public string[]? FileExtensions { get; set; }
    }
    
    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        var discoverer = new ProjectStructureDiscoverer();
        var structure = await discoverer.DiscoverAsync(settings.Path);
        
        switch (settings.Format)
        {
            case DiscoveryFormat.Tree:
                DisplayTreeView(structure, settings);
                break;
            case DiscoveryFormat.List:
                DisplayListView(structure, settings);
                break;
            case DiscoveryFormat.Json:
                Console.WriteLine(JsonSerializer.Serialize(structure, JsonOptions));
                break;
        }
        
        return 0;
    }
    
    private void DisplayTreeView(ProjectStructure structure, Settings settings)
    {
        var tree = new Tree($"[bold blue]{structure.RootPath}[/]");
        
        if (settings.IncludeGitInfo && structure.GitInfo != null)
        {
            tree.AddNode($"[dim]Git: {structure.GitInfo.Branch} ({structure.GitInfo.CommitHash[..8]})[/]");
        }
        
        // Add solution files
        if (structure.SolutionFiles.Any())
        {
            var solutionNode = tree.AddNode("[yellow]Solutions[/]");
            foreach (var sln in structure.SolutionFiles)
            {
                var slnNode = solutionNode.AddNode($"[yellow]{Path.GetFileName(sln.FilePath)}[/]");
                foreach (var project in sln.Projects)
                {
                    slnNode.AddNode($"[cyan]{Path.GetFileName(project)}[/]");
                }
            }
        }
        
        // Add standalone projects
        if (structure.StandaloneProjects.Any())
        {
            var projectNode = tree.AddNode("[cyan]Projects[/]");
            foreach (var project in structure.StandaloneProjects)
            {
                var projNode = projectNode.AddNode($"[cyan]{Path.GetFileName(project.FilePath)}[/]");
                if (settings.IncludeSizes)
                {
                    var size = new FileInfo(project.FilePath).Length;
                    projNode.AddNode($"[dim]Size: {FormatFileSize(size)}[/]");
                }
            }
        }
        
        AnsiConsole.Write(tree);
    }
}
```

## Local Repository Analyzer

### Core Analysis Engine
```csharp
public class LocalRepositoryAnalyzer
{
    private readonly ILogger<LocalRepositoryAnalyzer> _logger;
    private readonly ProjectMetadataExtractor _metadataExtractor;
    private readonly FileSystemWatcher? _watcher;

    public async Task<LocalAnalysisResult> AnalyzeAsync(LocalAnalysisRequest request)
    {
        var startTime = DateTime.UtcNow;
        
        try
        {
            // Detect repository type
            var repoType = DetectRepositoryType(request.Path);
            
            // Discover project structure
            request.ProgressCallback?.Invoke(new AnalysisProgress(10, "Discovering project structure"));
            var structure = await DiscoverProjectStructure(request.Path);
            
            // Filter projects if specified
            var projectsToAnalyze = FilterProjects(structure, request.ProjectFilter);
            
            // Analyze each project
            var projectAnalyses = new List<ProjectAnalysisResult>();
            var progressStep = 80.0 / Math.Max(projectsToAnalyze.Count, 1);
            
            for (int i = 0; i < projectsToAnalyze.Count; i++)
            {
                var project = projectsToAnalyze[i];
                request.ProgressCallback?.Invoke(new AnalysisProgress(
                    10 + (int)(i * progressStep), 
                    $"Analyzing {Path.GetFileName(project.FilePath)}"));
                
                var analysis = await AnalyzeProject(project, request);
                projectAnalyses.Add(analysis);
            }
            
            // Perform cross-project analysis
            request.ProgressCallback?.Invoke(new AnalysisProgress(90, "Performing cross-project analysis"));
            var crossProjectAnalysis = await PerformCrossProjectAnalysis(projectAnalyses);
            
            // Create final result
            request.ProgressCallback?.Invoke(new AnalysisProgress(100, "Complete"));
            
            return new LocalAnalysisResult
            {
                Path = request.Path,
                RepositoryType = repoType,
                Structure = structure,
                ProjectAnalyses = projectAnalyses,
                CrossProjectAnalysis = crossProjectAnalysis,
                AnalysisMetadata = new AnalysisMetadata
                {
                    StartTime = startTime,
                    EndTime = DateTime.UtcNow,
                    TotalFiles = projectAnalyses.Sum(p => p.FileCount),
                    TotalLines = projectAnalyses.Sum(p => p.LineCount)
                }
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to analyze local repository: {Path}", request.Path);
            throw;
        }
    }
}

public record LocalAnalysisRequest
{
    public string Path { get; init; } = string.Empty;
    public string? ProjectFilter { get; init; }
    public bool IncludeFullMetadata { get; init; } = true;
    public bool IncludeXaml { get; init; }
    public bool IncludeSql { get; init; }
    public Action<AnalysisProgress>? ProgressCallback { get; init; }
}

public record AnalysisProgress(int PercentComplete, string CurrentOperation);
```

### File System Watching
```csharp
public class FileSystemWatchService : IDisposable
{
    private readonly List<FileSystemWatcher> _watchers = new();
    private readonly ILogger<FileSystemWatchService> _logger;
    private readonly ConcurrentQueue<FileChangeEvent> _changeQueue = new();
    private readonly Timer _processingTimer;

    public event EventHandler<FileChangeEvent>? FileChanged;

    public void StartWatching(string path, WatchConfiguration config)
    {
        var watcher = new FileSystemWatcher(path)
        {
            IncludeSubdirectories = config.IncludeSubdirectories,
            NotifyFilter = config.NotifyFilter,
            Filter = "*.*"
        };
        
        // Configure file type filters
        watcher.Changed += (sender, e) => OnFileChanged(e, config);
        watcher.Created += (sender, e) => OnFileChanged(e, config);
        watcher.Deleted += (sender, e) => OnFileChanged(e, config);
        watcher.Renamed += (sender, e) => OnFileRenamed(e, config);
        
        watcher.EnableRaisingEvents = true;
        _watchers.Add(watcher);
        
        _logger.LogInformation("Started watching directory: {Path}", path);
    }
    
    private void OnFileChanged(FileSystemEventArgs e, WatchConfiguration config)
    {
        // Filter by file extensions
        if (config.FileExtensions?.Any() == true)
        {
            var extension = Path.GetExtension(e.FullPath).ToLower();
            if (!config.FileExtensions.Contains(extension))
                return;
        }
        
        // Debounce rapid changes
        var changeEvent = new FileChangeEvent
        {
            FullPath = e.FullPath,
            ChangeType = e.ChangeType,
            Timestamp = DateTime.UtcNow
        };
        
        _changeQueue.Enqueue(changeEvent);
    }
    
    private void ProcessChangeQueue(object? state)
    {
        var processedFiles = new HashSet<string>();
        
        while (_changeQueue.TryDequeue(out var changeEvent))
        {
            // Debounce: only process the latest change for each file
            if (!processedFiles.Add(changeEvent.FullPath))
                continue;
                
            // Ensure file still exists and is stable
            if (IsFileStable(changeEvent.FullPath))
            {
                FileChanged?.Invoke(this, changeEvent);
            }
        }
    }
    
    private bool IsFileStable(string filePath)
    {
        try
        {
            // Try to open file exclusively to ensure it's not being written
            using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            return true;
        }
        catch
        {
            return false;
        }
    }
}

public record WatchConfiguration
{
    public bool IncludeSubdirectories { get; init; } = true;
    public NotifyFilters NotifyFilter { get; init; } = NotifyFilters.LastWrite | NotifyFilters.FileName;
    public string[]? FileExtensions { get; init; } = { ".cs", ".csproj", ".sln", ".xaml" };
    public TimeSpan DebounceDelay { get; init; } = TimeSpan.FromMilliseconds(500);
}

public record FileChangeEvent
{
    public string FullPath { get; init; } = string.Empty;
    public WatcherChangeTypes ChangeType { get; init; }
    public DateTime Timestamp { get; init; }
}
```

### Interactive Watch Mode
```csharp
public class InteractiveWatchMode
{
    private readonly LocalRepositoryAnalyzer _analyzer;
    private readonly FileSystemWatchService _watchService;
    private readonly string _basePath;

    public async Task StartAsync(string path)
    {
        AnsiConsole.MarkupLine($"[green]Starting watch mode for:[/] {path}");
        AnsiConsole.MarkupLine("[dim]Press 'q' to quit, 'r' to force refresh, 'h' for help[/]");
        
        // Setup file system watching
        _watchService.StartWatching(path, new WatchConfiguration());
        _watchService.FileChanged += OnFileChanged;
        
        // Start interactive loop
        await RunInteractiveLoop();
    }
    
    private async Task RunInteractiveLoop()
    {
        while (true)
        {
            var key = Console.ReadKey(true);
            
            switch (char.ToLower(key.KeyChar))
            {
                case 'q':
                    AnsiConsole.MarkupLine("\n[yellow]Exiting watch mode...[/]");
                    return;
                    
                case 'r':
                    AnsiConsole.MarkupLine("\n[blue]Force refreshing analysis...[/]");
                    await PerformFullAnalysis();
                    break;
                    
                case 'h':
                    ShowHelpMenu();
                    break;
                    
                case 's':
                    await ShowCurrentStatus();
                    break;
                    
                case 'f':
                    await FilterAnalysis();
                    break;
            }
        }
    }
    
    private void OnFileChanged(object? sender, FileChangeEvent e)
    {
        var fileName = Path.GetFileName(e.FullPath);
        var changeType = e.ChangeType.ToString().ToLower();
        
        AnsiConsole.MarkupLine($"[dim]{DateTime.Now:HH:mm:ss}[/] [yellow]{changeType}[/] {fileName}");
        
        // Trigger incremental analysis
        _ = Task.Run(async () =>
        {
            try
            {
                await PerformIncrementalAnalysis(e.FullPath);
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"[red]Error analyzing {fileName}: {ex.Message}[/]");
            }
        });
    }
    
    private void ShowHelpMenu()
    {
        var table = new Table()
            .AddColumn("Key")
            .AddColumn("Action");
            
        table.AddRow("q", "Quit watch mode");
        table.AddRow("r", "Force refresh analysis");  
        table.AddRow("s", "Show current status");
        table.AddRow("f", "Filter analysis");
        table.AddRow("h", "Show this help");
        
        AnsiConsole.Write(table);
    }
}
```

## Configuration Management

### Configuration System
```csharp
public class RoslynMcpConfiguration
{
    public string WorkingDirectory { get; set; } = "./temp";
    public string[]? DefaultFileExtensions { get; set; } = { ".cs", ".csproj", ".sln", ".xaml" };
    public bool IncludeGitInfo { get; set; } = true;
    public bool EnableFileWatching { get; set; } = true;
    public int MaxConcurrentAnalyses { get; set; } = Environment.ProcessorCount;
    public TimeSpan WatchDebounceDelay { get; set; } = TimeSpan.FromMilliseconds(500);
    public LogLevel DefaultLogLevel { get; set; } = LogLevel.Information;
    public OutputFormat DefaultOutputFormat { get; set; } = OutputFormat.Json;
    
    // Authentication
    public string? GitHubToken { get; set; }
    public string? AzureDevOpsToken { get; set; }
    
    // Analysis settings
    public bool AnalyzeXamlByDefault { get; set; } = false;
    public bool AnalyzeSqlByDefault { get; set; } = false;
    public bool RunCodeAnalyzersByDefault { get; set; } = true;
    
    public static RoslynMcpConfiguration Load()
    {
        var configPath = GetConfigFilePath();
        
        if (File.Exists(configPath))
        {
            var json = File.ReadAllText(configPath);
            return JsonSerializer.Deserialize<RoslynMcpConfiguration>(json) ?? new();
        }
        
        return new();
    }
    
    public void Save()
    {
        var configPath = GetConfigFilePath();
        var configDir = Path.GetDirectoryName(configPath)!;
        
        if (!Directory.Exists(configDir))
        {
            Directory.CreateDirectory(configDir);
        }
        
        var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(configPath, json);
    }
    
    private static string GetConfigFilePath()
    {
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(userProfile, ".roslyn-mcp", "config.json");
    }
}
```

### Configuration Commands
```csharp
public class ConfigShowCommand : Command<ConfigShowCommand.Settings>
{
    public class Settings : CommandSettings { }
    
    public override int Execute(CommandContext context, Settings settings)
    {
        var config = RoslynMcpConfiguration.Load();
        
        var table = new Table()
            .AddColumn("Setting")
            .AddColumn("Value")
            .AddColumn("Description");
            
        table.AddRow("WorkingDirectory", config.WorkingDirectory, "Directory for temporary files");
        table.AddRow("IncludeGitInfo", config.IncludeGitInfo.ToString(), "Include git repository information");
        table.AddRow("EnableFileWatching", config.EnableFileWatching.ToString(), "Enable file system watching");
        table.AddRow("DefaultLogLevel", config.DefaultLogLevel.ToString(), "Default logging level");
        table.AddRow("MaxConcurrentAnalyses", config.MaxConcurrentAnalyses.ToString(), "Maximum parallel analyses");
        
        AnsiConsole.Write(table);
        return 0;
    }
}

public class ConfigSetCommand : Command<ConfigSetCommand.Settings>
{
    public class Settings : CommandSettings
    {
        [Description("Configuration key to set")]
        [CommandArgument(0, "<key>")]
        public string Key { get; set; } = string.Empty;
        
        [Description("Configuration value")]
        [CommandArgument(1, "<value>")]
        public string Value { get; set; } = string.Empty;
    }
    
    public override int Execute(CommandContext context, Settings settings)
    {
        var config = RoslynMcpConfiguration.Load();
        
        if (!SetConfigurationValue(config, settings.Key, settings.Value))
        {
            AnsiConsole.MarkupLine($"[red]Error:[/] Unknown configuration key: {settings.Key}");
            return 1;
        }
        
        config.Save();
        AnsiConsole.MarkupLine($"[green]Set {settings.Key} = {settings.Value}[/]");
        
        return 0;
    }
    
    private bool SetConfigurationValue(RoslynMcpConfiguration config, string key, string value)
    {
        return key.ToLower() switch
        {
            "workingdirectory" => SetValue(() => config.WorkingDirectory = value),
            "includegitinfo" => SetValue(() => config.IncludeGitInfo = bool.Parse(value)),
            "enablefilewatching" => SetValue(() => config.EnableFileWatching = bool.Parse(value)),
            "maxconcurrentanalyses" => SetValue(() => config.MaxConcurrentAnalyses = int.Parse(value)),
            "defaultloglevel" => SetValue(() => config.DefaultLogLevel = Enum.Parse<LogLevel>(value)),
            "analyzexamlbydefault" => SetValue(() => config.AnalyzeXamlByDefault = bool.Parse(value)),
            "analyzesqlbydefault" => SetValue(() => config.AnalyzeSqlByDefault = bool.Parse(value)),
            _ => false
        };
        
        static bool SetValue(Action setter)
        {
            try
            {
                setter();
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
```

## Output Formatting

### Multiple Output Formats
```csharp
public static class OutputFormatter
{
    public static string FormatAnalysisResult(LocalAnalysisResult result, OutputFormat format)
    {
        return format switch
        {
            OutputFormat.Json => JsonSerializer.Serialize(result, JsonOptions),
            OutputFormat.Yaml => ConvertToYaml(result),
            OutputFormat.Markdown => FormatAsMarkdown(result),
            OutputFormat.Summary => FormatAsSummary(result),
            OutputFormat.Tree => FormatAsTree(result),
            _ => throw new ArgumentException($"Unsupported format: {format}")
        };
    }
    
    private static string FormatAsMarkdown(LocalAnalysisResult result)
    {
        var markdown = new StringBuilder();
        
        markdown.AppendLine($"# Analysis Report: {Path.GetFileName(result.Path)}");
        markdown.AppendLine();
        markdown.AppendLine($"**Path**: {result.Path}");
        markdown.AppendLine($"**Repository Type**: {result.RepositoryType}");
        markdown.AppendLine($"**Analysis Date**: {result.AnalysisMetadata.StartTime:yyyy-MM-dd HH:mm:ss}");
        markdown.AppendLine($"**Duration**: {result.AnalysisMetadata.EndTime - result.AnalysisMetadata.StartTime}");
        markdown.AppendLine();
        
        // Project Structure
        markdown.AppendLine("## Project Structure");
        markdown.AppendLine();
        
        if (result.Structure.SolutionFiles.Any())
        {
            markdown.AppendLine("### Solutions");
            foreach (var sln in result.Structure.SolutionFiles)
            {
                markdown.AppendLine($"- **{Path.GetFileName(sln.FilePath)}**");
                foreach (var project in sln.Projects)
                {
                    markdown.AppendLine($"  - {Path.GetFileName(project)}");
                }
            }
            markdown.AppendLine();
        }
        
        // Analysis Summary
        markdown.AppendLine("## Analysis Summary");
        markdown.AppendLine();
        markdown.AppendLine($"- **Total Files**: {result.AnalysisMetadata.TotalFiles}");
        markdown.AppendLine($"- **Total Lines**: {result.AnalysisMetadata.TotalLines:N0}");
        markdown.AppendLine($"- **Projects Analyzed**: {result.ProjectAnalyses.Count}");
        markdown.AppendLine();
        
        // Individual Project Analyses
        foreach (var projectAnalysis in result.ProjectAnalyses)
        {
            markdown.AppendLine($"### {Path.GetFileName(projectAnalysis.ProjectFile.FilePath)}");
            markdown.AppendLine();
            
            if (projectAnalysis.Metadata != null)
            {
                markdown.AppendLine($"- **Target Framework**: {projectAnalysis.Metadata.TargetFramework}");
                markdown.AppendLine($"- **Types**: {projectAnalysis.Metadata.Types.Count}");
                markdown.AppendLine($"- **Dependencies**: {projectAnalysis.Metadata.Dependencies.Count}");
                markdown.AppendLine();
            }
        }
        
        return markdown.ToString();
    }
    
    private static string FormatAsSummary(LocalAnalysisResult result)
    {
        var summary = new StringBuilder();
        
        summary.AppendLine($"Analysis Summary for {Path.GetFileName(result.Path)}");
        summary.AppendLine(new string('=', 50));
        summary.AppendLine($"Repository Type: {result.RepositoryType}");
        summary.AppendLine($"Total Projects: {result.ProjectAnalyses.Count}");
        summary.AppendLine($"Total Files: {result.AnalysisMetadata.TotalFiles}");
        summary.AppendLine($"Total Lines: {result.AnalysisMetadata.TotalLines:N0}");
        summary.AppendLine($"Analysis Duration: {result.AnalysisMetadata.EndTime - result.AnalysisMetadata.StartTime}");
        summary.AppendLine();
        
        if (result.Structure.SolutionFiles.Any())
        {
            summary.AppendLine("Solutions:");
            foreach (var sln in result.Structure.SolutionFiles)
            {
                summary.AppendLine($"  ✓ {Path.GetFileName(sln.FilePath)} ({sln.Projects.Count} projects)");
            }
            summary.AppendLine();
        }
        
        summary.AppendLine("Projects:");
        foreach (var project in result.ProjectAnalyses)
        {
            var name = Path.GetFileName(project.ProjectFile.FilePath);
            var targetFramework = project.Metadata?.TargetFramework ?? "Unknown";
            summary.AppendLine($"  ✓ {name} ({targetFramework})");
        }
        
        return summary.ToString();
    }
}

public enum OutputFormat
{
    Json,
    Yaml,
    Markdown,
    Summary,
    Tree
}
```

## Testing Strategy

### CLI Command Testing
```csharp
[TestFixture]
public class CliCommandTests
{
    private string _testDirectory;
    
    [SetUp]
    public void SetUp()
    {
        _testDirectory = CreateTestRepository();
    }
    
    [TearDown] 
    public void TearDown()
    {
        if (Directory.Exists(_testDirectory))
        {
            Directory.Delete(_testDirectory, true);
        }
    }
    
    [Test]
    public async Task AnalyzeLocalCommand_ValidDirectory_ProducesOutput()
    {
        // Arrange
        var command = new AnalyzeLocalCommand();
        var settings = new AnalyzeLocalCommand.Settings
        {
            Path = _testDirectory,
            Format = OutputFormat.Json
        };
        
        // Act
        var exitCode = await command.ExecuteAsync(new CommandContext(settings, null, "analyze"), settings);
        
        // Assert
        Assert.That(exitCode, Is.EqualTo(0));
    }
    
    [Test]
    public async Task DiscoverStructureCommand_ValidDirectory_ShowsTree()
    {
        // Arrange
        var command = new DiscoverStructureCommand();
        var settings = new DiscoverStructureCommand.Settings
        {
            Path = _testDirectory,
            Format = DiscoveryFormat.Tree
        };
        
        using var consoleOutput = new StringWriter();
        Console.SetOut(consoleOutput);
        
        // Act
        var exitCode = await command.ExecuteAsync(new CommandContext(settings, null, "discover"), settings);
        var output = consoleOutput.ToString();
        
        // Assert
        Assert.That(exitCode, Is.EqualTo(0));
        Assert.That(output, Contains.Substring("Solutions"));
    }
}
```

### File System Watching Tests
```csharp
[TestFixture]
public class FileSystemWatchingTests
{
    [Test]
    public async Task FileSystemWatchService_FileChanged_RaisesEvent()
    {
        // Arrange
        var watchService = new FileSystemWatchService(NullLogger<FileSystemWatchService>.Instance);
        var tempDir = Path.GetTempPath();
        var testFile = Path.Combine(tempDir, $"test_{Guid.NewGuid()}.cs");
        
        var eventRaised = false;
        watchService.FileChanged += (sender, e) =>
        {
            if (e.FullPath == testFile) eventRaised = true;
        };
        
        watchService.StartWatching(tempDir, new WatchConfiguration
        {
            IncludeSubdirectories = false,
            FileExtensions = new[] { ".cs" }
        });
        
        try
        {
            // Act
            await File.WriteAllTextAsync(testFile, "// Test content");
            await Task.Delay(1000); // Allow for file system events
            
            // Assert
            Assert.That(eventRaised, Is.True);
        }
        finally
        {
            if (File.Exists(testFile)) File.Delete(testFile);
            watchService.Dispose();
        }
    }
}
```

## Performance Optimizations

### Incremental Analysis
```csharp
public class IncrementalAnalyzer
{
    private readonly Dictionary<string, FileAnalysisCache> _cache = new();
    
    public async Task<bool> RequiresAnalysis(string filePath)
    {
        var fileInfo = new FileInfo(filePath);
        
        if (!_cache.TryGetValue(filePath, out var cached))
        {
            return true; // Never analyzed
        }
        
        return cached.LastModified < fileInfo.LastWriteTime;
    }
    
    public async Task UpdateCache(string filePath, ProjectMetadata analysis)
    {
        var fileInfo = new FileInfo(filePath);
        _cache[filePath] = new FileAnalysisCache
        {
            LastModified = fileInfo.LastWriteTime,
            Analysis = analysis,
            CacheTime = DateTime.UtcNow
        };
    }
}

public record FileAnalysisCache
{
    public DateTime LastModified { get; init; }
    public ProjectMetadata Analysis { get; init; } = new();
    public DateTime CacheTime { get; init; }
}
```

### Parallel Processing
```csharp
public class ParallelAnalyzer
{
    private readonly SemaphoreSlim _semaphore;
    
    public ParallelAnalyzer(int maxConcurrency = 0)
    {
        var concurrency = maxConcurrency > 0 ? maxConcurrency : Environment.ProcessorCount;
        _semaphore = new SemaphoreSlim(concurrency, concurrency);
    }
    
    public async Task<List<ProjectAnalysisResult>> AnalyzeProjectsAsync(
        List<ProjectFileInfo> projects,
        LocalAnalysisRequest request)
    {
        var tasks = projects.Select(async project =>
        {
            await _semaphore.WaitAsync();
            try
            {
                return await AnalyzeProject(project, request);
            }
            finally
            {
                _semaphore.Release();
            }
        });
        
        return (await Task.WhenAll(tasks)).ToList();
    }
}
```

---
*Implementation Timeline: 2 weeks*  
*Dependencies: Spectre.Console, System.CommandLine*  
*Status: Ready for Development*