# Git Repository Integration Implementation Plan

## Overview
This document details the technical implementation of git repository support for the Roslyn MCP Server, enabling direct cloning and analysis of GitHub and Azure DevOps repositories.

## Architecture Design

### Core Components

#### 1. GitRepositoryService
```csharp
public class GitRepositoryService : IDisposable
{
    private readonly ILogger<GitRepositoryService> _logger;
    private readonly string _workingDirectory;
    private readonly Dictionary<string, Repository> _activeRepositories;

    public async Task<GitRepositoryInfo> CloneRepositoryAsync(
        string repositoryUrl, 
        string localPath, 
        GitCredentials? credentials = null)
    
    public async Task<GitRepositoryInfo> PullLatestAsync(
        string repositoryPath)
    
    public async Task<List<string>> DiscoverProjectFilesAsync(
        string repositoryPath, 
        ProjectFileType fileTypes = ProjectFileType.All)
}
```

#### 2. Authentication Pattern
```csharp
public class GitCredentials
{
    public string? GitHubToken { get; init; }
    public string? AzureDevOpsToken { get; init; }
    public string? Username { get; init; }
    public AuthenticationType Type { get; init; }
}

public enum AuthenticationType
{
    PersonalAccessToken,
    BasicAuth,
    SshKey
}
```

### Enhanced MCP Tools

#### 1. Repository Analysis Tool
```csharp
[McpServerTool, Description("Clone and analyze a git repository with comprehensive project discovery")]
public static async Task<string> AnalyzeGitRepository(
    [Description("Git repository URL (https://github.com/user/repo or Azure DevOps URL)")] 
    string repositoryUrl,
    
    [Description("Target branch (default: main/master)")] 
    string branch = "main",
    
    [Description("Working directory for cloning (container-safe path)")] 
    string? workingPath = null,
    
    [Description("Focus analysis on specific solution file")] 
    string? solutionFile = null,
    
    [Description("Include full project metadata in analysis")] 
    bool includeFullMetadata = true)
{
    try
    {
        var gitService = new GitRepositoryService(_workingDirectory, Logger);
        var credentials = GitCredentials.FromEnvironment();
        
        // Clone repository
        var repoInfo = await gitService.CloneRepositoryAsync(repositoryUrl, workingPath, credentials);
        
        // Discover project structure
        var projectFiles = await gitService.DiscoverProjectFilesAsync(repoInfo.LocalPath);
        
        // Determine working context (SLN preferred)
        var workingContext = DetermineWorkingContext(projectFiles, solutionFile);
        
        // Perform analysis based on working context
        var analysisResult = await PerformRepositoryAnalysis(workingContext, includeFullMetadata);
        
        // Return structured results
        return JsonSerializer.Serialize(new RepositoryAnalysisResult
        {
            Repository = repoInfo,
            ProjectStructure = projectFiles,
            WorkingContext = workingContext,
            Analysis = analysisResult,
            ProcessedAt = DateTime.UtcNow
        }, JsonOptions);
    }
    catch (Exception ex)
    {
        Logger.LogError(ex, "Failed to analyze repository: {RepositoryUrl}", repositoryUrl);
        return $"Error analyzing repository: {ex.Message}";
    }
}
```

#### 2. Project Discovery Tool
```csharp
[McpServerTool, Description("Discover and preprocess .NET project structure in a repository")]
public static async Task<string> DiscoverProjectStructure(
    [Description("Repository URL or local path")] 
    string repositoryPath,
    
    [Description("Return format: 'flat' for simple list, 'hierarchical' for tree structure")] 
    string format = "hierarchical")
{
    var discoveryResult = new ProjectDiscoveryResult();
    
    try
    {
        var gitService = new GitRepositoryService(_workingDirectory, Logger);
        bool isRemote = Uri.TryCreate(repositoryPath, UriKind.Absolute, out _);
        
        string workingPath;
        if (isRemote)
        {
            var repoInfo = await gitService.CloneRepositoryAsync(repositoryPath);
            workingPath = repoInfo.LocalPath;
            discoveryResult.RepositoryInfo = repoInfo;
        }
        else
        {
            workingPath = repositoryPath;
        }
        
        // Discover all project files
        var solutionFiles = await DiscoverFiles(workingPath, "*.sln");
        var projectFiles = await DiscoverFiles(workingPath, "*.csproj");
        
        discoveries.Result.SolutionFiles = solutionFiles.Select(f => new ProjectFileInfo
        {
            FilePath = f,
            RelativePath = Path.GetRelativePath(workingPath, f),
            Type = ProjectFileType.Solution,
            Projects = await ExtractSolutionProjects(f)
        }).ToList();
        
        discoveryResult.ProjectFiles = projectFiles.Select(f => new ProjectFileInfo
        {
            FilePath = f,
            RelativePath = Path.GetRelativePath(workingPath, f),
            Type = ProjectFileType.CSharpProject,
            Dependencies = await ExtractProjectMetadata(f)
        }).ToList();
        
        // Determine default working context (prefer .sln)
        discoveryResult.RecommendedContext = DetermineDefaultContext(discoveryResult);
        
        return format switch
        {
            "flat" => SerializeFlatStructure(discoveryResult),
            "hierarchical" => SerializeHierarchicalStructure(discoveryResult),
            _ => JsonSerializer.Serialize(discoveryResult, JsonOptions)
        };
    }
    catch (Exception ex)
    {
        Logger.LogError(ex, "Failed to discover project structure: {RepositoryPath}", repositoryPath);
        return $"Error discovering structure: {ex.Message}";
    }
}
```

