# PAV — SQLite and SQL Server Express

One PAV application. Two database backends. **SQLite is the default** and stays fully supported.

Do **not** put SQL Server on the internet. Office LAN only.

**SQL Server networking, firewall, and multi-PC behaviour have not been live-tested in this development environment.** Follow [SQLSERVER_LIVE_TEST_CHECKLIST.md](SQLSERVER_LIVE_TEST_CHECKLIST.md) on a Windows 11 host before a team cutover.

## Mode A — SQLite (current)

Same as today. `inventory.db` in a shared folder. No SQL Server required.

Theme, sidebar, and column layout are stored per Windows user in:

`%LOCALAPPDATA%\PAV Inventory\ui.json`

They are not written into a shared `appsettings.json`.

## Mode B — SQL Server Express

```
Engineer PCs  →  Office LAN  →  Windows 11 host  →  SQL Server Express  →  PAVInventory
```

Engineer PCs do **not** install SQL Server.

### Configuration precedence

Last matching file wins for the database provider:

1. Built-in default = **SQLite** + `inventory.db` next to the exe
2. `appsettings.json` next to the exe (legacy `DatabasePath`)
3. `%LOCALAPPDATA%\PAV Inventory\database.json` (this PC)
4. **`database.json` next to `PAV.Client.exe`** (team default — wins)

UI settings always come from AppData `ui.json`.

To switch every PC at once, copy `database.json` next to the shared exe. An old AppData SQLite file will not override it.

### 1. Install SQL Server Express (host PC only)

**REQUIRES LIVE WINDOWS VALIDATION**

1. Install SQL Server Express on one Windows 11 desktop.
2. PAV uses **Windows Authentication**. Mixed Mode is optional.
3. SSMS on the host is optional.

### 2. Enable the network

**REQUIRES LIVE WINDOWS VALIDATION**

SQL Server Configuration Manager on the host:

- SQL Server Network Configuration → Protocols for `SQLEXPRESS` → **TCP/IP = Enabled**
- TCP/IP Properties → IPAll → TCP Port **1433**
- Start **SQL Server Browser** if clients use `HOST\SQLEXPRESS`

Windows Firewall on the host:

- Allow inbound TCP **1433** (and UDP **1434** if using the Browser) from the office LAN only.

Restart the SQL Server service.

### 3. First database create — do this once

**Do not let 5 clients hit a brand-new empty server at the same time.** `EnsureCreated` is first-install only and is not a concurrent migration framework.

On the **host**, as a Windows admin:

1. Run PAV.
2. Settings (admin) → Database = **SqlServer**
3. Server: `THISPC\SQLEXPRESS` (or `.` if the default instance)
4. **Save / connect**

PAV creates database `PAVInventory` and tables on first connect. Confirm login works. Then point other PCs at it.

Future schema changes will need a controlled upgrade (not `EnsureCreated`).

### 4. Point engineer PCs at SQL Server

Team file next to the exe:

```json
{
  "Provider": "SqlServer",
  "SqlitePath": "",
  "SqlServerConnectionString": "HOST\\SQLEXPRESS"
}
```

A server name is enough. PAV expands it to Windows Authentication + `PAVInventory`.

Do not put SQL passwords in source code. If you paste a full connection string with `Password=`, it is stored in `database.json` on disk (same as any connection string file). Prefer Windows Authentication.

Normal users just launch PAV. They do not pick a folder.

If the server is down they see:

> Unable to connect to the PAV database server. Please contact the PAV administrator.

Then: start SQL Server, restart PAV. **REQUIRES LIVE VALIDATION** of that recovery path.

### 5. Copy inventory.db into SQL Server

1. Backup SQLite first (Settings → Backup now, and copy `inventory.db`).
2. Connect PAV to SQL Server (admin, once).
3. Settings → **Copy SQLite into SQL Server**
4. Choose the folder that contains `inventory.db`
5. Confirm replace. SQLite file is **not** deleted.

Check the on-screen report: table counts **and** relationship checks (AssignedUserId, history, stock, IPs).

Running copy again without replace on a database that already has users/assets/stock is **refused**. Replace wipes SQL PAV data then recopies. Do not run replace casually.

### 6. Switch clients

After a good copy, every PC uses `Provider: SqlServer`. Stop writing to the old `inventory.db`.

### 7. Backup

**SQLite:** Settings → Backup now. Copies `inventory.db`. Restore still works.

**SQL Server:** PAV does **not** run `BACKUP DATABASE` (that needs a path on the SQL host and sysadmin). In-app backup writes a **data snapshot** `.snapshot.db` under `%LOCALAPPDATA%\PAV Inventory\Backups`. That is a spare copy of rows, not a substitute for a host backup.

On the **host**, schedule:

```sql
BACKUP DATABASE PAVInventory
TO DISK = N'C:\PAV\Backups\PAVInventory_yyyy-MM-dd.bak'
WITH COPY_ONLY, INIT;
```

Restore only on the host with `RESTORE DATABASE`. **REQUIRES LIVE VALIDATION** that a `.bak` actually restores.

Never copy the live `.mdf` while SQL Server is running.

### 8. Rollback to SQLite

1. Keep the original `inventory.db` (migration never deletes it).
2. Remove or edit team `database.json` so Provider is SQLite.
3. Settings → **SQLite** → same shared folder → Save.

If you already wrote new data only in SQL Server, that work is not in the old file.

## Concurrency

- SQLite: process write lock + file busy retry (SQLite-only).
- SQL Server: no file lock. Deadlock (1205) / lock timeout (1222) retried a few times. Login and missing-server errors are not retried.
- Both: `Asset.Version` is a concurrency token. Stale edits get 409. Stock `OnHand` and IP `Status` are also tokens so two people cannot issue the last piece or steal the same IP silently.

## Capacity (engineering estimate, not a lab benchmark)

| Users | SQLite on a share | SQL Server Express on LAN |
|------|--------------------|---------------------------|
| 5–6 | Works. Avoid two Excel imports at once. | Comfortable. This is the point of Mode B. |
| 10 | File-share locks get annoying | Fine |
| 20 | Not recommended | Should be fine for this workload. Measure on the LAN. |

## What stays SQLite-only

- `PRAGMA`
- `sqlite_master` / `CREATE TABLE IF NOT EXISTS` patches
- File copy backup/restore
- Process write lock + busy retry
