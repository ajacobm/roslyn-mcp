# RoslynMCP Logging Modernization Plan

## Executive Summary

This plan outlines the migration from the current `Console.WriteLine` based logging to a modern, structured logging approach using `Microsoft.Extensions.Logging` (ILogger) with configurable providers. The new system will support console, file, and enterprise logging backends (Splunk/Logstash) while maintaining backward compatibility.

## Current State Analysis

### Console Usage Audit Results
- **Total Console.* Calls**: 76 instances across the codebase
- **Primary Locations**: 
  - `Program.cs`: 24 instances (error logging, debugging output)
  - Service classes: Distributed across various tools and analyzers
  - Mixed usage of `Console.WriteLine`, `Console.Error.WriteLine`, and `Console.Out`

### Current Patterns Identified
```csharp
// Pattern 1: Direct Console calls (most common)
Console.Error.WriteLine($"ERROR in ValidateFile: {ex.Message}");
Console.WriteLine($"Project loaded successfully: {project.Name}");

// Pattern 2: TextWriter abstraction (some methods)
writer ??= Console.Out;
writer.WriteLine($"Loading project: {projectPath}");

// Pattern 3: Debugging output
Console.Error.WriteLine($"ValidateFile called with path: '{filePath}'");
```

## Target Architecture

### Modern Logging Stack
```
┌─────────────────────────┐
│     ILogger<T>          │ ← Application Code Layer
├─────────────────────────┤
│ Microsoft.Extensions    │ ← Logging Abstraction
│ .Logging                │
├─────────────────────────┤
│ Configurable Providers  │ ← Provider Layer
├─────────────────────────┤
│ Console │ File │ Splunk  │ ← Output Destinations
└─────────────────────────┘
```

### Provider Strategy
- **Console Provider**: Development and local debugging
- **File Provider**: Local file logging with rotation
- **Splunk/Logstash**: Enterprise logging aggregation
- **Configuration-driven**: Switch providers without code changes

## Implementation Plan

### Phase 1: Foundation Setup

#### 1.1 Add Dependencies
Update `RoslynMCP.csproj`:
```xml
<ItemGroup>
  <!-- Existing logging -->
  <PackageReference Include="Microsoft.Extensions.Hosting" Version="9.0.4" />
  
  <!-- Add modern logging packages -->
  <PackageReference Include="Microsoft.Extensions.Logging" Version="8.0.1" />
  <PackageReference Include="Microsoft.Extensions.Logging.Console" Version="8.0.1" />
  <PackageReference Include="Microsoft.Extensions.Logging.Configuration" Version="8.0.1" />
  
  <!-- File logging support -->
  <PackageReference Include="Microsoft.Extensions.Logging.Abstractions" Version="8.0.2" />
  <PackageReference Include="Serilog.Extensions.Hosting" Version="8.0.0" />
  <PackageReference Include="Serilog.Sinks.Console" Version="6.0.0" />
  <PackageReference Include="Serilog.Sinks.File" Version="6.0.0" />
  <PackageReference Include="Serilog.Sinks.Splunk" Version="4.0.1" />
  <PackageReference Include="Serilog.Settings.Configuration" Version="8.0.4" />
  <PackageReference Include="Serilog.Enrichers.Environment" Version="3.0.1" />
  
  <!-- Source generators for performance -->
  <PackageReference Include="Microsoft.Extensions.Logging.Generators" Version="8.0.1" />
</ItemGroup>
```

#### 1.2 Configure Logging in Program.cs
```csharp
class Program
{
    static async Task Main(string[] args)
    {
        var builder = Host.CreateApplicationBuilder(args);
        
        // Configure logging
        ConfigureLogging(builder);
        
        builder.Services
            .AddMcpServer()
            .WithStdioServerTransport()
            .WithToolsFromAssembly();
            
        await builder.Build().RunAsync();
    }

    private static void ConfigureLogging(HostApplicationBuilder builder)
    {
        // Clear default providers
        builder.Logging.ClearProviders();
        
        // Add Serilog for structured logging
        builder.Host.UseSerilog((context, configuration) =>
        {
            configuration
                .ReadFrom.Configuration(context.Configuration)
                .Enrich.FromLogContext()
                .Enrich.WithMachineName()
                .Enrich.WithEnvironmentName()
                .WriteTo.Console(outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss} {Level:u3}] {Message:lj} {NewLine}{Exception}")
                .WriteTo.File(
                    path: "logs/roslyn-mcp-.log",
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: 7,
                    outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} {Level:u3}] [{SourceContext}] {Message:lj} {NewLine}{Exception}")
                .WriteTo.Conditional(evt => !string.IsNullOrEmpty(context.Configuration["Splunk:Url"]),
                    wt => wt.Splunk(context.Configuration["Splunk:Url"], context.Configuration["Splunk:Token"]));
        });
        
        // Set minimum log level from configuration
        builder.Logging.SetMinimumLevel(LogLevel.Information);
    }
}
```

