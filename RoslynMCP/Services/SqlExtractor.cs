using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.SqlServer.TransactSql.ScriptDom;
using RoslynMCP.Models;
using System.Text.RegularExpressions;

namespace RoslynMCP.Services;

public class SqlExtractor
{
    private readonly TSqlParser _sqlParser;
    private static readonly Regex SqlStringPattern = new Regex(
        @"(?i)(SELECT|INSERT|UPDATE|DELETE|CREATE|DROP|ALTER|EXEC|EXECUTE)\s+.*?(?=["";]|$)",
        RegexOptions.Compiled | RegexOptions.Multiline | RegexOptions.IgnoreCase);

    public SqlExtractor()
    {
        _sqlParser = new TSql160Parser(true);
    }

    public async Task<SqlMetadata> ExtractSqlFromFileAsync(string filePath)
    {
        var metadata = new SqlMetadata
        {
            Metadata = new SqlAnalysisMetadata
            {
                AnalyzedAt = DateTime.UtcNow,
                ProcessedFiles = new List<string> { filePath }
            }
        };

        try
        {
            var sourceCode = await File.ReadAllTextAsync(filePath);
            var syntaxTree = CSharpSyntaxTree.ParseText(sourceCode, path: filePath);
            var root = await syntaxTree.GetRootAsync();

            // Extract SQL from different contexts
            ExtractFromStringLiterals(root, filePath, metadata);
            ExtractFromEntityFramework(root, filePath, metadata);
            ExtractFromDapper(root, filePath, metadata);
            ExtractFromAdoNet(root, filePath, metadata);

            // Analyze extracted queries
            AnalyzeQueries(metadata);
            UpdateMetadata(metadata);

            return metadata;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to extract SQL from file {filePath}: {ex.Message}", ex);
        }
    }

    private void ExtractFromStringLiterals(SyntaxNode root, string filePath, SqlMetadata metadata)
    {
        var stringLiterals = root.DescendantNodes().OfType<LiteralExpressionSyntax>()
            .Where(l => l.IsKind(SyntaxKind.StringLiteralExpression));

        foreach (var literal in stringLiterals)
        {
            var content = literal.Token.ValueText;
            if (IsSqlQuery(content))
            {
                var query = CreateSqlQuery(content, literal, filePath, SqlFramework.RawSql);
                metadata.Queries.Add(query);
            }
        }
    }

    private void ExtractFromEntityFramework(SyntaxNode root, string filePath, SqlMetadata metadata)
    {
        // Look for Entity Framework patterns
        var invocations = root.DescendantNodes().OfType<InvocationExpressionSyntax>();

        foreach (var invocation in invocations)
        {
            var memberAccess = invocation.Expression as MemberAccessExpressionSyntax;
            if (memberAccess == null) continue;

            var methodName = memberAccess.Name.Identifier.ValueText;

            // Entity Framework raw SQL methods
            if (IsEntityFrameworkMethod(methodName))
            {
                var sqlArgument = invocation.ArgumentList.Arguments.FirstOrDefault();
                if (sqlArgument?.Expression is LiteralExpressionSyntax literal)
                {
                    var content = literal.Token.ValueText;
                    var query = CreateSqlQuery(content, invocation, filePath, SqlFramework.EntityFramework);
                    query.Properties["EFMethod"] = methodName;
                    metadata.Queries.Add(query);
                }
            }
        }
    }

    private void ExtractFromDapper(SyntaxNode root, string filePath, SqlMetadata metadata)
    {
        var invocations = root.DescendantNodes().OfType<InvocationExpressionSyntax>();

        foreach (var invocation in invocations)
        {
            var memberAccess = invocation.Expression as MemberAccessExpressionSyntax;
            if (memberAccess == null) continue;

            var methodName = memberAccess.Name.Identifier.ValueText;

            // Dapper methods
            if (IsDapperMethod(methodName))
            {
                var sqlArgument = invocation.ArgumentList.Arguments.FirstOrDefault();
                if (sqlArgument?.Expression is LiteralExpressionSyntax literal)
                {
                    var content = literal.Token.ValueText;
                    var query = CreateSqlQuery(content, invocation, filePath, SqlFramework.Dapper);
                    query.Properties["DapperMethod"] = methodName;
                    metadata.Queries.Add(query);
                }
            }
        }
    }

