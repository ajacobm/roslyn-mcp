# HTTP Facade & SSE Support Implementation Plan

## Overview
This document details the implementation of HTTP transport and Server-Sent Events (SSE) support for the Roslyn MCP Server, enabling web-based access and modern API patterns alongside the existing stdio transport.

## Architecture Design

### Dual Transport Architecture
```csharp
// Program.cs - Enhanced for dual transport
var builder = Host.CreateApplicationBuilder(args);

// Configure logging to stderr for stdio compatibility
builder.Logging.AddConsole(options =>
{
    options.LogToStandardErrorThreshold = LogLevel.Trace;
});

// Add MCP server with dual transport support
builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()    // Keep existing stdio transport
    .WithHttpTransport()           // Add HTTP transport
    .WithToolsFromAssembly();

// For HTTP mode, also configure ASP.NET Core
if (args.Contains("--http") || Environment.GetEnvironmentVariable("MCP_TRANSPORT_MODE") == "http")
{
    builder.Services.Configure<HttpTransportOptions>(options =>
    {
        options.Port = int.Parse(Environment.GetEnvironmentVariable("MCP_HTTP_PORT") ?? "3001");
        options.Host = Environment.GetEnvironmentVariable("MCP_HTTP_HOST") ?? "localhost";
        options.EnableCors = true;
        options.EnableOpenApi = true;
    });
}

var app = builder.Build();

// Configure HTTP pipeline if in HTTP mode
if (app.Services.GetService<HttpTransportOptions>() != null)
{
    app.UseCors();
    app.UseRouting();
    app.MapMcp();  // Map MCP endpoints
    
    // Add custom endpoints for enhanced functionality
    app.MapGet("/api/health", () => new { Status = "Healthy", Version = "2.0.0" });
    app.MapGet("/api/tools", async (IMcpServer server) => await server.ListToolsAsync());
}

await app.RunAsync();
```

### HTTP Transport Configuration
```csharp
public class HttpTransportOptions
{
    public string Host { get; set; } = "localhost";
    public int Port { get; set; } = 3001;
    public bool EnableCors { get; set; } = true;
    public bool EnableOpenApi { get; set; } = false;
    public bool EnableSSE { get; set; } = true;
    public string[]? AllowedOrigins { get; set; }
    public TimeSpan SseHeartbeatInterval { get; set; } = TimeSpan.FromSeconds(30);
}
```

## Enhanced MCP Server Implementation

### Startup Mode Detection
```csharp
public static class ServerModeDetector
{
    public static ServerMode DetectMode(string[] args)
    {
        // Command line arguments take precedence
        if (args.Contains("--http")) return ServerMode.Http;
        if (args.Contains("--stdio")) return ServerMode.Stdio;
        if (args.Contains("--dual")) return ServerMode.Dual;
        
        // Environment variable fallback
        var transportMode = Environment.GetEnvironmentVariable("MCP_TRANSPORT_MODE");
        return transportMode?.ToLower() switch
        {
            "http" => ServerMode.Http,
            "stdio" => ServerMode.Stdio,
            "dual" => ServerMode.Dual,
            _ => ServerMode.Stdio  // Default to stdio for backward compatibility
        };
    }
}

public enum ServerMode
{
    Stdio,    // Traditional stdio transport only
    Http,     // HTTP transport only
    Dual      // Both transports simultaneously
}
```

### HTTP Endpoint Implementation
```csharp
// Enhanced HTTP endpoints for MCP protocol
public static class McpHttpExtensions
{
    public static void MapMcp(this WebApplication app)
    {
        // Standard MCP JSON-RPC endpoint
        app.MapPost("/mcp", async (HttpContext context, IMcpServer server) =>
        {
            var jsonRpcRequest = await JsonSerializer.DeserializeAsync<JsonRpcRequest>(context.Request.Body);
            var response = await server.ProcessRequestAsync(jsonRpcRequest);
            return Results.Json(response);
        });
        
        // RESTful endpoints for easier integration
        app.MapGet("/mcp/tools", async (IMcpServer server) =>
        {
            var tools = await server.ListToolsAsync();
            return Results.Json(tools);
        });
        
        app.MapPost("/mcp/tools/{toolName}", async (string toolName, JsonElement arguments, IMcpServer server) =>
        {
            var result = await server.CallToolAsync(toolName, arguments);
            return Results.Json(result);
        });
        
        // Server-Sent Events endpoint
        app.MapGet("/mcp/events", async (HttpContext context, IMcpServer server) =>
        {
            await HandleSseConnection(context, server);
        });
        
        // Health and discovery endpoints
        app.MapGet("/mcp/health", () => new { 
            Status = "Healthy", 
            Transport = "HTTP",
            Capabilities = new[] { "tools", "prompts", "resources" }
        });
    }
}
```

