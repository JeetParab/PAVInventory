using System.IO;
using System.Text.Json;
using PAV.Core.Data;

namespace PAV.Client.Services;

public class ClientConfig
{
    public string DatabasePath { get; set; } = "";
    public string Provider { get; set; } = "SQLite";
    public string SqlServerConnectionString { get; set; } = "";
    public bool SidebarCollapsed { get; set; }
    public bool DarkMode { get; set; }
    public bool FreezeIdentityColumns { get; set; }
    public List<string> ColumnOrder { get; set; } = [];

    public static string UiFilePath => Path.Combine(PavDatabase.LocalDataFolder, "ui.json");
    public static string DatabaseFilePath => Path.Combine(PavDatabase.LocalDataFolder, "database.json");
    public static string TeamDatabaseFilePath => Path.Combine(AppContext.BaseDirectory, "database.json");
    public static string LegacyFilePath => Path.Combine(AppContext.BaseDirectory, "appsettings.json");

    public DatabaseSettings DatabaseSettings => new()
    {
        Provider = Provider,
        SqlitePath = DatabasePath,
        SqlServerConnectionString = SqlServerConnectionString
    };

    /// <summary>
    /// Precedence (last wins for database):
    /// defaults → next-to-exe appsettings.json → AppData database.json →
    /// team database.json next to the exe. UI always comes from AppData ui.json.
    /// Put database.json beside PAV.Client.exe to switch every PC at once.
    /// </summary>
    public static ClientConfig Load()
    {
        var cfg = new ClientConfig();
        Overlay(cfg, LegacyFilePath);
        OverlayDatabase(cfg, DatabaseFilePath);
        OverlayDatabase(cfg, TeamDatabaseFilePath);
        OverlayUi(cfg, UiFilePath);
        return cfg;
    }

    public void Save()
    {
        SaveUi();
        SaveDatabase();
    }

    public void SaveUi()
    {
        Directory.CreateDirectory(PavDatabase.LocalDataFolder);
        var ui = new
        {
            SidebarCollapsed,
            DarkMode,
            FreezeIdentityColumns,
            ColumnOrder
        };
        File.WriteAllText(UiFilePath, JsonSerializer.Serialize(ui, Json));
    }

    public void SaveDatabase()
    {
        Directory.CreateDirectory(PavDatabase.LocalDataFolder);
        var db = new
        {
            Provider,
            SqlitePath = DatabasePath,
            SqlServerConnectionString
        };
        File.WriteAllText(DatabaseFilePath, JsonSerializer.Serialize(db, Json));
    }

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private static void Overlay(ClientConfig cfg, string path)
    {
        var raw = Read(path);
        if (raw is null) return;
        if (!string.IsNullOrWhiteSpace(raw.DatabasePath)) cfg.DatabasePath = raw.DatabasePath;
        if (!string.IsNullOrWhiteSpace(raw.Provider)) cfg.Provider = raw.Provider;
        if (!string.IsNullOrWhiteSpace(raw.SqlServerConnectionString)) cfg.SqlServerConnectionString = raw.SqlServerConnectionString;
        cfg.SidebarCollapsed = raw.SidebarCollapsed;
        cfg.DarkMode = raw.DarkMode;
        cfg.FreezeIdentityColumns = raw.FreezeIdentityColumns;
        if (raw.ColumnOrder is { Count: > 0 }) cfg.ColumnOrder = raw.ColumnOrder;
    }

    private static void OverlayDatabase(ClientConfig cfg, string path)
    {
        try
        {
            if (!File.Exists(path)) return;
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            var r = doc.RootElement;
            if (r.TryGetProperty("Provider", out var p) && p.ValueKind == JsonValueKind.String)
                cfg.Provider = p.GetString() ?? cfg.Provider;
            if (r.TryGetProperty("SqlitePath", out var s) && s.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(s.GetString()))
                cfg.DatabasePath = s.GetString()!;
            if (r.TryGetProperty("DatabasePath", out var d) && d.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(d.GetString()))
                cfg.DatabasePath = d.GetString()!;
            if (r.TryGetProperty("SqlServerConnectionString", out var c) && c.ValueKind == JsonValueKind.String)
                cfg.SqlServerConnectionString = c.GetString() ?? "";
        }
        catch { /* keep current */ }
    }

    private static void OverlayUi(ClientConfig cfg, string path)
    {
        var raw = Read(path);
        if (raw is null) return;
        cfg.SidebarCollapsed = raw.SidebarCollapsed;
        cfg.DarkMode = raw.DarkMode;
        cfg.FreezeIdentityColumns = raw.FreezeIdentityColumns;
        if (raw.ColumnOrder is { Count: > 0 }) cfg.ColumnOrder = raw.ColumnOrder;
    }

    private static ClientConfig? Read(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            return JsonSerializer.Deserialize<ClientConfig>(File.ReadAllText(path), Json);
        }
        catch
        {
            return null;
        }
    }
}
