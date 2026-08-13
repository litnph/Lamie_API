# Local Final Acceptance

## Overall result

ACCEPTED_WITH_WARNINGS

All Critical business flows passed against the real local Admin, API, filesystem storage, and SQL database `Lamie`. Warnings are limited to validation tooling/fixtures: the Admin project has no lint script, and a dedicated HTTP 403 case was not created because no safe least-privilege credential fixture exists. HTTP 400, 401, 404, and 409 were exercised.

## Environment

- Admin baseline commit: `598b1fe4b5277f91b6d13c5827641309f1ff3ec9`
- API baseline commit: `e59fbea9a014e6e6efc448ea0a097f797172bc70`
- Local integration changes: uncommitted and preserved in their respective repositories.
- API: Release build, Development environment, `http://127.0.0.1:5137`
- Admin: Vite, `http://localhost:5173`, process-only `VITE_API_BASE_URL=http://127.0.0.1:5137`
- Database: `LIT\SQLEXPRESS` / `Lamie`
- Migration: `20260810090000_AddOrderItemCardAndBanner`
- Final health: API live `200`, ready `200`, Swagger `200`, Admin `200`.
- The SQL session owned by the final API process reported database `Lamie`.

## Skills read

Project-local instructions were read before edits: Admin `SKILL.md` files for design-taste frontend, full-output enforcement, GPT taste, minimalist UI, and redesign guidance, plus `README.md`, `CODEX_REFACTOR_RULES.md`, `ADMIN_UI_REDESIGN.md`, architecture/module/RBAC guidance, and manual regression notes. No API `SKILL.md`, `AGENTS.md`, or `README.md` existed. Design/redesign guidance did not trigger a visual redesign; changes stayed within integration and defect scope.

## Admin/API contract

- Full matrix: `docs/LOCAL_ADMIN_API_CONTRACT_MATRIX.md`.
- All matrix entries are `RUNTIME_VERIFIED`.
- Product catalog image and Order illustration remain separate contracts.
- Recipient-first UI order did not swap recipient/orderer mappings.
- Create deposit now distinguishes omitted/null from explicit values.
- Product SKU is immutable on edit; the server accepts an omitted create SKU and generates a valid value.
- Existing Order Item images are retained on edit; new images append after existing sort order.

## Database migration

- Recovery backup created and verified before migration.
- Idempotent SQL generated and reviewed before apply.
- Migration added only Card/Banner columns to `sales_order_items`.
- Actual history, columns, types, defaults, and absence of new foreign keys were queried after apply.
- Full details: `docs/LOCAL_DATABASE_MIGRATION_REPORT.md`.

## Data migration

- No explicit DML backfill was required.
- Eleven legacy Order Item rows received additive `false` defaults and `NULL` messages through the schema migration.
- No legacy SKU or image was rewritten.
- Invalid Card/Banner rows: 0.
- Null/blank Product SKUs: 0; duplicate SKU groups: 0.
- Legacy examples `BOU-001` and `HOA-BO-9YJOZPR` remain present.

## Feature acceptance matrix

| Feature | Status | Evidence |
| --- | --- | --- |
| Card | PASS | Create, DB, Detail, Edit, Update, Detail; enabled message and disabled legacy/default cases verified; empty enabled Card rejected. |
| Banner | PASS | Create, DB, Detail, Edit, Update, Detail; enabled message and disabled legacy/default cases verified; empty enabled Banner rejected by the same validator path. |
| Recipient-first UI | PASS | Recipient `Nguyễn A` and orderer `Trần B` stayed distinct in UI, API, and DB. |
| Deposit default | PASS | Omitted deposit at `550000` resolved to `200000`; all requested boundaries pass automated tests. |
| Deposit manual override | PASS | Explicit `0` remained `0`; Quick Import `200000` survived quantity changes; edit override `350000` persisted. |
| Quick Text Import | PASS | Sample parsed and applied as 11/08/2026, 17:15–17:30, `550000`, ship `50000`, deposit `200000`, phone/address. |
| Chat Screenshot Attachment | PASS | Quick Import file reached form, multipart API, storage, DB, Detail, Edit and preview. |
| Create Order | PASS | UI success, HTTP 200, DB Order/Item/images, totals and detail verified. |
| Edit Order | PASS | Recipient, delivery, quantity, Card, Banner, deposit, retained image and appended image round-tripped. |
| Order Detail | PASS | Product/SKU/name, Card/Banner, images, price/quantity, contacts, delivery, deposit/payment fields rendered. |
| New SKU | PASS | Server generated `UML4` and `GY3T`; both match `^[A-Z0-9]{4}$` and are unique. |
| Legacy SKU | PASS | `BOU-001` and `HOA-BO-9YJOZPR` preserved. |
| SKU Search | PASS | Admin search found both `UML4` and a legacy SKU. |
| Product Watermark | PASS | Actual persisted `UML4` image visually shows one readable bottom-right watermark; no-upload update kept identical bytes/URL. |
| Order Attachment Not Watermarked | PASS | Saved Quick Import screenshot has identical SHA-256 to its source and no SKU watermark on visual inspection. |
| Image Preview | PASS | Detail attachment opened in the Admin lightbox and closed normally. |
| Admin/API contract | PASS | Static mismatches fixed and complete real round trip passed. |
| Database migration | PASS | Backup, apply, history, physical schema and no-pending checks passed. |
| Data migration | PASS | Safe defaults applied; no explicit mass rewrite required. |
| Regression | PASS | Full API and Admin suites passed; Order/Product list, search, filters, details, editor, image and legacy flows covered. |

