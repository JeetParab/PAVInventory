# Stock / consumables

Separate from Inventory. Quantity items only. Issue/return use existing PAV users.

## Excel import order

1. **Consumable Stock Details 2026.xlsx** — current opening (one row = one piece).
2. **Consumable Stock Details 2025 - Cleaned.xlsx** — leftover 2025 on-hand only.

| File | Detected as | Import? |
|---|---|---|
| 2026 workbook (filename / unit sheet) | `2026-unit-list` | Yes — first |
| Cleaned 2025 (`Review Needed` / `Read Me` sheets) | `2025-cleaned-ledger` | Yes — leftover on-hand only |
| OpeningStock sheet | `summary-opening` | Yes — Name + Quantity |
| Original 2025 (`New Laptop` / Printer sheets) | `2025-historical` | No |

Detection uses filename and sheet names, not “how many rows are issued”.

Product identity: Name + Manufacturer + Model. Same generic name with different models can coexist.

On-hand = Opening + Received + Returned + Adjustment − Issued.
Opening is not counted as Received.

Admin: **Check integrity** compares stored OnHand to the movement ledger. It does not change stock.