    private void ExtractFromAdoNet(SyntaxNode root, string filePath, SqlMetadata metadata)
    {
        var assignments = root.DescendantNodes().OfType<AssignmentExpressionSyntax>();

        foreach (var assignment in assignments)
        {
            if (assignment.Left is MemberAccessExpressionSyntax memberAccess &&
                memberAccess.Name.Identifier.ValueText == "CommandText" &&
                assignment.Right is LiteralExpressionSyntax literal)
            {
                var content = literal.Token.ValueText;
                if (IsSqlQuery(content))
                {
                    var query = CreateSqlQuery(content, assignment, filePath, SqlFramework.AdoNet);
                    metadata.Queries.Add(query);
                }
            }
        }
    }

    private SqlQuery CreateSqlQuery(string content, SyntaxNode node, string filePath, SqlFramework framework)
    {
        var location = GetSqlLocation(node, filePath);
        var query = new SqlQuery
        {
            Id = Guid.NewGuid().ToString(),
            Content = content.Trim(),
            Framework = framework,
            Location = location,
            Context = GetContext(node)
        };

        // Parse SQL to extract metadata
        ParseSqlContent(query);

        return query;
    }

    private void ParseSqlContent(SqlQuery query)
    {
        try
        {
            var parseResult = _sqlParser.Parse(new StringReader(query.Content), out var errors);
            
            if (parseResult != null && errors.Count == 0)
            {
                var visitor = new SqlVisitor();
                parseResult.Accept(visitor);
                
                query.Type = visitor.QueryType;
                query.Tables.AddRange(visitor.Tables);
                query.Columns.AddRange(visitor.Columns);
                query.Parameters.AddRange(visitor.Parameters);
            }
            else
            {
                // Fallback to regex parsing if SQL parser fails
                ParseSqlWithRegex(query);
            }
        }
        catch
        {
            // Fallback to regex parsing
            ParseSqlWithRegex(query);
        }
    }

    private void ParseSqlWithRegex(SqlQuery query)
    {
        var content = query.Content.ToUpperInvariant();
        
        // Determine query type
        if (content.StartsWith("SELECT")) query.Type = SqlQueryType.Select;
        else if (content.StartsWith("INSERT")) query.Type = SqlQueryType.Insert;
        else if (content.StartsWith("UPDATE")) query.Type = SqlQueryType.Update;
        else if (content.StartsWith("DELETE")) query.Type = SqlQueryType.Delete;
        else if (content.StartsWith("CREATE")) query.Type = SqlQueryType.Create;
        else if (content.StartsWith("DROP")) query.Type = SqlQueryType.Drop;
        else if (content.StartsWith("ALTER")) query.Type = SqlQueryType.Alter;
        else if (content.StartsWith("EXEC") || content.StartsWith("EXECUTE")) query.Type = SqlQueryType.StoredProcedure;
        else query.Type = SqlQueryType.Unknown;

        // Extract table names (simple regex approach)
        var tablePattern = new Regex(@"\b(?:FROM|JOIN|UPDATE|INTO)\s+([a-zA-Z_][a-zA-Z0-9_]*)", RegexOptions.IgnoreCase);
        var tableMatches = tablePattern.Matches(query.Content);
        foreach (Match match in tableMatches)
        {
            var tableName = match.Groups[1].Value;
            if (!query.Tables.Contains(tableName))
            {
                query.Tables.Add(tableName);
            }
        }

        // Extract parameters
        var paramPattern = new Regex(@"@([a-zA-Z_][a-zA-Z0-9_]*)", RegexOptions.IgnoreCase);
        var paramMatches = paramPattern.Matches(query.Content);
        foreach (Match match in paramMatches)
        {
            var paramName = match.Groups[1].Value;
            if (!query.Parameters.Contains(paramName))
            {
                query.Parameters.Add(paramName);
            }
        }
    }

    private SqlLocation GetSqlLocation(SyntaxNode node, string filePath)
    {
        var span = node.GetLocation().GetLineSpan();
        var method = node.Ancestors().OfType<MethodDeclarationSyntax>().FirstOrDefault();
        var @class = node.Ancestors().OfType<ClassDeclarationSyntax>().FirstOrDefault();

        return new SqlLocation
        {
            FilePath = filePath,
            Line = span.StartLinePosition.Line + 1,
            Column = span.StartLinePosition.Character + 1,
            EndLine = span.EndLinePosition.Line + 1,
            EndColumn = span.EndLinePosition.Character + 1,
            MethodName = method?.Identifier.ValueText ?? string.Empty,
            ClassName = @class?.Identifier.ValueText ?? string.Empty
        };
    }

    private string GetContext(SyntaxNode node)
    {
        var method = node.Ancestors().OfType<MethodDeclarationSyntax>().FirstOrDefault();
        var @class = node.Ancestors().OfType<ClassDeclarationSyntax>().FirstOrDefault();

        if (method != null && @class != null)
        {
            return $"{@class.Identifier.ValueText}.{method.Identifier.ValueText}";
        }
        else if (@class != null)
        {
            return @class.Identifier.ValueText;
        }

        return "Unknown";
    }