## Configuration Management

### Environment Variables
```bash
# Required for GitHub repositories
GITHUB_PERSONAL_ACCESS_TOKEN=ghp_xxxxxxxxxxxxxxxxxxxx

# Required for Azure DevOps repositories  
AZURE_DEVOPS_PERSONAL_ACCESS_TOKEN=xxxxxxxxxxxxxxxxxxxxxx

# Optional: Custom working directory
ROSLYN_MCP_WORKING_DIRECTORY=/tmp/roslyn-mcp-repos

# Optional: Repository cache settings
ROSLYN_MCP_CACHE_RETENTION_HOURS=24
ROSLYN_MCP_MAX_CACHE_SIZE_MB=1024
```

### Container Configuration
```dockerfile
# Enhanced Dockerfile for git support
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS runtime

# Install git and dependencies
RUN apt-get update && apt-get install -y \
    git \
    openssh-client \
    && rm -rf /var/lib/apt/lists/*

# Configure git for container use
RUN git config --global user.email "roslyn-mcp@container.local" && \
    git config --global user.name "Roslyn MCP Server" && \
    git config --global init.defaultBranch main

# Create working directory for repositories
RUN mkdir -p /app/repositories && \
    chown -R mcpuser:mcpuser /app/repositories

# Set environment variables
ENV ROSLYN_MCP_WORKING_DIRECTORY=/app/repositories
```

### Data Models

```csharp
public record GitRepositoryInfo
{
    public string Url { get; init; } = string.Empty;
    public string LocalPath { get; init; } = string.Empty;
    public string Branch { get; init; } = "main";
    public string CommitHash { get; init; } = string.Empty;
    public DateTime ClonedAt { get; init; }
    public long SizeBytes { get; init; }
}

public record ProjectFileInfo
{
    public string FilePath { get; init; } = string.Empty;
    public string RelativePath { get; init; } = string.Empty;
    public ProjectFileType Type { get; init; }
    public List<string> Projects { get; init; } = new();
    public ProjectMetadata? Metadata { get; init; }
}

public record RepositoryAnalysisResult
{
    public GitRepositoryInfo Repository { get; init; } = new();
    public List<ProjectFileInfo> ProjectStructure { get; init; } = new();
    public WorkingContext WorkingContext { get; init; } = new();
    public ProjectMetadata Analysis { get; init; } = new();
    public DateTime ProcessedAt { get; init; }
}

public record WorkingContext  
{
    public string ContextFile { get; init; } = string.Empty;
    public ProjectFileType ContextType { get; init; }
    public List<string> IncludedProjects { get; init; } = new();
    public string ReasonForSelection { get; init; } = string.Empty;
}
```

## Implementation Dependencies

### NuGet Packages
```xml
<PackageReference Include="LibGit2Sharp" Version="0.30.0" />
<PackageReference Include="Octokit" Version="9.0.0" />
<PackageReference Include="Microsoft.TeamFoundationServer.Client" Version="16.205.1" />
```

### Platform Considerations

#### Windows Container Support
```dockerfile
# For Windows containers
FROM mcr.microsoft.com/dotnet/framework/sdk:4.8-windowsservercore-ltsc2019

# Install Git for Windows
RUN powershell -Command \
    Invoke-WebRequest -Uri https://github.com/git-for-windows/git/releases/download/v2.42.0.windows.1/Git-2.42.0-64-bit.exe -OutFile git-installer.exe ; \
    Start-Process git-installer.exe -ArgumentList '/VERYSILENT' -Wait ; \
    Remove-Item git-installer.exe
```

#### Authentication Patterns
```csharp
public static class GitCredentials
{
    public static GitCredentials FromEnvironment()
    {
        return new GitCredentials
        {
            GitHubToken = Environment.GetEnvironmentVariable("GITHUB_PERSONAL_ACCESS_TOKEN"),
            AzureDevOpsToken = Environment.GetEnvironmentVariable("AZURE_DEVOPS_PERSONAL_ACCESS_TOKEN"),
            Type = DetermineAuthType()
        };
    }
    
    private static LibGit2Sharp.Credentials ToLibGit2Credentials(this GitCredentials creds)
    {
        return creds.Type switch
        {
            AuthenticationType.PersonalAccessToken => 
                new UsernamePasswordCredentials
                {
                    Username = creds.GitHubToken ?? creds.AzureDevOpsToken,
                    Password = string.Empty
                },
            _ => throw new NotSupportedException($"Authentication type {creds.Type} not supported")
        };
    }
}
```