#### 1.3 Add Configuration Support
Create `appsettings.json`:
```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft": "Warning",
      "Microsoft.Hosting.Lifetime": "Information",
      "RoslynMCP": "Debug"
    }
  },
  "Serilog": {
    "Using": ["Serilog.Sinks.Console", "Serilog.Sinks.File"],
    "MinimumLevel": {
      "Default": "Information",
      "Override": {
        "Microsoft": "Warning",
        "RoslynMCP": "Debug"
      }
    },
    "WriteTo": [
      {
        "Name": "Console",
        "Args": {
          "outputTemplate": "[{Timestamp:yyyy-MM-dd HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}"
        }
      },
      {
        "Name": "File",
        "Args": {
          "path": "logs/roslyn-mcp-.log",
          "rollingInterval": "Day",
          "retainedFileCountLimit": 7,
          "outputTemplate": "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} {Level:u3}] [{SourceContext}] {Message:lj}{NewLine}{Exception}"
        }
      }
    ]
  },
  "Splunk": {
    "Url": "",
    "Token": ""
  }
}
```

Environment-specific configurations:
- `appsettings.Development.json` - Console + Debug level
- `appsettings.Production.json` - File + Splunk + Information level

### Phase 2: Create Logging Abstractions

#### 2.1 Create Logging Service Interface
```csharp
// Services/Logging/IRoslynLogger.cs
using Microsoft.Extensions.Logging;

namespace RoslynMCP.Services.Logging;

public interface IRoslynLogger<T>
{
    void LogValidationStart(string filePath, string projectPath);
    void LogValidationComplete(string filePath, TimeSpan duration);
    void LogValidationError(string filePath, Exception exception);
    void LogProjectMetadataExtraction(string projectPath, int typeCount, int memberCount);
    void LogWorkspaceWarning(string message);
    void LogAnalyzerExecution(string analyzerName, int diagnosticCount);
    void LogSymbolGraphExtraction(string scope, int nodeCount, TimeSpan duration);
}
```

#### 2.2 Implement Logging Service
```csharp
// Services/Logging/RoslynLogger.cs
using Microsoft.Extensions.Logging;

namespace RoslynMCP.Services.Logging;

public partial class RoslynLogger<T> : IRoslynLogger<T>
{
    private readonly ILogger<T> _logger;

    public RoslynLogger(ILogger<T> logger)
    {
        _logger = logger;
    }

    [LoggerMessage(
        EventId = 1001,
        Level = LogLevel.Information,
        Message = "Starting validation for file '{filePath}' in project '{projectPath}'")]
    public partial void LogValidationStart(string filePath, string projectPath);

    [LoggerMessage(
        EventId = 1002,
        Level = LogLevel.Information,
        Message = "Validation completed for file '{filePath}' in {duration}ms")]
    public partial void LogValidationComplete(string filePath, TimeSpan duration);

    [LoggerMessage(
        EventId = 1003,
        Level = LogLevel.Error,
        Message = "Validation failed for file '{filePath}'")]
    public partial void LogValidationError(string filePath, Exception exception);

    [LoggerMessage(
        EventId = 2001,
        Level = LogLevel.Information,
        Message = "Extracted metadata from project '{projectPath}': {typeCount} types, {memberCount} members")]
    public partial void LogProjectMetadataExtraction(string projectPath, int typeCount, int memberCount);

    [LoggerMessage(
        EventId = 3001,
        Level = LogLevel.Warning,
        Message = "Workspace warning: {message}")]
    public partial void LogWorkspaceWarning(string message);

    [LoggerMessage(
        EventId = 4001,
        Level = LogLevel.Debug,
        Message = "Executed analyzer '{analyzerName}', found {diagnosticCount} diagnostics")]
    public partial void LogAnalyzerExecution(string analyzerName, int diagnosticCount);

    [LoggerMessage(
        EventId = 5001,
        Level = LogLevel.Information,
        Message = "Extracted symbol graph with scope '{scope}': {nodeCount} nodes in {duration}ms")]
    public partial void LogSymbolGraphExtraction(string scope, int nodeCount, TimeSpan duration);
}
```

### Phase 3: Migrate Console.WriteLine Usage

#### 3.1 Update Program.cs Methods
**Before:**
```csharp
workspace.WorkspaceFailed += (sender, args) =>
{
    Console.WriteLine($"Workspace warning: {args.Diagnostic.Message}");
};
```

**After:**
```csharp
private readonly ILogger<Program> _logger;

workspace.WorkspaceFailed += (sender, args) =>
{
    _logger.LogWorkspaceWarning(args.Diagnostic.Message);
};
```

#### 3.2 Update Tool Methods
**Before:**
```csharp
Console.Error.WriteLine($"ValidateFile called with path: '{filePath}'");
```

**After:**
```csharp
private static readonly ILogger<RoslynTools> _logger = 
    LoggerFactory.Create(builder => builder.AddConsole()).CreateLogger<RoslynTools>();

_logger.LogDebug("ValidateFile called with path: '{FilePath}'", filePath);
```

