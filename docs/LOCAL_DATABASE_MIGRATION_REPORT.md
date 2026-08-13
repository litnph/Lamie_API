# Local Database Migration Report

## Result

PASS — the additive EF migration was reviewed, backed up, applied to the verified local Development database `Lamie`, and validated against the physical schema and retained data.

## Target verification

- Server: `LIT\SQLEXPRESS`
- Database: `Lamie`
- Effective Development connection source: `Lamie.API/appsettings.Development.json`
- Effective target (redacted): `Server=.\SQLEXPRESS;Database=Lamie;Trusted_Connection=True;...`
- Safety gate: both the configured catalog and `SELECT DB_NAME()` returned exactly `Lamie` before migration and before every later cleanup write.
- No password or token is recorded in this report.

## Recovery point

- Backup: `C:\Program Files\Microsoft SQL Server\MSSQL16.SQLEXPRESS\MSSQL\Backup\Lamie_pre_20260810090000_20260811_230256.bak`
- Method: full `COPY_ONLY`, `INIT`, `CHECKSUM` backup.
- Verification: `RESTORE VERIFYONLY ... WITH CHECKSUM` passed.
- SQL Server Express rejected backup compression, so the verified recovery point is uncompressed.
- Rollback options:
  1. Preferred full recovery: restore the verified backup.
  2. Schema-only rollback: migrate to `20260804041956_SeedDefaultAdminNavigation`; the migration `Down` drops only the four new columns. This discards Card/Banner values written after deployment and therefore is not the preferred recovery path after runtime use.

## Migration review

- Context: `AppDbContext`
- Project: `Lamie.Infrastructure/Lamie.Infrastructure.csproj`
- Startup project: `Lamie.API/Lamie.API.csproj`
- Migration reviewed and applied: `20260810090000_AddOrderItemCardAndBanner`
- Idempotent review script: `docs/LOCAL_ADD_ORDER_ITEM_CARD_BANNER_IDEMPOTENT.sql`
- Migration before: `20260804041956_SeedDefaultAdminNavigation`
- Database history rows before: 18, including the legacy compatibility entry `20260731102859_AddOrderDeliveryAddressDescription`.
- Database history rows after: 19.
- Migration after: `20260810090000_AddOrderItemCardAndBanner`, EF product version `8.0.23`.

The generated SQL and migration source were additive. They did not drop/recreate Order or Product tables, truncate data, rewrite legacy SKUs, create indexes, or create physical foreign keys.

## Schema changes

Four columns were added to `dbo.sales_order_items`:

| Column | SQL type | Nullable | Default |
| --- | --- | --- | --- |
| `has_card` | `bit` | No | `0` |
| `card_message` | `nvarchar(1000)` | Yes | None |
| `has_banner` | `bit` | No | `0` |
| `banner_message` | `nvarchar(1000)` | Yes | None |

Physical metadata validation confirmed all four columns and defaults. `__EFMigrationsHistory` contains the applied migration. A connected EF migration list showed no pending migration.

## Data migration and backfill

- Explicit DML backfill: 0 rows. No separate update was necessary.
- Existing `sales_order_items` at migration time: 11.
- All 11 legacy rows received the safe DDL defaults `has_card = 0` and `has_banner = 0`; both message columns remained `NULL`.
- Invalid Card/Banner combinations after acceptance: 0.
- No Product image or SKU data was mass-processed.
- Legacy SKU count before acceptance: 11; legacy examples `BOU-001` and `HOA-BO-9YJOZPR` remain unchanged and searchable.
- Current Product rows after retaining two acceptance products: 13; null/blank SKU rows: 0; duplicate SKU groups: 0.

## Foreign-key and integrity review

- Physical foreign keys before/after: 0, matching current project policy.
- New physical foreign keys created by this migration: 0.
- Orphan Order Item rows after test cleanup: 0.
- Orphan Order Image rows after test cleanup: 0.
- No production or non-`Lamie` database was contacted or modified.

## Acceptance data retained

Final run prefix: `TEST-LOCAL-20260811-233153`.

- Test admin: `test_local_acceptance` / `test-local-acceptance@lamie.local` (created through the existing bootstrap hosted service; password not recorded).
- Products:
  - ID `4017`, SKU `UML4`, watermarked thumbnail.
  - ID `4018`, SKU `GY3T`, uniqueness/control product.
- Orders:
  - `410515fa-b157-4b37-92c5-a1e45bf24baf` — omitted deposit resolved to `200000`.
  - `6c9715de-4559-4480-af93-c4d25b472cd7` — explicit deposit `0` remained `0`.
  - `166974a0-5877-430f-8f53-50fc33ce105c`, code `L260811-62EE` — UI Quick Import/Create/Edit/Detail acceptance order.

Iterative failed-run data was identified only by the `TEST-LOCAL` prefix and removed through authenticated application APIs: 19 failed-run Orders and 14 failed-run Products. The final retained counts are exactly three Orders, two Products, and one test admin. The cleanup left no orphan rows.

## Final status

- Database name verified: PASS
- Recovery point: PASS
- Migration source/SQL review: PASS
- Migration apply: PASS
- Physical schema: PASS
- Required legacy defaults: PASS
- Legacy data preservation: PASS
- FK policy: PASS
- Data integrity: PASS
