using Microsoft.EntityFrameworkCore;
using PAV.Core.Data;
using PAV.Core.Services;
using PAV.Shared.Dtos;
using PAV.Shared.Enums;
using PAV.Shared.Models;

namespace PAV.Client.Services;

public class ApiException : Exception
{
    public int Status { get; }
    public string Code { get; }
    public List<string>? Errors { get; }

    public ApiException(int status, string code, string message, List<string>? errors = null)
        : base(message)
    {
        Status = status;
        Code = code;
        Errors = errors;
    }

    public bool IsConflict => Status == 409 || Code == "conflict";
    public bool IsDisconnected => Status == 0 || Code == "disconnected";
}

public class ApiClient
{
    private readonly ClientConfig _config;
    private IWriteLock _lock;
    private PavDatabase _pav;
    private User? _user;

    public bool IsConnected { get; private set; }
    public MeDto? SignedInUser { get; private set; }
    public event Action<bool>? ConnectionChanged;

    public ApiClient(ClientConfig config)
    {
        _config = config;
        _pav = new PavDatabase(config.DatabaseSettings);
        _lock = PavDatabase.CreateWriteLock(_pav.Provider);
    }

    public string DatabasePath => _pav.DatabasePath;
    public string DatabasePathSetting => _config.DatabasePath;
    public bool IsSqlite => _pav.IsSqlite;
    public bool IsSqlServer => _pav.IsSqlServer;
    public string ProviderDisplay => DatabaseSettings.Display(_pav.Provider);