### Server-Sent Events Implementation
```csharp
public class SseManager
{
    private readonly ConcurrentDictionary<string, SseConnection> _connections = new();
    private readonly ILogger<SseManager> _logger;
    private readonly Timer _heartbeatTimer;

    public async Task HandleSseConnection(HttpContext context, IMcpServer server)
    {
        var connectionId = Guid.NewGuid().ToString();
        
        context.Response.Headers.Add("Content-Type", "text/event-stream");
        context.Response.Headers.Add("Cache-Control", "no-cache");
        context.Response.Headers.Add("Connection", "keep-alive");
        context.Response.Headers.Add("Access-Control-Allow-Origin", "*");
        
        var connection = new SseConnection(connectionId, context.Response, server);
        _connections.TryAdd(connectionId, connection);
        
        try
        {
            // Send initial connection event
            await connection.SendEventAsync("connected", new { 
                ConnectionId = connectionId, 
                ServerInfo = await server.GetServerInfoAsync() 
            });
            
            // Keep connection alive and handle client disconnection
            await connection.WaitForDisconnectionAsync();
        }
        finally
        {
            _connections.TryRemove(connectionId, out _);
            connection.Dispose();
        }
    }
    
    public async Task BroadcastToolUpdate(string toolName, object data)
    {
        var tasks = _connections.Values.Select(conn => 
            conn.SendEventAsync("tool-update", new { Tool = toolName, Data = data }));
        await Task.WhenAll(tasks);
    }
}

public class SseConnection : IDisposable
{
    private readonly string _connectionId;
    private readonly HttpResponse _response;
    private readonly IMcpServer _server;
    private readonly CancellationTokenSource _cancellationTokenSource = new();

    public async Task SendEventAsync(string eventType, object data)
    {
        var json = JsonSerializer.Serialize(data);
        var sseData = $"event: {eventType}\ndata: {json}\n\n";
        await _response.WriteAsync(sseData, _cancellationTokenSource.Token);
        await _response.Body.FlushAsync(_cancellationTokenSource.Token);
    }
    
    public async Task WaitForDisconnectionAsync()
    {
        try
        {
            while (!_cancellationTokenSource.Token.IsCancellationRequested)
            {
                // Send heartbeat
                await SendEventAsync("heartbeat", new { Timestamp = DateTime.UtcNow });
                await Task.Delay(TimeSpan.FromSeconds(30), _cancellationTokenSource.Token);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected when client disconnects
        }
    }
}
```

## Enhanced CLI Implementation

