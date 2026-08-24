# Stock / consumables

Separate from Inventory. Quantity items only. Issue/return use existing PAV users.

## Excel import order

1. **Consumable Stock Details 2026.xlsx** — current 2026 shelf (one row = one piece).
2. **Consumable Stock Details 2025 - Cleaned.xlsx** — leftover 2025 on-hand only.

| File | Import? |
|---|---|
| 2026 workbook | Yes — first |
| Cleaned 2025 (10 columns, Review Needed sheet) | Yes — second. Unissued rows only. Same models as 2026 are skipped. Monitors stay in Inventory. |
| Original 2025 (New Laptop / Printer list sheets) | No — rejected |

Do not import both leftover files. The cleaned 2025 ledger replaces `PAV-Stock-2025-Remaining.xlsx`.
