using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using RoslynMCP.Models;
using RoslynMCP.Utils;
using System.Text;

namespace RoslynMCP.Services;

public class MultiLanguageChunker
{
    private readonly SqlExtractor _sqlExtractor;
    private readonly XamlAnalyzer _xamlAnalyzer;
    private readonly CodeChunker _codeChunker;

    public MultiLanguageChunker()
    {
        _sqlExtractor = new SqlExtractor();
        _xamlAnalyzer = new XamlAnalyzer();
        _codeChunker = new CodeChunker();
    }

    public async Task<MultiLanguageChunkingResult> ChunkMultiLanguageCodeAsync(
        string path, 
        string strategy = "feature", 
        bool includeDependencies = true,
        bool includeXaml = false,
        bool includeSql = false)
    {
        var result = new MultiLanguageChunkingResult
        {
            MultiLanguageMetadata = new MultiLanguageChunkingMetadata
            {
                Strategy = strategy,
                AnalysisTime = DateTime.UtcNow,
                FilePath = path,
                SupportedLanguages = new List<string> { "CSharp" }
            }
        };

        try
        {
            // Determine if this is a file or project
            bool isProject = path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase);
            string projectPath = isProject ? path : FindContainingProjectAsync(path);

            if (string.IsNullOrEmpty(projectPath))
            {
                throw new InvalidOperationException("Could not find a project file");
            }

            var projectDir = Path.GetDirectoryName(projectPath) ?? throw new InvalidOperationException("Invalid project path");

            // Collect all relevant files
            var csharpFiles = Directory.GetFiles(projectDir, "*.cs", SearchOption.AllDirectories).ToList();
            var xamlFiles = includeXaml ? Directory.GetFiles(projectDir, "*.xaml", SearchOption.AllDirectories).ToList() : new List<string>();

            // Update supported languages
            if (includeXaml && xamlFiles.Any())
            {
                result.MultiLanguageMetadata.SupportedLanguages.Add("XAML");
            }
            if (includeSql)
            {
                result.MultiLanguageMetadata.SupportedLanguages.Add("SQL");
            }

            // Perform multi-language chunking based on strategy
            switch (strategy.ToLowerInvariant())
            {
                case "feature":
                    result.MultiLanguageChunks = await ChunkByFeatureAsync(csharpFiles, xamlFiles, includeSql);
                    break;
                case "dataaccess":
                    result.MultiLanguageChunks = await ChunkByDataAccessAsync(csharpFiles, includeSql);
                    break;
                case "mvvm":
                    result.MultiLanguageChunks = await ChunkByMvvmAsync(csharpFiles, xamlFiles);
                    break;
                case "component":
                    result.MultiLanguageChunks = await ChunkByComponentAsync(csharpFiles, xamlFiles);
                    break;
                default:
                    result.MultiLanguageChunks = await ChunkByFeatureAsync(csharpFiles, xamlFiles, includeSql);
                    break;
            }

            if (includeDependencies)
            {
                result.CrossLanguageRelationships = AnalyzeCrossLanguageRelationships(result.MultiLanguageChunks);
            }

            // Update metadata
            UpdateMultiLanguageMetadata(result);

            return result;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to chunk multi-language code: {ex.Message}", ex);
        }
    }

    private async Task<List<MultiLanguageChunk>> ChunkByFeatureAsync(List<string> csharpFiles, List<string> xamlFiles, bool includeSql)
    {
        var chunks = new List<MultiLanguageChunk>();

        // Group files by feature (based on naming conventions and directory structure)
        var featureGroups = GroupFilesByFeature(csharpFiles, xamlFiles);

        foreach (var featureGroup in featureGroups)
        {
            var chunk = new MultiLanguageChunk
            {
                Id = Guid.NewGuid().ToString(),
                Type = "Feature",
                Name = featureGroup.Key,
                ChunkType = MultiLanguageChunkType.Feature,
                Components = new List<LanguageComponent>()
            };

            // Process C# files in this feature
            foreach (var csharpFile in featureGroup.Value.CSharpFiles)
            {
                var component = await CreateCSharpComponentAsync(csharpFile, includeSql);
                chunk.Components.Add(component);
            }

            // Process XAML files in this feature
            foreach (var xamlFile in featureGroup.Value.XamlFiles)
            {
                var component = await CreateXamlComponentAsync(xamlFile);
                chunk.Components.Add(component);
            }

            // Combine content and calculate complexity
            chunk.Content = CombineComponentContent(chunk.Components);
            chunk.ComplexityScore = CalculateMultiLanguageComplexity(chunk.Components);
            chunk.Location = GetPrimaryLocation(chunk.Components);

            // Add cross-language metadata
            chunk.CrossLanguageMetadata = AnalyzeFeatureMetadata(chunk.Components);

            chunks.Add(chunk);
        }

        return chunks;
    }

    private async Task<List<MultiLanguageChunk>> ChunkByDataAccessAsync(List<string> csharpFiles, bool includeSql)
    {
        var chunks = new List<MultiLanguageChunk>();

        // Find data access related files
        var dataAccessFiles = csharpFiles.Where(f => 
            IsDataAccessFile(f)).ToList();

        foreach (var file in dataAccessFiles)
        {
            var chunk = new MultiLanguageChunk
            {
                Id = Guid.NewGuid().ToString(),
                Type = "DataAccess",
                Name = Path.GetFileNameWithoutExtension(file),
                ChunkType = MultiLanguageChunkType.DataAccess,
                Components = new List<LanguageComponent>()
            };

            // Add C# component
            var csharpComponent = await CreateCSharpComponentAsync(file, includeSql);
            csharpComponent.Role = DetermineDataAccessRole(file);
            chunk.Components.Add(csharpComponent);

            // Extract SQL if requested
            if (includeSql)
            {
                var sqlMetadata = await _sqlExtractor.ExtractSqlFromFileAsync(file);
                foreach (var query in sqlMetadata.Queries)
                {
                    var sqlComponent = new LanguageComponent
                    {
                        Id = Guid.NewGuid().ToString(),
                        Language = "SQL",
                        FilePath = file,
                        Content = query.Content,
                        Location = LocationConverter.ToSymbolLocation(new Models.Location
                        {
                            FilePath = query.Location.FilePath,
                            StartLine = query.Location.Line,
                            EndLine = query.Location.EndLine,
                            StartColumn = query.Location.Column,
                            EndColumn = query.Location.EndColumn
                        }),
                        Role = ComponentRole.Query,
                        LanguageSpecificMetadata = new Dictionary<string, object>
                        {
                            ["QueryType"] = query.Type.ToString(),
                            ["Framework"] = query.Framework.ToString(),
                            ["Tables"] = query.Tables,
                            ["Parameters"] = query.Parameters
                        }
                    };
                    chunk.Components.Add(sqlComponent);
                }
            }

            chunk.Content = CombineComponentContent(chunk.Components);
            chunk.ComplexityScore = CalculateMultiLanguageComplexity(chunk.Components);
            chunk.Location = GetPrimaryLocation(chunk.Components);

            chunks.Add(chunk);
        }

        return chunks;
    }

    private async Task<List<MultiLanguageChunk>> ChunkByMvvmAsync(List<string> csharpFiles, List<string> xamlFiles)
    {
        var chunks = new List<MultiLanguageChunk>();

        // Group XAML files with their corresponding ViewModels and code-behind
        foreach (var xamlFile in xamlFiles)
        {
            var chunk = new MultiLanguageChunk
            {
                Id = Guid.NewGuid().ToString(),
                Type = "MVVM",
                Name = Path.GetFileNameWithoutExtension(xamlFile),
                ChunkType = MultiLanguageChunkType.MvvmPattern,
                Components = new List<LanguageComponent>()
            };

            // Add XAML component (View)
            var xamlComponent = await CreateXamlComponentAsync(xamlFile);
            xamlComponent.Role = ComponentRole.View;
            chunk.Components.Add(xamlComponent);

            // Find and add code-behind
            var codeBehindFile = xamlFile.Replace(".xaml", ".xaml.cs");
            if (File.Exists(codeBehindFile))
            {
                var codeBehindComponent = await CreateCSharpComponentAsync(codeBehindFile, false);
                codeBehindComponent.Role = ComponentRole.CodeBehind;
                chunk.Components.Add(codeBehindComponent);
            }

            // Find corresponding ViewModel
            var viewModelFile = FindViewModelFile(xamlFile, csharpFiles);
            if (!string.IsNullOrEmpty(viewModelFile))
            {
                var viewModelComponent = await CreateCSharpComponentAsync(viewModelFile, false);
                viewModelComponent.Role = ComponentRole.ViewModel;
                chunk.Components.Add(viewModelComponent);

                // Find corresponding Model(s)
                var modelFiles = FindModelFiles(viewModelFile, csharpFiles);
                foreach (var modelFile in modelFiles)
                {
                    var modelComponent = await CreateCSharpComponentAsync(modelFile, false);
                    modelComponent.Role = ComponentRole.Model;
                    chunk.Components.Add(modelComponent);
                }
            }

            chunk.Content = CombineComponentContent(chunk.Components);
            chunk.ComplexityScore = CalculateMultiLanguageComplexity(chunk.Components);
            chunk.Location = GetPrimaryLocation(chunk.Components);
            chunk.CrossLanguageMetadata = AnalyzeMvvmMetadata(chunk.Components);

            chunks.Add(chunk);
        }

        return chunks;
    }

    private async Task<List<MultiLanguageChunk>> ChunkByComponentAsync(List<string> csharpFiles, List<string> xamlFiles)
    {
        var chunks = new List<MultiLanguageChunk>();

        // Group XAML files with their code-behind as reusable components
        foreach (var xamlFile in xamlFiles)
        {
            // Skip if this is a Window or Page (not a reusable component)
            var xamlContent = await File.ReadAllTextAsync(xamlFile);
            if (xamlContent.Contains("<Window") || xamlContent.Contains("<Page"))
                continue;

            var chunk = new MultiLanguageChunk
            {
                Id = Guid.NewGuid().ToString(),
                Type = "Component",
                Name = Path.GetFileNameWithoutExtension(xamlFile),
                ChunkType = MultiLanguageChunkType.Component,
                Components = new List<LanguageComponent>()
            };

            // Add XAML component
            var xamlComponent = await CreateXamlComponentAsync(xamlFile);
            chunk.Components.Add(xamlComponent);

            // Add code-behind if exists
            var codeBehindFile = xamlFile.Replace(".xaml", ".xaml.cs");
            if (File.Exists(codeBehindFile))
            {
                var codeBehindComponent = await CreateCSharpComponentAsync(codeBehindFile, false);
                chunk.Components.Add(codeBehindComponent);
            }

            chunk.Content = CombineComponentContent(chunk.Components);
            chunk.ComplexityScore = CalculateMultiLanguageComplexity(chunk.Components);
            chunk.Location = GetPrimaryLocation(chunk.Components);

            chunks.Add(chunk);
        }

        return chunks;
    }

    private async Task<LanguageComponent> CreateCSharpComponentAsync(string filePath, bool includeSql)
    {
        var content = await File.ReadAllTextAsync(filePath);
        var component = new LanguageComponent
        {
            Id = Guid.NewGuid().ToString(),
            Language = "CSharp",
            FilePath = filePath,
            Content = content,
            Location = LocationConverter.ToSymbolLocation(new Models.Location { FilePath = filePath, StartLine = 1, EndLine = content.Split('\n').Length }),
            Role = DetermineCSharpRole(filePath, content)
        };

        // Add C#-specific metadata
        var syntaxTree = CSharpSyntaxTree.ParseText(content);
        var root = syntaxTree.GetRoot();
        
        component.LanguageSpecificMetadata = new Dictionary<string, object>
        {
            ["ClassCount"] = root.DescendantNodes().OfType<ClassDeclarationSyntax>().Count(),
            ["MethodCount"] = root.DescendantNodes().OfType<MethodDeclarationSyntax>().Count(),
            ["PropertyCount"] = root.DescendantNodes().OfType<PropertyDeclarationSyntax>().Count(),
            ["InterfaceCount"] = root.DescendantNodes().OfType<InterfaceDeclarationSyntax>().Count()
        };

        // Add SQL metadata if requested
        if (includeSql)
        {
            try
            {
                var sqlMetadata = await _sqlExtractor.ExtractSqlFromFileAsync(filePath);
                component.LanguageSpecificMetadata["SqlQueries"] = sqlMetadata.Queries.Count;
                component.LanguageSpecificMetadata["SqlTables"] = sqlMetadata.ReferencedTables;
                component.LanguageSpecificMetadata["SqlOperations"] = sqlMetadata.OperationCounts;
            }
            catch
            {
                // SQL extraction failed, continue without SQL metadata
            }
        }

        return component;
    }

    private async Task<LanguageComponent> CreateXamlComponentAsync(string filePath)
    {
        var content = await File.ReadAllTextAsync(filePath);
        var component = new LanguageComponent
        {
            Id = Guid.NewGuid().ToString(),
            Language = "XAML",
            FilePath = filePath,
            Content = content,
            Location = LocationConverter.ToSymbolLocation(new Models.Location { FilePath = filePath, StartLine = 1, EndLine = content.Split('\n').Length }),
            Role = DetermineXamlRole(filePath, content)
        };

        // Add XAML-specific metadata
        try
        {
            var xamlMetadata = await _xamlAnalyzer.AnalyzeXamlFileAsync(filePath);
            component.LanguageSpecificMetadata = new Dictionary<string, object>
            {
                ["ElementCount"] = xamlMetadata.Elements.Count,
                ["BindingCount"] = xamlMetadata.Bindings.Count,
                ["ResourceCount"] = xamlMetadata.Resources.Count,
                ["EventHandlerCount"] = xamlMetadata.EventHandlers.Count,
                ["MaxNestingDepth"] = xamlMetadata.Metadata.MaxNestingDepth,
                ["ElementTypes"] = xamlMetadata.Metadata.ElementTypeDistribution
            };
        }
        catch
        {
            // XAML analysis failed, use basic metadata
            component.LanguageSpecificMetadata = new Dictionary<string, object>
            {
                ["ElementCount"] = 0,
                ["BindingCount"] = 0
            };
        }

        return component;
    }

    private Dictionary<string, FeatureGroup> GroupFilesByFeature(List<string> csharpFiles, List<string> xamlFiles)
    {
        var groups = new Dictionary<string, FeatureGroup>();

        // Group by directory structure and naming conventions
        var allFiles = csharpFiles.Concat(xamlFiles);
        
        foreach (var file in allFiles)
        {
            var featureName = DetermineFeatureName(file);
            
            if (!groups.ContainsKey(featureName))
            {
                groups[featureName] = new FeatureGroup();
            }

            if (file.EndsWith(".cs"))
            {
                groups[featureName].CSharpFiles.Add(file);
            }
            else if (file.EndsWith(".xaml"))
            {
                groups[featureName].XamlFiles.Add(file);
            }
        }

        return groups;
    }

    private string DetermineFeatureName(string filePath)
    {
        var fileName = Path.GetFileNameWithoutExtension(filePath);
        var directory = Path.GetDirectoryName(filePath) ?? "";

        // Extract feature name from directory structure
        var pathParts = directory.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        
        // Look for feature indicators in path
        var featureIndicators = new[] { "Views", "ViewModels", "Models", "Controllers", "Services", "Features" };
        var featurePart = pathParts.LastOrDefault(p => !featureIndicators.Contains(p, StringComparer.OrdinalIgnoreCase));

        if (!string.IsNullOrEmpty(featurePart))
        {
            return featurePart;
        }

        // Fallback to filename-based grouping
        if (fileName.EndsWith("View") || fileName.EndsWith("ViewModel") || fileName.EndsWith("Model"))
        {
            return fileName.Replace("View", "").Replace("Model", "");
        }

        return "Core";
    }

    private ComponentRole DetermineCSharpRole(string filePath, string content)
    {
        var fileName = Path.GetFileNameWithoutExtension(filePath);
        
        if (fileName.EndsWith("ViewModel") || fileName.EndsWith("VM"))
            return ComponentRole.ViewModel;
        if (fileName.EndsWith("Model") || fileName.EndsWith("Entity"))
            return ComponentRole.Model;
        if (fileName.EndsWith("Service"))
            return ComponentRole.Service;
        if (fileName.EndsWith("Controller"))
            return ComponentRole.Controller;
        if (fileName.EndsWith("Repository"))
            return ComponentRole.Repository;
        if (filePath.Contains(".xaml.cs"))
            return ComponentRole.CodeBehind;
        
        // Analyze content for data access patterns
        if (content.Contains("SqlConnection") || content.Contains("DbContext") || content.Contains("IRepository"))
            return ComponentRole.DataAccess;

        return ComponentRole.Unknown;
    }

    private ComponentRole DetermineXamlRole(string filePath, string content)
    {
        if (content.Contains("<Window"))
            return ComponentRole.View;
        if (content.Contains("<UserControl"))
            return ComponentRole.View;
        if (content.Contains("<Page"))
            return ComponentRole.View;
        
        return ComponentRole.View;
    }

    private ComponentRole DetermineDataAccessRole(string filePath)
    {
        var fileName = Path.GetFileNameWithoutExtension(filePath);
        
        if (fileName.EndsWith("Repository"))
            return ComponentRole.Repository;
        if (fileName.EndsWith("Service") && fileName.Contains("Data"))
            return ComponentRole.DataAccess;
        if (fileName.Contains("Context") || fileName.Contains("DbContext"))
            return ComponentRole.DataAccess;
        
        return ComponentRole.DataAccess;
    }

    private bool IsDataAccessFile(string filePath)
    {
        var fileName = Path.GetFileNameWithoutExtension(filePath);
        var dataAccessIndicators = new[] { "Repository", "Context", "DataAccess", "Dal", "Dao" };
        
        return dataAccessIndicators.Any(indicator => 
            fileName.Contains(indicator, StringComparison.OrdinalIgnoreCase));
    }

    private string FindViewModelFile(string xamlFile, List<string> csharpFiles)
    {
        var baseName = Path.GetFileNameWithoutExtension(xamlFile);
        var viewModelName = baseName + "ViewModel";
        
        return csharpFiles.FirstOrDefault(f => 
            Path.GetFileNameWithoutExtension(f).Equals(viewModelName, StringComparison.OrdinalIgnoreCase)) ?? "";
    }

    private List<string> FindModelFiles(string viewModelFile, List<string> csharpFiles)
    {
        // Simple heuristic: look for files that might be models referenced by the ViewModel
        var viewModelContent = File.ReadAllText(viewModelFile);
        var modelFiles = new List<string>();

        foreach (var csharpFile in csharpFiles)
        {
            var fileName = Path.GetFileNameWithoutExtension(csharpFile);
            if ((fileName.EndsWith("Model") || fileName.EndsWith("Entity")) &&
                viewModelContent.Contains(fileName))
            {
                modelFiles.Add(csharpFile);
            }
        }

        return modelFiles;
    }

    private string CombineComponentContent(List<LanguageComponent> components)
    {
        var sb = new StringBuilder();
        
        foreach (var component in components)
        {
            sb.AppendLine($"// === {component.Language} Component: {Path.GetFileName(component.FilePath)} ===");
            sb.AppendLine(component.Content);
            sb.AppendLine();
        }

        return sb.ToString();
    }

    private int CalculateMultiLanguageComplexity(List<LanguageComponent> components)
    {
        var totalComplexity = 0;

        foreach (var component in components)
        {
            switch (component.Language)
            {
                case "CSharp":
                    totalComplexity += CalculateCSharpComplexity(component.Content);
                    break;
                case "XAML":
                    totalComplexity += CalculateXamlComplexity(component.Content);
                    break;
                case "SQL":
                    totalComplexity += 5; // Base complexity for SQL queries
                    break;
            }
        }

        // Add cross-language complexity bonus
        var languageCount = components.Select(c => c.Language).Distinct().Count();
        totalComplexity += (languageCount - 1) * 2;

        return totalComplexity;
    }

    private int CalculateCSharpComplexity(string content)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(content);
        var root = syntaxTree.GetRoot();

        var complexity = 1; // Base complexity
        
        var controlFlowNodes = root.DescendantNodes().Where(n =>
            n is IfStatementSyntax ||
            n is WhileStatementSyntax ||
            n is ForStatementSyntax ||
            n is ForEachStatementSyntax ||
            n is SwitchStatementSyntax ||
            n is ConditionalExpressionSyntax ||
            n is CatchClauseSyntax);

        complexity += controlFlowNodes.Count();
        
        return complexity;
    }

    private int CalculateXamlComplexity(string content)
    {
        // Simple XAML complexity based on element count and binding count
        var elementCount = content.Split('<').Length - 1;
        var bindingCount = content.Split("{Binding").Length - 1;
        
        return elementCount + (bindingCount * 2);
    }

    private Models.Location GetPrimaryLocation(List<LanguageComponent> components)
    {
        // Return the location of the first component, or create a default
        var primaryComponent = components.FirstOrDefault();
        return primaryComponent?.Location != null 
            ? LocationConverter.ToLocation(primaryComponent.Location) 
            : new Models.Location();
    }

    private Dictionary<string, object> AnalyzeFeatureMetadata(List<LanguageComponent> components)
    {
        var metadata = new Dictionary<string, object>();
        
        var languageDistribution = components
            .GroupBy(c => c.Language)
            .ToDictionary(g => g.Key, g => g.Count());
        
        var roleDistribution = components
            .GroupBy(c => c.Role.ToString())
            .ToDictionary(g => g.Key, g => g.Count());

        metadata["LanguageDistribution"] = languageDistribution;
        metadata["RoleDistribution"] = roleDistribution;
        metadata["ComponentCount"] = components.Count;
        metadata["FileCount"] = components.Select(c => c.FilePath).Distinct().Count();

        return metadata;
    }

    private Dictionary<string, object> AnalyzeMvvmMetadata(List<LanguageComponent> components)
    {
        var metadata = new Dictionary<string, object>();
        
        var hasView = components.Any(c => c.Role == ComponentRole.View);
        var hasViewModel = components.Any(c => c.Role == ComponentRole.ViewModel);
        var hasModel = components.Any(c => c.Role == ComponentRole.Model);
        var hasCodeBehind = components.Any(c => c.Role == ComponentRole.CodeBehind);

        metadata["HasView"] = hasView;
        metadata["HasViewModel"] = hasViewModel;
        metadata["HasModel"] = hasModel;
        metadata["HasCodeBehind"] = hasCodeBehind;
        metadata["MvvmCompliance"] = hasView && hasViewModel;
        metadata["ComponentCount"] = components.Count;

        return metadata;
    }

    private Dictionary<string, List<string>> AnalyzeCrossLanguageRelationships(List<MultiLanguageChunk> chunks)
    {
        var relationships = new Dictionary<string, List<string>>();

        foreach (var chunk in chunks)
        {
            relationships[chunk.Id] = new List<string>();
            
            // Find relationships based on shared files, naming conventions, etc.
            foreach (var otherChunk in chunks.Where(c => c.Id != chunk.Id))
            {
                if (HasRelationship(chunk, otherChunk))
                {
                    relationships[chunk.Id].Add(otherChunk.Id);
                }
            }
        }

        return relationships;
    }

    private bool HasRelationship(MultiLanguageChunk chunk1, MultiLanguageChunk chunk2)
    {
        // Check for shared file names or similar naming patterns
        var chunk1Files = chunk1.Components.Select(c => Path.GetFileNameWithoutExtension(c.FilePath));
        var chunk2Files = chunk2.Components.Select(c => Path.GetFileNameWithoutExtension(c.FilePath));

        return chunk1Files.Any(f1 => chunk2Files.Any(f2 => 
            f1.Contains(f2, StringComparison.OrdinalIgnoreCase) || 
            f2.Contains(f1, StringComparison.OrdinalIgnoreCase)));
    }

    private void UpdateMultiLanguageMetadata(MultiLanguageChunkingResult result)
    {
        result.MultiLanguageMetadata.TotalChunks = result.MultiLanguageChunks.Count;
        
        result.MultiLanguageMetadata.LanguageDistribution = result.MultiLanguageChunks
            .SelectMany(c => c.Components)
            .GroupBy(c => c.Language)
            .ToDictionary(g => g.Key, g => g.Count());

        result.MultiLanguageMetadata.ChunkTypeDistribution = result.MultiLanguageChunks
            .GroupBy(c => c.ChunkType.ToString())
            .ToDictionary(g => g.Key, g => g.Count());

        result.MultiLanguageMetadata.CrossLanguageRelationships = result.CrossLanguageRelationships.Values.Sum(v => v.Count);
    }

    private string FindContainingProjectAsync(string filePath)
    {
        var directory = new DirectoryInfo(Path.GetDirectoryName(filePath) ?? ".");
        
        while (directory != null)
        {
            var projectFiles = directory.GetFiles("*.csproj");
            if (projectFiles.Length > 0)
            {
                return projectFiles[0].FullName;
            }
            directory = directory.Parent;
        }

        return string.Empty;
    }

    private class FeatureGroup
    {
        public List<string> CSharpFiles { get; set; } = new();
        public List<string> XamlFiles { get; set; } = new();
    }
}
