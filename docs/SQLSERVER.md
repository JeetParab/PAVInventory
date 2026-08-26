# PAV — SQLite and SQL Server Express

One PAV application. Two database backends. **SQLite is the default** and stays fully supported.

Do **not** put SQL Server on the internet. Office LAN only.

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

### 1. Install SQL Server Express (host PC only)

1. Install SQL Server Express on one Windows 11 desktop.
2. During setup, enable **Mixed Mode** is optional. PAV uses **Windows Authentication**.
3. Install SSMS on the host if you want a GUI. Not required for PAV.

### 2. Enable the network

SQL Server Configuration Manager on the host:

- SQL Server Network Configuration → Protocols for `SQLEXPRESS` → **TCP/IP = Enabled**
- TCP/IP Properties → IPAll → TCP Port **1433**
- Start **SQL Server Browser** if clients use `HOST\SQLEXPRESS`

Windows Firewall on the host:

- Allow inbound TCP **1433** (and UDP **1434** if using the Browser) from the office LAN only.

Restart the SQL Server service.

### 3. First database create

On the host, as a Windows admin:

1. Run PAV.
2. Settings (admin) → Database = **SqlServer**
3. Server: `THISPC\SQLEXPRESS` (or `.` if the default instance)
4. **Save / connect**

PAV creates database `PAVInventory` and tables on first connect. Other engineers do not need dbcreator rights if the database already exists — grant them access to `PAVInventory` (Windows group / logins).

### 4. Point engineer PCs at SQL Server

On each engineer PC (or once next to the exe as `database.json`):

```json
{
  "Provider": "SqlServer",
  "SqlitePath": "",
  "SqlServerConnectionString": "HOST\\SQLEXPRESS"
}
```

A server name is enough. PAV expands it to Windows Authentication + `PAVInventory`.

File locations:

- Team default (optional): `database.json` next to `PAV.Client.exe`
- Per PC: `%LOCALAPPDATA%\PAV Inventory\database.json` (wins if present)

Normal users just launch PAV. They do not pick a folder.

If the server is down they see:

> Unable to connect to the PAV database server. Please contact the PAV administrator.

### 5. Copy inventory.db into SQL Server

1. Connect PAV to SQL Server (empty of PAV users, or accept replace).
2. Settings → **Copy SQLite into SQL Server**
3. Choose the folder that contains `inventory.db`
4. Confirm. SQLite file is **not** deleted.

Check the on-screen counts. Users, assets, history, IPs, stock must match.

### 6. Switch clients

After a good copy, every PC uses `Provider: SqlServer`. Stop writing to the old `inventory.db` so you do not fork the data.

### 7. Backup

**SQLite:** Settings → Backup now. Copies `inventory.db` into `Backups\`. Restore still works.

**SQL Server:** Do not copy the live MDF while the service is running.

On the **host**:

```sql
BACKUP DATABASE PAVInventory
TO DISK = N'C:\PAV\Backups\PAVInventory_2026-08-26.bak'
WITH COPY_ONLY, INIT;
```

Schedule that daily. Restore only on the host with `RESTORE DATABASE`.

PAV can also write a **data snapshot** `.snapshot.db` under `%LOCALAPPDATA%\PAV Inventory\Backups` (a SQLite copy of the rows). That is a spare copy, not a substitute for `BACKUP DATABASE`.

### 8. Rollback to SQLite

1. Keep the original `inventory.db` (migration never deletes it).
2. Settings → Database = **SQLite** → shared folder → Save.
3. Everyone points at that folder again.

If you already wrote new data in SQL Server and need it back in the file, use a snapshot `.snapshot.db` as the SQLite file, or restore a host `.bak` and keep SQL Server.

## Capacity (engineering estimate, not a lab benchmark)

This environment could not run live SQL Server. Treat as guidance for 5–6 trusted users:

| Users | SQLite on a share | SQL Server Express on LAN |
|------|--------------------|---------------------------|
| 5–6 | Works. Avoid two Excel imports at once. Occasional “database is locked” is normal. | Comfortable. This is the point of Mode B. |
| 10 | Risky on a file share. | Fine. |
| 20 | Not recommended. | Should be fine for this workload. Measure before you grow. |

## What stays SQLite-only

- `PRAGMA` (busy_timeout, journal_mode, …)
- `sqlite_master` / `CREATE TABLE IF NOT EXISTS` patches
- File copy backup/restore
- Process write lock + busy retry

SQL Server uses SQL Server transactions and deadlock retry only. Asset `Version` conflict checks are unchanged on both.
