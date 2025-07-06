using System.Text.Json.Serialization;

namespace RoslynWebApi.Models;

/// <summary>
/// Standard API response wrapper
/// </summary>
/// <typeparam name="T">The type of data being returned</typeparam>
public class ApiResponse<T>
{
    /// <summary>
    /// Indicates if the operation was successful
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// The actual data returned by the operation
    /// </summary>
    public T? Data { get; set; }

    /// <summary>
    /// Error message if the operation failed
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Unique correlation ID for tracking requests
    /// </summary>
    public string CorrelationId { get; set; } = Guid.NewGuid().ToString();

    /// <summary>
    /// Timestamp of the response
    /// </summary>
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Execution time in milliseconds
    /// </summary>
    public long ExecutionTimeMs { get; set; }

    /// <summary>
    /// Creates a successful response
    /// </summary>
    public static ApiResponse<T> CreateSuccess(T data, long executionTimeMs = 0, string? correlationId = null)
    {
        return new ApiResponse<T>
        {
            Success = true,
            Data = data,
            ExecutionTimeMs = executionTimeMs,
            CorrelationId = correlationId ?? Guid.NewGuid().ToString()
        };
    }

    /// <summary>
    /// Creates an error response
    /// </summary>
    public static ApiResponse<T> CreateError(string errorMessage, string? correlationId = null)
    {
        return new ApiResponse<T>
        {
            Success = false,
            ErrorMessage = errorMessage,
            CorrelationId = correlationId ?? Guid.NewGuid().ToString()
        };
    }
}

/// <summary>
/// Standard API response without generic data
/// </summary>
public class ApiResponse
{
    /// <summary>
    /// Indicates if the operation was successful
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// Error message if the operation failed
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Unique correlation ID for tracking requests
    /// </summary>
    public string CorrelationId { get; set; } = Guid.NewGuid().ToString();

    /// <summary>
    /// Timestamp of the response
    /// </summary>
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Execution time in milliseconds
    /// </summary>
    public long ExecutionTimeMs { get; set; }

    /// <summary>
    /// Creates a successful response without data
    /// </summary>
    public static ApiResponse CreateSuccess(long executionTimeMs = 0, string? correlationId = null)
    {
        return new ApiResponse
        {
            Success = true,
            ExecutionTimeMs = executionTimeMs,
            CorrelationId = correlationId ?? Guid.NewGuid().ToString()
        };
    }

    /// <summary>
    /// Creates an error response without data
    /// </summary>
    public static ApiResponse CreateError(string errorMessage, string? correlationId = null)
    {
        return new ApiResponse
        {
            Success = false,
            ErrorMessage = errorMessage,
            CorrelationId = correlationId ?? Guid.NewGuid().ToString()
        };
    }
}

/// <summary>
/// Health check response model
/// </summary>
public class HealthResponse
{
    /// <summary>
    /// Overall health status
    /// </summary>
    public string Status { get; set; } = "Healthy";

    /// <summary>
    /// Individual health check results
    /// </summary>
    public Dictionary<string, HealthCheckResult> Checks { get; set; } = new();

    /// <summary>
    /// Total time taken for all health checks
    /// </summary>
    public TimeSpan TotalDuration { get; set; }
}

/// <summary>
/// Individual health check result
/// </summary>
public class HealthCheckResult
{
    /// <summary>
    /// Status of this specific check
    /// </summary>
    public string Status { get; set; } = "Healthy";

    /// <summary>
    /// Description of the check
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Time taken for this check
    /// </summary>
    public TimeSpan Duration { get; set; }

    /// <summary>
    /// Additional data from the check
    /// </summary>
    public Dictionary<string, object>? Data { get; set; }

    /// <summary>
    /// Exception details if the check failed
    /// </summary>
    public string? Exception { get; set; }
}

/// <summary>
/// API information response
/// </summary>
public class ApiInfoResponse
{
    /// <summary>
    /// Name of the API
    /// </summary>
    public string Name { get; set; } = "Roslyn Web API";

    /// <summary>
    /// Version of the API
    /// </summary>
    public string Version { get; set; } = "1.0.0";

    /// <summary>
    /// Description of the API
    /// </summary>
    public string Description { get; set; } = "REST API for Roslyn code analysis tools";

    /// <summary>
    /// Build timestamp
    /// </summary>
    public DateTime BuildTime { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Environment the API is running in
    /// </summary>
    public string Environment { get; set; } = "Development";

    /// <summary>
    /// Available endpoints summary
    /// </summary>
    public Dictionary<string, string> AvailableEndpoints { get; set; } = new();
}

/// <summary>
/// Metrics response model
/// </summary>
public class MetricsResponse
{
    /// <summary>
    /// Request count metrics
    /// </summary>
    public Dictionary<string, long> RequestCounts { get; set; } = new();

    /// <summary>
    /// Average execution times in milliseconds
    /// </summary>
    public Dictionary<string, double> AverageExecutionTimes { get; set; } = new();

    /// <summary>
    /// Error counts by endpoint
    /// </summary>
    public Dictionary<string, long> ErrorCounts { get; set; } = new();

    /// <summary>
    /// System metrics
    /// </summary>
    public SystemMetrics System { get; set; } = new();
}

/// <summary>
/// System metrics
/// </summary>
public class SystemMetrics
{
    /// <summary>
    /// CPU usage percentage
    /// </summary>
    public double CpuUsage { get; set; }

    /// <summary>
    /// Memory usage in MB
    /// </summary>
    public long MemoryUsageMB { get; set; }

    /// <summary>
    /// Available disk space in MB
    /// </summary>
    public long AvailableDiskSpaceMB { get; set; }

    /// <summary>
    /// Process uptime
    /// </summary>
    public TimeSpan Uptime { get; set; }

    /// <summary>
    /// Active thread count
    /// </summary>
    public int ThreadCount { get; set; }
}
