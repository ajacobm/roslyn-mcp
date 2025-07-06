using System.Text.Json;

namespace RoslynWebApi.Middleware;

/// <summary>
/// Middleware for API key authentication
/// </summary>
public class ApiKeyAuthenticationMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ApiKeyAuthenticationMiddleware> _logger;
    private readonly string? _apiKey;

    public ApiKeyAuthenticationMiddleware(RequestDelegate next, ILogger<ApiKeyAuthenticationMiddleware> logger, IConfiguration configuration)
    {
        _next = next;
        _logger = logger;
        _apiKey = configuration["Authentication:ApiKey"] ?? Environment.GetEnvironmentVariable("API_KEY");
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Skip authentication for certain endpoints
        var path = context.Request.Path.Value?.ToLowerInvariant() ?? "";
        var skipPaths = new[] { "/health", "/metrics", "/swagger", "/", "/api/v1/health" };
        
        if (skipPaths.Any(skipPath => path.StartsWith(skipPath)))
        {
            await _next(context);
            return;
        }

        // Skip authentication if no API key is configured
        if (string.IsNullOrEmpty(_apiKey))
        {
            _logger.LogWarning("API key authentication is not configured. Skipping authentication.");
            await _next(context);
            return;
        }

        // Check for API key in headers
        var providedApiKey = context.Request.Headers["X-API-Key"].FirstOrDefault() 
                           ?? context.Request.Headers["Authorization"].FirstOrDefault()?.Replace("Bearer ", "");

        if (string.IsNullOrEmpty(providedApiKey))
        {
            _logger.LogWarning("Request to {Path} missing API key", path);
            await WriteUnauthorizedResponse(context, "API key is required");
            return;
        }

        if (providedApiKey != _apiKey)
        {
            _logger.LogWarning("Request to {Path} with invalid API key", path);
            await WriteUnauthorizedResponse(context, "Invalid API key");
            return;
        }

        _logger.LogDebug("API key authentication successful for {Path}", path);
        await _next(context);
    }

    private static async Task WriteUnauthorizedResponse(HttpContext context, string message)
    {
        context.Response.StatusCode = 401;
        context.Response.ContentType = "application/json";

        var response = new
        {
            success = false,
            error = message,
            correlationId = context.TraceIdentifier,
            timestamp = DateTime.UtcNow
        };

        await context.Response.WriteAsync(JsonSerializer.Serialize(response, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        }));
    }
}
