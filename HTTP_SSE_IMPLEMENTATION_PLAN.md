# HTTP Facade & SSE Support Implementation Plan (Revised)

## Overview
This revised document details the implementation of an HTTP facade using ASP.NET Core Minimal APIs that acts as a bridge to the existing MCP stdio server. Both the HTTP facade and the stdio MCP server will share the same underlying analysis services, providing a dual-interface architecture.

## Corrected Architecture Design

### Facade Pattern Architecture
```
┌─ HTTP Facade (ASP.NET Core) ─┐    ┌─ Stdio MCP Server ─┐
│ • Minimal API endpoints      │    │ • [McpServerTool]   │
│ • REST API translation       │    │ • stdio transport   │  
│ • SSE real-time updates      │    │ • existing tools    │
│ • OpenAPI documentation      │    │ • JSON-RPC          │
└───────────────────────────────┘    └─────────────────────┘
              │                                │
              └──────────┬─────────────────────┘
                        │ 
              ┌─ Shared Services Layer ─┐
              │ • Git Repository Service │
              │ • Project Analysis Tools │
              │ • File System Operations │
              │ • Authentication Services │
              └─────────────────────────────┘
```

### Dual-Mode Implementation
```csharp
// Program.cs - Mode detection and configuration
public static async Task Main(string[] args)
{
    var mode = DetectServerMode(args);
    
    switch (mode)
    {
        case ServerMode.Stdio:
            await RunStdioMcpServer(args);
            break;
        case ServerMode.Http:
            await RunHttpFacadeServer(args);
            break;
        case ServerMode.Dual:
            await RunDualModeServer(args);
            break;
        default:
            throw new ArgumentException($"Unknown server mode: {mode}");
    }
}

private static ServerMode DetectServerMode(string[] args)
{
    if (args.Contains("--http")) return ServerMode.Http;
    if (args.Contains("--stdio")) return ServerMode.Stdio;
    if (args.Contains("--dual")) return ServerMode.Dual;
    
    // Environment variable fallback
    var transportMode = Environment.GetEnvironmentVariable("ROSLYN_MCP_TRANSPORT_MODE");
    return transportMode?.ToLower() switch
    {
        "http" => ServerMode.Http,
        "dual" => ServerMode.Dual,
        _ => ServerMode.Stdio  // Default for backward compatibility
    };
}
```

## HTTP Facade Implementation

### Core HTTP Server Setup
```csharp
private static async Task RunHttpFacadeServer(string[] args)
{
    var builder = WebApplication.CreateBuilder(args);
    
    // Configure services
    builder.Services.AddHttpFacadeServices();
    builder.Services.AddSharedAnalysisServices(); // Same services as MCP version
    builder.Services.AddCors();
    
    // Add OpenAPI/Swagger support
    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen();
    
    var app = builder.Build();
    
    // Configure middleware pipeline
    if (app.Environment.IsDevelopment())
    {
        app.UseSwagger();
        app.UseSwaggerUI();
    }
    
    app.UseCors(policy => policy
        .AllowAnyOrigin()
        .AllowAnyMethod() 
        .AllowAnyHeader());
    
    // Map our facade endpoints
    app.MapRoslynMcpFacade();
    app.MapServerSentEvents();
    app.MapHealthEndpoints();
    
    await app.RunAsync();
}
```

### Shared Service Registration
```csharp
public static class ServiceRegistrationExtensions
{
    public static IServiceCollection AddSharedAnalysisServices(this IServiceCollection services)
    {
        // Register all the same services that the MCP server uses
        services.AddSingleton<GitRepositoryService>();
        services.AddSingleton<ProjectMetadataExtractor>();
        services.AddSingleton<CodeChunker>();
        services.AddSingleton<StructureAnalyzer>();
        services.AddSingleton<CodeFactsGenerator>();
        // ... other analysis services
        
        return services;
    }
    
    public static IServiceCollection AddHttpFacadeServices(this IServiceCollection services)
    {
        services.AddSingleton<SseConnectionManager>();
        services.AddSingleton<RoslynMcpFacadeService>();
        
        return services;
    }
}
```

