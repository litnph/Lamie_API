# Local Admin/API Contract Matrix

Audit scope: Admin baseline commit `598b1fe` against API baseline commit `e59fbea`, including the local integration fixes listed below. The wire format for order and product writes is `multipart/form-data`; the "Request JSON" column names the equivalent request property/form key. Source code, not the Cloud contract document, is the implementation truth. Runtime verification used Admin `http://localhost:5173`, API `http://127.0.0.1:5137`, SQL database `Lamie`, and acceptance run `TEST-LOCAL-20260811-233153`.

| Feature | Admin field | Admin type | Request JSON | API DTO | Response JSON | Admin mapping | Status |
| --- | --- | --- | --- | --- | --- | --- | --- |
| Order Item Card flag | `item.hasCard` | `boolean` | `items[n].hasCard` | `OrderLineRequest.HasCard: bool` | `items[n].hasCard: boolean` | Edit hydration, checkbox, create/update form mapping, detail render | RUNTIME_VERIFIED |
| Order Item Card message | `item.cardMessage` | `string \| null \| undefined` | `items[n].cardMessage` | `OrderLineRequest.CardMessage: string?` | `items[n].cardMessage: string?` | Required when enabled; trimmed; cleared when disabled | RUNTIME_VERIFIED |
| Order Item Banner flag | `item.hasBanner` | `boolean` | `items[n].hasBanner` | `OrderLineRequest.HasBanner: bool` | `items[n].hasBanner: boolean` | Edit hydration, checkbox, create/update form mapping, detail render | RUNTIME_VERIFIED |
| Order Item Banner message | `item.bannerMessage` | `string \| null \| undefined` | `items[n].bannerMessage` | `OrderLineRequest.BannerMessage: string?` | `items[n].bannerMessage: string?` | Required when enabled; trimmed; cleared when disabled | RUNTIME_VERIFIED |
| Order Item images (new) | `item.imageFiles` | `File[]` | `images[n].imageFile`, `orderItemIndex`, `sortOrder` | `CreateOrderImageForm` | top-level `images[]` and item `images[]` | Quick Import and line uploader feed the same upload list | RUNTIME_VERIFIED |
| Order Item images (existing) | `item.existingImages` | `OrderImageDto[]` | Existing item `id`; no file resend | Existing image aggregate retained for retained item IDs | top-level `images[]` and item `images[]` | Hydrates by `orderItemId`; previews existing images; sends retained item ID | RUNTIME_VERIFIED |
| Order Item image response | `item.images` | `OrderImageDto[] \| undefined` | n/a | `OrderItemDto.Images` | `items[n].images[]` | Nested response is typed; top-level compatibility mapping remains in use | RUNTIME_VERIFIED |
| New image append order | `imageFiles` index | `number` | `images[n].sortOrder` | `CreateOrderImageForm.SortOrder: int` | sorted by `sortOrder` | New edit uploads start after the retained image count | RUNTIME_VERIFIED |
| Product image | `thumbnailUrl`, product `images[]` | URL strings | product `thumbnailFile` / `images[n].imageFile` | `CreateProductCommand` / `UpdateProductCommand` | product `thumbnailUrl`, `images[]` | Product selector/detail use catalog thumbnail independently from order illustrations | RUNTIME_VERIFIED |
| Recipient name | `recipientName` | `string` | `recipientName` | `CreateOrderForm.RecipientName: string` | `recipientName: string` | Recipient fields render first but map to recipient properties | RUNTIME_VERIFIED |
| Recipient phone | `recipientPhone` | `string` | `recipientPhone` | `CreateOrderForm.RecipientPhone: string` | `recipientPhone: string` | Quick Import phone and normal editor both map to recipient | RUNTIME_VERIFIED |
| Orderer name | `ordererName` | `string` | `ordererName` | `CreateOrderForm.OrdererName: string` | `ordererName: string` | UI renders after recipient; mapping remains orderer | RUNTIME_VERIFIED |
| Orderer phone | `ordererPhone` | `string` | `ordererPhone` | `CreateOrderForm.OrdererPhone: string?` | `ordererPhone: string` | Empty value is submitted and normalized to empty string | RUNTIME_VERIFIED |
| Delivery start | `deliveryAt` | local `datetime-local` string | `deliveryAt` ISO-8601 UTC string | `DateTimeOffset DeliveryAt` | `deliveryAt` UTC offset string | Local input -> `toISOString()` -> UTC DB -> browser local input | RUNTIME_VERIFIED |
| Delivery end | `deliveryTo` | local string or empty | `deliveryTo` ISO-8601 UTC or omitted | `DateTimeOffset? DeliveryTo` | `deliveryTo: string?` | Range validation prevents end before start; exact mode omits end | RUNTIME_VERIFIED |
| Delivery address | `deliveryAddress` | `string` | `deliveryAddress` | `string?` | `deliveryAddress: string?` | Cleared/omitted for shop pickup; otherwise round-trips | RUNTIME_VERIFIED |
| Unit price | `item.unitPrice` | `number` | `items[n].unitPrice` | `decimal UnitPrice` | `unitPrice: number` | Positive in Admin; API allows catalog zero fallback and rejects negative | RUNTIME_VERIFIED |
| Quantity | `item.quantity` | `number` | `items[n].quantity` | `int Quantity` | `quantity: number` | Admin requires positive integer; domain requires greater than zero | RUNTIME_VERIFIED |
| Shipping fee | `shippingFee` | `number` | `shippingFee` | `decimal ShippingFee` | `shippingFee: number` | Included once in server total; forced to zero for pickup | RUNTIME_VERIFIED |
| Deposit explicit | `depositAmount` | `number` | editor appends the explicit value | create `decimal?`; update `decimal` | `depositAmount: number` | Manual edit stops automatic recalculation and explicit zero is preserved | RUNTIME_VERIFIED |
| Deposit omitted/default | optional Admin API payload | `number \| null \| undefined` | omitted/null on create | `CreateOrderForm.DepositAmount: decimal?` | resolved numeric deposit | API client omits nullish deposit; editor still submits its visible suggestion explicitly | RUNTIME_VERIFIED |
| Order subtotal | response only | `number` | not submitted | server/domain calculated | `subTotal: number` | Detail/list display response | RUNTIME_VERIFIED |
| Order total | response only | `number` | not submitted | server/domain calculated | `totalAmount: number` | Detail/list display response; includes effective shipping fee | RUNTIME_VERIFIED |
| Product ID | `product.id`; order `productId` | product `number`; order form `string` | `items[n].productId` positive integer text | `OrderLineRequest.ProductId: string?` | order item `productId: string?` | String conversion is explicit at form boundary | RUNTIME_VERIFIED |
| Product SKU snapshot | `productSku` | `string?` | `items[n].productSku` | `OrderLineRequest.ProductSku: string?` | `items[n].productSku: string?` | Selector and detail preserve snapshot SKU | RUNTIME_VERIFIED |
| Product name snapshot | `productName` | `string` | `items[n].productName` | `OrderLineRequest.ProductName: string` | `items[n].productName: string` | Catalog Vietnamese translation or manual name becomes immutable order snapshot | RUNTIME_VERIFIED |
| Product thumbnail snapshot | `snapshotThumbnailUrl` / `thumbnailUrl` | `string?` | not trusted as a write field for catalog items | resolved from Product entity | `items[n].thumbnailUrl: string?` | Rendered separately from illustration/chat screenshots | RUNTIME_VERIFIED |
| Legacy SKU | `ProductDto.sku` | `string` | unchanged on edit | existing `Product.Sku` max 100 | unchanged string | Product list/search and order selector do not assume length four | RUNTIME_VERIFIED |
| New 4-character SKU | `form.sku` | uppercase alphanumeric `string` | product `sku` optional on create | `CreateProductCommand.Sku: string?` | product `sku: string` | Admin validates explicit values; API generates when omitted, normalizes, validates, and checks uniqueness | RUNTIME_VERIFIED |
| SKU immutability on update | disabled `form.sku` | preserved string | original product `sku` | API rejects a changed SKU defensively | unchanged `sku` | Admin edit disables SKU and preserves legacy/new values | RUNTIME_VERIFIED |
| Duplicate SKU error | create form error surface | API message string | duplicate product `sku` | pre-check throws `ConflictException` | HTTP 409 | Admin displays server message without crashing | RUNTIME_VERIFIED |
| Quick Text Import batch queue | `QuickOrderDraft[]` | local drafts with `clientDraftId` and `File` references | `orders[n].*` multipart fields | `BatchCreateOrdersForm` reuses `CreateOrderForm` | `createdCount`, mapped `clientDraftId/orderId/orderNumber` | Parse -> preview -> Apply queues only; Save All calls one all-or-nothing endpoint; screenshots remain scoped to each draft | RUNTIME_VERIFIED |

