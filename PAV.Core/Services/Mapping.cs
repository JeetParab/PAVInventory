using PAV.Shared.Dtos;
using PAV.Shared.Enums;
using PAV.Shared.Models;

namespace PAV.Core.Services;

public static class Mapping
{
    public static void FillList(AssetListDto d, Asset a)
    {
        d.Id = a.Id;
        d.AssetTag = a.AssetTag;
        d.SrNo = a.SrNo;
        d.CategoryId = a.CategoryId;
        d.Category = a.Category?.Name ?? "";
        d.Manufacturer = a.Manufacturer;
        d.Model = a.Model;
        d.SerialNumber = a.SerialNumber;
        d.Hostname = a.Hostname;
        d.IpAddress = a.IpAddress;
        d.LocationId = a.LocationId;
        d.Location = a.Location?.Name;
        d.AssignedUserId = a.AssignedUserId;
        d.AssignedUser = a.AssignedUser?.Name ?? Clean(a.AssignedUserName);
        d.Designation = a.Designation;
        d.AlternateUser = a.AlternateUser;
        d.Status = a.Status.Display();
        d.StatusValue = a.Status;
        d.Domain = a.Domain;
        d.MacAddress = a.MacAddress;
        d.Processor = a.Processor;
        d.Ram = a.Ram;
        d.Storage = a.Storage;
        d.OperatingSystem = a.OperatingSystem;
        d.DcInstalled = a.DcInstalled;
        d.AvInstalled = a.AvInstalled;
        d.MsOfficeVersion = a.MsOfficeVersion;
        d.MfaEnabled = a.MfaEnabled;
        d.IvantiInstalled = a.IvantiInstalled;
        d.AdminRights = a.AdminRights;
        d.UsbAccess = a.UsbAccess;
        d.ChromeUpdated = a.ChromeUpdated;
        d.StockAvailability = a.StockAvailability;
        d.StockWorking = a.StockWorking;
        d.PmCompleted = a.PmCompleted;
        d.LastConnected = a.LastConnected;
        d.CollectBy = a.CollectBy;
        d.Remarks = a.Remarks;
        d.PurchaseDate = a.PurchaseDate;
        d.WarrantyExpiry = a.WarrantyExpiry;
        d.Version = a.Version;
    }

    public static AssetListDto ToListDto(Asset a)
    {
        var d = new AssetListDto();
        FillList(d, a);
        return d;
    }

    public static AssetDetailDto ToDetailDto(Asset a, IEnumerable<HistoryDto> history)
    {
        var d = new AssetDetailDto
        {
            AssignedDepartment = a.Designation ?? a.AssignedUser?.Department,
            AssignedDate = a.AssignedDate,
            CreatedAt = a.CreatedAt,
            UpdatedAt = a.UpdatedAt,
            History = history.ToList()
        };
        FillList(d, a);
        return d;
    }

    public static void Apply(Asset asset, SaveAssetRequest req)
    {
        asset.AssetTag = req.AssetTag.Trim();
        asset.SrNo = req.SrNo;
        asset.SerialNumber = Clean(req.SerialNumber);
        asset.CategoryId = req.CategoryId;
        asset.Manufacturer = Clean(req.Manufacturer);
        asset.Model = Clean(req.Model);
        asset.Hostname = Clean(req.Hostname);
        asset.IpAddress = Clean(req.IpAddress);
        asset.LocationId = req.LocationId;
        asset.Designation = Clean(req.Designation);
        asset.AlternateUser = Clean(req.AlternateUser);
        asset.Status = req.Status;
        asset.Domain = Clean(req.Domain);
        asset.MacAddress = Clean(req.MacAddress);
        asset.Processor = Clean(req.Processor);
        asset.Ram = Clean(req.Ram);
        asset.Storage = Clean(req.Storage);
        asset.OperatingSystem = Clean(req.OperatingSystem);
        asset.DcInstalled = YesNo(req.DcInstalled);
        asset.AvInstalled = YesNo(req.AvInstalled);
        asset.MsOfficeVersion = Clean(req.MsOfficeVersion);
        asset.MfaEnabled = YesNo(req.MfaEnabled);
        asset.IvantiInstalled = YesNo(req.IvantiInstalled);
        asset.AdminRights = YesNo(req.AdminRights);
        asset.UsbAccess = YesNo(req.UsbAccess);
        asset.ChromeUpdated = YesNo(req.ChromeUpdated);
        asset.StockAvailability = Clean(req.StockAvailability);
        asset.StockWorking = Clean(req.StockWorking);
        asset.PmCompleted = YesNo(req.PmCompleted);
        asset.LastConnected = req.LastConnected?.Date;
        asset.CollectBy = Clean(req.CollectBy);
        asset.PurchaseDate = req.PurchaseDate?.Date;
        asset.WarrantyExpiry = req.WarrantyExpiry?.Date;
        asset.Remarks = Clean(req.Remarks);
        // Assignment is applied separately (AssignedUserId is authoritative).
    }

