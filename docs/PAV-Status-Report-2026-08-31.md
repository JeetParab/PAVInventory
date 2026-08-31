# PAV IT Inventory — Status Report

**PAV IT Inventory for SIDBI**  
Crafted by Jeet Parab  
Report date: 31 August 2026  
Repository: [github.com/JeetParab/PAVInventory](https://github.com/JeetParab/PAVInventory)  
Latest commit: `f5be3c2` — Add people from existing inventory names

**Verdict: LIVE on SQLite for the IT team. SQL Server Express is built but not yet piloted.**

---

## 1. What PAV is

PAV is a Windows desktop application that replaced the shared Excel trackers used by SIDBI IT (Mumbai) for:

- Serialized IT assets (laptops, desktops, printers, etc.)
- Static office IP addresses (floors 1–7)
- Consumable / quantity stock (mice, cables, toner, spare monitors without serials)
- Office people (who has which kit)
- Sign-in accounts for the 5–6 engineers who run the app

It is **not** a website and **not** a Java server. Each engineer runs `PAV.Client.exe`. Data lives in one shared SQLite file (`inventory.db`) on a network folder. Optional SQL Server Express is in the same exe, selectable in Settings.

Built for a small trusted internal team. History records who changed what.

---

## 2. How it is built

| Piece | Role |
|---|---|
| **PAV.Client** | WPF desktop UI (.NET 8) |
| **PAV.Core** | Database, login, Excel import/export, backup, stock, IP |
| **PAV.Shared** | Models and DTOs |

**Technology**

- .NET 8 + WPF
- CommunityToolkit.Mvvm
- Entity Framework Core (SQLite default, SQL Server Express optional)
- ClosedXML for Excel / CSV
- PBKDF2 password hashing
- Self-contained single-file publish (`deploy\Publish.ps1`)
- Optional Authenticode signing of the exe

**Distribution**

```
\\fileserver\IT\PAV\
  PAV.Client.exe
  inventory.db
  Backups\
```

Engineer PCs do not install SQL Server or the .NET runtime when using the self-contained exe.

---

## 3. Screens (sidebar)

1. **Dashboard** — counts and shortcuts  
2. **Inventory** — serialized assets grid (search, filter, assign, bulk, import/export)  
3. **IP Inventory** — floor pools, next-free, check an address, full allocation list  
4. **Stock** — quantity items, receive / issue / return / adjust, Excel import  
5. **Toner** — same stock engine, toner category only  
6. **Users** — office people directory (name, employee ID, email, department) + their assets  
7. **Locations** — office locations  
8. **Settings** — database, dark mode, categories, backup, **sign-in accounts** (admin)

---

## 4. What each module does

### 4.1 Inventory (serialized assets)

Replaces the Excel asset register.

- Asset ID, serial, hostname, IP, MAC, make/model, location, category, status
- Assignment to a person (`AssignedUserId` is the link; display name is secondary)
- History of who assigned / edited / returned
- Search across tag, serial, hostname, IP, user, location, category, model, MAC, designation
- Bulk add / bulk edit / bulk status
- Assign / unassign without opening the full editor
- Duplicate serial finder
- Renumber Sr by IP order
- Optional freeze of identity columns while scrolling
- Excel / CSV import and export
- Undo on delete (short window)

Shortcuts: Ctrl+F search, Ctrl+N add, F2 edit, Enter open, Delete, F5 reload.

### 4.2 IP Inventory

Replaces **IP Address Management FM - SIDBI.xlsx**.

- Floor cards: used / free / next free (lowest free on that floor)
- Assign next free, or assign any free address in the pool
- Check an address (e.g. `172.16.103.178`) — Free / Used / Reserved with details
- Full allocation list (Excel-style grid; default used + reserved)
- Import `Floors_Config` + `IP_Inventory`
- Wi-Fi and other subnets stay on the asset record; they are not in the Floor 1–7 pool

### 4.3 Stock and Toner

Replaces **Consumable Stock Details** workbooks. Separate from the serialized Inventory grid.

| Movement | Who | Effect on on-hand |
|---|---|---|
| Opening | Import | Shelf count at go-live |
| Receive | Engineer | + |
| Issue | Engineer | − (linked to a person) |
| Return | Engineer | + |
| Adjust | Admin | signed correction |
| Item master / Excel import | Admin | |

On-hand = Opening + Received − Issued + Returned + Adjusted.  
Low stock = on-hand at or below minimum.

**2026 workbook:** one Excel row = one piece. That is the opening baseline. Already-issued 2026 rows (e.g. KM7321W to CGM Sandeep Verma) become Opening + Issue so shelf count is what is left.

**2025 original workbook:** rejected as stock (history, laptops, printers, purchases — would double-count).

**2025 cleaned leftover file:** importable as remaining on-hand only, not a replay of issued history.

Toner is the same engine filtered to toner items.

### 4.4 People vs sign-in accounts (31 Aug 2026)

Two lists. Same database table, `CanSignIn` flag. Asset links stay intact.

| | Users tab | Settings → Sign-in accounts |
|---|---|---|
| Who | Office staff you assign kit to | Engineers / admins who open PAV |
| Fields | Name, employee ID, email, department | Username, password, role |
| Can log in | No | Yes |
| Who edits | Engineers can add/edit | Admin only |

**Add from inventory** takes unique names already on assets/stock, creates directory people, and links those records. Sign-in names are not duplicated. Junk (`N/A`, `Unassigned`) is skipped.

Selecting a person shows their assigned assets and stock issue/return history.

### 4.5 Auth, roles, backup

- First person on a new database creates the administrator (password ≥ 8 characters; `admin` / `engineer` / `guest` rejected)
- No built-in default passwords
- Legacy default passwords force a change at next login
- Roles: Administrator / Engineer / Guest

| | Admin | Engineer | Guest |
|---|---|---|---|
| View / search / filter | ✓ | ✓ | ✓ |
| Add / edit / assign | ✓ | ✓ | |
| Delete | ✓ | | |
| Import / export | ✓ | ✓ | export only |
| Sign-in accounts, locations, categories | ✓ | | |
| Backup / restore | ✓ | | |
| Stock receive / issue / return | ✓ | ✓ | |
| Stock adjust / item master / stock import | ✓ | | |
| Directory people add/edit | ✓ | ✓ | |
| Directory people remove | ✓ | | |

SQLite backup copies `inventory.db` (last 30 kept). SQL Server backup in-app is a data snapshot; official backup is `BACKUP DATABASE` on the host.

---

## 5. Timeline (what shipped)

Work started as a desktop replacement for the Excel asset sheet, then grew module by module. GitHub history from 23 Aug 2026:

| Date | What landed |
|---|---|
| 23 Aug | Single-file publish for office PCs |
| 24 Aug | IP Inventory (floors, next-free, check, Excel import); allocation list split from assign |
| 24 Aug | Stock module + 2026 Excel import |
| 24 Aug | 2025 leftover import (cleaned file only; original 2025 still rejected) |
| 25 Aug | Toner view; Stock hardening (identity keys, preview=import plan, UserNameResolver, integrity check) |
| 25 Aug | Assign any free IP, not only next-free |
| 25 Aug | UI polish (inventory flash, ComboBox, themed dialogs/scrollbars) |
| 25 Aug | Signed single-file `PAV.Client.exe` |
| 26 Aug | Dual backend: SQLite (default) + optional SQL Server Express |
| 26 Aug | SQL Server pre-flight: concurrency tokens, migrator FK/PK checks, team `database.json` precedence |
| 31 Aug | Stock horizontal scroll; Users tab shows each person’s assets |
| 31 Aug | Split office people from engineer/admin sign-in accounts |
| 31 Aug | Add people from names already on inventory |

UI work before GitHub (padding, tools menu, column freeze, branding, login, themed dialogs, shared-folder SQLite) is in the live app.

---

## 6. Database (current architecture)

```
                 PAV.Client.exe
                       │
              ┌────────┴────────┐
              ↓                 ↓
           SQLite          SQL Server Express
         (default,          (optional)
          LIVE)                  │
              │                  ↓
        inventory.db       PAVInventory
```

SQLite remains the deployed mode. Three engineers have already used it at the same time on the office LAN.

SQL Server path exists in the same exe:

- Windows Authentication, no passwords in source
- `database.json` next to the exe (team file wins over AppData)
- SQLite → SQL Server copy (IDENTITY_INSERT, row counts, FK checks)
- Concurrency tokens on Asset.Version, StockItem.OnHand, IpRecord.Status
- Deadlock retry on SQL writes
- SQLite file lock remains the SQLite write serializer

**Not claimed:** SQL Server networking, Windows Firewall, WPF-on-Windows UI, or multi-PC SQL concurrency have been tested in this Linux build environment. Status: **ready for a live 2–3 client pilot**, not “production proven.”

Recommended SQL pilot (when you choose to): 1 Express host, 2–3 PAV clients, then 5–6 after it holds.

Rollback: point Settings back to SQLite; keep the original `inventory.db`.

---

## 7. Data integrity rules that must not be broken

- `AssignedUserId` is the assignment link; display name is not
- User / person resolution: trim, case-insensitive, unique match only — never pick the first of several
- Stock 2026 = opening baseline; do not import original 2025 as stock
- 2025 cleaned file = leftover on-hand only
- Preview import and real import use the same plan
- Opening / Received / Issued / Returned / Adjusted are separate totals
- Unique serial numbers (empty serials allowed, duplicates blocked)

---

## 8. How to build and run (office)

On a PC with Git + .NET 8 SDK:

```powershell
cd $env:USERPROFILE\Documents
git clone https://github.com/JeetParab/PAVInventory.git
cd PAVInventory
git pull
.\deploy\Publish.ps1
```

Copy **only** `dist\client\PAV.Client.exe` over the office exe. Do **not** copy a new `inventory.db`. Unblock the exe once.

Login: Browse to the shared folder if the exe is not already next to `inventory.db`.

---

## 9. Current live status

| Item | Status |
|---|---|
| Serialized inventory | Live |
| IP inventory | Live |
| Stock + Toner | Live |
| Directory people + add from inventory | In latest source (`f5be3c2`) — rebuild exe to pick up |
| Sign-in accounts in Settings | In latest source — rebuild exe to pick up |
| Shared SQLite, 3+ engineers concurrent | Confirmed in office |
| Antivirus / DLP | Ran on office desktop; no block reported |
| SQL Server Express | Code complete; not live-piloted |
| Code signing | Script supports a local PAV / Jeet Parab cert if present |

---

## 10. Known limits (honest)

- SQLite on a share is fine for ~5–6 trusted users; two saves at the same instant retry. It is not a 20-user server.
- Directory people created from inventory get **name only**. Email / employee ID / department are filled in by edit.
- SQL Server `EnsureCreated` is first-install only; later schema uses explicit column patches.
- In-app SQL backup is a snapshot, not `BACKUP DATABASE`.
- SmartScreen may warn on an unsigned or self-signed exe until the cert is trusted.
- This is an internal tool, not an enterprise IAM / CMDB.

---

## 11. Suggested next steps (when you want them)

1. Pull `f5be3c2`, publish, replace the office exe, click **Add from inventory** on Users.  
2. Fill email / employee ID / department on people you care about.  
3. Keep using SQLite until it hurts; only then run the SQL Server live checklist (`docs/SQLSERVER_LIVE_TEST_CHECKLIST.md`) with 2–3 clients.  
4. Do not start a new major module until the people directory has been used for a few days.

---

*End of report — 31 August 2026.*
