# Stock / consumables

Separate from Inventory. Quantity items only. Issue/return use existing PAV users.

## Excel (do not merge 2025 + 2026)

| Workbook | Sheet | What it is | Import? |
|---|---|---|---|
| **2026** Sheet1 | One row = one physical piece currently on the shelf (168 rows, 15 products). One already issued (KM7321W → CGM Sandeep Verma). | **Yes — opening stock** |
| 2025 Sheet1 | Historical issue ledger (425 rows, many “Provided to user”) | No |
| 2025 New Laptop | Serialized laptops with serials | No — Inventory |
| 2025 Printer list | Serialized printers | No — Inventory |
| 2025 Printer catrage | Toner qty + messy issue notes | No |
| 2025 Sheet2 | Older remaining/utilized summary | No — would double-count |
| 2025 Sheet3 / Sheet5 | Purchase / requirement / price | No |

## 2026 opening (after import)

Opening 168 units − 1 already issued = **167 on hand**. Re-import is refused (would double-count).

Ambiguous (imported as stock, flagged for review): Dell 22" E2225HSM monitors (no serials in 2026), Brother P-Touch label machine (qty 1, no serial).