#### 3.3 Structured Logging Migration Pattern
```csharp
// Old approach
Console.WriteLine($"Project loaded successfully: {project.Name}");

// New structured approach  
_logger.LogInformation("Project loaded successfully: {ProjectName} with {DocumentCount} documents", 
    project.Name, project.Documents.Count());
```

### Phase 4: Advanced Features

#### 4.1 Add Log Enrichment
```csharp
// Services/Logging/LogEnrichment.cs
public static class LogEnrichment
{
    public static IServiceCollection AddRoslynLogging(this IServiceCollection services)
    {
        services.AddScoped(typeof(IRoslynLogger<>), typeof(RoslynLogger<>));
        return services;
    }

    public static LoggerConfiguration AddRoslynEnrichment(this LoggerConfiguration configuration)
    {
        return configuration
            .Enrich.WithProperty("Application", "RoslynMCP")
            .Enrich.WithProperty("Version", Assembly.GetExecutingAssembly().GetName().Version?.ToString())
            .Enrich.WithThreadId()
            .Enrich.WithThreadName();
    }
}
```

#### 4.2 Performance Monitoring
```csharp
[LoggerMessage(
    EventId = 9001,
    Level = LogLevel.Debug,
    Message = "Performance: {OperationName} completed in {Duration}ms with {ItemCount} items processed")]
public partial void LogPerformanceMetric(string operationName, long duration, int itemCount);

// Usage with scopes
using var scope = _logger.BeginScope("ProjectAnalysis-{ProjectId}", projectId);
var stopwatch = Stopwatch.StartNew();
// ... operation
_logger.LogPerformanceMetric("ProjectAnalysis", stopwatch.ElapsedMilliseconds, resultCount);
```

#### 4.3 Enterprise Integration
```csharp
// appsettings.Production.json
{
  "Serilog": {
    "WriteTo": [
      {
        "Name": "Splunk",
        "Args": {
          "serverUrl": "https://your-splunk-instance.com:8088",
          "token": "{SPLUNK_TOKEN}",
          "index": "roslyn-mcp",
          "source": "roslyn-analyzer",
          "sourceType": "_json"
        }
      }
    ]
  }
}
```

## Migration Strategy

### Phase Implementation Order
1. **Phase 1** (Week 1): Foundation setup and configuration
2. **Phase 2** (Week 1-2): Create logging abstractions and services  
3. **Phase 3** (Week 2-3): Migrate Console.WriteLine calls systematically
4. **Phase 4** (Week 3-4): Add advanced features and enterprise integration

### Backward Compatibility
- Maintain `TextWriter` parameters where external consumers depend on them
- Create adapter pattern for existing console-based integrations
- Gradual migration approach - both systems can coexist temporarily

### Testing Strategy
```csharp
// Unit test example
[Test]
public async Task ValidateFile_LogsStartAndCompletion()
{
    // Arrange
    var loggerFactory = new TestLoggerFactory();  
    var logger = loggerFactory.CreateLogger<RoslynTools>();
    
    // Act
    await RoslynTools.ValidateFile(testFilePath, true);
    
    // Assert
    loggerFactory.Sink.Should().Contain(log => 
        log.EventId.Id == 1001 && 
        log.Message.Contains("Starting validation"));
}
```

## Benefits of Migration

### Immediate Benefits
- **Structured Logging**: Machine-readable logs with consistent formatting
- **Configuration-driven**: Switch log destinations without code changes  
- **Performance**: Source generators eliminate runtime overhead
- **Enterprise Ready**: Direct integration with Splunk/ELK stack

### Long-term Benefits
- **Observability**: Rich telemetry and monitoring capabilities
- **Debugging**: Better log correlation and filtering
- **Compliance**: Centralized log retention and audit trails
- **Scalability**: Async logging and buffering for high-throughput scenarios

## Risk Mitigation

### Compatibility Risks
- **Risk**: Breaking existing console output consumers
- **Mitigation**: Maintain TextWriter abstraction layer, gradual migration

### Performance Risks  
- **Risk**: Logging overhead impacting analysis performance
- **Mitigation**: Use LoggerMessage source generators, configure appropriate log levels

### Configuration Risks
- **Risk**: Complex configuration causing deployment issues  
- **Mitigation**: Provide clear defaults, environment-specific configs, validation

## Success Metrics

- [ ] Zero console.WriteLine calls remaining in codebase
- [ ] All major operations have structured logging events
- [ ] Log aggregation working in test environment
- [ ] Performance impact < 5% overhead
- [ ] Configuration validation and documentation complete

## Conclusion

This logging modernization plan provides a clear path from the current Console-based logging to a production-ready, enterprise-grade logging system. The phased approach ensures minimal disruption while delivering immediate benefits in debugging, monitoring, and operational visibility.

The investment in modern logging infrastructure will pay dividends in operational excellence, debugging efficiency, and enterprise integration capabilities.