### Command Line Interface
```csharp
// Enhanced Program.cs with CLI support
public class Program
{
    public static async Task Main(string[] args)
    {
        var rootCommand = new RootCommand("Roslyn MCP Server - Advanced C# Code Analysis")
        {
            CreateServeCommand(),
            CreateAnalyzeCommand(), 
            CreateDiscoverCommand()
        };
        
        await rootCommand.InvokeAsync(args);
    }
    
    private static Command CreateServeCommand()
    {
        var serveCommand = new Command("serve", "Start the MCP server")
        {
            new Option<ServerMode>("--mode", () => ServerMode.Stdio, "Server transport mode"),
            new Option<int>("--port", () => 3001, "HTTP port (when using HTTP mode)"),
            new Option<string>("--host", () => "localhost", "HTTP host"),
            new Option<bool>("--cors", () => true, "Enable CORS for HTTP mode"),
            new Option<string>("--working-dir", "Working directory for repository cloning")
        };
        
        serveCommand.SetHandler(async (mode, port, host, cors, workingDir) =>
        {
            Environment.SetEnvironmentVariable("MCP_TRANSPORT_MODE", mode.ToString().ToLower());
            Environment.SetEnvironmentVariable("MCP_HTTP_PORT", port.ToString());
            Environment.SetEnvironmentVariable("MCP_HTTP_HOST", host);
            Environment.SetEnvironmentVariable("MCP_ENABLE_CORS", cors.ToString());  
            Environment.SetEnvironmentVariable("ROSLYN_MCP_WORKING_DIRECTORY", workingDir ?? "./temp");
            
            await StartServerAsync();
        }, 
        new Argument<ServerMode>("mode"),
        new Argument<int>("port"),
        new Argument<string>("host"),
        new Argument<bool>("cors"),
        new Argument<string>("workingDir"));
        
        return serveCommand;
    }
    
    private static Command CreateAnalyzeCommand()
    {
        var analyzeCommand = new Command("analyze", "Analyze a repository directly")
        {
            new Argument<string>("repository", "Repository URL or local path"),
            new Option<string>("--output", "Output file path"),
            new Option<string>("--format", () => "json", "Output format (json, markdown)"),
            new Option<string>("--project", "Specific project file to analyze")
        };
        
        analyzeCommand.SetHandler(async (repository, output, format, project) =>
        {
            var result = await RoslynTools.AnalyzeGitRepository(repository, projectPath: project);
            
            if (!string.IsNullOrEmpty(output))
            {
                await File.WriteAllTextAsync(output, result);
                Console.WriteLine($"Analysis saved to: {output}");
            }
            else
            {
                Console.WriteLine(result);
            }
        },
        new Argument<string>("repository"),
        new Argument<string>("output"),
        new Argument<string>("format"),
        new Argument<string>("project"));
        
        return analyzeCommand;
    }
}
```

### Local Filesystem Mode
```csharp
[McpServerTool, Description("Analyze local repository or directory")]
public static async Task<string> AnalyzeLocalRepository(
    [Description("Local repository or directory path")] 
    string localPath,
    
    [Description("Watch for file changes and provide real-time updates")] 
    bool watchMode = false)
{
    if (!Directory.Exists(localPath))
    {
        return $"Error: Directory not found: {localPath}";
    }
    
    try
    {
        // Detect if it's a git repository
        var isGitRepo = Directory.Exists(Path.Combine(localPath, ".git"));
        
        // Discover project structure
        var discoveryResult = await DiscoverLocalProjects(localPath);
        
        // Perform analysis on discovered projects
        var analysisResults = new List<ProjectAnalysisResult>();
        
        foreach (var projectFile in discoveryResult.ProjectFiles)
        {
            var metadata = await new ProjectMetadataExtractor(CreateWorkspace())
                .ExtractAsync(projectFile.FilePath);
            analysisResults.Add(new ProjectAnalysisResult
            {
                ProjectFile = projectFile,
                Metadata = metadata
            });
        }
        
        var result = new LocalRepositoryAnalysisResult
        {
            LocalPath = localPath,
            IsGitRepository = isGitRepo,
            Discovery = discoveryResult,
            ProjectAnalyses = analysisResults,
            WatchMode = watchMode
        };
        
        // If watch mode is enabled, set up file system watcher
        if (watchMode && HttpContext.Current?.Features.Get<ISseManager>() is ISseManager sseManager)
        {
            SetupFileSystemWatcher(localPath, sseManager);
        }
        
        return JsonSerializer.Serialize(result, JsonOptions);
    }
    catch (Exception ex)
    {
        Logger.LogError(ex, "Failed to analyze local repository: {LocalPath}", localPath);
        return $"Error analyzing local repository: {ex.Message}";
    }
}

private static void SetupFileSystemWatcher(string path, ISseManager sseManager)
{
    var watcher = new FileSystemWatcher(path)
    {
        IncludeSubdirectories = true,
        NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.DirectoryName
    };
    
    watcher.Changed += async (sender, e) =>
    {
        if (IsCSharpFile(e.FullPath))
        {
            await sseManager.BroadcastToolUpdate("file-changed", new { 
                FilePath = e.FullPath,
                ChangeType = e.ChangeType,
                Timestamp = DateTime.UtcNow
            });
        }
    };
    
    watcher.EnableRaisingEvents = true;
}
```

## Container Configuration Updates

