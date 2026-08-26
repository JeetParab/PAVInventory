namespace PAV.Core.Data;

public enum DatabaseProvider
{
    Sqlite = 0,
    SqlServer = 1
}

public sealed class DatabaseSettings
{
    public string Provider { get; set; } = "SQLite";
    public string SqlitePath { get; set; } = "";
    public string SqlServerConnectionString { get; set; } = "";

    public DatabaseProvider Kind => ParseProvider(Provider);

    public bool IsSqlite => Kind == DatabaseProvider.Sqlite;
    public bool IsSqlServer => Kind == DatabaseProvider.SqlServer;

    public static DatabaseProvider ParseProvider(string? raw)
    {
        var v = (raw ?? "").Trim();
        if (v.Equals("SqlServer", StringComparison.OrdinalIgnoreCase) ||
            v.Equals("SQL Server", StringComparison.OrdinalIgnoreCase) ||
            v.Equals("MSSQL", StringComparison.OrdinalIgnoreCase))
            return DatabaseProvider.SqlServer;
        return DatabaseProvider.Sqlite;
    }

    public static string Display(DatabaseProvider kind) =>
        kind == DatabaseProvider.SqlServer ? "SQL Server" : "SQLite";

    /// <summary>
    /// Accepts a full connection string or just a server name / instance
    /// (e.g. DESKTOP-PAV\SQLEXPRESS).
    /// </summary>
    public static string NormalizeSqlServer(string? raw)
    {
        var v = (raw ?? "").Trim();
        if (string.IsNullOrWhiteSpace(v))
            return "";
        if (v.Contains('=', StringComparison.Ordinal))
            return v;
        return $"Server={v};Database=PAVInventory;Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=True";
    }

    public DatabaseSettings Clone() => new()
    {
        Provider = Provider,
        SqlitePath = SqlitePath,
        SqlServerConnectionString = SqlServerConnectionString
    };
}
