# PAV — SQL Server live validation checklist

Use this on a **Windows 11** PC with SQL Server Express, 2–6 client PCs, and the office LAN.

Nothing below was live-tested in the Linux build environment. Tick items only after you actually do them.

Do **not** cut the whole team over until section A–E pass with 2–3 clients.

---

## A. Host setup

- [ ] SQL Server Express installed on one Windows 11 desktop only
- [ ] SQL Server (`SQLEXPRESS` or default) service is Running
- [ ] SQL Server Browser running if clients will use `HOST\SQLEXPRESS`
- [ ] TCP/IP enabled in SQL Server Configuration Manager
- [ ] IPAll TCP port = 1433 (or a known static port)
- [ ] Windows Firewall inbound TCP 1433 allowed **from the office LAN only**
- [ ] UDP 1434 allowed only if using the Browser
- [ ] No port forwarding, no public IP, no internet SQL
- [ ] Windows accounts for the 2–3 pilot engineers can authenticate (Windows Auth)

Record:

```
Host computer name: ________________
Instance:           ________________
TCP port:           ________________
```

## B. First PAV connection (host only)

- [ ] Publish/copy the new `PAV.Client.exe` (commit with dual-provider)
- [ ] Run PAV **once** on the host as admin — do not start other PCs yet
- [ ] Settings → Database = SqlServer → `THISPC\SQLEXPRESS` → Save / connect
- [ ] Database `PAVInventory` exists in SSMS (or sqlcmd)
- [ ] Tables exist: Users, Assets, AssetHistory, Locations, Categories, IpRanges, IpRecords, StockItems, StockMovements
- [ ] Sign-in / create administrator works
- [ ] Second Save / connect on the same host does not duplicate seed categories

If first connect fails, stop. Do not “fix” it by starting clients.

## C. SQLite → SQL Server copy

- [ ] SQLite `inventory.db` backed up (copy the file + Settings Backup now)
- [ ] Settings → Copy SQLite into SQL Server → folder that contains `inventory.db`
- [ ] Confirm replace
- [ ] Report says counts **and** relationships match
- [ ] Spot-check 5 assets: tag, serial, AssignedUserId, assigned name, history
- [ ] Spot-check 3 stock items: on-hand vs last movements
- [ ] Spot-check 3 IPs
- [ ] Original `inventory.db` still on disk and unchanged size/timestamp (or only timestamp from backup)
- [ ] Second copy **without** replace is refused (destination not empty)
- [ ] Users can sign in with the same PAV passwords as SQLite

## D. Client PCs (start with 2, then 3)

On each client:

- [ ] `database.json` next to the exe **or** Settings saved as SqlServer
- [ ] Shared folder / sqlite path is **not** required on login
- [ ] Sign in works
- [ ] Login, search, open an asset, stock, IP pages load

Then:

- [ ] Client 1 creates an asset
- [ ] Client 2 sees it after refresh
- [ ] Client 1 assigns an asset to a user; history shows the actor
- [ ] Client 2 unassigns; history is consistent
- [ ] Client 1 issues stock; on-hand drops; Client 2 sees new on-hand
- [ ] Client 1 assigns next free IP; Client 2 cannot get the same address
- [ ] Import/export still works (one person at a time for large imports)

## E. Concurrency (must pass before 5–6 users)

- [ ] Two users edit **different** assets → both saves succeed
- [ ] Two users open the **same** asset, A saves, B saves stale form → B gets conflict / refresh, A’s data remains
- [ ] Three saves at once (assign / stock issue / IP) → no crash, no duplicate IP, on-hand not negative
- [ ] Two users issue the last 1 unit → one succeeds, the other is told insufficient or refresh
- [ ] 2–3 clients idle overnight, morning login still works

## F. Failure / recovery

- [ ] Stop SQL Server service → PAV shows “Unable to connect to the PAV database server…” (not a raw stack)
- [ ] App does not hang forever
- [ ] Start SQL Server → restart PAV → works, data intact
- [ ] Restart the host PC → SQL service auto-start? If not, document who starts it
- [ ] Unplug client LAN briefly → error, then reconnect + retry works
- [ ] Block TCP 1433 on host firewall → same friendly error
- [ ] In-app snapshot backup creates a `.snapshot.db`
- [ ] Host `BACKUP DATABASE` to `.bak` **and** a test `RESTORE` to a copy database (not production) succeeds

## G. Expand to 5–6 users

Only after A–F.

- [ ] Team `database.json` next to the shared exe
- [ ] All 5–6 sign in
- [ ] Normal morning: assign laptop, issue mouse, allocate IP
- [ ] No daily “database is locked” (that was SQLite). SQL errors should be rare.
- [ ] Admin Backup snapshot + host `.bak` job scheduled

## H. Rollback drill (do this once in pilot, not on live data)

- [ ] Keep `inventory.db` aside
- [ ] Switch Provider back to SQLite, point at that file
- [ ] App opens, data is the **pre-migration** SQLite (SQL-only work is not in it)

---

## Stop / do not go live if

- First host `EnsureCreated` fails
- Migration report has relationship errors
- Two users can overwrite the same asset without a warning
- Two users can take the same IP
- Stock on-hand goes negative
- SQL is reachable from outside the LAN

## After a pass

Keep SQLite file as cold backup for a week. Then SQL Server is the only write path.