### Enhanced Dockerfile
```dockerfile
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS runtime

# Install git and network tools
RUN apt-get update && apt-get install -y \
    git \
    curl \
    wget \
    openssh-client \
    && rm -rf /var/lib/apt/lists/*

# Configure git for container use
RUN git config --global user.email "roslyn-mcp@container.local" && \
    git config --global user.name "Roslyn MCP Server" && \
    git config --global init.defaultBranch main

# Create application user and directories
RUN groupadd -r mcpuser && useradd -r -g mcpuser mcpuser
RUN mkdir -p /app/repositories /app/temp && \
    chown -R mcpuser:mcpuser /app

WORKDIR /app
COPY --from=build /app/publish .

# Set environment variables
ENV DOTNET_CLI_TELEMETRY_OPTOUT=1 \
    DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1 \
    NUGET_PACKAGES=/home/mcpuser/.nuget/packages \
    DOTNET_NOLOGO=1 \
    ROSLYN_MCP_WORKING_DIRECTORY=/app/repositories

# Expose HTTP port (optional)
EXPOSE 3001

USER mcpuser

# Support multiple startup modes
ENTRYPOINT ["dotnet", "RoslynMCP.dll"]
CMD ["serve", "--mode", "stdio"]

# Health check for HTTP mode
HEALTHCHECK --interval=30s --timeout=10s --start-period=5s --retries=3 \
    CMD curl -f http://localhost:3001/mcp/health || exit 1
```

### Docker Compose Configuration
```yaml
# docker-compose.yml - Enhanced for dual mode support
version: '3.8'

services:
  roslyn-mcp-stdio:
    build: .
    container_name: roslyn-mcp-stdio
    environment:
      - MCP_TRANSPORT_MODE=stdio
      - GITHUB_PERSONAL_ACCESS_TOKEN=${GITHUB_TOKEN:-}
      - AZURE_DEVOPS_PERSONAL_ACCESS_TOKEN=${AZURE_TOKEN:-}
    volumes:
      - ./temp:/app/repositories
    stdin_open: true
    tty: true
    
  roslyn-mcp-http:
    build: .
    container_name: roslyn-mcp-http
    ports:
      - "3001:3001"
    environment:
      - MCP_TRANSPORT_MODE=http
      - MCP_HTTP_HOST=0.0.0.0
      - MCP_HTTP_PORT=3001
      - GITHUB_PERSONAL_ACCESS_TOKEN=${GITHUB_TOKEN:-}
      - AZURE_DEVOPS_PERSONAL_ACCESS_TOKEN=${AZURE_TOKEN:-}
    volumes:
      - ./temp:/app/repositories
    command: ["serve", "--mode", "http", "--host", "0.0.0.0", "--port", "3001"]
    healthcheck:
      test: ["CMD", "curl", "-f", "http://localhost:3001/mcp/health"]
      interval: 30s
      timeout: 10s
      retries: 3
      start_period: 40s

  roslyn-mcp-dual:
    build: .
    container_name: roslyn-mcp-dual  
    ports:
      - "3001:3001"
    environment:
      - MCP_TRANSPORT_MODE=dual
      - MCP_HTTP_HOST=0.0.0.0
      - MCP_HTTP_PORT=3001
    volumes:
      - ./temp:/app/repositories
    stdin_open: true
    tty: true
    command: ["serve", "--mode", "dual", "--host", "0.0.0.0"]
```

## API Documentation & OpenAPI Support

### OpenAPI Configuration
```csharp
public static class OpenApiExtensions
{
    public static void AddMcpOpenApi(this IServiceCollection services)
    {
        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen(options =>
        {
            options.SwaggerDoc("v1", new OpenApiInfo
            {
                Title = "Roslyn MCP Server API",
                Version = "v2.0.0",
                Description = "HTTP API for Roslyn Multi-Language Code Analysis",
                Contact = new OpenApiContact
                {
                    Name = "Roslyn MCP Server",
                    Url = new Uri("https://github.com/[repo]/roslyn-mcp")
                }
            });
            
            options.AddMcpToolSchemas();
        });
    }
    
    public static void UseMcpOpenApi(this WebApplication app)
    {
        if (app.Environment.IsDevelopment())
        {
            app.UseSwagger();
            app.UseSwaggerUI(options =>
            {
                options.SwaggerEndpoint("/swagger/v1/swagger.json", "Roslyn MCP API v2.0.0");
                options.RoutePrefix = "api/docs";
            });
        }
    }
}
```

