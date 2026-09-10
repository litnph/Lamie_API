# Ingredients, FE Data, and Content runbook

## Deployment order

1. Back up the target SQL Server database and verify that it can be restored.
2. Apply migration `20260817082016_AddIngredientPlanningAndContentPublishing`.
3. Deploy/start `API_Lamie`, then `Admin_Lamie`.
4. Export **FE Data** after product visibility and images are ready.
5. Build and deploy `FE_Lamie` with the generated `public/fe-data` directory.

The migration is additive for existing Order/Product data, but its `Down` removes the new ingredient snapshots, recipes, catalog flags, and Content tables. Use a database backup—not `Down`—as the recovery plan after users have created new data.

## Database migration

From `API_Lamie`:

```powershell
dotnet tool restore
dotnet restore Lamie.sln
dotnet ef database update --project Lamie.Infrastructure --startup-project Lamie.API
```

The API also applies pending migrations on startup when `Database:ApplyMigrationsOnStartup` is `true`. A controlled deployment can set `Database__ApplyMigrationsOnStartup=false` and run the explicit command above.

The migration adds:

- `md_measurement_units`, `md_ingredients`, `md_ingredient_conversions`;
- `rel_product_ingredients` and `sales_order_item_ingredient_snapshots`;
- `content_footer_settings`, `content_generations`, `content_items`, `content_assets`;
- `cat_products.is_visible_on_fe` and `sales_order_items.ingredient_snapshot_captured_at_utc`;
- indexes, permissions, and six Admin navigation routes.

Existing active products are backfilled to `is_visible_on_fe = 1`; newly created products default to `false`. This project deliberately uses logical EF relationships without physical SQL foreign keys, matching the existing database policy.

## Ingredient/report rules

- Recipe and conversion quantities use `decimal(18,6)`.
- Every conversion points directly to an ingredient's base unit; factors must be greater than one.
- An inactive unit/ingredient already referenced by a recipe can be retained unchanged during an edit, but cannot be newly selected or have its inactive reference quantity changed.
- Order-item snapshots are refreshed only when product or ordered quantity changes. Later recipe edits do not rewrite historical snapshots.
- Legacy order items with no snapshot marker intentionally use the current recipe and are labelled as fallback data.
- Demand report dates use `DeliveryAt` and Vietnam business days (`Asia/Ho_Chi_Minh`) with a half-open UTC range. Default statuses are Created and Producing; Cancelled is rejected.
- Conversion breakdown is an exact, deterministic decomposition: minimum base remainder, then minimum pack count, then larger factors. `totalBaseQuantity` is always retained.

## OpenAI Content configuration

The browser never receives an OpenAI credential. ChatGPT Plus does not provide API credentials or API billing.

For local development, prefer .NET user secrets:

```powershell
dotnet user-secrets set "OpenAIContent:ApiKey" "<your-api-key>" --project Lamie.API
dotnet user-secrets set "OpenAIContent:Model" "gpt-5.6" --project Lamie.API
dotnet run --project Lamie.API
```

For a deployment, inject configuration from the secret manager/environment:

```text
OpenAIContent__ApiKey=<secret>
OpenAIContent__Model=gpt-5.6
OpenAIContent__Endpoint=https://api.openai.com/v1/responses
OpenAIContent__TimeoutSeconds=60
OpenAIContent__MaximumRetries=2
OpenAIContent__MaximumImages=4
OpenAIContent__MaximumImageBytes=10485760
```

The provider uses the Responses API with strict JSON-schema output, `store=false`, bounded retries/timeouts, server-side parsing, and JPEG/PNG/WebP inputs only. With no key, all other modules remain available and Admin shows that Content generation is not configured.

Implementation references: [Responses structured outputs](https://developers.openai.com/api/docs/guides/structured-outputs), [image inputs](https://developers.openai.com/api/docs/guides/images-vision), and [latest model guidance](https://developers.openai.com/api/docs/guides/latest-model).

Generation is limited to four concurrent server requests with a bounded queue wait. An `Idempotency-Key` is normalized, fingerprinted, and protected by a SQL Server application lock; reusing it with different input returns conflict. Saved edits use a unique content fingerprint so retrying the same edit does not create duplicate versions.

Generated drafts and content-owned image copies are persisted under public storage because they are marketing assets that Admin must preview/copy. The current release does not automatically expire abandoned drafts. Before accepting private/customer-sensitive uploads in production, define consent and retention, add scheduled draft/file reconciliation, or move Content assets to private/signed storage.

## FE Data export

Default local configuration resolves relative to `Lamie.API`:

```json
{
  "FeDataExport": {
    "AllowedRoot": "../../FE_Lamie/public",
    "TargetDirectory": "../../FE_Lamie/public/fe-data"
  }
}
```

The target must be a dedicated directory named `fe-data` strictly below `AllowedRoot`. The endpoint never accepts a path from the browser. Export uses a repeatable-read database snapshot, writes and validates staging files, then swaps the whole directory. It preserves the old catalog on validation/publish failure and never deletes siblings outside `fe-data`.

Policy:

- export only active products with `IsVisibleOnFE=true`;
- products with no referenced image are exported with a fallback warning;
- any referenced image that is missing, corrupt, external, or unsupported fails the complete export;
- slugs are ASCII and unique; images use stable content hashes;
- recipes/ingredients and other Admin-only fields are never exported.

For Docker/shared storage, the image defaults are:

```text
FeDataExport__AllowedRoot=/app/data
FeDataExport__TargetDirectory=/app/data/fe-data
LocalStorage__RootPath=/app/data/uploads
```

Mount a persistent/shared host directory at `/app/data`; make its `fe-data` output available as `FE_Lamie/public/fe-data` before the FE build. If API and FE build workers do not share a filesystem, direct export cannot update a sibling deployment. A ZIP/download or deployment artifact flow is intentionally not implemented because it changes the **FE Data** workflow and the feature brief requires confirmation before adding that fallback.

## FE build and offline smoke test

From `FE_Lamie`, after export:

```powershell
npm install
npm run lint
npm run typecheck
npm test
npm run build
npm run preview
```

Confirm that `dist/fe-data/manifest.json`, `dist/fe-data/products.json`, and `dist/fe-data/images/products` exist. Stop `API_Lamie`, serve `dist`, and load the home, shop, category/filter, and product-detail views. Catalog requests must stay on the FE origin; missing files, invalid schema, empty lists, and broken images must render their explicit UI states.

## Full verification commands

```powershell
# API_Lamie
dotnet restore Lamie.sln
dotnet build Lamie.sln -c Release
dotnet test Lamie.Tests/Lamie.Tests.csproj -c Release --no-build
dotnet ef migrations has-pending-model-changes --project Lamie.Infrastructure --startup-project Lamie.API --no-build

# Admin_Lamie
npm run typecheck
npm run test:e2e
npm run build

# FE_Lamie
npm run lint
npm run typecheck
npm test
npm run build
```
