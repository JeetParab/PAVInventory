# PAV Inventory — session handoff

**Date:** 4 September 2026  
**Repo:** https://github.com/JeetParab/PAVInventory  
**Branch:** `main`  
**HEAD:** `8e2f2f4` — Slow DataGrid wheel to one native row per notch  
**App:** PAV IT Inventory for SIDBI, crafted by Jeet Parab  
**Users:** ~5–6 trusted internal IT engineers, Mumbai BKC  

This note is for the next chat. Do not redesign PAV. Do not invent enterprise features. Keep SQLite as the default; SQL Server Express is optional.

---

## What PAV is

WPF .NET 8 desktop app. No server process.

```
PAV.Client  →  EF Core  →  SQLite (inventory.db)     default, currently in use
                     └→  SQL Server Express          optional, not live-piloted
```

Projects:

| Project | Role |
|---|---|
| `PAV.Client` | WPF UI |
| `PAV.Core` | DB, login, import/export, backup, ME import, stock, IP |
| `PAV.Shared` | Models, DTOs, enums |

Engineers run `PAV.Client.exe`. Database is a shared `inventory.db` (or SQL Server on the LAN). First run creates admin. Roles: Administrator / Engineer / Guest.

---

## Tabs (current)

| Tab | What it is |
|---|---|
| **Dashboard** | Counts / overview |
| **Inventory** | Laptops, desktops, all-in-ones only (`CategoryFamily.Computer`) |
| **Peripherals** | Monitors, printers, UPS, accessories, **network devices**, other (`CategoryFamily.Peripheral`) |
| **Pending** | IP assigned but asset details missing; ME import awaiting confirm; mismatch warnings |
| **IP Inventory** | Static office IPs. Assign next/random/custom. Allocation list. Bidirectional sync with assets |
| **Users** | Two tabs: **PAV users** (assignable people) and **AD users** (AD snapshot — import/add/edit/remove) |
| **Stock** | Consumables. Default columns: Item, On hand, Issued. **More columns** shows the rest |
| **Toner** | Same Stock UI, toner category scope |
| **Locations** | Location master |
| **Settings** | Sign-in accounts (engineers/admins), categories (with Computers/Peripherals family), backups, DB path |

Office people ≠ sign-in accounts. `CanSignIn` flag. Asset assignment uses `UserNameResolver` (trim, case-insensitive, unique match only).

---

## What we did in this stretch (1–3 Sep 2026)

### 1. Computers vs Peripherals split — `0556cc5`

- `CategoryFamily` on Category: Computer vs Peripheral.
- Inventory tab = PCs. Peripherals tab = the rest.
- Backfill on open: `EnsureCategoryFamilyAsync` sets family from name.
- Settings category grid has a Tab column so a category can be moved.

### 2. Freeze / NRE crash on Peripherals — `d467702`, `fda161c`

- Frozen columns + hidden IP/hostname columns + `ClipToBounds` threw `NullReferenceException`.
- Removed XAML `FrozenColumnCount` binding and `ClipToBounds`.
- Freeze is optional (Tools), applied in code after Loaded, computers only.
- Unhandled errors write `%LocalAppData%\PAV\crash.log`.

### 3. Network devices moved to Peripherals — `e23d6d3`

- `FromName("network")` → Peripheral. Seed + backfill move existing Network category.
- Inventory hint: “Laptops, desktops and all-in-ones”.
- Peripherals hint: “Monitors, printers, UPS, network devices and other kit”.
- Peripherals add/edit: **no** hostname, last connected, collect-by, domain, compliance, processor/RAM/storage/OS.
- Peripherals **keep** IP + MAC (needed for switches/APs/printers).
- Validation on peripherals: Asset ID **or** serial **or** IP.

### 4. Peripherals add-asset layout — `74cc167`

- Hiding hostname had left a hole next to IP.
- Packed form: IP | MAC on the same row. Hardware/compliance blocks only on the computer form.

### 5. Stock / Toner columns — `ed6fa86`

- Default grid: **Item, On hand, Issued** only.
- Button **More columns** / **Fewer columns** shows make, model, category, min, status, opening, received, returned, adjusted.
- Movements tab and the recent-movements pane under a selected item are unchanged.

### 6. Mouse wheel (3 Sep, last thing we touched) — `9a2f80b` … `8e2f2f4`

| Commit | What happened |
|---|---|
| `9a2f80b` / `2e6c566` | Tried to unify speed. Custom grid handler felt **laggy**. |
| `2f421de` | Reverted grids to native (3 rows/notch). Settings stayed 48px. |
| **`8e2f2f4` HEAD** | Grids: **one native `LineUp`/`LineDown` per notch** (cached ScrollViewer, no visual-tree walk every tick). Settings/forms: still 48px via `PageScroll.Smooth`. Shift+wheel pans wide grids. |

