using System.Collections.Concurrent;
using System.Text.Json;

namespace RoslynWebApi.Middleware;

/// <summary>
/// Simple rate limiting middleware
/// </summary>
public class RateLimitingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<RateLimitingMiddleware> _logger;
    private readonly ConcurrentDictionary<string, UserRateLimit> _clients = new();
    private readonly int _requestsPerMinute;
    private readonly int _burstLimit;
    private readonly Timer _cleanupTimer;

    public RateLimitingMiddleware(RequestDelegate next, ILogger<RateLimitingMiddleware> logger, IConfiguration configuration)
    {
        _next = next;
        _logger = logger;
        _requestsPerMinute = configuration.GetValue<int>("RoslynWebApi:RateLimiting:RequestsPerMinute", 100);
        _burstLimit = configuration.GetValue<int>("RoslynWebApi:RateLimiting:BurstLimit", 10);

        // Cleanup timer runs every minute
        _cleanupTimer = new Timer(CleanupExpiredEntries, null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var clientId = GetClientIdentifier(context);
        var userRateLimit = _clients.GetOrAdd(clientId, _ => new UserRateLimit(_requestsPerMinute, _burstLimit));

        if (!userRateLimit.AllowRequest())
        {
            _logger.LogWarning("Rate limit exceeded for client {ClientId}", clientId);
            await WriteRateLimitResponse(context, userRateLimit);
            return;
        }

        await _next(context);
    }

    private string GetClientIdentifier(HttpContext context)
    {
        // Use API key if available, otherwise fall back to IP address
        var apiKey = context.Request.Headers["X-API-Key"].FirstOrDefault();
        if (!string.IsNullOrEmpty(apiKey))
        {
            return $"api_key:{apiKey.Substring(0, Math.Min(8, apiKey.Length))}***"; // Mask for logging
        }

        return $"ip:{context.Connection.RemoteIpAddress}";
    }

    private static async Task WriteRateLimitResponse(HttpContext context, UserRateLimit userRateLimit)
    {
        context.Response.StatusCode = 429;
        context.Response.ContentType = "application/json";

        // Add rate limit headers
        context.Response.Headers.Add("X-RateLimit-Limit", userRateLimit.RequestsPerMinute.ToString());
        context.Response.Headers.Add("X-RateLimit-Remaining", userRateLimit.RemainingRequests.ToString());
        context.Response.Headers.Add("X-RateLimit-Reset", userRateLimit.ResetTime.ToString());
        context.Response.Headers.Add("Retry-After", "60");

        var response = new
        {
            success = false,
            error = "Rate limit exceeded",
            correlationId = context.TraceIdentifier,
            timestamp = DateTime.UtcNow,
            rateLimitInfo = new
            {
                limit = userRateLimit.RequestsPerMinute,
                remaining = userRateLimit.RemainingRequests,
                resetTime = userRateLimit.ResetTime,
                retryAfter = 60
            }
        };

        await context.Response.WriteAsync(JsonSerializer.Serialize(response, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        }));
    }

    private void CleanupExpiredEntries(object? state)
    {
        var expiredKeys = _clients
            .Where(kvp => kvp.Value.IsExpired)
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (var key in expiredKeys)
        {
            _clients.TryRemove(key, out _);
        }

        if (expiredKeys.Count > 0)
        {
            _logger.LogDebug("Cleaned up {Count} expired rate limit entries", expiredKeys.Count);
        }
    }

    public void Dispose()
    {
        _cleanupTimer?.Dispose();
    }
}

/// <summary>
/// Rate limit tracking for individual users/clients
/// </summary>
public class UserRateLimit
{
    private readonly int _requestsPerMinute;
    private readonly int _burstLimit;
    private readonly Queue<DateTime> _requests = new();
    private readonly object _lock = new();

    public UserRateLimit(int requestsPerMinute, int burstLimit)
    {
        _requestsPerMinute = requestsPerMinute;
        _burstLimit = burstLimit;
    }

    public int RequestsPerMinute => _requestsPerMinute;
    public int RemainingRequests => Math.Max(0, _requestsPerMinute - _requests.Count);
    public long ResetTime => DateTimeOffset.UtcNow.AddMinutes(1).ToUnixTimeSeconds();
    public bool IsExpired => _requests.Count == 0 && DateTime.UtcNow.AddMinutes(-5) > (_requests.LastOrDefault());

    public bool AllowRequest()
    {
        lock (_lock)
        {
            var now = DateTime.UtcNow;
            var oneMinuteAgo = now.AddMinutes(-1);

            // Remove requests older than 1 minute
            while (_requests.Count > 0 && _requests.Peek() < oneMinuteAgo)
            {
                _requests.Dequeue();
            }

            // Check burst limit (requests in last few seconds)
            var fiveSecondsAgo = now.AddSeconds(-5);
            var recentRequests = _requests.Count(r => r >= fiveSecondsAgo);
            if (recentRequests >= _burstLimit)
            {
                return false;
            }

            // Check rate limit (requests per minute)
            if (_requests.Count >= _requestsPerMinute)
            {
                return false;
            }

            _requests.Enqueue(now);
            return true;
        }
    }
}
