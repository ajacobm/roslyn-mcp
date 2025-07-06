using Microsoft.OpenApi.Models;
using Prometheus;
using Serilog;
using Serilog.Events;
using RoslynWebApi.Services;
using RoslynWebApi.Middleware;
using RoslynRuntime.Services.Logging;
using System.Reflection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using System.Text.Json;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;

var builder = WebApplication.CreateBuilder(args);

// Configure Serilog
var loggerConfiguration = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .AddRoslynEnrichment()
    .Enrich.WithProperty("ApplicationName", "RoslynWebApi")
    .Enrich.WithProperty("Version", Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "1.0.0");

// Add Splunk logging if enabled
var splunkConfig = builder.Configuration.GetSection("SplunkLogging");
if (splunkConfig.GetValue<bool>("Enabled"))
{
    var splunkUrl = splunkConfig.GetValue<string>("SplunkCollectorUrl");
    var indexName = splunkConfig.GetValue<string>("IndexName");
    var sourceType = splunkConfig.GetValue<string>("SourceType");
    
    if (!string.IsNullOrEmpty(splunkUrl))
    {
        loggerConfiguration.WriteTo.EventCollector(
            splunkHost: splunkUrl,
            eventCollectorToken: Environment.GetEnvironmentVariable("SPLUNK_TOKEN") ?? "",
            index: indexName,
            sourceType: sourceType);
    }
}

Log.Logger = loggerConfiguration.CreateLogger();
builder.Host.UseSerilog();

// Add services to the container
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        options.JsonSerializerOptions.WriteIndented = true;
    });

// Add memory caching
builder.Services.AddMemoryCache();

// Add custom services
builder.Services.AddSingleton<IMetricsService, MetricsService>();
builder.Services.AddScoped<IRoslynAnalysisService, RoslynAnalysisService>();

// Add logging services
builder.Services.AddRoslynLogging();

// Add health checks
builder.Services.AddHealthChecks()
    .AddCheck("self", () => HealthCheckResult.Healthy("API is running"));

// Configure OpenAPI/Swagger
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Roslyn Web API",
        Version = "v1",
        Description = "Comprehensive REST API for Roslyn code analysis tools",
        Contact = new OpenApiContact
        {
            Name = "Roslyn Web API",
            Email = "support@roslynapi.com",
            Url = new Uri("https://github.com/your-org/roslyn-mcp")
        },
        License = new OpenApiLicense
        {
            Name = "MIT",
            Url = new Uri("https://opensource.org/licenses/MIT")
        }
    });

    // Include XML documentation
    var xmlFile = $"{Assembly.GetExecutingAssembly().GetName().Name}.xml";
    var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
    if (File.Exists(xmlPath))
    {
        c.IncludeXmlComments(xmlPath);
    }

    // Add security definitions
    c.AddSecurityDefinition("ApiKey", new OpenApiSecurityScheme
    {
        Description = "API Key for authentication",
        Name = "X-API-Key",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.ApiKey
    });

    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "ApiKey"
                },
                Scheme = "ApiKeyScheme",
                Name = "X-API-Key",
                In = ParameterLocation.Header
            },
            new List<string>()
        }
    });
});

// Configure CORS
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

// Add HTTP client for external dependencies
builder.Services.AddHttpClient();

var app = builder.Build();

// Configure the HTTP request pipeline
if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
}

// Enable Swagger UI
app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "Roslyn Web API v1");
    c.RoutePrefix = "swagger";
    c.DocumentTitle = "Roslyn Web API Documentation";
    c.DefaultModelsExpandDepth(-1);
    c.DocExpansion(Swashbuckle.AspNetCore.SwaggerUI.DocExpansion.None);
});

// Add Prometheus metrics middleware
app.UseHttpMetrics();