### RESTful API Endpoints
```csharp
public static class RestApiExtensions
{
    public static void MapRestApi(this WebApplication app)
    {
        var api = app.MapGroup("/api/v1").WithTags("Roslyn Analysis API");
        
        // Repository operations
        api.MapPost("/repositories/analyze", async (AnalyzeRepositoryRequest request) =>
        {
            var result = await RoslynTools.AnalyzeGitRepository(
                request.RepositoryUrl, 
                request.Branch, 
                request.WorkingPath,
                request.SolutionFile);
            return Results.Ok(result);
        }).WithSummary("Analyze Git Repository");
        
        // Project discovery
        api.MapPost("/projects/discover", async (DiscoverProjectRequest request) =>
        {
            var result = await RoslynTools.DiscoverProjectStructure(request.Path, request.Format);
            return Results.Ok(result);
        }).WithSummary("Discover Project Structure");
        
        // File analysis
        api.MapPost("/files/validate", async (ValidateFileRequest request) =>
        {
            var result = await RoslynTools.ValidateFile(request.FilePath, request.RunAnalyzers);
            return Results.Ok(result);
        }).WithSummary("Validate C# File");
        
        // Streaming endpoints
        api.MapGet("/repositories/{repoId}/watch", async (string repoId, HttpContext context) =>
        {
            await StreamRepositoryUpdates(repoId, context);
        }).WithSummary("Watch Repository Changes");
    }
}
```

## Testing Strategy

### HTTP Transport Tests
```csharp
[Test]
public async Task HttpTransport_ToolDiscovery_ReturnsAllTools()
{
    // Arrange
    var webApp = CreateTestWebApp();
    var client = webApp.GetTestClient();
    
    // Act
    var response = await client.GetAsync("/mcp/tools");
    var tools = await response.Content.ReadFromJsonAsync<List<McpTool>>();
    
    // Assert
    Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
    Assert.That(tools, Has.Count.GreaterThan(0));
    Assert.That(tools.Any(t => t.Name == "AnalyzeGitRepository"), Is.True);
}

[Test]
public async Task SseEndpoint_ClientConnection_ReceivesHeartbeat()
{
    // Arrange
    var webApp = CreateTestWebApp();
    var client = webApp.GetTestClient();
    
    // Act
    using var response = await client.GetAsync("/mcp/events", HttpCompletionOption.ResponseHeadersRead);
    using var stream = await response.Content.ReadAsStreamAsync();
    using var reader = new StreamReader(stream);
    
    // Assert
    var firstLine = await reader.ReadLineAsync();
    Assert.That(firstLine, Contains.Substring("event: connected"));
    
    // Wait for heartbeat
    var heartbeatReceived = false;
    var timeout = TimeSpan.FromSeconds(35);
    var cancellationToken = new CancellationTokenSource(timeout).Token;
    
    while (!cancellationToken.IsCancellationRequested && !heartbeatReceived)
    {
        var line = await reader.ReadLineAsync();
        if (line?.Contains("event: heartbeat") == true)
        {
            heartbeatReceived = true;
        }
    }
    
    Assert.That(heartbeatReceived, Is.True);
}
```

### CLI Integration Tests
```csharp
[Test]
public async Task CLI_AnalyzeCommand_LocalRepository_ProducesOutput()
{
    // Arrange
    var testRepoPath = CreateTestRepository();
    var outputFile = Path.GetTempFileName();
    
    try
    {
        // Act
        var exitCode = await RunCliCommand($"analyze {testRepoPath} --output {outputFile} --format json");
        
        // Assert
        Assert.That(exitCode, Is.EqualTo(0));
        Assert.That(File.Exists(outputFile), Is.True);
        
        var content = await File.ReadAllTextAsync(outputFile);
        var result = JsonSerializer.Deserialize<RepositoryAnalysisResult>(content);
        
        Assert.That(result, Is.Not.Null);
        Assert.That(result.ProjectStructure, Is.Not.Empty);
    }
    finally
    {
        if (File.Exists(outputFile)) File.Delete(outputFile);
        Directory.Delete(testRepoPath, true);
    }
}
```

