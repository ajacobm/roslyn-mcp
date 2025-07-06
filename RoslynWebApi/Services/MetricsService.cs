using System.Collections.Concurrent;
using System.Diagnostics;
using RoslynWebApi.Models;

namespace RoslynWebApi.Services;

/// <summary>
/// Service for collecting and reporting metrics
/// </summary>
public interface IMetricsService
{
    void RecordRequest(string endpoint);
    void RecordExecutionTime(string endpoint, long milliseconds);
    void RecordError(string endpoint);
    Task<MetricsResponse> GetMetricsAsync();
    Task<SystemMetrics> GetSystemMetricsAsync();
}

/// <summary>
/// Implementation of metrics collection service
/// </summary>
public class MetricsService : IMetricsService
{
    private readonly ILogger<MetricsService> _logger;
    private readonly ConcurrentDictionary<string, long> _requestCounts = new();
    private readonly ConcurrentDictionary<string, List<long>> _executionTimes = new();
    private readonly ConcurrentDictionary<string, long> _errorCounts = new();
    private readonly DateTime _startTime = DateTime.UtcNow;

    public MetricsService(ILogger<MetricsService> logger)
    {
        _logger = logger;
    }

    public void RecordRequest(string endpoint)
    {
        _requestCounts.AddOrUpdate(endpoint, 1, (key, count) => count + 1);
        _logger.LogDebug("Recorded request for endpoint: {Endpoint}", endpoint);
    }

    public void RecordExecutionTime(string endpoint, long milliseconds)
    {
        _executionTimes.AddOrUpdate(endpoint, 
            new List<long> { milliseconds }, 
            (key, times) => 
            {
                lock (times)
                {
                    times.Add(milliseconds);
                    // Keep only last 1000 measurements to prevent memory issues
                    if (times.Count > 1000)
                    {
                        times.RemoveAt(0);
                    }
                    return times;
                }
            });
        _logger.LogDebug("Recorded execution time {ExecutionTime}ms for endpoint: {Endpoint}", milliseconds, endpoint);
    }

    public void RecordError(string endpoint)
    {
        _errorCounts.AddOrUpdate(endpoint, 1, (key, count) => count + 1);
        _logger.LogDebug("Recorded error for endpoint: {Endpoint}", endpoint);
    }

    public async Task<MetricsResponse> GetMetricsAsync()
    {
        return await Task.FromResult(new MetricsResponse
        {
            RequestCounts = new Dictionary<string, long>(_requestCounts),
            AverageExecutionTimes = _executionTimes.ToDictionary(
                kvp => kvp.Key,
                kvp => 
                {
                    lock (kvp.Value)
                    {
                        return kvp.Value.Count > 0 ? kvp.Value.Average() : 0.0;
                    }
                }),
            ErrorCounts = new Dictionary<string, long>(_errorCounts),
            System = await GetSystemMetricsAsync()
        });
    }

    public async Task<SystemMetrics> GetSystemMetricsAsync()
    {
        var currentProcess = Process.GetCurrentProcess();
        
        return await Task.FromResult(new SystemMetrics
        {
            MemoryUsageMB = currentProcess.WorkingSet64 / (1024 * 1024),
            Uptime = DateTime.UtcNow - _startTime,
            ThreadCount = currentProcess.Threads.Count,
            // Note: More sophisticated CPU and disk metrics would require additional libraries
            CpuUsage = 0.0, // Placeholder - would need performance counters
            AvailableDiskSpaceMB = GetAvailableDiskSpace()
        });
    }

    private long GetAvailableDiskSpace()
    {
        try
        {
            var drive = new DriveInfo(Path.GetPathRoot(Directory.GetCurrentDirectory()) ?? "C:");
            return drive.AvailableFreeSpace / (1024 * 1024);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get disk space information");
            return 0;
        }
    }
}