// Request logging middleware
app.UseSerilogRequestLogging(options =>
{
    options.MessageTemplate = "HTTP {RequestMethod} {RequestPath} responded {StatusCode} in {Elapsed:0.0000} ms";
    options.GetLevel = (httpContext, elapsed, ex) => ex != null
        ? LogEventLevel.Error
        : httpContext.Response.StatusCode > 499
            ? LogEventLevel.Error
            : LogEventLevel.Information;
    
    options.EnrichDiagnosticContext = (diagnosticContext, httpContext) =>
    {
        diagnosticContext.Set("RequestHost", httpContext.Request.Host.Value);
        diagnosticContext.Set("RequestScheme", httpContext.Request.Scheme);
        diagnosticContext.Set("UserAgent", httpContext.Request.Headers["User-Agent"].FirstOrDefault());
        diagnosticContext.Set("CorrelationId", httpContext.TraceIdentifier);
    };
});

app.UseHttpsRedirection();
app.UseCors();

// Add rate limiting middleware
app.UseMiddleware<RateLimitingMiddleware>();

// Add API key authentication middleware
app.UseMiddleware<ApiKeyAuthenticationMiddleware>();

// Custom middleware for request/response correlation
app.Use(async (context, next) =>
{
    // Ensure correlation ID exists
    if (string.IsNullOrEmpty(context.TraceIdentifier))
    {
        context.TraceIdentifier = Guid.NewGuid().ToString();
    }
    
    // Add correlation ID to response headers
    context.Response.Headers.Add("X-Correlation-ID", context.TraceIdentifier);
    
    await next();
});

app.UseRouting();

// Health check endpoints
app.MapHealthChecks("/health", new HealthCheckOptions
{
    ResponseWriter = async (context, report) =>
    {
        context.Response.ContentType = "application/json";
        
        var response = new
        {
            status = report.Status.ToString(),
            checks = report.Entries.ToDictionary(
                kvp => kvp.Key,
                kvp => new
                {
                    status = kvp.Value.Status.ToString(),
                    description = kvp.Value.Description,
                    duration = kvp.Value.Duration.TotalMilliseconds,
                    data = kvp.Value.Data,
                    exception = kvp.Value.Exception?.Message
                }),
            totalDuration = report.TotalDuration.TotalMilliseconds
        };
        
        await context.Response.WriteAsync(JsonSerializer.Serialize(response, new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        }));
    }
});

// Prometheus metrics endpoint
app.MapMetrics();

app.MapControllers();

// Root endpoint
app.MapGet("/", () => new
{
    name = "Roslyn Web API",
    version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "1.0.0",
    status = "running",
    timestamp = DateTime.UtcNow,
    documentation = "/swagger",
    health = "/health",
    metrics = "/metrics",
    endpoints = new
    {
        roslyn = "/api/v1/roslyn",
        health = "/api/v1/health"
    }
});

// Global exception handling
app.UseExceptionHandler(errorApp =>
{
    errorApp.Run(async context =>
    {
        var exceptionHandlerPathFeature = context.Features.Get<Microsoft.AspNetCore.Diagnostics.IExceptionHandlerPathFeature>();
        var exception = exceptionHandlerPathFeature?.Error;

        Log.Error(exception, "Unhandled exception occurred");

        context.Response.StatusCode = 500;
        context.Response.ContentType = "application/json";

        await context.Response.WriteAsync(JsonSerializer.Serialize(new
        {
            success = false,
            error = "Internal server error occurred",
            correlationId = context.TraceIdentifier,
            timestamp = DateTime.UtcNow
        }, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        }));
    });
});

// Graceful shutdown
var lifetime = app.Services.GetRequiredService<IHostApplicationLifetime>();
lifetime.ApplicationStopping.Register(() =>
{
    Log.Information("Application is shutting down gracefully");
});

lifetime.ApplicationStopped.Register(() =>
{
    Log.Information("Application has stopped");
    Log.CloseAndFlush();
});

try
{
    Log.Information("Starting Roslyn Web API");
    await app.RunAsync();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Application terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}
