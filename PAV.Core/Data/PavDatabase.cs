using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using PAV.Core.Services;

namespace PAV.Core.Data;

public sealed class PavDatabase
{
    private readonly SemaphoreSlim _openGate = new(1, 1);
    private bool _ready;
    private bool _backfillDone;

    public DatabaseSettings Settings { get; }
    public DatabaseProvider Provider => Settings.Kind;
    public bool IsSqlite => Settings.IsSqlite;
    public bool IsSqlServer => Settings.IsSqlServer;

    public string DatabasePath { get; }
    public string SqlConnectionString { get; }
    public string Folder => IsSqlite
        ? (Path.GetDirectoryName(DatabasePath) ?? ".")
        : LocalDataFolder;
    public string BackupDirectory => Path.Combine(Folder, "Backups");
    public bool IsReady => _ready;

    public static string LocalDataFolder =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PAV Inventory");

    public PavDatabase(string databasePath) : this(new DatabaseSettings { Provider = "SQLite", SqlitePath = databasePath })
    {
    }

    public PavDatabase(DatabaseSettings settings)
    {
        Settings = settings.Clone();
        DatabasePath = ResolvePath(Settings.SqlitePath);
        SqlConnectionString = DatabaseSettings.NormalizeSqlServer(Settings.SqlServerConnectionString);
    }

    public static string ResolvePath(string? configured)
    {
        if (string.IsNullOrWhiteSpace(configured))
            return Path.Combine(AppContext.BaseDirectory, "inventory.db");

        var path = configured.Trim().Trim('"');
        if (Directory.Exists(path) || path.EndsWith(Path.DirectorySeparatorChar) || path.EndsWith(Path.AltDirectorySeparatorChar))
            return Path.Combine(path, "inventory.db");
        if (!path.EndsWith(".db", StringComparison.OrdinalIgnoreCase))
            return Path.Combine(path, "inventory.db");
        return path;
    }

    public static IWriteLock CreateWriteLock(DatabaseProvider provider) =>
        provider == DatabaseProvider.SqlServer ? new SqlServerWriteLock() : new SqliteWriteLock();

    public AppDbContext Create()
    {
        var builder = new DbContextOptionsBuilder<AppDbContext>();
        if (IsSqlServer)
        {
            if (string.IsNullOrWhiteSpace(SqlConnectionString))
                throw new InvalidOperationException("SQL Server connection string is not set.");
            builder.UseSqlServer(SqlConnectionString, o => o.CommandTimeout(30));
        }
        else
        {
            var cs = new SqliteConnectionStringBuilder
            {
                DataSource = DatabasePath,
                Mode = SqliteOpenMode.ReadWriteCreate,
                Cache = SqliteCacheMode.Private,
                DefaultTimeout = 15
            }.ToString();
            builder.UseSqlite(cs, o => o.CommandTimeout(30));
        }
        return new AppDbContext(builder.Options);
    }

    public void Invalidate()
    {
        _ready = false;
        _backfillDone = false;
    }

    public async Task OpenAsync()
    {
        if (_ready) return;
        await _openGate.WaitAsync();
        try
        {
            if (_ready) return;
            using var _ = Perf.Measure("Database.Open");
            if (IsSqlServer)
                await OpenSqlServerAsync();
            else
                await OpenSqliteAsync();
            _ready = true;
        }
        finally
        {
            _openGate.Release();
        }
    }

