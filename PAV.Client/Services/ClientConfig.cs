using System.IO;
using System.Text.Json;

namespace PAV.Client.Services;

public class ClientConfig
{
    public string DatabasePath { get; set; } = "";
    public bool SidebarCollapsed { get; set; }
    public bool DarkMode { get; set; }
    public bool FreezeIdentityColumns { get; set; }
    public List<string> ColumnOrder { get; set; } = [];

    public static string FilePath => Path.Combine(AppContext.BaseDirectory, "appsettings.json");

    public static ClientConfig Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var json = File.ReadAllText(FilePath);
                var cfg = JsonSerializer.Deserialize<ClientConfig>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });
                if (cfg is not null)
                    return cfg;
            }
        }
        catch
        {
            // fall through to default
        }
        return new ClientConfig();
    }

    public void Save()
    {
        var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(FilePath, json);
    }
}