### MCP Tool Bridge Service
```csharp
public class RoslynMcpFacadeService
{
    private readonly GitRepositoryService _gitService;
    private readonly ProjectMetadataExtractor _metadataExtractor;
    private readonly CodeChunker _codeChunker;
    private readonly ILogger<RoslynMcpFacadeService> _logger;

    public RoslynMcpFacadeService(
        GitRepositoryService gitService,
        ProjectMetadataExtractor metadataExtractor,
        CodeChunker codeChunker,
        ILogger<RoslynMcpFacadeService> logger)
    {
        _gitService = gitService;
        _metadataExtractor = metadataExtractor;
        _codeChunker = codeChunker;
        _logger = logger;
    }

    // Bridge methods that call the same logic as MCP tools
    public async Task<string> AnalyzeGitRepositoryAsync(AnalyzeRepositoryRequest request)
    {
        // Call the same underlying logic as RoslynTools.AnalyzeGitRepository
        // but return structured result instead of JSON string
        return await AnalyzeGitRepositoryCore(
            request.RepositoryUrl, 
            request.Branch, 
            request.WorkingPath, 
            request.SolutionFile, 
            request.IncludeFullMetadata);
    }
    
    public async Task<string> ValidateFileAsync(ValidateFileRequest request)
    {
        // Call the same underlying logic as RoslynTools.ValidateFile
        return await ValidateFileCore(
            request.FilePath, 
            request.RunAnalyzers);
    }
    
    public async Task<string> DiscoverProjectStructureAsync(DiscoverProjectRequest request)
    {
        // Call the same underlying logic as RoslynTools.DiscoverProjectStructure  
        return await DiscoverProjectStructureCore(
            request.Path, 
            request.Format);
    }
    
    // Private methods that contain the actual implementation
    // These are extracted from the static RoslynTools methods
    private async Task<string> AnalyzeGitRepositoryCore(/* parameters */)
    {
        // Implementation extracted from RoslynTools.AnalyzeGitRepository
        // Same logic, but as instance method for better testing/DI
    }
}
```

### HTTP API Endpoints
```csharp
public static class RoslynMcpFacadeEndpoints
{
    public static void MapRoslynMcpFacade(this WebApplication app)
    {
        var api = app.MapGroup("/api/v1")
            .WithTags("Roslyn MCP Analysis")
            .WithOpenApi();
        
        // Repository analysis endpoints
        api.MapPost("/repositories/analyze", AnalyzeRepository)
           .WithName("AnalyzeGitRepository")
           .WithSummary("Analyze a Git repository")
           .WithDescription("Clone and analyze a Git repository with comprehensive project discovery");
           
        api.MapPost("/projects/discover", DiscoverProjectStructure)
           .WithName("DiscoverProjectStructure") 
           .WithSummary("Discover project structure")
           .WithDescription("Discover and preprocess .NET project structure in a repository");
           
        api.MapPost("/files/validate", ValidateFile)
           .WithName("ValidateFile")
           .WithSummary("Validate C# file")
           .WithDescription("Validate a C# file using Roslyn and run code analyzers");
           
        // Additional endpoints for other MCP tools
        api.MapPost("/code/chunk", ChunkCodeBySemantics);
        api.MapPost("/code/analyze-structure", AnalyzeCodeStructure);
        api.MapPost("/code/generate-facts", GenerateCodeFacts);
        api.MapPost("/symbols/extract-graph", ExtractSymbolGraph);
        
        // Real-time status endpoints
        api.MapGet("/status", GetServerStatus);
        api.MapGet("/tools", ListAvailableTools);
    }
    
    private static async Task<IResult> AnalyzeRepository(
        AnalyzeRepositoryRequest request,
        RoslynMcpFacadeService facade,
        SseConnectionManager sseManager)
    {
        try
        {
            // Notify SSE clients that analysis is starting
            await sseManager.BroadcastAsync("analysis-started", new { 
                RepositoryUrl = request.RepositoryUrl,
                Timestamp = DateTime.UtcNow 
            });
            
            var result = await facade.AnalyzeGitRepositoryAsync(request);
            
            // Notify SSE clients that analysis is complete
            await sseManager.BroadcastAsync("analysis-completed", new { 
                RepositoryUrl = request.RepositoryUrl,
                Success = true,
                Timestamp = DateTime.UtcNow 
            });
            
            return Results.Ok(result);
        }
        catch (Exception ex)
        {
            await sseManager.BroadcastAsync("analysis-failed", new { 
                RepositoryUrl = request.RepositoryUrl,
                Error = ex.Message,
                Timestamp = DateTime.UtcNow 
            });
            
            return Results.Problem(
                detail: ex.Message,
                title: "Repository Analysis Failed",
                statusCode: 500);
        }
    }
    
    private static async Task<IResult> ValidateFile(
        ValidateFileRequest request,
        RoslynMcpFacadeService facade)
    {
        try
        {
            var result = await facade.ValidateFileAsync(request);
            return Results.Ok(result);
        }
        catch (Exception ex)
        {
            return Results.Problem(
                detail: ex.Message,
                title: "File Validation Failed",
                statusCode: 500);
        }
    }
}
```

