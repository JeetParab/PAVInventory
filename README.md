# PAV Inventory

Lightweight multi-user IT asset inventory for a small Windows team. Replaces an Excel tracker.

No server process. One SQLite database sits in a **shared folder**. Each engineer runs the desktop app, signs in with a PAV username and password, and reads/writes that file. History records who changed what.

| Project | What it is |
|---|---|
| **PAV.Client** | Windows desktop app (WPF, .NET 8) |
| **PAV.Core** | Database (SQLite or SQL Server Express), login, Excel import/export, backup |

| **PAV.Shared** | Models and DTOs |

---

## How you use it

1. Put the published app on the shared folder (or install it locally and point at that folder).
2. First run creates `inventory.db` in that folder, plus a `Backups` subfolder.
3. The first person to open a new database **creates the administrator** (password at least 8 characters; `admin` / `engineer` / `guest` are rejected).
4. Sign in. Changes from any engineer land in the same database. Asset history shows who did it.

Example share:

```
\\fileserver\IT\PAV\
  PAV.Client.exe
  inventory.db          ← created on first run
  Backups\
```

Leave **Shared folder** empty on the sign-in screen if the app itself lives in the share. If the app is installed on each PC, Browse to `\\fileserver\IT\PAV`.

There are **no built-in default passwords**. Existing databases that still use `admin` / `engineer` / `guest` will ask that person to change the password on next sign-in.

### IP Inventory

Static office IPs live in the same database. Import the SIDBI workbook (`Floors_Config` + `IP_Inventory`).

- Floor cards show **Used / Free / next free IP** (lowest free on that floor).
- **Assign next free** on a selected floor, or **check** an address such as `172.16.103.178` — Free can be assigned; Used/Reserved shows who has it.
- **Allocation list** is the full Excel-style grid (default: used + reserved). Filter Free / All when you want the complete view.
- Wi-Fi and other subnets stay on the asset record; they are not part of the Floor 1–7 pool.

### Stock / consumables

Quantity items (mice, cables, batteries, toner, spare monitors without serials) live in **Stock**, not the Inventory grid. Issue and return link to existing PAV users.

- Import **Consumable Stock Details 2026.xlsx** (one Excel row = one piece on the shelf). That is the opening balance.
- Do **not** import the 2025 workbook as stock — it is history, laptops, printers and purchase lists. Importing it would double-count.
- Receive / Issue / Return (engineer). Adjust and item master (admin). Low stock = on hand at or below minimum.
- Already-issued 2026 rows (e.g. KM7321W to CGM Sandeep Verma) become an Opening + Issue, so on-hand is the remaining shelf count.

| | Admin | Engineer | Guest |
|---|---|---|---|
| View / search / filter | ✓ | ✓ | ✓ |
| Add / edit / assign | ✓ | ✓ | |
| Delete | ✓ | | |
| Import / export | ✓ | ✓ | export only |
| Users, locations, categories | ✓ | | |
| Backup / restore | ✓ | | |
| Stock receive / issue / return | ✓ | ✓ | |
| Stock adjust / item master / stock import | ✓ | | |

---

## Daily shortcuts (inventory)

| Key | Action |
|---|---|
| Ctrl+F | Focus search |
| Ctrl+N | Add asset |
| F2 | Edit selected |
| Enter | Open selected |
| Delete | Delete selected (with undo) |
| F5 | Reload |
| Ctrl+S | Save (edit dialog) |

Search covers asset tag, serial, hostname, IP, assigned user, location, category, status, model, manufacturer, MAC, designation.

**Assign** on the toolbar (or right-click → Assign…) updates the selected rows without opening the full editor. Type-ahead uses PAV users plus names already on assets.

---

## Run it on your own PC (development)

```powershell
cd PAVInventory
dotnet build
dotnet run --project PAV.Client
```

That uses `inventory.db` next to the built exe. Fine for testing. For the team, point it at the share.

---

## Production — shared folder

On a PC with the .NET 8 SDK:

```powershell
cd PAVInventory
.\deploy\Publish.ps1
```

Self-contained (no runtime on other PCs):

```powershell
.\deploy\Publish.ps1 -SelfContained
```

Copy `dist\client\` to the share, e.g. `\\fileserver\IT\PAV\`.

Unblock the files once (Explorer → Properties → Unblock, or):

```powershell
Get-ChildItem \\fileserver\IT\PAV -Recurse | Unblock-File
```

Each engineer runs `PAV.Client.exe` from the share (or a shortcut to it).

If you prefer a local install, copy the folder to `C:\Program Files\PAV Inventory` and in the sign-in screen Browse to the shared folder so everyone still uses the same `inventory.db`.

You need write permission on the share. Two people can be in the app at once; if both save at the same instant one waits a moment and retries.

Optional **SQL Server Express** (same app, different backend) is documented in [docs/SQLSERVER.md](docs/SQLSERVER.md). SQLite remains the default. Engineer PCs never install SQL Server.

---

## Excel / CSV

Your current SIDBI master sheet imports as-is (Sr No, Location, Asset_Category, Make_Model, Serial_Number, Hostname, IP, User Name, compliance columns, …).

**Tools → Import from Excel / CSV** and pick `assets_master.csv` (or .xlsx). Locations and categories are created if missing. Assigned user is the person’s name, not a PAV login. If that name uniquely matches a PAV user, the asset is linked. Every imported row is logged under the engineer who ran the import.

The whole file is applied together. If any row is invalid, nothing is written. The preview lists duplicates, missing identity, and unknown status in one summary.

Export writes the same column layout back out.

### Users vs AD users

On **Users** there are two tabs:

- **PAV users** — people you assign laptops, IPs and stock to.
- **AD users** — AD snapshot (user id, name, email, department). Import, add, edit, remove. Edit also updates a PAV user with the same user id. Remove deletes the snapshot only.

Assign in Inventory suggests names and user ids from both lists as you type. A unique AD match creates the PAV person when you assign.

---

## Notes

- Do not put this database on OneDrive/Dropbox sync. Use a normal file share.
- Backup (admin) copies `inventory.db` into `Backups\` on the same share. Keep the last 30.
- If someone already ran an older build that needed a server, you do not need that server any more. Start this build against a new shared folder (or copy a working `inventory.db` into the share).
- Serial numbers are unique when the existing data has no duplicates. Older duplicate serials are left in place so the upgrade never fails.
