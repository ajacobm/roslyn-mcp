using Microsoft.AspNetCore.Mvc;
using RoslynWebApi.Models;
using RoslynWebApi.Services;
using System.Diagnostics;
using System.ComponentModel.DataAnnotations;

namespace RoslynWebApi.Controllers;

/// <summary>
/// Controller for Roslyn code analysis operations
/// </summary>
[ApiController]
[Route("api/v1/[controller]")]
[Produces("application/json")]
public class RoslynController : ControllerBase
{
    private readonly IRoslynAnalysisService _analysisService;
    private readonly IMetricsService _metricsService;
    private readonly ILogger<RoslynController> _logger;

    public RoslynController(
        IRoslynAnalysisService analysisService,
        IMetricsService metricsService,
        ILogger<RoslynController> logger)
    {
        _analysisService = analysisService;
        _metricsService = metricsService;
        _logger = logger;
    }

    /// <summary>
    /// Validates a C# file using Roslyn and runs code analyzers
    /// </summary>
    /// <param name="request">File validation request</param>
    /// <returns>Validation results</returns>
    [HttpPost("validate-file")]
    public async Task<ActionResult<ApiResponse<string>>> ValidateFile([FromBody] ValidateFileRequest request)
    {
        var stopwatch = Stopwatch.StartNew();
        var endpoint = "validate-file";
        
        try
        {
            _metricsService.RecordRequest(endpoint);
            _logger.LogInformation("ValidateFile called with path: {FilePath}", request.FilePath);

            var result = await _analysisService.ValidateFileAsync(request.FilePath, request.RunAnalyzers);
            
            stopwatch.Stop();
            _metricsService.RecordExecutionTime(endpoint, stopwatch.ElapsedMilliseconds);
            
            return Ok(ApiResponse<string>.CreateSuccess(result, stopwatch.ElapsedMilliseconds, HttpContext.TraceIdentifier));
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _metricsService.RecordError(endpoint);
            _logger.LogError(ex, "Error in ValidateFile for path: {FilePath}", request.FilePath);
            
            return StatusCode(500, ApiResponse<string>.CreateError($"Internal server error: {ex.Message}", HttpContext.TraceIdentifier));
        }
    }

    /// <summary>
    /// Extracts comprehensive metadata from a .NET project
    /// </summary>
    /// <param name="request">Project metadata extraction request</param>
    /// <returns>Project metadata in JSON format</returns>
    [HttpPost("extract-project-metadata")]
    public async Task<ActionResult<ApiResponse<string>>> ExtractProjectMetadata([FromBody] ExtractProjectMetadataRequest request)
    {
        var stopwatch = Stopwatch.StartNew();
        var endpoint = "extract-project-metadata";
        
        try
        {
            _metricsService.RecordRequest(endpoint);
            _logger.LogInformation("ExtractProjectMetadata called with path: {ProjectPath}", request.ProjectPath);

            var result = await _analysisService.ExtractProjectMetadataAsync(request.ProjectPath);
            
            stopwatch.Stop();
            _metricsService.RecordExecutionTime(endpoint, stopwatch.ElapsedMilliseconds);
            
            return Ok(ApiResponse<string>.CreateSuccess(result, stopwatch.ElapsedMilliseconds, HttpContext.TraceIdentifier));
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _metricsService.RecordError(endpoint);
            _logger.LogError(ex, "Error in ExtractProjectMetadata for path: {ProjectPath}", request.ProjectPath);
            
            return StatusCode(500, ApiResponse<string>.CreateError($"Internal server error: {ex.Message}", HttpContext.TraceIdentifier));
        }
    }

    /// <summary>
    /// Finds all references to a symbol at the specified position
    /// </summary>
    /// <param name="request">Find usages request</param>
    /// <returns>Symbol usage analysis</returns>
    [HttpPost("find-usages")]
    public async Task<ActionResult<ApiResponse<string>>> FindUsages([FromBody] FindUsagesRequest request)
    {
        var stopwatch = Stopwatch.StartNew();
        var endpoint = "find-usages";
        
        try
        {
            _metricsService.RecordRequest(endpoint);
            _logger.LogInformation("FindUsages called with path: {FilePath}, line: {Line}, column: {Column}", 
                request.FilePath, request.Line, request.Column);

            var result = await _analysisService.FindUsagesAsync(request.FilePath, request.Line, request.Column);
            
            stopwatch.Stop();
            _metricsService.RecordExecutionTime(endpoint, stopwatch.ElapsedMilliseconds);
            
            return Ok(ApiResponse<string>.CreateSuccess(result, stopwatch.ElapsedMilliseconds, HttpContext.TraceIdentifier));
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _metricsService.RecordError(endpoint);
            _logger.LogError(ex, "Error in FindUsages for path: {FilePath}", request.FilePath);
            
            return StatusCode(500, ApiResponse<string>.CreateError($"Internal server error: {ex.Message}", HttpContext.TraceIdentifier));
        }
    }