    public async Task SetDatabasePath(string path)
    {
        _config.DatabasePath = path?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(_config.Provider))
            _config.Provider = "SQLite";
        _config.SaveDatabase();
        await ApplyDatabaseAsync(_config.DatabaseSettings);
    }

    public async Task ApplyDatabaseAsync(DatabaseSettings settings)
    {
        _config.Provider = DatabaseSettings.Display(settings.Kind) == "SQL Server" ? "SqlServer" : "SQLite";
        _config.DatabasePath = settings.SqlitePath ?? "";
        _config.SqlServerConnectionString = settings.SqlServerConnectionString ?? "";
        _config.SaveDatabase();

        var next = new PavDatabase(_config.DatabaseSettings);
        _pav = next;
        _lock = PavDatabase.CreateWriteLock(_pav.Provider);
        await OpenAsync();
    }

    public async Task OpenAsync()
    {
        using var _ = Perf.Measure("ApiClient.Open");
        try
        {
            await _pav.OpenAsync();
            SetConnected(true);
        }
        catch (Exception ex)
        {
            SetConnected(false);
            throw new ApiException(0, "disconnected", FriendlyDisconnect(ex));
        }
    }

    public async Task<bool> PingAsync()
    {
        try
        {
            if (_pav.IsSqlServer)
            {
                await using var db = _pav.Create();
                var ok = await db.Database.CanConnectAsync();
                SetConnected(ok);
                return ok;
            }
            if (_pav.IsReady && File.Exists(_pav.DatabasePath))
            {
                SetConnected(true);
                return true;
            }
            if (!File.Exists(_pav.DatabasePath))
                await OpenAsync();
            await using var sqlite = _pav.Create();
            var connected = await sqlite.Database.CanConnectAsync();
            SetConnected(connected);
            return connected;
        }
        catch
        {
            SetConnected(false);
            return false;
        }
    }

    public async Task<bool> NeedsSetupAsync()
    {
        if (!_pav.IsReady)
            await OpenAsync();
        await using var db = _pav.Create();
        return !await db.Users.AsNoTracking().AnyAsync();
    }

    public async Task<LoginResponse> SetupAdministratorAsync(string username, string name, string password)
    {
        var pwdError = AuthRules.ValidateNewPassword(password);
        if (pwdError is not null)
            throw new ApiException(400, "validation", pwdError);
        if (string.IsNullOrWhiteSpace(username))
            throw new ApiException(400, "validation", "Username is required.");
        if (string.IsNullOrWhiteSpace(name))
            throw new ApiException(400, "validation", "Display name is required.");

        await OpenAsync();
        return await _lock.WriteAsync(async () =>
        {
            await using var db = _pav.Create();
            if (await db.Users.AnyAsync())
                throw new ApiException(400, "validation", "An administrator already exists. Sign in instead.");

            var user = new User
            {
                Username = username.Trim(),
                Name = name.Trim(),
                PasswordHash = PasswordHasher.Hash(password.Trim()),
                Role = UserRole.Administrator,
                IsActive = true,
                CanSignIn = true,
                MustChangePassword = false
            };
            db.Users.Add(user);
            await db.SaveChangesAsync();
            _user = user;
            SignedInUser = Mapping.ToMe(user);
            return new LoginResponse { User = SignedInUser };
        });
    }

    public async Task<LoginResponse> LoginAsync(string username, string password)
    {
        using var _ = Perf.Measure("Login");
        if (!_pav.IsReady)
            await OpenAsync();
        await using var db = _pav.Create();
        var user = await db.Users
            .FirstOrDefaultAsync(u => u.Username.ToLower() == username.Trim().ToLower());
        if (user is null || !user.IsActive || !user.CanSignIn || !PasswordHasher.Verify(password, user.PasswordHash))
            throw new ApiException(401, "unauthenticated", "Wrong username or password.");

        if (AuthRules.IsKnownDefaultPassword(password) && !user.MustChangePassword)
        {
            user.MustChangePassword = true;
            await db.SaveChangesAsync();
        }

        _user = user;
        SignedInUser = Mapping.ToMe(user);
        return new LoginResponse { User = SignedInUser };
    }

    public async Task ChangePasswordAsync(string currentPassword, string newPassword)
    {
        var actor = Actor();
        var pwdError = AuthRules.ValidateNewPassword(newPassword);
        if (pwdError is not null)
            throw new ApiException(400, "validation", pwdError);
        if (string.Equals(currentPassword, newPassword, StringComparison.Ordinal))
            throw new ApiException(400, "validation", "New password must be different from the current password.");

        await _lock.WriteAsync(async () =>
        {
            await using var db = _pav.Create();
            var user = await db.Users.FirstOrDefaultAsync(u => u.Id == actor.User.Id)
                       ?? throw new ApiException(401, "unauthenticated", "Please sign in.");
            if (!PasswordHasher.Verify(currentPassword, user.PasswordHash))
                throw new ApiException(400, "validation", "Current password is incorrect.");

            user.PasswordHash = PasswordHasher.Hash(newPassword.Trim());
            user.MustChangePassword = false;
            await db.SaveChangesAsync();
            _user = user;
            SignedInUser = Mapping.ToMe(user);
        });
    }

    public Task LogoutAsync()
    {
        _user = null;
        SignedInUser = null;
        return Task.CompletedTask;
    }

    public Task<MeDto> MeAsync()
    {
        var actor = Actor();
        return Task.FromResult(Mapping.ToMe(actor.User));
    }

    public Task<DashboardDto> DashboardAsync() =>
        Read(Permissions.View, (db, _) => new AssetService(db, _lock).DashboardAsync());

    public Task<List<AssetListDto>> AssetsAsync(string? query = null) =>
        Read(Permissions.View, (db, _) => new AssetService(db, _lock).ListAsync(ParseQuery(query)));

    public Task<InventoryLoadDto> InventoryLoadAsync(string? query = null) =>
        Read(Permissions.View, (db, _) => new AssetService(db, _lock).InventoryAsync(ParseQuery(query)));

    public Task<AssetDetailDto> AssetAsync(int id, bool history = true) =>
        Read(Permissions.View, (db, _) => new AssetService(db, _lock).GetAsync(id, history));

    public Task<List<HistoryDto>> HistoryAsync(int id) =>
        Read(Permissions.View, (db, _) => new AssetService(db, _lock).GetHistoryAsync(id));

    public Task<AssetDetailDto> CreateAssetAsync(SaveAssetRequest req) =>
        Read(Permissions.Add, (db, actor) => new AssetService(db, _lock).CreateAsync(req, actor));

    public Task<AssetDetailDto> UpdateAssetAsync(int id, SaveAssetRequest req) =>
        Read(Permissions.Edit, (db, actor) => new AssetService(db, _lock).UpdateAsync(id, req, actor));

    public Task DeleteAssetAsync(int id) =>
        Read(Permissions.Delete, async (db, actor) =>
        {
            await new AssetService(db, _lock).DeleteAsync(id, actor);
            return 0;
        });

    public Task<int> BulkPatchAsync(BulkEditRequest req) =>
        Read(Permissions.Edit, (db, actor) => new AssetService(db, _lock).BulkPatchAsync(req, actor));

    public Task<int> BulkAddAsync(BulkAddRequest req) =>
        Read(Permissions.Add, (db, actor) => new AssetService(db, _lock).BulkAddAsync(req, actor));

    public Task<int> RenumberByIpAsync(List<int>? ids) =>
        Read(Permissions.Edit, (db, actor) => new AssetService(db, _lock).RenumberByIpAsync(ids, actor));

    public Task<List<DuplicateGroupDto>> DuplicateSerialsAsync() =>
        Read(Permissions.View, (db, _) => new AssetService(db, _lock).DuplicateSerialsAsync());

    public Task<List<UserDto>> UsersAsync() =>
        Read(Permissions.View, (db, _) => new LookupService(db, _lock).UsersAsync());

    public Task<List<UserDto>> PeopleAsync() =>
        Read(Permissions.View, (db, _) => new LookupService(db, _lock).PeopleAsync());

    public Task<List<UserDto>> SignInUsersAsync() =>
        Read(Permissions.View, (db, _) => new LookupService(db, _lock).SignInUsersAsync());

    public Task<List<UnlinkedAssignmentDto>> UnlinkedAssignmentsAsync() =>
        Read(Permissions.View, (db, _) => new LookupService(db, _lock).UnlinkedAssignmentsAsync());

    public Task<UserDto> CreateUserAsync(SaveUserRequest req) =>
        Read(Permissions.ManageUsers, (db, _) => new LookupService(db, _lock).CreateUserAsync(req));

    public Task<UserDto> UpdateUserAsync(int id, SaveUserRequest req) =>
        Read(Permissions.ManageUsers, (db, _) => new LookupService(db, _lock).UpdateUserAsync(id, req));

    public Task<UserDto> CreatePersonAsync(SavePersonRequest req) =>
        Read(Permissions.Add, (db, _) => new LookupService(db, _lock).CreatePersonAsync(req));

    public Task<UserDto> UpdatePersonAsync(int id, SavePersonRequest req) =>
        Read(Permissions.Edit, (db, _) => new LookupService(db, _lock).UpdatePersonAsync(id, req));

    public Task DeletePersonAsync(int id) =>
        Read(Permissions.Delete, async (db, _) =>
        {
            await new LookupService(db, _lock).DeletePersonAsync(id);
            return 0;
        });

    public Task<List<LocationDto>> LocationsAsync() =>
        Read(Permissions.View, (db, _) => new LookupService(db, _lock).LocationsAsync());

    public Task<LocationDto> CreateLocationAsync(SaveLocationRequest req) =>
        Read(Permissions.ManageLocations, (db, _) => new LookupService(db, _lock).CreateLocationAsync(req));

    public Task<LocationDto> UpdateLocationAsync(int id, SaveLocationRequest req) =>
        Read(Permissions.ManageLocations, (db, _) => new LookupService(db, _lock).UpdateLocationAsync(id, req));

    public Task DeleteLocationAsync(int id) =>
        Read(Permissions.ManageLocations, async (db, _) =>
        {
            await new LookupService(db, _lock).DeleteLocationAsync(id);
            return 0;
        });

    public Task<List<CategoryDto>> CategoriesAsync() =>
        Read(Permissions.View, (db, _) => new LookupService(db, _lock).CategoriesAsync());

    public Task<CategoryDto> CreateCategoryAsync(SaveCategoryRequest req) =>
        Read(Permissions.ManageCategories, (db, _) => new LookupService(db, _lock).CreateCategoryAsync(req));

    public Task<CategoryDto> UpdateCategoryAsync(int id, SaveCategoryRequest req) =>
        Read(Permissions.ManageCategories, (db, _) => new LookupService(db, _lock).UpdateCategoryAsync(id, req));

    public Task DeleteCategoryAsync(int id) =>
        Read(Permissions.ManageCategories, async (db, _) =>
        {
            await new LookupService(db, _lock).DeleteCategoryAsync(id);
            return 0;
        });

    public Task<List<BackupInfo>> BackupsAsync() =>
        Read(Permissions.Backup, (_, _) => Task.FromResult(new BackupService(_pav, _lock).List()));

    public Task<BackupInfo> BackupNowAsync() =>
        Read(Permissions.Backup, (_, _) => new BackupService(_pav, _lock).BackupNowAsync());

    public Task RestoreAsync(string fileName) =>
        Read(Permissions.Restore, async (_, _) =>
        {
            await new BackupService(_pav, _lock).RestoreAsync(fileName);
            _pav.Invalidate();
            await OpenAsync();
            return 0;
        });

    public async Task<byte[]> ExportAsync(string? query = null)
    {
        Require(Permissions.Export);
        var assets = await Read(Permissions.View, (db, _) => new AssetService(db, _lock).ListAsync(ParseQuery(query)));
        await using var db = _pav.Create();
        return new ImportExportService(db, _lock).Export(assets);
    }

    public async Task<byte[]> ImportTemplateAsync()
    {
        Require(Permissions.Import);
        await using var db = _pav.Create();
        return new ImportExportService(db, _lock).Template();
    }

    public Task<ImportPreviewResponse> PreviewImportAsync(string filePath, ImportColumnMap map) =>
        Read(Permissions.Import, async (db, _) =>
        {
            await using var fs = File.OpenRead(filePath);
            return await new ImportExportService(db, _lock).PreviewAsync(fs, map);
        });

    public Task<ImportResult> ImportAsync(string filePath, ImportColumnMap map) =>
        Read(Permissions.Import, async (db, actor) =>
        {
            await using var fs = File.OpenRead(filePath);
            return await new ImportExportService(db, _lock).ImportAsync(fs, map, actor);
        });

    public Task<IpOverviewDto> IpOverviewAsync() =>
        Read(Permissions.View, (db, _) => new IpAddressService(db, _lock).OverviewAsync());

    public Task<List<IpAddressDto>> IpListAsync(int? rangeId = null, string? status = null, string? search = null) =>
        Read(Permissions.View, (db, _) => new IpAddressService(db, _lock).ListAsync(rangeId, status, search));

    public Task<IpCheckResultDto> IpCheckAsync(string address) =>
        Read(Permissions.View, (db, _) => new IpAddressService(db, _lock).CheckAsync(address));

    public Task<IpAddressDto> IpAssignAsync(AssignIpRequest req) =>
        Read(Permissions.Assign, (db, actor) => new IpAddressService(db, _lock).AssignAsync(req, actor));

    public Task<IpAddressDto> IpReserveAsync(int id, string? notes = null) =>
        Read(Permissions.Assign, (db, actor) => new IpAddressService(db, _lock).ReserveAsync(id, notes, actor));

    public Task<IpAddressDto> IpReleaseAsync(int id) =>
        Read(Permissions.Assign, (db, actor) => new IpAddressService(db, _lock).ReleaseAsync(id, actor));

    public Task<IpImportResult> ImportIpWorkbookAsync(string filePath) =>
        Read(Permissions.Import, async (db, actor) =>
        {
            await using var fs = File.OpenRead(filePath);
            return await new IpAddressService(db, _lock).ImportWorkbookAsync(fs, actor);
        });

    public Task<StockOverviewDto> StockOverviewAsync(string? search = null, string? status = null, bool includeInactive = false, string? categoryScope = null) =>
        Read(Permissions.View, (db, _) => new StockService(db, _lock).OverviewAsync(search, status, includeInactive, categoryScope));

    public Task<StockItemDto> StockGetAsync(int id) =>
        Read(Permissions.View, (db, _) => new StockService(db, _lock).GetAsync(id));

    public Task<List<StockMovementDto>> StockMovementsAsync(int? itemId = null, int? userId = null, string? search = null, string? categoryScope = null) =>
        Read(Permissions.View, (db, _) => new StockService(db, _lock).MovementsAsync(itemId, userId, search, categoryScope));

    public Task<StockItemDto> SaveStockItemAsync(int? id, SaveStockItemRequest req) =>
        Read(Permissions.View, (db, actor) => new StockService(db, _lock).SaveItemAsync(id, req, actor));

    public Task<StockItemDto> StockReceiveAsync(StockMoveRequest req) =>
        Read(Permissions.Assign, (db, actor) => new StockService(db, _lock).ReceiveAsync(req, actor));

    public Task<StockItemDto> StockIssueAsync(StockMoveRequest req) =>
        Read(Permissions.Assign, (db, actor) => new StockService(db, _lock).IssueAsync(req, actor));

    public Task<StockItemDto> StockReturnAsync(StockMoveRequest req) =>
        Read(Permissions.Assign, (db, actor) => new StockService(db, _lock).ReturnAsync(req, actor));

    public Task<StockItemDto> StockAdjustAsync(StockMoveRequest req) =>
        Read(Permissions.View, (db, actor) => new StockService(db, _lock).AdjustAsync(req, actor));

    public Task<StockImportPreviewDto> PreviewStockImportAsync(string filePath) =>
        Read(Permissions.View, async (db, _) =>
        {
            await using var fs = File.OpenRead(filePath);
            return await new StockService(db, _lock).PreviewImportAsync(fs, Path.GetFileName(filePath));
        });

    public Task<StockImportResultDto> ImportStockAsync(string filePath) =>
        Read(Permissions.View, async (db, actor) =>
        {
            await using var fs = File.OpenRead(filePath);
            return await new StockService(db, _lock).ImportAsync(fs, Path.GetFileName(filePath), actor);
        });

    public Task<byte[]> ExportStockAsync() =>
        Read(Permissions.Export, (db, _) => new StockService(db, _lock).ExportAsync());

    public Task<StockIntegrityDto> StockIntegrityAsync() =>
        Read(Permissions.View, (db, actor) => new StockService(db, _lock).IntegrityCheckAsync(actor));

    public async Task<PAV.Core.Services.MigrationReport> MigrateSqliteToSqlServerAsync(string sqlitePath, bool replaceDestination)
    {
        Require(Permissions.Backup);
        var src = new PavDatabase(new DatabaseSettings { Provider = "SQLite", SqlitePath = sqlitePath });
        if (!_pav.IsSqlServer)
            throw new ApiException(400, "validation", "Switch the provider to SQL Server first, then copy the SQLite file into it.");
        return await new SqliteToSqlServerMigrator().CopyAsync(src, _pav, replaceDestination);
    }

    private async Task<T> Read<T>(string permission, Func<AppDbContext, CurrentUser, Task<T>> work)
    {
        var actor = Actor();
        if (!string.IsNullOrEmpty(permission) && !actor.Can(permission))
            throw new ApiException(403, "forbidden", "You do not have permission to do that.");
        try
        {
            await using var db = _pav.Create();
            var result = await work(db, actor);
            SetConnected(true);
            return result;
        }
        catch (ApiException)
        {
            throw;
        }
        catch (AppException ex)
        {
            throw new ApiException(ex.StatusCode, ex.Code, ex.Message, ex.Errors);
        }
        catch (Exception ex) when (IsDisconnect(ex))
        {
            SetConnected(false);
            throw new ApiException(0, "disconnected", FriendlyDisconnect(ex));
        }
    }

    private static bool IsDisconnect(Exception ex)
    {
        for (var e = ex; e is not null; e = e.InnerException)
        {
            if (e is IOException) return true;
            if (e.GetType().Name == "SqlException")
            {
                var num = e.GetType().GetProperty("Number")?.GetValue(e);
                if (num is int n && n is 2601 or 2627 or 547 or 515)
                    return false;
                return true;
            }
        }
        return false;
    }

    private string FriendlyDisconnect(Exception ex)
    {
        if (_pav.IsSqlServer)
        {
            return "Unable to connect to the PAV database server." + Environment.NewLine + Environment.NewLine +
                   "Please contact the PAV administrator.";
        }
        return "Cannot open the shared database." + Environment.NewLine + Environment.NewLine +
               DatabasePath + Environment.NewLine + Environment.NewLine + ex.Message;
    }

    private CurrentUser Actor()
    {
        if (_user is null)
            throw new ApiException(401, "unauthenticated", "Please sign in.");
        return new CurrentUser { User = _user };
    }

    private void Require(string permission)
    {
        if (!Actor().Can(permission))
            throw new ApiException(403, "forbidden", "You do not have permission to do that.");
    }

    private void SetConnected(bool value)
    {
        if (IsConnected == value) return;
        IsConnected = value;
        ConnectionChanged?.Invoke(value);
    }

    public static string Query(
        string? search = null,
        string? status = null,
        int? categoryId = null,
        int? locationId = null,
        string? manufacturer = null,
        bool? assigned = null,
        int? assignedUserId = null)
    {
        var p = new List<string>();
        if (!string.IsNullOrWhiteSpace(search)) p.Add("search=" + Uri.EscapeDataString(search));
        if (!string.IsNullOrWhiteSpace(status) && status != "All") p.Add("status=" + Uri.EscapeDataString(status));
        if (categoryId is > 0) p.Add("categoryId=" + categoryId);
        if (locationId is > 0) p.Add("locationId=" + locationId);
        if (!string.IsNullOrWhiteSpace(manufacturer) && manufacturer != "All")
            p.Add("manufacturer=" + Uri.EscapeDataString(manufacturer));
        if (assigned is not null) p.Add("assigned=" + (assigned.Value ? "true" : "false"));
        if (assignedUserId is > 0) p.Add("assignedUserId=" + assignedUserId);
        return string.Join("&", p);
    }

    private static AssetQuery ParseQuery(string? query)
    {
        var q = new AssetQuery();
        if (string.IsNullOrWhiteSpace(query)) return q;
        foreach (var part in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var kv = part.Split('=', 2);
            if (kv.Length != 2) continue;
            var key = kv[0];
            var val = Uri.UnescapeDataString(kv[1]);
            switch (key)
            {
                case "search": q.Search = val; break;
                case "status":
                    if (AssetStatusNames.TryParse(val, out var s)) q.Status = s;
                    break;
                case "categoryId" when int.TryParse(val, out var c): q.CategoryId = c; break;
                case "locationId" when int.TryParse(val, out var l): q.LocationId = l; break;
                case "manufacturer": q.Manufacturer = val; break;
                case "assigned": q.Assigned = val == "true"; break;
                case "assignedUserId" when int.TryParse(val, out var uid): q.AssignedUserId = uid; break;

            }
        }
        return q;
    }
}
