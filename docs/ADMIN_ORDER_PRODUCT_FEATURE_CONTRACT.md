# Admin Order and Product API contract

This document is the implementation contract for the Lamie Admin client. JSON responses use ASP.NET Core's camel-case convention. Order and product write endpoints require the corresponding `orders.manage` or `products.manage` permission; reads require the view permission.

## Create an order

`POST /api/orders`, `multipart/form-data`

Top-level fields:

| Field | Type | Required / behavior |
|---|---|---|
| `ordererName` | string | Required |
| `ordererPhone` | string? | Optional; used for customer matching |
| `channelId` | UUID? | Optional; defaults to Admin channel |
| `recipientName` | string | Required |
| `recipientPhone` | string | Required |
| `pickupAtShop` | boolean | Required, defaults false |
| `provinceShipping` | boolean | Must not be true with `pickupAtShop` |
| `deliveryAddress` | string? | Recipient address; cleared for shop pickup |
| `deliveryAddressDescription` | string? | Optional directions |
| `deliveryLatitude`, `deliveryLongitude` | decimal? | Optional pair |
| `deliveryAt` | ISO-8601 datetime offset | Delivery window start |
| `deliveryTo` | ISO-8601 datetime offset? | Optional end, not before start |
| `depositAmount` | decimal? | Explicit override or omit for default calculation |
| `shippingFee` | decimal | Expected shipping fee; backend includes it in total |
| `description`, `contentNote` | string? | Optional |
| `items` | array | At least one; form keys such as `items[0].productId` |
| `images` | array | New attachment files; form keys described below |

Each `items[n]` accepts:

| Field | Type | Behavior |
|---|---|---|
| `id` | UUID string? | Omit on create |
| `productId` | positive integer string? | Catalog product reference |
| `productSku` | string? | Catalog lookup or manual-item snapshot; legacy SKU lookup remains supported |
| `productName` | string | Required for a manual item |
| `unitPrice` | decimal | Admin-entered product price; cannot be negative; catalog price is used when zero |
| `quantity` | integer | Must be greater than zero |
| `note` | string? | Item note |
| `hasCard` | boolean | Whether this individual item has a card |
| `cardMessage` | string? | Required and trimmed when `hasCard=true`; otherwise persisted as null |
| `hasBanner` | boolean | Whether this individual item has a banner |
| `bannerMessage` | string? | Required and trimmed when `hasBanner=true`; otherwise persisted as null |

Each `images[n]` accepts `imageFile` (JPG/PNG/WEBP/GIF, maximum 10 MB), `orderItemIndex` (zero-based index in `items`) and non-negative `sortOrder`. The API stores these under the order, linked to the resulting OrderItem. These are immutable order snapshots, are **not** product catalog images, and are never SKU-watermarked.

## Update an order

`PUT /api/orders/{id}`, `multipart/form-data`

The update fields are the same except that `id`, `rowVersion`, and a non-null `channelId` are supported, `depositAmount` is required under the existing update contract, and `shippingFeeActual` is optional. Send each existing `items[n].id` to retain/update that item. Omitting an existing item means delete it and its attachments. When an existing item ID is retained, its existing images remain even when no new `images` files are sent. New files are appended and linked by `orderItemIndex`.

There is currently no independent attachment-delete operation: deleting the corresponding item deletes its attachments. Detail responses expose attachments both in each `items[n].images` collection and in the backward-compatible top-level `images` collection.

## Order response

`GET /api/orders/{id}` and create/update responses return totals calculated by the backend. The client must not supply or trust `subTotal`, `lineTotal`, or `totalAmount`. Each item contains `id`, product snapshot fields, `unitPrice`, `quantity`, `lineTotal`, `note`, `hasCard`, `cardMessage`, `hasBanner`, `bannerMessage`, and `images`. Each image contains `id`, `orderItemId`, `imageUrl`, `sortOrder`, and `description`.

## Default deposit

Only create applies a default when `depositAmount` is omitted/null. The order value is the server-calculated item value after line discounts plus effective expected shipping fee.

* Up to 400,000: 100,000.
* 400,000.01 through 700,000: 200,000.
* 700,000.01 through 1,000,000: 300,000.
* Above 1,000,000: one third, rounded to the nearest 100,000 with midpoint away from zero.

An explicitly supplied value (including zero) is never replaced. Deposits cannot be negative or greater than the final order total.

## Product contract and SKU

`POST /api/settings/products` and `PUT /api/settings/products/{id}` use `multipart/form-data`. Product detail (`GET /api/settings/products/{id}`) returns `id`, `sku`, `price`, `salePrice`, inventory/category/type fields, `thumbnailUrl`, translations (which contain the localized `name`), and `images` (`id`, `imageUrl`, `isActive`, `sortOrder`).

For a new product, `sku` may be omitted/blank to generate a value, or supplied as exactly four uppercase ASCII letters/digits (`^[A-Z0-9]{4}$`). Supplied SKU is normalized to uppercase and rejected when duplicated. Generation checks uniqueness, retries collisions at most 20 times, and relies on the existing unique database index as the final concurrency guard. Existing long/prefixed SKUs are not migrated and remain readable/searchable. SKU is immutable on product update, preventing stale watermarks.

### Product image upload and watermark

Use `thumbnailFile` and/or `images[n].imageFile`, with optional `images[n].sortOrder`. Uploaded source files are decoded in the API, watermarked once with the product SKU at bottom-right on a semi-transparent dark panel, and encoded at the original media type (JPEG quality 90). The dimensions/aspect ratio are unchanged. New uploaded thumbnail and additional images are processed; already stored image URLs are not reprocessed, which avoids stacked watermarks. Decode/processing failure aborts persistence and uploaded-file cleanup is best effort.

URL-only `thumbnailUrl` / `images[n].imageUrl` values are retained for backward compatibility but cannot be modified by the API because their bytes are not uploaded; Admin should upload files when guaranteed watermarking is required.

## Errors

Errors use this shape:

```json
{
  "success": false,
  "code": "VALIDATION_ERROR",
  "message": "Validation failed",
  "errors": { "field": ["message"] }
}
```

Business validation normally returns HTTP 400 (`VALIDATION_ERROR` or `BUSINESS_RULE_VIOLATION`), missing resources return 404, uniqueness/concurrency conflicts return 409, authorization failures return 401/403, and unexpected image/storage failures return 500 with no internal details.
