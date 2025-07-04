using System.Text.Json.Serialization;

namespace RoslynMCP.Models;

public class SqlMetadata
{
    public List<SqlQuery> Queries { get; set; } = new();
    public List<string> ReferencedTables { get; set; } = new();
    public List<string> ReferencedColumns { get; set; } = new();
    public Dictionary<string, int> OperationCounts { get; set; } = new();
    public List<SqlConnection> Connections { get; set; } = new();
    public SqlAnalysisMetadata Metadata { get; set; } = new();
}

public class SqlQuery
{
    public string Id { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public SqlQueryType Type { get; set; }
    public List<string> Tables { get; set; } = new();
    public List<string> Columns { get; set; } = new();
    public List<string> Parameters { get; set; } = new();
    public SqlLocation Location { get; set; } = new();
    public string Context { get; set; } = string.Empty; // Method/class where found
    public SqlFramework Framework { get; set; }
    public Dictionary<string, object> Properties { get; set; } = new();
}

public class SqlConnection
{
    public string Id { get; set; } = string.Empty;
    public string ConnectionString { get; set; } = string.Empty;
    public string Provider { get; set; } = string.Empty;
    public SqlLocation Location { get; set; } = new();
    public List<string> UsedByQueries { get; set; } = new();
}

public class SqlLocation
{
    public string FilePath { get; set; } = string.Empty;
    public int Line { get; set; }
    public int Column { get; set; }
    public int EndLine { get; set; }
    public int EndColumn { get; set; }
    public string MethodName { get; set; } = string.Empty;
    public string ClassName { get; set; } = string.Empty;
}

public class SqlAnalysisMetadata
{
    public DateTime AnalyzedAt { get; set; }
    public int TotalQueries { get; set; }
    public int TotalTables { get; set; }
    public int TotalConnections { get; set; }
    public Dictionary<string, int> QueryTypeDistribution { get; set; } = new();
    public Dictionary<string, int> FrameworkDistribution { get; set; } = new();
    public List<string> ProcessedFiles { get; set; } = new();
}

public enum SqlQueryType
{
    Select,
    Insert,
    Update,
    Delete,
    Create,
    Drop,
    Alter,
    StoredProcedure,
    Function,
    Unknown
}

public enum SqlFramework
{
    RawSql,
    EntityFramework,
    Dapper,
    AdoNet,
    LinqToSql,
    NHibernate,
    Unknown
}