    /// <summary>
    /// Breaks down C# code into semantically meaningful chunks
    /// </summary>
    /// <param name="request">Code chunking request</param>
    /// <returns>Chunked code analysis</returns>
    [HttpPost("chunk-code")]
    public async Task<ActionResult<ApiResponse<string>>> ChunkCodeBySemantics([FromBody] ChunkCodeRequest request)
    {
        var stopwatch = Stopwatch.StartNew();
        var endpoint = "chunk-code";
        
        try
        {
            _metricsService.RecordRequest(endpoint);
            _logger.LogInformation("ChunkCodeBySemantics called with path: {Path}, strategy: {Strategy}", 
                request.Path, request.Strategy);

            var result = await _analysisService.ChunkCodeBySemanticsAsync(request.Path, request.Strategy, request.IncludeDependencies);
            
            stopwatch.Stop();
            _metricsService.RecordExecutionTime(endpoint, stopwatch.ElapsedMilliseconds);
            
            return Ok(ApiResponse<string>.CreateSuccess(result, stopwatch.ElapsedMilliseconds, HttpContext.TraceIdentifier));
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _metricsService.RecordError(endpoint);
            _logger.LogError(ex, "Error in ChunkCodeBySemantics for path: {Path}", request.Path);
            
            return StatusCode(500, ApiResponse<string>.CreateError($"Internal server error: {ex.Message}", HttpContext.TraceIdentifier));
        }
    }

    /// <summary>
    /// Analyzes code structure, patterns, and architectural metrics
    /// </summary>
    /// <param name="request">Structure analysis request</param>
    /// <returns>Code structure analysis</returns>
    [HttpPost("analyze-structure")]
    public async Task<ActionResult<ApiResponse<string>>> AnalyzeCodeStructure([FromBody] AnalyzeStructureRequest request)
    {
        var stopwatch = Stopwatch.StartNew();
        var endpoint = "analyze-structure";
        
        try
        {
            _metricsService.RecordRequest(endpoint);
            _logger.LogInformation("AnalyzeCodeStructure called with path: {Path}", request.Path);

            var result = await _analysisService.AnalyzeCodeStructureAsync(request.Path, request.DetectPatterns, request.CalculateMetrics);
            
            stopwatch.Stop();
            _metricsService.RecordExecutionTime(endpoint, stopwatch.ElapsedMilliseconds);
            
            return Ok(ApiResponse<string>.CreateSuccess(result, stopwatch.ElapsedMilliseconds, HttpContext.TraceIdentifier));
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _metricsService.RecordError(endpoint);
            _logger.LogError(ex, "Error in AnalyzeCodeStructure for path: {Path}", request.Path);
            
            return StatusCode(500, ApiResponse<string>.CreateError($"Internal server error: {ex.Message}", HttpContext.TraceIdentifier));
        }
    }

    /// <summary>
    /// Generates factual information about code for documentation and analysis
    /// </summary>
    /// <param name="request">Code facts generation request</param>
    /// <returns>Generated code facts</returns>
    [HttpPost("generate-facts")]
    public async Task<ActionResult<ApiResponse<string>>> GenerateCodeFacts([FromBody] GenerateCodeFactsRequest request)
    {
        var stopwatch = Stopwatch.StartNew();
        var endpoint = "generate-facts";
        
        try
        {
            _metricsService.RecordRequest(endpoint);
            _logger.LogInformation("GenerateCodeFacts called with path: {Path}, format: {Format}", 
                request.Path, request.Format);

            var result = await _analysisService.GenerateCodeFactsAsync(request.Path, request.Format, request.IncludeDescriptions);
            
            stopwatch.Stop();
            _metricsService.RecordExecutionTime(endpoint, stopwatch.ElapsedMilliseconds);
            
            return Ok(ApiResponse<string>.CreateSuccess(result, stopwatch.ElapsedMilliseconds, HttpContext.TraceIdentifier));
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _metricsService.RecordError(endpoint);
            _logger.LogError(ex, "Error in GenerateCodeFacts for path: {Path}", request.Path);
            
            return StatusCode(500, ApiResponse<string>.CreateError($"Internal server error: {ex.Message}", HttpContext.TraceIdentifier));
        }
    }

