namespace PAV.Core.Data;

public sealed class DatabaseSettings
{
    public string SqlitePath { get; set; } = "";

    public DatabaseSettings Clone() => new() { SqlitePath = SqlitePath };
}