    private bool IsSqlQuery(string content)
    {
        if (string.IsNullOrWhiteSpace(content) || content.Length < 6)
            return false;

        return SqlStringPattern.IsMatch(content.Trim());
    }

    private bool IsEntityFrameworkMethod(string methodName)
    {
        var efMethods = new[]
        {
            "FromSqlRaw", "FromSqlInterpolated", "ExecuteSqlRaw", "ExecuteSqlInterpolated",
            "ExecuteSqlCommand", "SqlQuery", "Database.SqlQuery"
        };
        return efMethods.Contains(methodName);
    }

    private bool IsDapperMethod(string methodName)
    {
        var dapperMethods = new[]
        {
            "Query", "QueryAsync", "QueryFirst", "QueryFirstAsync", "QuerySingle", "QuerySingleAsync",
            "Execute", "ExecuteAsync", "ExecuteScalar", "ExecuteScalarAsync"
        };
        return dapperMethods.Contains(methodName);
    }

    private void AnalyzeQueries(SqlMetadata metadata)
    {
        // Group queries by table
        var tableGroups = metadata.Queries
            .SelectMany(q => q.Tables.Select(t => new { Table = t, Query = q }))
            .GroupBy(x => x.Table);

        foreach (var group in tableGroups)
        {
            if (!metadata.ReferencedTables.Contains(group.Key))
            {
                metadata.ReferencedTables.Add(group.Key);
            }
        }

        // Count operations
        var operationCounts = metadata.Queries
            .GroupBy(q => q.Type.ToString())
            .ToDictionary(g => g.Key, g => g.Count());

        metadata.OperationCounts = operationCounts;
    }

    private void UpdateMetadata(SqlMetadata metadata)
    {
        metadata.Metadata.TotalQueries = metadata.Queries.Count;
        metadata.Metadata.TotalTables = metadata.ReferencedTables.Count;
        
        metadata.Metadata.QueryTypeDistribution = metadata.Queries
            .GroupBy(q => q.Type.ToString())
            .ToDictionary(g => g.Key, g => g.Count());

        metadata.Metadata.FrameworkDistribution = metadata.Queries
            .GroupBy(q => q.Framework.ToString())
            .ToDictionary(g => g.Key, g => g.Count());
    }
}

// SQL AST Visitor for parsing SQL content
public class SqlVisitor : TSqlFragmentVisitor
{
    public SqlQueryType QueryType { get; private set; } = SqlQueryType.Unknown;
    public List<string> Tables { get; } = new();
    public List<string> Columns { get; } = new();
    public List<string> Parameters { get; } = new();

    public override void Visit(SelectStatement node)
    {
        QueryType = SqlQueryType.Select;
        base.Visit(node);
    }

    public override void Visit(InsertStatement node)
    {
        QueryType = SqlQueryType.Insert;
        base.Visit(node);
    }

    public override void Visit(UpdateStatement node)
    {
        QueryType = SqlQueryType.Update;
        base.Visit(node);
    }

    public override void Visit(DeleteStatement node)
    {
        QueryType = SqlQueryType.Delete;
        base.Visit(node);
    }

    public override void Visit(CreateTableStatement node)
    {
        QueryType = SqlQueryType.Create;
        base.Visit(node);
    }

    public override void Visit(DropTableStatement node)
    {
        QueryType = SqlQueryType.Drop;
        base.Visit(node);
    }

    public override void Visit(AlterTableStatement node)
    {
        QueryType = SqlQueryType.Alter;
        base.Visit(node);
    }

    public override void Visit(ExecuteStatement node)
    {
        QueryType = SqlQueryType.StoredProcedure;
        base.Visit(node);
    }

    public override void Visit(NamedTableReference node)
    {
        var tableName = node.SchemaObject.BaseIdentifier.Value;
        if (!Tables.Contains(tableName))
        {
            Tables.Add(tableName);
        }
        base.Visit(node);
    }

    public override void Visit(ColumnReferenceExpression node)
    {
        var columnName = string.Join(".", node.MultiPartIdentifier.Identifiers.Select(i => i.Value));
        if (!Columns.Contains(columnName))
        {
            Columns.Add(columnName);
        }
        base.Visit(node);
    }

    public override void Visit(VariableReference node)
    {
        var paramName = node.Name.TrimStart('@');
        if (!Parameters.Contains(paramName))
        {
            Parameters.Add(paramName);
        }
        base.Visit(node);
    }
}
