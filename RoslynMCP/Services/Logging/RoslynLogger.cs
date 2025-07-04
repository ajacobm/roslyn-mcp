using Microsoft.Extensions.Logging;

namespace RoslynMCP.Services.Logging;

public class RoslynLogger<T> : IRoslynLogger<T>
{
    private readonly ILogger<T> _logger;

    public RoslynLogger(ILogger<T> logger)
    {
        _logger = logger;
    }

    public void LogValidationStart(string filePath, string? projectPath)
    {
        _logger.LogInformation("Starting validation for file '{FilePath}' in project '{ProjectPath}'", filePath, projectPath);
    }

    public void LogValidationComplete(string filePath, TimeSpan duration)
    {
        _logger.LogInformation("Validation completed for file '{FilePath}' in {Duration} ms", filePath, duration.TotalMilliseconds);
    }

    public void LogValidationError(string filePath, Exception exception)
    {
        _logger.LogError(exception, "Validation failed for file '{FilePath}'", filePath);
    }

    public void LogProjectMetadataExtraction(string projectPath, int typeCount, int memberCount)
    {
        _logger.LogInformation("Extracted metadata from project '{ProjectPath}': {TypeCount} types, {MemberCount} members", projectPath, typeCount, memberCount);
    }

    public void LogWorkspaceWarning(string message)
    {
        _logger.LogWarning("Workspace warning: {Message}", message);
    }

    public void LogAnalyzerExecution(string analyzerName, int diagnosticCount)
    {
        _logger.LogDebug("Executed analyzer '{AnalyzerName}', found {DiagnosticCount} diagnostics", analyzerName, diagnosticCount);
    }

    public void LogSymbolGraphExtraction(string scope, int nodeCount, TimeSpan duration)
    {
        _logger.LogInformation("Extracted symbol graph with scope '{Scope}': {NodeCount} nodes in {Duration} ms", scope, nodeCount, duration.TotalMilliseconds);
    }

    public void LogPerformanceMetric(string operationName, long duration, int itemCount)
    {
        _logger.LogDebug("Performance: {OperationName} completed in {Duration} ms with {ItemCount} items processed", operationName, duration, itemCount);
    }

    // Generic logging methods for flexible usage
    public void LogDebugMessage(string message, params object?[] args)
    {
        _logger.LogDebug(message, args);
    }

    public void LogInformation(string message, params object?[] args)
    {
        _logger.LogInformation(message, args);
    }

    public void LogWarning(string message, params object?[] args)
    {
        _logger.LogWarning(message, args);
    }

    public void LogError(string message, params object?[] args)
    {
        _logger.LogError(message, args);
    }

    public void LogError(Exception exception, string message, params object?[] args)
    {
        _logger.LogError(exception, message, args);
    }
}