### Request/Response Models
```csharp
public record AnalyzeRepositoryRequest(
    string RepositoryUrl,
    string Branch = "main",
    string? WorkingPath = null,
    string? SolutionFile = null,
    bool IncludeFullMetadata = true);

public record ValidateFileRequest(
    string FilePath,
    bool RunAnalyzers = true);

public record DiscoverProjectRequest(
    string Path,
    string Format = "hierarchical");

public record ChunkCodeRequest(
    string Path,
    string Strategy = "semantic",
    bool IncludeDependencies = true);

// Response wrapper for consistent API responses
public record ApiResponse<T>(
    bool Success,
    T? Data,
    string? Error = null,
    DateTime Timestamp = default)
{
    public static ApiResponse<T> Ok(T data) => new(true, data, null, DateTime.UtcNow);
    public static ApiResponse<T> Failed(string error) => new(false, default, error, DateTime.UtcNow);
}
```

## Server-Sent Events Implementation

### SSE Connection Manager
```csharp
public class SseConnectionManager
{
    private readonly ConcurrentDictionary<string, SseConnection> _connections = new();
    private readonly ILogger<SseConnectionManager> _logger;

    public async Task HandleSseConnectionAsync(HttpContext context)
    {
        var connectionId = Guid.NewGuid().ToString();
        
        context.Response.Headers.Add("Content-Type", "text/event-stream");
        context.Response.Headers.Add("Cache-Control", "no-cache");
        context.Response.Headers.Add("Connection", "keep-alive");
        context.Response.Headers.Add("Access-Control-Allow-Origin", "*");
        
        var connection = new SseConnection(connectionId, context);
        _connections.TryAdd(connectionId, connection);
        
        try
        {
            // Send initial connection event
            await connection.SendEventAsync("connected", new { 
                ConnectionId = connectionId,
                ServerInfo = new { 
                    Version = "2.0.0",
                    Transport = "HTTP/SSE",
                    Capabilities = new[] { "tools", "real-time-updates", "file-watching" }
                }
            });
            
            // Keep connection alive
            await connection.WaitForDisconnectionAsync(context.RequestAborted);
        }
        finally
        {
            _connections.TryRemove(connectionId, out _);
            connection.Dispose();
        }
    }
    
    public async Task BroadcastAsync(string eventType, object data)
    {
        var disconnectedConnections = new List<string>();
        
        foreach (var (connectionId, connection) in _connections)
        {
            try
            {
                await connection.SendEventAsync(eventType, data);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to send SSE event to connection {ConnectionId}", connectionId);
                disconnectedConnections.Add(connectionId);
            }
        }
        
        // Clean up disconnected connections
        foreach (var connectionId in disconnectedConnections)
        {
            _connections.TryRemove(connectionId, out _);
        }
    }
}

public class SseConnection : IDisposable
{
    private readonly string _connectionId;
    private readonly HttpResponse _response;
    private readonly SemaphoreSlim _sendSemaphore = new(1, 1);

    public async Task SendEventAsync(string eventType, object data)
    {
        await _sendSemaphore.WaitAsync();
        try
        {
            var json = JsonSerializer.Serialize(data);
            var sseData = $"event: {eventType}\ndata: {json}\n\n";
            
            await _response.WriteAsync(sseData);
            await _response.Body.FlushAsync();
        }
        finally
        {
            _sendSemaphore.Release();
        }
    }
    
    public async Task WaitForDisconnectionAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                // Send periodic heartbeat
                await SendEventAsync("heartbeat", new { Timestamp = DateTime.UtcNow });
                await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected when client disconnects
        }
    }
}
```

### SSE Endpoint Mapping
```csharp
public static class SseEndpoints
{
    public static void MapServerSentEvents(this WebApplication app)
    {
        app.MapGet("/api/events", HandleSseConnection)
           .WithName("ServerSentEvents")
           .WithSummary("Connect to real-time event stream")
           .WithDescription("Establishes an SSE connection for real-time updates during analysis operations");
    }
    
    private static async Task HandleSseConnection(
        HttpContext context,
        SseConnectionManager sseManager)
    {
        await sseManager.HandleSseConnectionAsync(context);
    }
}
```

## Dual Mode Implementation

### Dual Mode Server
```csharp
private static async Task RunDualModeServer(string[] args)
{
    // Start both servers concurrently
    var stdioTask = Task.Run(() => RunStdioMcpServer(args));
    var httpTask = Task.Run(() => RunHttpFacadeServer(args));
    
    // Wait for either to complete (or both)
    await Task.WhenAny(stdioTask, httpTask);
}
```