## Runtime tests

Primary acceptance run: `TEST-LOCAL-20260811-233153`.

- UI Order: `L260811-62EE`, ID `166974a0-5877-430f-8f53-50fc33ce105c`.
- Final UI Order values: quantity `3`, subtotal `1650000`, shipping `50000`, total `1700000`, deposit `350000`, Card `TEST thiệp updated`, Banner `TEST banner updated`.
- Timezone: browser local `18:00–18:30` in `Asia/Ho_Chi_Minh` persisted as SQL UTC `11:00–11:30` and rehydrated to the same local values.
- Image retention: original attachment ID remained; new attachment appended; final sort orders are `0, 1`.
- Product thumbnail bytes differ from source because watermarking ran. Order screenshot bytes equal source because no catalog watermarking ran.
- Error contract: HTTP 400 invalid Card; 401 unauthenticated; 404 missing Order; 409 duplicate SKU. Admin-side invalid Card and localized business-rule error behavior are covered by Playwright regression.

## Admin validation

- lint: N/A — `package.json` defines no lint script.
- type-check: PASS — `tsc --noEmit`.
- test: PASS — 56 passed, 1 skipped. The skipped test is the opt-in DB-writing runtime test, which passed separately against `Lamie`.
- production build: PASS — Vite production build.

## API validation

- restore: PASS — all projects up to date.
- build: PASS — 0 warnings, 0 errors.
- test: PASS — 148 passed, 0 failed, 0 skipped.

## Issues discovered

1. New API tests did not import xUnit and could not compile.
2. One Product synchronization fixture contained a corrupt PNG CRC after watermark validation became real.
3. Migration-count regression expected the pre-Card/Banner migration list.
4. Admin omitted nested Order Item response images from its type.
5. Admin create API could not express an omitted deposit.
6. Edit upload sort order restarted at zero after retained images.
7. Admin allowed Product SKU edits while the API correctly treated SKU as immutable.
8. Explicit duplicate Product SKU used HTTP 400 instead of the documented 409.
9. ASP.NET implicit non-null validation rejected omitted create SKU before server generation ran.
10. Quick Import cleared the file input before its deferred state updater copied selected files.
11. Port `4175` was outside Development CORS; runtime Admin was moved to the configured `localhost:5173` origin without widening CORS.
12. Existing `master` credentials did not match the current bootstrap secret. No existing credential was changed; a prefixed test admin was created through the normal bootstrap service.

## Fixes applied

- Added missing xUnit imports and repaired the image fixture.
- Updated migration-count assertions and all requested deposit boundary coverage.
- Aligned Admin response images, optional deposit, edit image ordering, and SKU immutability.
- Changed duplicate SKU pre-check to `ConflictException` / HTTP 409.
- Made create SKU nullable at the HTTP contract and retained server generation/normalization in the handler.
- Snapshot Quick Import `File[]` before resetting the input and added regression coverage.
- Added an opt-in local Playwright configuration/test for real Admin/API/DB acceptance.
- Generated the contract matrix, idempotent migration SQL, migration report, and this report.

## Remaining risks

- No Admin lint command exists; type-check, build, and Playwright remain the available automated gates.
- A dedicated 403 runtime case was not generated because the local fixtures contain no safely usable least-privilege credential. Authorization policies and fail-closed behavior remain covered statically/unit-wise.
- The Development `master` password predates the current bootstrap secret. This does not affect the test account, but local credential ownership should be reconciled by the maintainer rather than reset automatically.
- Acceptance uses local filesystem storage and SQL Server Express; production object storage, TLS, secrets, and proxy/CORS settings still need deployment-environment validation.

## Test data created

- Admin user: `test_local_acceptance` (`test-local-acceptance@lamie.local`).
- Products: ID `4017` / SKU `UML4`; ID `4018` / SKU `GY3T`.
- Orders: IDs `410515fa-b157-4b37-92c5-a1e45bf24baf`, `6c9715de-4559-4480-af93-c4d25b472cd7`, and `166974a0-5877-430f-8f53-50fc33ce105c`.
- All retained business records use prefix `TEST-LOCAL-20260811-233153`.
- Failed iterative acceptance records were deleted through authenticated application APIs; no orphan rows remain.

## Manual checks still recommended

- Remove or disable the test admin and delete the three retained Orders/two Products when the team no longer needs the audit trail.
- Exercise a real least-privilege account to observe the Admin HTTP 403 message in the target deployment identity setup.
- Recheck watermark readability on the smallest production thumbnail variants and actual CDN/object-storage transformations.

## Production deployment considerations

- Take a production-grade backup and verify restore before applying the additive migration in any non-local environment.
- Review and execute the generated idempotent SQL or the equivalent EF migration through the normal deployment pipeline; do not copy the local `Lamie` database.
- Configure production connection strings, JWT/bootstrap secrets, CORS origins, and storage outside source control.
- Do not mass-convert legacy SKU values or mass-watermark legacy images.
- Keep Product SKU immutable after creation because it identifies watermarked catalog media and historical Order snapshots.