    private async Task OpenSqliteAsync()
    {
        Directory.CreateDirectory(Folder);
        Directory.CreateDirectory(BackupDirectory);

        await using var db = Create();

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var fileExisted = File.Exists(DatabasePath);
        if (!fileExisted)
        {
            await db.Database.EnsureCreatedAsync();
        }
        else
        {
            await db.Database.OpenConnectionAsync();
            var conn = db.Database.GetDbConnection();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT 1 FROM sqlite_master WHERE type='table' AND name='Assets' LIMIT 1";
            var has = await cmd.ExecuteScalarAsync();
            if (has is null)
                await db.Database.EnsureCreatedAsync();
        }
        Perf.Log("Database.EnsureCreated", sw.ElapsedMilliseconds);

        sw.Restart();
        await db.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=DELETE;");
        await db.Database.ExecuteSqlRawAsync("PRAGMA busy_timeout=15000;");
        await db.Database.ExecuteSqlRawAsync("PRAGMA synchronous=FULL;");
        await db.Database.ExecuteSqlRawAsync("PRAGMA foreign_keys=ON;");
        Perf.Log("Database.Pragma", sw.ElapsedMilliseconds);

        sw.Restart();
        await EnsureColumnsAsync(db);
        Perf.Log("Database.EnsureColumns", sw.ElapsedMilliseconds);

        sw.Restart();
        await EnsureIpTablesAsync(db);
        Perf.Log("Database.IpTables", sw.ElapsedMilliseconds);

        sw.Restart();
        await EnsureStockTablesAsync(db);
        Perf.Log("Database.StockTables", sw.ElapsedMilliseconds);

        sw.Restart();
        await EnsureSerialNumberUniqueAsync(db);
        Perf.Log("Database.SerialIndex", sw.ElapsedMilliseconds);

        sw.Restart();
        await SeedData.EnsureSeededAsync(db);
        Perf.Log("Database.Seed", sw.ElapsedMilliseconds);

        sw.Restart();
        await BackfillAssignedUserIdsAsync(db);
        Perf.Log("Database.Backfill", sw.ElapsedMilliseconds);
    }

    private async Task OpenSqlServerAsync()
    {
        Directory.CreateDirectory(LocalDataFolder);
        Directory.CreateDirectory(BackupDirectory);

        await using var db = Create();
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            await db.Database.EnsureCreatedAsync();
        }
        catch (Exception ex) when (SqliteGuard.IsConnectFailure(ex))
        {
            throw new InvalidOperationException(
                "Unable to connect to the PAV database server." + Environment.NewLine + Environment.NewLine +
                "Please contact the PAV administrator.", ex);
        }
        Perf.Log("Database.EnsureCreated", sw.ElapsedMilliseconds);

        // EnsureCreated is first-install only. Later schema changes need a
        // controlled upgrade. Concurrent first-start is not guaranteed —
        // an administrator should open PAV once on the host before clients.
        sw.Restart();
        await EnsureCanSignInColumnAsync(db);
        Perf.Log("Database.CanSignIn", sw.ElapsedMilliseconds);

        sw.Restart();
        await SeedData.EnsureSeededAsync(db);
        Perf.Log("Database.Seed", sw.ElapsedMilliseconds);