### Original MCP Server (Unchanged)
```csharp
private static async Task RunStdioMcpServer(string[] args)
{
    // Keep the existing implementation exactly as-is
    var builder = Host.CreateApplicationBuilder(args);
    
    builder.Logging.AddConsole(consoleLogOptions =>
    {
        consoleLogOptions.LogToStandardErrorThreshold = LogLevel.Trace;
    });
    
    builder.Services.AddSharedAnalysisServices(); // Same shared services
    builder.Services
        .AddMcpServer()
        .WithStdioServerTransport()
        .WithToolsFromAssembly();
    
    await builder.Build().RunAsync();
}
```

## Health and Discovery Endpoints

### Health Check Implementation
```csharp
public static class HealthEndpoints
{
    public static void MapHealthEndpoints(this WebApplication app)
    {
        app.MapGet("/health", () => new HealthStatus
        {
            Status = "Healthy",
            Transport = "HTTP",
            Version = "2.0.0",
            Timestamp = DateTime.UtcNow
        });
        
        app.MapGet("/health/detailed", GetDetailedHealth)
           .WithName("DetailedHealth");
    }
    
    private static async Task<IResult> GetDetailedHealth(
        GitRepositoryService gitService,
        HttpContext context)
    {
        var health = new DetailedHealthStatus
        {
            Status = "Healthy",
            Transport = "HTTP",
            Version = "2.0.0",
            Timestamp = DateTime.UtcNow,
            Components = new()
        };
        
        // Check Git service health
        try
        {
            // Test basic git operations
            health.Components["GitService"] = new ComponentHealth
            {
                Status = "Healthy",
                ResponseTime = TimeSpan.FromMilliseconds(10) // Mock
            };
        }
        catch (Exception ex)
        {
            health.Components["GitService"] = new ComponentHealth
            {
                Status = "Unhealthy",
                Error = ex.Message
            };
            health.Status = "Degraded";
        }
        
        return Results.Ok(health);
    }
}

public record HealthStatus
{
    public string Status { get; init; } = string.Empty;
    public string Transport { get; init; } = string.Empty;
    public string Version { get; init; } = string.Empty;
    public DateTime Timestamp { get; init; }
}

public record DetailedHealthStatus : HealthStatus
{
    public Dictionary<string, ComponentHealth> Components { get; init; } = new();
}

public record ComponentHealth
{
    public string Status { get; init; } = string.Empty;
    public string? Error { get; init; }
    public TimeSpan? ResponseTime { get; init; }
}
```

## Container Configuration Updates

### Enhanced Dockerfile for Dual Mode
```dockerfile
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS runtime

# Install git and network tools
RUN apt-get update && apt-get install -y \
    git \
    curl \
    wget \
    openssh-client \
    && rm -rf /var/lib/apt/lists/*

# Create application user and directories
RUN groupadd -r mcpuser && useradd -r -g mcpuser mcpuser
RUN mkdir -p /app/repositories /app/temp && \
    chown -R mcpuser:mcpuser /app

WORKDIR /app
COPY --from=build /app/publish .

# Set environment variables
ENV DOTNET_CLI_TELEMETRY_OPTOUT=1 \
    ROSLYN_MCP_WORKING_DIRECTORY=/app/repositories

# Expose HTTP port
EXPOSE 3001

USER mcpuser

# Support multiple startup modes
ENTRYPOINT ["dotnet", "RoslynMCP.dll"]

# Default arguments (can be overridden)
CMD ["--stdio"]

# Health check for HTTP mode  
HEALTHCHECK --interval=30s --timeout=10s --start-period=5s --retries=3 \
    CMD curl -f http://localhost:3001/health || exit 1
```

### Docker Compose for Both Modes
```yaml
version: '3.8'

services:
  # Traditional stdio MCP server
  roslyn-mcp-stdio:
    build: .
    container_name: roslyn-mcp-stdio
    environment:
      - GITHUB_PERSONAL_ACCESS_TOKEN=${GITHUB_TOKEN:-}
      - AZURE_DEVOPS_PERSONAL_ACCESS_TOKEN=${AZURE_TOKEN:-}
    volumes:
      - ./temp:/app/repositories
    stdin_open: true
    tty: true
    command: ["--stdio"]
    
  # HTTP facade server
  roslyn-mcp-http:
    build: .
    container_name: roslyn-mcp-http
    ports:
      - "3001:3001"
    environment:
      - GITHUB_PERSONAL_ACCESS_TOKEN=${GITHUB_TOKEN:-}
      - AZURE_DEVOPS_PERSONAL_ACCESS_TOKEN=${AZURE_TOKEN:-}
    volumes:
      - ./temp:/app/repositories
    command: ["--http", "--urls", "http://0.0.0.0:3001"]
    healthcheck:
      test: ["CMD", "curl", "-f", "http://localhost:3001/health"]
      interval: 30s
      timeout: 10s
      retries: 3
      
  # Dual mode server (both stdio and HTTP)
  roslyn-mcp-dual:
    build: .
    container_name: roslyn-mcp-dual
    ports:
      - "3001:3001"  
    environment:
      - GITHUB_PERSONAL_ACCESS_TOKEN=${GITHUB_TOKEN:-}
      - AZURE_DEVOPS_PERSONAL_ACCESS_TOKEN=${AZURE_TOKEN:-}
    volumes:
      - ./temp:/app/repositories
    stdin_open: true
    tty: true
    command: ["--dual", "--urls", "http://0.0.0.0:3001"]
```