    /// <summary>
    /// Extracts a comprehensive symbol graph showing relationships between types, methods, and other code elements
    /// </summary>
    /// <param name="request">Symbol graph extraction request</param>
    /// <returns>Symbol graph analysis</returns>
    [HttpPost("extract-symbol-graph")]
    public async Task<ActionResult<ApiResponse<string>>> ExtractSymbolGraph([FromBody] ExtractSymbolGraphRequest request)
    {
        var stopwatch = Stopwatch.StartNew();
        var endpoint = "extract-symbol-graph";
        
        try
        {
            _metricsService.RecordRequest(endpoint);
            _logger.LogInformation("ExtractSymbolGraph called with path: {Path}, scope: {Scope}", 
                request.Path, request.Scope);

            var result = await _analysisService.ExtractSymbolGraphAsync(
                request.Path, request.Scope, request.IncludeInheritance,
                request.IncludeMethodCalls, request.IncludeFieldAccess, request.IncludeNamespaces,
                request.IncludeXaml, request.IncludeSql, request.MaxDepth);
            
            stopwatch.Stop();
            _metricsService.RecordExecutionTime(endpoint, stopwatch.ElapsedMilliseconds);
            
            return Ok(ApiResponse<string>.CreateSuccess(result, stopwatch.ElapsedMilliseconds, HttpContext.TraceIdentifier));
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _metricsService.RecordError(endpoint);
            _logger.LogError(ex, "Error in ExtractSymbolGraph for path: {Path}", request.Path);
            
            return StatusCode(500, ApiResponse<string>.CreateError($"Internal server error: {ex.Message}", HttpContext.TraceIdentifier));
        }
    }

    /// <summary>
    /// Breaks down multi-language code into semantically meaningful chunks spanning C#, XAML, and SQL
    /// </summary>
    /// <param name="request">Multi-language chunking request</param>
    /// <returns>Multi-language chunk analysis</returns>
    [HttpPost("chunk-multi-language")]
    public async Task<ActionResult<ApiResponse<string>>> ChunkMultiLanguageCode([FromBody] ChunkMultiLanguageCodeRequest request)
    {
        var stopwatch = Stopwatch.StartNew();
        var endpoint = "chunk-multi-language";
        
        try
        {
            _metricsService.RecordRequest(endpoint);
            _logger.LogInformation("ChunkMultiLanguageCode called with path: {Path}, strategy: {Strategy}", 
                request.Path, request.Strategy);

            var result = await _analysisService.ChunkMultiLanguageCodeAsync(
                request.Path, request.Strategy, request.IncludeDependencies,
                request.IncludeXaml, request.IncludeSql);
            
            stopwatch.Stop();
            _metricsService.RecordExecutionTime(endpoint, stopwatch.ElapsedMilliseconds);
            
            return Ok(ApiResponse<string>.CreateSuccess(result, stopwatch.ElapsedMilliseconds, HttpContext.TraceIdentifier));
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _metricsService.RecordError(endpoint);
            _logger.LogError(ex, "Error in ChunkMultiLanguageCode for path: {Path}", request.Path);
            
            return StatusCode(500, ApiResponse<string>.CreateError($"Internal server error: {ex.Message}", HttpContext.TraceIdentifier));
        }
    }

    /// <summary>
    /// Extracts a unified semantic graph of an entire solution using Roslyn's SymbolFinder APIs
    /// </summary>
    /// <param name="request">Unified semantic graph extraction request</param>
    /// <returns>Unified semantic graph analysis</returns>
    [HttpPost("extract-semantic-graph")]
    public async Task<ActionResult<ApiResponse<string>>> ExtractUnifiedSemanticGraph([FromBody] ExtractUnifiedSemanticGraphRequest request)
    {
        var stopwatch = Stopwatch.StartNew();
        var endpoint = "extract-semantic-graph";
        
        try
        {
            _metricsService.RecordRequest(endpoint);
            _logger.LogInformation("ExtractUnifiedSemanticGraph called with path: {Path}", request.Path);

            var result = await _analysisService.ExtractUnifiedSemanticGraphAsync(
                request.Path, request.IncludeRoles, request.IncludeFeatures,
                request.IncludeCrossProject, request.IncludeCrossLanguage);
            
            stopwatch.Stop();
            _metricsService.RecordExecutionTime(endpoint, stopwatch.ElapsedMilliseconds);
            
            return Ok(ApiResponse<string>.CreateSuccess(result, stopwatch.ElapsedMilliseconds, HttpContext.TraceIdentifier));
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _metricsService.RecordError(endpoint);
            _logger.LogError(ex, "Error in ExtractUnifiedSemanticGraph for path: {Path}", request.Path);
            
            return StatusCode(500, ApiResponse<string>.CreateError($"Internal server error: {ex.Message}", HttpContext.TraceIdentifier));
        }
    }