## Error Handling & Resilience

### Repository Access Errors
```csharp
public class GitRepositoryService
{
    public async Task<GitRepositoryInfo> CloneRepositoryAsync(string url, string? localPath = null, GitCredentials? creds = null)
    {
        try
        {
            return await AttemptCloneWithRetry(url, localPath, creds);
        }
        catch (AuthenticationException)
        {
            throw new McpException("Repository authentication failed. Check your personal access token.");
        }
        catch (NotFoundException)
        {
            throw new McpException($"Repository not found or not accessible: {url}");
        }
        catch (LibGit2SharpException ex) when (ex.Message.Contains("SSL"))
        {
            throw new McpException("SSL/TLS error connecting to repository. Check certificate configuration.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to clone repository: {Url}", url);
            throw new McpException($"Repository clone failed: {ex.Message}");
        }
    }
}
```

### Working Directory Management
```csharp
public class RepositoryWorkspaceManager : IDisposable
{
    private readonly string _basePath;
    private readonly ConcurrentDictionary<string, string> _activePaths = new();
    
    public string CreateWorkingDirectory(string repositoryUrl)
    {
        var pathHash = ComputeRepositoryHash(repositoryUrl);
        var workingPath = Path.Combine(_basePath, pathHash);
        
        if (Directory.Exists(workingPath))
        {
            CleanDirectory(workingPath);
        }
        
        Directory.CreateDirectory(workingPath);
        _activePaths.TryAdd(repositoryUrl, workingPath);
        
        return workingPath;
    }
    
    public void Dispose()
    {
        foreach (var path in _activePaths.Values)
        {
            try
            {
                if (Directory.Exists(path))
                {
                    Directory.Delete(path, recursive: true);
                }
            }
            catch (Exception ex)
            {
                // Log but don't throw on cleanup
                _logger.LogWarning(ex, "Failed to clean up working directory: {Path}", path);
            }
        }
    }
}
```

## Testing Strategy

### Unit Tests
```csharp
[Test]
public async Task CloneRepositoryAsync_ValidGitHubRepo_ReturnsRepositoryInfo()
{
    // Arrange
    var service = new GitRepositoryService(_tempDirectory, _logger);
    var testRepo = "https://github.com/octocat/Hello-World.git";
    
    // Act
    var result = await service.CloneRepositoryAsync(testRepo);
    
    // Assert
    Assert.That(result.Url, Is.EqualTo(testRepo));
    Assert.That(Directory.Exists(result.LocalPath), Is.True);
    Assert.That(result.CommitHash, Is.Not.Empty);
}

[Test]
public async Task DiscoverProjectStructure_SolutionWithProjects_ReturnsHierarchy()
{
    // Arrange
    var testSolutionPath = CreateTestSolution();
    
    // Act  
    var result = await RoslynTools.DiscoverProjectStructure(testSolutionPath);
    var parsed = JsonSerializer.Deserialize<ProjectDiscoveryResult>(result);
    
    // Assert
    Assert.That(parsed.SolutionFiles, Has.Count.EqualTo(1));
    Assert.That(parsed.ProjectFiles, Has.Count.GreaterThan(0));
    Assert.That(parsed.RecommendedContext.ContextType, Is.EqualTo(ProjectFileType.Solution));
}
```

### Integration Tests
```csharp
[Test]
[Category("Integration")]
public async Task AnalyzeGitRepository_PublicRepo_CompletesPipeline()
{
    // Test against a known public repository
    var result = await RoslynTools.AnalyzeGitRepository(
        "https://github.com/dotnet/samples.git", 
        branch: "main");
    
    var analysis = JsonSerializer.Deserialize<RepositoryAnalysisResult>(result);
    Assert.That(analysis.Repository.Url, Contains.Substring("dotnet/samples"));
    Assert.That(analysis.ProjectStructure, Is.Not.Empty);
}
```

## Performance Considerations

### Caching Strategy
- **Repository Caching**: Cache cloned repositories by URL+commit hash
- **Analysis Caching**: Cache expensive analysis results 
- **Cleanup Policies**: Automatic cleanup of old cached repositories

### Resource Limits
- **Max Repository Size**: Configurable limit (default 500MB)
- **Timeout Settings**: Clone timeout, analysis timeout
- **Concurrent Operations**: Limit simultaneous repository operations

## Security Considerations

### Authentication Security
- **Environment Variables Only**: No embedded tokens
- **Token Scope Validation**: Verify minimum required scopes
- **Audit Logging**: Log all repository access attempts

### Repository Safety
- **URL Validation**: Whitelist allowed domains
- **Path Traversal Protection**: Validate local paths
- **Resource Limits**: Prevent disk space exhaustion

---
*Implementation Timeline: 3 weeks*  
*Dependencies: LibGit2Sharp, Container configuration*  
*Status: Ready for Development*