## Implementation Benefits

### 🟢 Advantages of Facade Pattern
1. **Code Reuse**: Both HTTP and stdio use the same underlying analysis services
2. **Maintainability**: Single source of truth for analysis logic
3. **Testability**: Analysis services can be tested independently
4. **Flexibility**: Can support both transports without duplication
5. **Gradual Migration**: Existing stdio clients continue to work unchanged

### 🟢 Technical Benefits  
1. **No MCP SDK Dependencies**: Uses standard ASP.NET Core patterns
2. **Full Control**: Complete control over HTTP implementation
3. **Standard Patterns**: Familiar to ASP.NET Core developers
4. **Production Ready**: Built on mature, proven technology
5. **Extensible**: Easy to add new endpoints and features

## Testing Strategy

### HTTP Facade Tests
```csharp
[Test]
public async Task AnalyzeRepository_ValidGitHubRepo_ReturnsSuccess()
{
    // Arrange
    var webApp = CreateTestWebApp();
    var client = webApp.GetTestClient();
    var request = new AnalyzeRepositoryRequest("https://github.com/octocat/Hello-World.git");
    
    // Act
    var response = await client.PostAsJsonAsync("/api/v1/repositories/analyze", request);
    
    // Assert
    Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
    var result = await response.Content.ReadFromJsonAsync<ApiResponse<string>>();
    Assert.That(result.Success, Is.True);
}

[Test]
public async Task SseEndpoint_ClientConnection_ReceivesEvents()
{
    // Test SSE connection and event reception
    var webApp = CreateTestWebApp();
    var client = webApp.GetTestClient();
    
    using var response = await client.GetAsync("/api/events", HttpCompletionOption.ResponseHeadersRead);
    using var stream = await response.Content.ReadAsStreamAsync();
    using var reader = new StreamReader(stream);
    
    // Verify connection event received
    var firstEvent = await reader.ReadLineAsync();
    Assert.That(firstEvent, Contains.Substring("event: connected"));
}
```

### Integration Tests
```csharp
[Test]
public async Task DualMode_BothTransports_ProvideSameFunctionality()
{
    // Test that both stdio and HTTP provide identical results
    var httpResult = await CallHttpApi("/api/v1/files/validate", new ValidateFileRequest("test.cs"));
    var stdioResult = await CallStdioMcp("ValidateFile", new { filePath = "test.cs" });
    
    // Results should be functionally equivalent
    Assert.That(httpResult.Success, Is.True);
    Assert.That(JsonNormalize(httpResult.Data), Is.EqualTo(JsonNormalize(stdioResult)));
}
```

## Revised Assessment

### ✅ Feasibility: Highly Viable
The facade pattern approach is **significantly more feasible** than the original dual transport concept:

1. **No Custom MCP SDK**: Uses standard ASP.NET Core patterns
2. **Code Sharing**: Same analysis logic for both interfaces  
3. **Incremental Implementation**: Can build gradually without affecting stdio
4. **Production Ready**: Built on mature, well-documented technology
5. **Full Control**: Complete control over HTTP API design

### ✅ Implementation Complexity: Medium
- **Week 1**: Extract shared services from static MCP tools
- **Week 2**: Implement HTTP facade with core endpoints
- **Week 3**: Add SSE support and real-time features
- **Week 4**: Testing, documentation, and production hardening

### ✅ Risk Level: Low
- Uses established ASP.NET Core patterns
- Maintains backward compatibility with stdio MCP 
- No dependency on experimental MCP SDK features
- Clear separation of concerns

The facade approach provides all the benefits of HTTP transport while maintaining the existing MCP functionality, making it a much more practical and maintainable solution.

---
*Revised Implementation Timeline: 3 weeks*  
*Dependencies: ASP.NET Core, standard HTTP/SSE patterns*  
*Status: Ready for Implementation*