    public static MeDto ToMe(User u) => new()
    {
        Id = u.Id,
        Name = u.Name,
        Username = u.Username,
        Role = u.Role,
        RoleName = u.Role.Display(),
        Permissions = Permissions.ForRole(u.Role).ToList(),
        MustChangePassword = u.MustChangePassword
    };

    public static UserDto ToDto(User u) => new()
    {
        Id = u.Id,
        EmployeeId = u.EmployeeId,
        Username = u.Username,
        Name = u.Name,
        Email = u.Email,
        Department = u.Department,
        LocationId = u.LocationId,
        Location = u.Location?.Name,
        Role = u.Role,
        RoleName = u.Role.Display(),
        IsActive = u.IsActive
    };

    public static LocationDto ToDto(Location l) => new()
    {
        Id = l.Id,
        Name = l.Name,
        Description = l.Description
    };

    public static CategoryDto ToDto(Category c) => new()
    {
        Id = c.Id,
        Name = c.Name,
        Description = c.Description
    };

    public static HistoryDto ToDto(AssetHistory h, string? displayName = null) => new()
    {
        Id = h.Id,
        Username = h.Username,
        DisplayName = displayName ?? h.Username,
        Action = h.Action,
        FieldName = h.FieldName,
        OldValue = h.OldValue,
        NewValue = h.NewValue,
        Timestamp = h.Timestamp
    };

    public static string ShortName(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "Unknown";
        return value.Trim();
    }

    public static string? Clean(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        var t = value.Trim();
        return t.Length == 0 ? null : t;
    }

    public static string? YesNo(string? value)
    {
        var t = Clean(value);
        if (t is null) return null;
        if (t.Equals("yes", StringComparison.OrdinalIgnoreCase) || t == "1" || t.Equals("true", StringComparison.OrdinalIgnoreCase) || t.Equals("y", StringComparison.OrdinalIgnoreCase))
            return "Yes";
        if (t.Equals("no", StringComparison.OrdinalIgnoreCase) || t == "0" || t.Equals("false", StringComparison.OrdinalIgnoreCase) || t.Equals("n", StringComparison.OrdinalIgnoreCase))
            return "No";
        return t;
    }

    public static (string? Manufacturer, string? Model) SplitMakeModel(string? raw)
    {
        var t = Clean(raw);
        if (t is null) return (null, null);

        (string mfr, string? rest)? Hit(string brand, string? asMfr = null)
        {
            if (t.Equals(brand, StringComparison.OrdinalIgnoreCase))
                return (asMfr ?? brand, null);
            if (t.StartsWith(brand + " ", StringComparison.OrdinalIgnoreCase))
                return (asMfr ?? brand, t[(brand.Length + 1)..].Trim());
            return null;
        }

        var known =
            Hit("ACER") ??
            Hit("Acer", "ACER") ??
            Hit("HP") ??
            Hit("DELL") ??
            Hit("Dell", "DELL") ??
            Hit("Inspiron", "DELL") ??
            Hit("Microsoft") ??
            Hit("Surface", "Microsoft") ??
            Hit("Lenovo") ??
            Hit("Apple");
        if (known is { } k)
            return (k.mfr, Clean(k.rest));

        var i = t.IndexOf(' ');
        if (i < 0) return (t, null);
        return (t[..i], Clean(t[(i + 1)..]));
    }
}