## Static findings before fixes

1. Add the nested `images` response property to the Admin `OrderItemDto` and retain the top-level compatibility mapping.
2. Make the Admin create API contract capable of omitting/nulling `depositAmount`; the editor may still submit its visible calculated suggestion explicitly.
3. Start new image sort order after the retained image count during order edit.
4. Make SKU immutable in the Admin edit form to match the API and preserve legacy values.
5. Return HTTP 409 for an explicitly duplicated SKU, matching the documented uniqueness contract.
6. API baseline tests cannot compile until the two new test files import xUnit.

## Runtime evidence

- Create/Edit/Detail round trip: Order `L260811-62EE` (`166974a0-5877-430f-8f53-50fc33ce105c`).
- Recipient and orderer remained distinct through UI, API, and DB.
- Local `18:00–18:30` became UTC `11:00–11:30` in SQL and mapped back to local inputs without drift.
- Retained illustration remained at `sortOrder = 0`; the edit upload was appended at `sortOrder = 1`.
- Default deposit with an omitted request value resolved to `200000`; explicit zero remained `0`; edited manual deposit remained `350000` after quantity changes.
- Product SKUs `UML4` and `GY3T` were generated server-side, passed the four-character uppercase-alphanumeric rule, and were unique. Duplicate `UML4` returned HTTP 409.
- Product watermark `UML4` was visually verified bottom-right. The Quick Import screenshot retained the source SHA-256 and had no SKU watermark.
- Batch run `TEST-BATCH-20260812-231624` used Admin `http://localhost:5173`, API `http://127.0.0.1:5137`, and SQL database `Lamie_Dev`.
- The UI queued three drafts, deleted and re-added draft #2, then created exactly three orders in one request. Image counts remained isolated at `0`, `1`, and `1` before the normal edit-flow regression appended a second image to order #3.
- A three-order request with an invalid product at index `1` returned the matching `clientDraftId`; querying immediately afterward found zero matching orders. Retrying after omitting the invalid entry created exactly two orders.
