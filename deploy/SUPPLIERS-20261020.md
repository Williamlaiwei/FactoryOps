# Supplier management and dashboard risk labels

Released to the existing demo on 20 October 2026 as `suppliers-20261020`.

The Dashboard product picker shows red text and an English reason for projected
shortages, empty stock, reorder levels, overdue receipts, uncertain holds and
dependence on planned receipts. For example, 6 on hand + 5 planned - 8 held is
marked `Depends on incoming stock` until the receipt is actually received. Normal
and untracked products without commitments are not marked as known shortages.
The picker supports mouse, keyboard and mobile, with a native select fallback.

## Suppliers

- Open Inventory > Suppliers to create/edit company-scoped supplier details.
- A product can link to multiple suppliers and have at most one default.
- Schedule Replenishment offers `Manage product suppliers / default` and
  `Edit selected supplier`. Selecting a product loads its active suppliers and
  selects its default. Another supplier or manual entry can be selected.
- Changing a default affects new replenishments. Existing schedules retain the
  recorded supplier name, including after a rename, deactivation or unlink.
- Deactivation removes a supplier from defaults/new choices, while existing
  schedules can still be rescheduled without inventing a replacement supplier.

The additive, repeatable SQL script `009-product-suppliers.sql` creates Suppliers
and ProductSuppliers, and adds nullable Replenishments.SupplierId. Existing
free-text supplier values are preserved. No supplier records are seeded.

## Validation and deployment

- .NET build: no errors or warnings.
- `python tests/order_editing.py --suppliers --browser`: 46 HTTP/SQL cases passed
  (37 inventory regressions + 9 new cases), plus inventory and supplier browser flows.
- Checks cover defaults, alternatives, company separation, stale forms, failure
  rollback, CSV roundtrip, stock/receipt/dispatch behavior and red mobile options.
- Both databases and server settings were backed up before updating the cloud.
  Existing table fingerprints matched after the additive migration, excluding
  its new schema marker and nullable supplier column.
- HTTPS Dashboard, supplier pages/endpoints, replenishments and new static assets
  passed live read-only checks. The live Dashboard showed 2 risk products out of
  16 at verification. Authentication and the crawler remained active.
- Server update metadata: `/opt/factoryops/suppliers-update.json`; contains the
  backup directory, release tag and preservation checks, without credentials.

Local screenshots: `artifacts/dashboard-risk-dropdown.png` and
`artifacts/dashboard-risk-dropdown-mobile.png` (isolated test fixtures).
