using System.Xaml;
using System.Xml;
using System.Xml.Linq;
using RoslynMCP.Models;
using System.Text.RegularExpressions;

namespace RoslynMCP.Services;

public class XamlAnalyzer
{
    private static readonly Regex BindingPattern = new Regex(
        @"\{Binding\s+(?:Path=)?([^,}]+)(?:,\s*Mode=([^,}]+))?(?:,\s*Source=([^,}]+))?(?:,\s*Converter=([^,}]+))?\}",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex EventHandlerPattern = new Regex(
        @"(\w+)=""([^""]+)""",
        RegexOptions.Compiled);

    public async Task<XamlMetadata> AnalyzeXamlFileAsync(string filePath)
    {
        var metadata = new XamlMetadata
        {
            Metadata = new XamlAnalysisMetadata
            {
                AnalyzedAt = DateTime.UtcNow,
                ProcessedFiles = new List<string> { filePath }
            }
        };

        try
        {
            var xamlContent = await File.ReadAllTextAsync(filePath);
            var document = XDocument.Parse(xamlContent);

            if (document.Root == null)
            {
                throw new InvalidOperationException("XAML document has no root element");
            }

            // Analyze XAML structure
            AnalyzeElements(document.Root, null, filePath, metadata);
            AnalyzeResources(document, filePath, metadata);
            
            // Update metadata
            UpdateMetadata(metadata);

            return metadata;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to analyze XAML file {filePath}: {ex.Message}", ex);
        }
    }

    public async Task<MvvmRelationships> AnalyzeMvvmRelationshipsAsync(string projectPath)
    {
        var relationships = new MvvmRelationships
        {
            Metadata = new MvvmAnalysisMetadata
            {
                AnalyzedAt = DateTime.UtcNow
            }
        };

        try
        {
            var projectDir = Path.GetDirectoryName(projectPath) ?? throw new InvalidOperationException("Invalid project path");
            
            // Find all XAML files
            var xamlFiles = Directory.GetFiles(projectDir, "*.xaml", SearchOption.AllDirectories);
            
            // Find all C# files
            var csharpFiles = Directory.GetFiles(projectDir, "*.cs", SearchOption.AllDirectories);

            foreach (var xamlFile in xamlFiles)
            {
                await AnalyzeViewViewModelRelationship(xamlFile, csharpFiles, relationships);
            }

            // Analyze ViewModel-Model relationships
            await AnalyzeViewModelModelRelationships(csharpFiles, relationships);

            UpdateMvvmMetadata(relationships);

            return relationships;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to analyze MVVM relationships: {ex.Message}", ex);
        }
    }

    private void AnalyzeElements(XElement element, string? parentId, string filePath, XamlMetadata metadata, int depth = 0)
    {
        var elementId = Guid.NewGuid().ToString();
        var elementName = GetElementName(element);
        var elementType = element.Name.LocalName;

        var xamlElement = new XamlElement
        {
            Id = elementId,
            Name = elementName,
            Type = elementType,
            ParentId = parentId,
            Location = GetXamlLocation(element, filePath),
            Attributes = GetAttributes(element)
        };

        // Analyze bindings in this element
        AnalyzeElementBindings(element, xamlElement, metadata);
        
        // Analyze event handlers
        AnalyzeElementEventHandlers(element, xamlElement, metadata);

        metadata.Elements.Add(xamlElement);

        // Update nesting depth
        metadata.Metadata.MaxNestingDepth = Math.Max(metadata.Metadata.MaxNestingDepth, depth);

        // Recursively analyze child elements
        foreach (var child in element.Elements())
        {
            AnalyzeElements(child, elementId, filePath, metadata, depth + 1);
            xamlElement.ChildIds.Add(child.Name.LocalName);
        }
    }

    private void AnalyzeElementBindings(XElement element, XamlElement xamlElement, XamlMetadata metadata)
    {
        foreach (var attribute in element.Attributes())
        {
            var attributeValue = attribute.Value;
            var bindingMatches = BindingPattern.Matches(attributeValue);

            foreach (Match match in bindingMatches)
            {
                var binding = new XamlBinding
                {
                    Id = Guid.NewGuid().ToString(),
                    ElementId = xamlElement.Id,
                    Property = attribute.Name.LocalName,
                    Path = match.Groups[1].Value.Trim(),
                    Mode = ParseBindingMode(match.Groups[2].Value),
                    Source = match.Groups[3].Success ? match.Groups[3].Value.Trim() : null,
                    Converter = match.Groups[4].Success ? match.Groups[4].Value.Trim() : null,
                    Location = GetXamlLocation(element, xamlElement.Location.FilePath)
                };

                xamlElement.ElementBindings.Add(binding);
                metadata.Bindings.Add(binding);
            }
        }
    }

    private void AnalyzeElementEventHandlers(XElement element, XamlElement xamlElement, XamlMetadata metadata)
    {
        foreach (var attribute in element.Attributes())
        {
            var attributeName = attribute.Name.LocalName;
            var attributeValue = attribute.Value;

            // Check if this looks like an event handler (typically ends with event names)
            if (IsEventAttribute(attributeName) && !string.IsNullOrWhiteSpace(attributeValue))
            {
                var eventHandler = new XamlEventHandler
                {
                    Id = Guid.NewGuid().ToString(),
                    ElementId = xamlElement.Id,
                    EventName = attributeName,
                    HandlerName = attributeValue,
                    Location = GetXamlLocation(element, xamlElement.Location.FilePath),
                    CodeBehindMethod = attributeValue // Assume it maps to a code-behind method
                };

                xamlElement.ElementEventHandlers.Add(eventHandler);
                metadata.EventHandlers.Add(eventHandler);
            }
        }
    }

    private void AnalyzeResources(XDocument document, string filePath, XamlMetadata metadata)
    {
        // Find all resource dictionaries
        var resourceElements = document.Descendants()
            .Where(e => e.Name.LocalName.EndsWith("Resources") || e.Name.LocalName == "ResourceDictionary");

        foreach (var resourceElement in resourceElements)
        {
            var scope = DetermineResourceScope(resourceElement);
            
            foreach (var resource in resourceElement.Elements())
            {
                var key = resource.Attribute("Key")?.Value ?? 
                         resource.Attribute(XName.Get("Key", "http://schemas.microsoft.com/winfx/2006/xaml"))?.Value ??
                         Guid.NewGuid().ToString();

                var xamlResource = new XamlResource
                {
                    Id = Guid.NewGuid().ToString(),
                    Key = key,
                    Type = resource.Name.LocalName,
                    Value = resource.ToString(),
                    Scope = scope,
                    Location = GetXamlLocation(resource, filePath)
                };

                metadata.Resources.Add(xamlResource);
            }
        }
    }

    private async Task AnalyzeViewViewModelRelationship(string xamlFile, string[] csharpFiles, MvvmRelationships relationships)
    {
        try
        {
            var xamlContent = await File.ReadAllTextAsync(xamlFile);
            var document = XDocument.Parse(xamlContent);

            // Look for DataContext bindings or ViewModel references
            var dataContextElements = document.Descendants()
                .Where(e => e.Attributes().Any(a => a.Name.LocalName == "DataContext"));

            foreach (var element in dataContextElements)
            {
                var dataContextValue = element.Attribute("DataContext")?.Value;
                if (!string.IsNullOrWhiteSpace(dataContextValue))
                {
                    // Try to find corresponding ViewModel class
                    var viewModelFile = FindViewModelFile(dataContextValue, csharpFiles);
                    if (!string.IsNullOrEmpty(viewModelFile))
                    {
                        var mapping = new ViewViewModelMapping
                        {
                            Id = Guid.NewGuid().ToString(),
                            ViewFilePath = xamlFile,
                            ViewModelClassName = ExtractClassName(dataContextValue),
                            ViewModelFilePath = viewModelFile,
                            MappingType = MvvmMappingType.DataContext
                        };

                        // Analyze shared properties and commands
                        await AnalyzeSharedMembers(xamlFile, viewModelFile, mapping);

                        relationships.ViewViewModelMappings.Add(mapping);
                    }
                }
            }

            // Also check for code-behind file
            var codeBehindFile = xamlFile.Replace(".xaml", ".xaml.cs");
            if (File.Exists(codeBehindFile))
            {
                await AnalyzeCodeBehindRelationship(xamlFile, codeBehindFile, relationships);
            }
        }
        catch (Exception ex)
        {
            // Log error but continue processing other files
            Console.Error.WriteLine($"Error analyzing XAML file {xamlFile}: {ex.Message}");
        }
    }

    private async Task AnalyzeCodeBehindRelationship(string xamlFile, string codeBehindFile, MvvmRelationships relationships)
    {
        try
        {
            var codeBehindContent = await File.ReadAllTextAsync(codeBehindFile);
            
            // Simple analysis - look for class name
            var classNameMatch = Regex.Match(codeBehindContent, @"class\s+(\w+)\s*:");
            if (classNameMatch.Success)
            {
                var className = classNameMatch.Groups[1].Value;
                
                var mapping = new ViewViewModelMapping
                {
                    Id = Guid.NewGuid().ToString(),
                    ViewFilePath = xamlFile,
                    ViewModelClassName = className,
                    ViewModelFilePath = codeBehindFile,
                    MappingType = MvvmMappingType.Property
                };

                relationships.ViewViewModelMappings.Add(mapping);
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error analyzing code-behind file {codeBehindFile}: {ex.Message}");
        }
    }

    private async Task AnalyzeViewModelModelRelationships(string[] csharpFiles, MvvmRelationships relationships)
    {
        var viewModelFiles = csharpFiles.Where(f => 
            Path.GetFileName(f).Contains("ViewModel") || 
            Path.GetFileName(f).Contains("VM")).ToArray();

        foreach (var viewModelFile in viewModelFiles)
        {
            try
            {
                var content = await File.ReadAllTextAsync(viewModelFile);
                
                // Look for model references (simple pattern matching)
                var modelReferences = Regex.Matches(content, @"(\w+Model|\w+Entity)\s+(\w+)")
                    .Cast<Match>()
                    .Select(m => m.Groups[1].Value)
                    .Distinct();

                foreach (var modelRef in modelReferences)
                {
                    var modelFile = FindModelFile(modelRef, csharpFiles);
                    if (!string.IsNullOrEmpty(modelFile))
                    {
                        var mapping = new ViewModelModelMapping
                        {
                            Id = Guid.NewGuid().ToString(),
                            ViewModelClassName = Path.GetFileNameWithoutExtension(viewModelFile),
                            ModelClassName = modelRef,
                            ModelFilePath = modelFile,
                            MappingType = MvvmMappingType.Property
                        };

                        relationships.ViewModelModelMappings.Add(mapping);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Error analyzing ViewModel file {viewModelFile}: {ex.Message}");
            }
        }
    }

    private async Task AnalyzeSharedMembers(string xamlFile, string viewModelFile, ViewViewModelMapping mapping)
    {
        try
        {
            var xamlContent = await File.ReadAllTextAsync(xamlFile);
            var viewModelContent = await File.ReadAllTextAsync(viewModelFile);

            // Extract binding paths from XAML
            var bindingPaths = BindingPattern.Matches(xamlContent)
                .Cast<Match>()
                .Select(m => m.Groups[1].Value.Trim())
                .Distinct()
                .ToList();

            // Extract properties from ViewModel (simple pattern)
            var viewModelProperties = Regex.Matches(viewModelContent, @"public\s+\w+\s+(\w+)\s*\{")
                .Cast<Match>()
                .Select(m => m.Groups[1].Value)
                .Distinct()
                .ToList();

            // Find shared properties
            mapping.SharedProperties = bindingPaths.Intersect(viewModelProperties).ToList();

            // Extract commands (simple pattern)
            var commands = Regex.Matches(viewModelContent, @"public\s+ICommand\s+(\w+)")
                .Cast<Match>()
                .Select(m => m.Groups[1].Value)
                .Distinct()
                .ToList();

            mapping.SharedCommands = commands;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error analyzing shared members: {ex.Message}");
        }
    }

    private string GetElementName(XElement element)
    {
        return element.Attribute("Name")?.Value ?? 
               element.Attribute(XName.Get("Name", "http://schemas.microsoft.com/winfx/2006/xaml"))?.Value ?? 
               element.Name.LocalName;
    }

    private Dictionary<string, string> GetAttributes(XElement element)
    {
        return element.Attributes()
            .ToDictionary(a => a.Name.LocalName, a => a.Value);
    }

    private XamlLocation GetXamlLocation(XElement element, string filePath)
    {
        var lineInfo = element as IXmlLineInfo;
        return new XamlLocation
        {
            FilePath = filePath,
            Line = lineInfo?.LineNumber ?? 0,
            Column = lineInfo?.LinePosition ?? 0,
            EndLine = lineInfo?.LineNumber ?? 0,
            EndColumn = lineInfo?.LinePosition ?? 0
        };
    }

    private XamlBindingMode ParseBindingMode(string modeString)
    {
        if (string.IsNullOrWhiteSpace(modeString))
            return XamlBindingMode.Default;

        return modeString.Trim() switch
        {
            "OneWay" => XamlBindingMode.OneWay,
            "TwoWay" => XamlBindingMode.TwoWay,
            "OneTime" => XamlBindingMode.OneTime,
            "OneWayToSource" => XamlBindingMode.OneWayToSource,
            _ => XamlBindingMode.Unknown
        };
    }

    private bool IsEventAttribute(string attributeName)
    {
        var eventSuffixes = new[] { "Click", "Changed", "Loaded", "Unloaded", "MouseEnter", "MouseLeave", "KeyDown", "KeyUp", "GotFocus", "LostFocus" };
        return eventSuffixes.Any(suffix => attributeName.EndsWith(suffix));
    }

    private XamlResourceScope DetermineResourceScope(XElement resourceElement)
    {
        var parent = resourceElement.Parent;
        if (parent == null) return XamlResourceScope.Unknown;

        return parent.Name.LocalName switch
        {
            "Application" => XamlResourceScope.Application,
            "Window" => XamlResourceScope.Window,
            "UserControl" => XamlResourceScope.UserControl,
            _ => XamlResourceScope.Local
        };
    }

    private string FindViewModelFile(string dataContextValue, string[] csharpFiles)
    {
        var className = ExtractClassName(dataContextValue);
        return csharpFiles.FirstOrDefault(f => 
            Path.GetFileNameWithoutExtension(f).Equals(className, StringComparison.OrdinalIgnoreCase)) ?? string.Empty;
    }

    private string FindModelFile(string modelClassName, string[] csharpFiles)
    {
        return csharpFiles.FirstOrDefault(f => 
            Path.GetFileNameWithoutExtension(f).Equals(modelClassName, StringComparison.OrdinalIgnoreCase)) ?? string.Empty;
    }

    private string ExtractClassName(string dataContextValue)
    {
        // Simple extraction - assumes format like "{Binding}" or "ViewModelName"
        var match = Regex.Match(dataContextValue, @"(\w+)(?:ViewModel|VM)?$");
        return match.Success ? match.Groups[1].Value : dataContextValue;
    }

    private void UpdateMetadata(XamlMetadata metadata)
    {
        metadata.Metadata.TotalElements = metadata.Elements.Count;
        metadata.Metadata.TotalBindings = metadata.Bindings.Count;
        metadata.Metadata.TotalResources = metadata.Resources.Count;
        metadata.Metadata.TotalEventHandlers = metadata.EventHandlers.Count;

        metadata.Metadata.ElementTypeDistribution = metadata.Elements
            .GroupBy(e => e.Type)
            .ToDictionary(g => g.Key, g => g.Count());

        metadata.Metadata.BindingModeDistribution = metadata.Bindings
            .GroupBy(b => b.Mode.ToString())
            .ToDictionary(g => g.Key, g => g.Count());

        metadata.Metadata.ResourceTypeDistribution = metadata.Resources
            .GroupBy(r => r.Type)
            .ToDictionary(g => g.Key, g => g.Count());
    }

    private void UpdateMvvmMetadata(MvvmRelationships relationships)
    {
        relationships.Metadata.TotalViewViewModelMappings = relationships.ViewViewModelMappings.Count;
        relationships.Metadata.TotalViewModelModelMappings = relationships.ViewModelModelMappings.Count;

        var allMappings = relationships.ViewViewModelMappings.Select(m => m.MappingType)
            .Concat(relationships.ViewModelModelMappings.Select(m => m.MappingType));

        relationships.Metadata.MappingTypeDistribution = allMappings
            .GroupBy(t => t.ToString())
            .ToDictionary(g => g.Key, g => g.Count());

        relationships.Metadata.ProcessedFiles = relationships.ViewViewModelMappings
            .Select(m => m.ViewFilePath)
            .Concat(relationships.ViewViewModelMappings.Select(m => m.ViewModelFilePath))
            .Concat(relationships.ViewModelModelMappings.Select(m => m.ModelFilePath))
            .Distinct()
            .ToList();
    }
}