    /// <summary>
    /// Extracts SQL queries and database operations from C# code
    /// </summary>
    /// <param name="request">SQL extraction request</param>
    /// <returns>SQL extraction results</returns>
    [HttpPost("extract-sql")]
    public async Task<ActionResult<ApiResponse<string>>> ExtractSqlFromCode([FromBody] ExtractSqlRequest request)
    {
        var stopwatch = Stopwatch.StartNew();
        var endpoint = "extract-sql";
        
        try
        {
            _metricsService.RecordRequest(endpoint);
            _logger.LogInformation("ExtractSqlFromCode called with path: {FilePath}", request.FilePath);

            var result = await _analysisService.ExtractSqlFromCodeAsync(request.FilePath);
            
            stopwatch.Stop();
            _metricsService.RecordExecutionTime(endpoint, stopwatch.ElapsedMilliseconds);
            
            return Ok(ApiResponse<string>.CreateSuccess(result, stopwatch.ElapsedMilliseconds, HttpContext.TraceIdentifier));
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _metricsService.RecordError(endpoint);
            _logger.LogError(ex, "Error in ExtractSqlFromCode for path: {FilePath}", request.FilePath);
            
            return StatusCode(500, ApiResponse<string>.CreateError($"Internal server error: {ex.Message}", HttpContext.TraceIdentifier));
        }
    }

    /// <summary>
    /// Analyzes XAML files for UI structure, data bindings, and resources
    /// </summary>
    /// <param name="request">XAML analysis request</param>
    /// <returns>XAML analysis results</returns>
    [HttpPost("analyze-xaml")]
    public async Task<ActionResult<ApiResponse<string>>> AnalyzeXamlFile([FromBody] AnalyzeXamlRequest request)
    {
        var stopwatch = Stopwatch.StartNew();
        var endpoint = "analyze-xaml";
        
        try
        {
            _metricsService.RecordRequest(endpoint);
            _logger.LogInformation("AnalyzeXamlFile called with path: {FilePath}", request.FilePath);

            var result = await _analysisService.AnalyzeXamlFileAsync(request.FilePath);
            
            stopwatch.Stop();
            _metricsService.RecordExecutionTime(endpoint, stopwatch.ElapsedMilliseconds);
            
            return Ok(ApiResponse<string>.CreateSuccess(result, stopwatch.ElapsedMilliseconds, HttpContext.TraceIdentifier));
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _metricsService.RecordError(endpoint);
            _logger.LogError(ex, "Error in AnalyzeXamlFile for path: {FilePath}", request.FilePath);
            
            return StatusCode(500, ApiResponse<string>.CreateError($"Internal server error: {ex.Message}", HttpContext.TraceIdentifier));
        }
    }

    /// <summary>
    /// Analyzes MVVM relationships between Views, ViewModels, and Models in a project
    /// </summary>
    /// <param name="request">MVVM analysis request</param>
    /// <returns>MVVM relationship analysis</returns>
    [HttpPost("analyze-mvvm")]
    public async Task<ActionResult<ApiResponse<string>>> AnalyzeMvvmRelationships([FromBody] AnalyzeMvvmRequest request)
    {
        var stopwatch = Stopwatch.StartNew();
        var endpoint = "analyze-mvvm";
        
        try
        {
            _metricsService.RecordRequest(endpoint);
            _logger.LogInformation("AnalyzeMvvmRelationships called with path: {ProjectPath}", request.ProjectPath);

            var result = await _analysisService.AnalyzeMvvmRelationshipsAsync(request.ProjectPath);
            
            stopwatch.Stop();
            _metricsService.RecordExecutionTime(endpoint, stopwatch.ElapsedMilliseconds);
            
            return Ok(ApiResponse<string>.CreateSuccess(result, stopwatch.ElapsedMilliseconds, HttpContext.TraceIdentifier));
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _metricsService.RecordError(endpoint);
            _logger.LogError(ex, "Error in AnalyzeMvvmRelationships for path: {ProjectPath}", request.ProjectPath);
            
            return StatusCode(500, ApiResponse<string>.CreateError($"Internal server error: {ex.Message}", HttpContext.TraceIdentifier));
        }
    }
}