## Deployment Scenarios

### Development Setup
```bash
# Local development - stdio mode (backward compatible)
dotnet run

# Local development - HTTP mode  
dotnet run -- serve --mode http --port 3001

# Local development - dual mode
dotnet run -- serve --mode dual
```

### Production Deployment
```bash
# Container - stdio mode (for MCP clients)
docker run -i --rm \
  -e GITHUB_PERSONAL_ACCESS_TOKEN \
  roslyn-mcp:latest

# Container - HTTP mode (for web clients)
docker run -d -p 3001:3001 \
  -e MCP_TRANSPORT_MODE=http \
  -e GITHUB_PERSONAL_ACCESS_TOKEN \
  roslyn-mcp:latest

# Kubernetes deployment
kubectl apply -f k8s/roslyn-mcp-deployment.yaml
```

### Kubernetes Configuration
```yaml
apiVersion: apps/v1
kind: Deployment
metadata:
  name: roslyn-mcp-http
spec:
  replicas: 2
  selector:
    matchLabels:
      app: roslyn-mcp
      mode: http
  template:
    metadata:
      labels:
        app: roslyn-mcp
        mode: http
    spec:
      containers:
      - name: roslyn-mcp
        image: roslyn-mcp:latest
        command: ["dotnet", "RoslynMCP.dll", "serve", "--mode", "http", "--host", "0.0.0.0"]
        ports:
        - containerPort: 3001
          name: http
        env:
        - name: MCP_TRANSPORT_MODE
          value: "http"
        - name: GITHUB_PERSONAL_ACCESS_TOKEN
          valueFrom:
            secretKeyRef:
              name: github-credentials
              key: token
        resources:
          requests:
            memory: "512Mi"
            cpu: "250m"
          limits:
            memory: "2Gi"
            cpu: "1000m"
        livenessProbe:
          httpGet:
            path: /mcp/health
            port: 3001
          initialDelaySeconds: 30
          periodSeconds: 30
        readinessProbe:
          httpGet:
            path: /mcp/health
            port: 3001
          initialDelaySeconds: 5
          periodSeconds: 5

---
apiVersion: v1
kind: Service
metadata:
  name: roslyn-mcp-service
spec:
  selector:
    app: roslyn-mcp
    mode: http
  ports:
  - port: 80
    targetPort: 3001
    name: http
  type: LoadBalancer
```

## Security Considerations

### HTTP Security
```csharp
public static class SecurityExtensions
{
    public static void AddMcpSecurity(this IServiceCollection services)
    {
        services.AddCors(options =>
        {
            options.AddDefaultPolicy(builder =>
            {
                builder.AllowAnyOrigin()
                       .AllowAnyMethod()
                       .AllowAnyHeader();
            });
        });
        
        services.AddRateLimiter(options =>
        {
            options.AddFixedWindowLimiter("McpApi", limiterOptions =>
            {
                limiterOptions.PermitLimit = 100;
                limiterOptions.Window = TimeSpan.FromMinutes(1);
            });
        });
    }
}
```

### Authentication Middleware
```csharp
public class McpAuthenticationMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<McpAuthenticationMiddleware> _logger;

    public async Task InvokeAsync(HttpContext context)
    {
        // Basic API key authentication for HTTP mode
        if (context.Request.Path.StartsWithSegments("/mcp"))
        {
            var apiKey = context.Request.Headers["X-API-Key"].FirstOrDefault() ??
                        context.Request.Query["api_key"].FirstOrDefault();
                        
            if (!ValidateApiKey(apiKey))
            {
                context.Response.StatusCode = 401;
                await context.Response.WriteAsync("Unauthorized: Invalid or missing API key");
                return;
            }
        }
        
        await _next(context);
    }
    
    private bool ValidateApiKey(string? apiKey)
    {
        var validApiKey = Environment.GetEnvironmentVariable("MCP_API_KEY");
        return !string.IsNullOrEmpty(apiKey) && apiKey == validApiKey;
    }
}
```

---
*Implementation Timeline: 3 weeks*  
*Dependencies: ModelContextProtocol.AspNetCore, enhanced CLI*  
*Status: Ready for Development*