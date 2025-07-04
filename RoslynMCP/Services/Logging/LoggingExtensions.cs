using Microsoft.Extensions.DependencyInjection;
using Serilog;
using System.Reflection;

namespace RoslynMCP.Services.Logging;

public static class LoggingExtensions
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
            .Enrich.WithProperty("Version", Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "Unknown")
            .Enrich.WithThreadId()
            .Enrich.WithThreadName()
            .Enrich.FromLogContext();
    }
}