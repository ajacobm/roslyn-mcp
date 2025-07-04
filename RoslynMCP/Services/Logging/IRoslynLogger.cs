using Microsoft.Extensions.Logging;

namespace RoslynMCP.Services.Logging;

public interface IRoslynLogger<T>
{
    void LogValidationStart(string filePath, string? projectPath);
    void LogValidationComplete(string filePath, TimeSpan duration);
    void LogValidationError(string filePath, Exception exception);
    void LogProjectMetadataExtraction(string projectPath, int typeCount, int memberCount);
    void LogWorkspaceWarning(string message);
    void LogAnalyzerExecution(string analyzerName, int diagnosticCount);
    void LogSymbolGraphExtraction(string scope, int nodeCount, TimeSpan duration);
    void LogPerformanceMetric(string operationName, long duration, int itemCount);
    void LogDebugMessage(string message, params object?[] args);
    void LogInformation(string message, params object?[] args);
    void LogWarning(string message, params object?[] args);
    void LogError(string message, params object?[] args);
    void LogError(Exception exception, string message, params object?[] args);
}