**Not live-tested after `8e2f2f4`.** If lists still feel laggy, drop `PageScroll.Grid` from `Theme.xaml` DataGrid style and leave Settings Smooth as-is.

---

## Earlier work still in the app (do not rip out)

- **ManageEngine import** (`916e327`): serial-first match, trust ME for hostname/IP/MAC/model/OS/RAM/last connected. Duplicate IP winner = newest Last Contact Time. New machines → Pending (`NeedsReview`) until engineer confirms. ME logon is matched to a one-time **AdDirectory** (SAM / user id from ADMP All Users). Unique last-logon match creates/updates a Users person (`CanSignIn = false`) and assigns only if the PC has no assigned user yet.
- **Pending** (`b496ff4`, `50a5ed2`, `79af554`): missing details + mismatch warnings (user / hostname / MAC / duplicate IP). Cancel must not reopen the dialog (reentrancy guard).
- **IP ↔ Inventory bridge** (`48f2191`, `56af3e3`): assign IP can create/update asset; delete asset frees IP. Purpose: Inventory vs Temporary. Temporary hidden from main inventory list.
- **Random free IP** (`dce686f`).
- **Inventory search** (`963b0b6`, `24b5e0e`): debounce + virtualization; placeholder no longer overlaps typed text.
- **Stock module** with Excel import (2026 opening; 2025 is history — do not double-count), Receive/Issue/Return/Adjust, integrity check, `UserNameResolver`.
- **Dual DB** (`efd4245`, `028dabc`): SQLite default. SQL Server Express optional. Linux cannot validate SQL networking/firewall/WPF concurrency. Pilot rec: 1 host, 2–3 clients first.
- **Single-file publish** exists (`publish.ps1` / signed exe path). Office is currently running a published client against shared SQLite; 3 engineers already used it together.

---

## Important behaviour to preserve

1. **Do not remove SQLite.** SQL Server is optional.
2. **Do not merge Users and Settings accounts.** Office people vs engineers.
3. **User resolution:** unique match on name or username; zero or many matches → unresolved. Never pick the first hit.
4. **Network = Peripherals**, not Inventory.
5. **Stock default columns stay short.** Extra totals behind More columns.
6. **ME import:** preview required, transactional, serial is identity, engineer confirms new machines.
7. **Pending cancel** must not re-open.
8. **Column freeze** is optional, computers only, applied after Loaded — do not bind `FrozenColumnCount` in XAML.

---

## How to pull and build (Windows)

```powershell
cd <your-clone>
git pull origin main
# confirm HEAD is 8e2f2f4 (or later)

dotnet build PAV.sln -c Release -p:EnableWindowsTargeting=true

# run
dotnet run --project PAV.Client -c Release
```

Publish single-file (existing script in repo): `publish.ps1` — not an exe; run it from PowerShell. Point Shared folder at the office `inventory.db`.

Crash log if Inventory/Peripherals dies: `%LocalAppData%\PAV\crash.log`.

---

## Known gaps / next (user has not asked to start these)

- Live Windows validation of `8e2f2f4` scroll feel.
- Other-office ManageEngine files (only MUM-ReM was designed against).
- SQL Server Express live pilot (docs exist; not proven on the LAN).
- Code signing if SmartScreen/DLP flags the exe (user was going to check at office first).
- Stock/Toner may still want category/make visible without More columns — only if they ask.

---

## Files touched in the latest stretch (for the next agent)

| Area | Files |
|---|---|
| Category split | `PAV.Shared/Enums/CategoryFamily.cs`, `PAV.Core/Data/SeedData.cs`, `PAV.Core/Data/PavDatabase.cs` (`EnsureCategoryFamilyAsync`) |
| Inventory/Peripherals UI | `PAV.Client/ViewModels/InventoryViewModel.cs`, `Views/InventoryView.xaml`, `Views/InventoryView.xaml.cs` |
| Add/edit form | `PAV.Client/ViewModels/AssetEditViewModel.cs`, `Views/AssetEditWindow.xaml` |
| Stock columns | `PAV.Client/ViewModels/StockViewModel.cs`, `Views/StockView.xaml`, `Views/StockView.xaml.cs` |
| Scroll | `PAV.Client/Services/PageScroll.cs`, `Themes/Theme.xaml`, `Views/SettingsView.xaml(.cs)` |

---

## New-chat starter prompt (paste this)

```
Continue PAV Inventory from github.com/JeetParab/PAVInventory main @ 8e2f2f4.
WPF .NET 8, SQLite default, optional SQL Server. Do not redesign.
Latest: computers vs peripherals tabs; Network is Peripheral; Stock shows Item/On hand/Issued;
grid wheel is one LineUp per notch (Settings still 48px).
Read PAV-HANDOFF-2026-09-04.md in the repo/workspace before changing anything.
```