        sw.Restart();
        await BackfillAssignedUserIdsAsync(db);
        Perf.Log("Database.Backfill", sw.ElapsedMilliseconds);
    }

    public bool CanOpen()
    {
        try
        {
            if (IsSqlite && !File.Exists(DatabasePath) && !Directory.Exists(Folder))
                Directory.CreateDirectory(Folder);
            using var db = Create();
            return db.Database.CanConnect();
        }
        catch
        {
            return false;
        }
    }

    private static async Task EnsureColumnsAsync(AppDbContext db)
    {
        await EnsureTableColumnsAsync(db, "Assets",
        [
            ("SrNo", "INTEGER"),
            ("IpAddress", "TEXT"),
            ("AssignedUserName", "TEXT"),
            ("Designation", "TEXT"),
            ("AlternateUser", "TEXT"),
            ("Domain", "TEXT"),
            ("MacAddress", "TEXT"),
            ("Processor", "TEXT"),
            ("Ram", "TEXT"),
            ("Storage", "TEXT"),
            ("OperatingSystem", "TEXT"),
            ("DcInstalled", "TEXT"),
            ("AvInstalled", "TEXT"),
            ("MsOfficeVersion", "TEXT"),
            ("MfaEnabled", "TEXT"),
            ("IvantiInstalled", "TEXT"),
            ("AdminRights", "TEXT"),
            ("UsbAccess", "TEXT"),
            ("ChromeUpdated", "TEXT"),
            ("StockAvailability", "TEXT"),
            ("StockWorking", "TEXT"),
            ("PmCompleted", "TEXT"),
            ("LastConnected", "TEXT"),
            ("CollectBy", "TEXT")
        ]);

        await EnsureTableColumnsAsync(db, "Users",
        [
            ("MustChangePassword", "INTEGER NOT NULL DEFAULT 0"),
            ("CanSignIn", "INTEGER NOT NULL DEFAULT 1")
        ]);
    }

    private static async Task EnsureTableColumnsAsync(AppDbContext db, string table, (string Name, string Sql)[] extra)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var conn = db.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open)
            await conn.OpenAsync();
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "PRAGMA table_info(" + table + ")";
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                names.Add(reader.GetString(1));
        }

        if (names.Count == 0)
            return;

        foreach (var (name, sql) in extra)
        {
            if (names.Contains(name)) continue;
            await db.Database.ExecuteSqlRawAsync("ALTER TABLE " + table + " ADD COLUMN " + name + " " + sql);
        }
    }

    private static async Task EnsureCanSignInColumnAsync(AppDbContext db)
    {
        if (db.Database.IsSqlite())
            return;
        if (!db.Database.IsSqlServer())
            return;
        await db.Database.ExecuteSqlRawAsync("""
            IF COL_LENGTH('dbo.Users', 'CanSignIn') IS NULL
            BEGIN
                ALTER TABLE dbo.Users ADD CanSignIn BIT NOT NULL CONSTRAINT DF_Users_CanSignIn DEFAULT 1;
            END
            """);
    }

    private static async Task EnsureSerialNumberUniqueAsync(AppDbContext db)
    {
        var conn = db.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open)
            await conn.OpenAsync();

        await using (var exists = conn.CreateCommand())
        {
            exists.CommandText =
                "SELECT 1 FROM sqlite_master WHERE type='index' AND name='IX_Assets_SerialNumber_Unique'";
            var already = await exists.ExecuteScalarAsync();
            if (already is not null)
                return;
        }

        var dup = 0;
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText =
                "SELECT COUNT(*) FROM (" +
                "SELECT SerialNumber FROM Assets " +
                "WHERE SerialNumber IS NOT NULL AND SerialNumber != '' " +
                "GROUP BY SerialNumber HAVING COUNT(*) > 1)";
            var result = await cmd.ExecuteScalarAsync();
            dup = Convert.ToInt32(result ?? 0);
        }

        if (dup > 0)
            return;

        await db.Database.ExecuteSqlRawAsync(
            "CREATE UNIQUE INDEX IF NOT EXISTS IX_Assets_SerialNumber_Unique ON Assets(SerialNumber) " +
            "WHERE SerialNumber IS NOT NULL AND SerialNumber != ''");
    }

    private async Task BackfillAssignedUserIdsAsync(AppDbContext db)
    {
        if (_backfillDone) return;

        var pending = await db.Assets.AsNoTracking()
            .Where(a => a.AssignedUserId == null
                        && a.AssignedUserName != null
                        && a.AssignedUserName != "")
            .Select(a => new { a.Id, a.AssignedUserName })
            .ToListAsync();
        if (pending.Count == 0)
        {
            _backfillDone = true;
            return;
        }

        var users = (await db.Users.AsNoTracking()
                .Select(u => new { u.Id, u.Name, u.Username })
                .ToListAsync())
            .Select(u => (u.Id, u.Name, u.Username))
            .ToList();
        if (users.Count == 0)
        {
            _backfillDone = true;
            return;
        }

        var updates = new List<(int Id, int UserId)>();
        foreach (var row in pending)
        {
            var id = UserNameResolver.ResolveUniqueId(users, row.AssignedUserName);
            if (id is { } uid)
                updates.Add((row.Id, uid));
        }

        if (updates.Count > 0)
        {
            foreach (var g in updates.GroupBy(x => x.UserId))
            {
                var ids = g.Select(x => x.Id).ToList();
                var uid = g.Key;
                foreach (var chunk in ids.Chunk(400))
                {
                    var part = chunk.ToList();
                    await db.Assets
                        .Where(a => part.Contains(a.Id) && a.AssignedUserId == null)
                        .ExecuteUpdateAsync(s => s.SetProperty(a => a.AssignedUserId, uid));
                }
            }
        }

        _backfillDone = true;
    }

    private static async Task EnsureIpTablesAsync(AppDbContext db)
    {
        await db.Database.ExecuteSqlRawAsync("""
            CREATE TABLE IF NOT EXISTS IpRanges (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                FloorNumber INTEGER NOT NULL,
                Name TEXT NOT NULL,
                ThirdOctet INTEGER NOT NULL,
                Cidr TEXT NOT NULL,
                GatewayIp TEXT,
                Notes TEXT
            );
            """);
        await db.Database.ExecuteSqlRawAsync(
            "CREATE UNIQUE INDEX IF NOT EXISTS IX_IpRanges_FloorNumber ON IpRanges(FloorNumber);");
        await db.Database.ExecuteSqlRawAsync(
            "CREATE UNIQUE INDEX IF NOT EXISTS IX_IpRanges_Cidr ON IpRanges(Cidr);");

        await db.Database.ExecuteSqlRawAsync("""
            CREATE TABLE IF NOT EXISTS IpRecords (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                RangeId INTEGER NOT NULL,
                Address TEXT NOT NULL,
                HostOctet INTEGER NOT NULL,
                Status INTEGER NOT NULL DEFAULT 0,
                AssignedDevice TEXT,
                AssignedUser TEXT,
                Department TEXT,
                DateAssigned TEXT,
                MacAddress TEXT,
                DeviceType TEXT,
                Notes TEXT,
                LastUpdated TEXT,
                AllocatedBy TEXT,
                FOREIGN KEY(RangeId) REFERENCES IpRanges(Id) ON DELETE CASCADE
            );
            """);
        await db.Database.ExecuteSqlRawAsync(
            "CREATE UNIQUE INDEX IF NOT EXISTS IX_IpRecords_Address ON IpRecords(Address);");
        await db.Database.ExecuteSqlRawAsync(
            "CREATE INDEX IF NOT EXISTS IX_IpRecords_RangeId ON IpRecords(RangeId);");
        await db.Database.ExecuteSqlRawAsync(
            "CREATE INDEX IF NOT EXISTS IX_IpRecords_Status ON IpRecords(Status);");
    }

    private static async Task EnsureStockTablesAsync(AppDbContext db)
    {
        await db.Database.ExecuteSqlRawAsync("""
            CREATE TABLE IF NOT EXISTS StockItems (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Name TEXT NOT NULL,
                NormalizedKey TEXT NOT NULL,
                Category TEXT,
                Manufacturer TEXT,
                Model TEXT,
                Unit TEXT NOT NULL DEFAULT 'pcs',
                OnHand INTEGER NOT NULL DEFAULT 0,
                MinimumQuantity INTEGER NOT NULL DEFAULT 0,
                IsActive INTEGER NOT NULL DEFAULT 1,
                NeedsReview INTEGER NOT NULL DEFAULT 0,
                Notes TEXT,
                CreatedAt TEXT NOT NULL,
                UpdatedAt TEXT NOT NULL
            );
            """);
        await db.Database.ExecuteSqlRawAsync(
            "CREATE UNIQUE INDEX IF NOT EXISTS IX_StockItems_NormalizedKey ON StockItems(NormalizedKey);");
        await db.Database.ExecuteSqlRawAsync(
            "CREATE INDEX IF NOT EXISTS IX_StockItems_IsActive ON StockItems(IsActive);");
        await db.Database.ExecuteSqlRawAsync(
            "CREATE INDEX IF NOT EXISTS IX_StockItems_Category ON StockItems(Category);");

        await db.Database.ExecuteSqlRawAsync("""
            CREATE TABLE IF NOT EXISTS StockMovements (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                StockItemId INTEGER NOT NULL,
                MovementType INTEGER NOT NULL,
                Quantity INTEGER NOT NULL,
                UserId INTEGER,
                AssignedUserName TEXT,
                Reference TEXT,
                Notes TEXT,
                SerialNumber TEXT,
                CreatedBy TEXT NOT NULL,
                CreatedAt TEXT NOT NULL,
                FOREIGN KEY(StockItemId) REFERENCES StockItems(Id) ON DELETE RESTRICT,
                FOREIGN KEY(UserId) REFERENCES Users(Id) ON DELETE SET NULL
            );
            """);
        await db.Database.ExecuteSqlRawAsync(
            "CREATE INDEX IF NOT EXISTS IX_StockMovements_StockItemId ON StockMovements(StockItemId);");
        await db.Database.ExecuteSqlRawAsync(
            "CREATE INDEX IF NOT EXISTS IX_StockMovements_UserId ON StockMovements(UserId);");
        await db.Database.ExecuteSqlRawAsync(
            "CREATE INDEX IF NOT EXISTS IX_StockMovements_CreatedAt ON StockMovements(CreatedAt);");
        await db.Database.ExecuteSqlRawAsync(
            "CREATE INDEX IF NOT EXISTS IX_StockMovements_MovementType ON StockMovements(MovementType);");
    }
}
