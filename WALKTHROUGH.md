# Code Walkthrough — Lakbay.Cms

Umbraco 18, headless (ADR-0006: zero Razor presentation - `Lakbay.Web` owns
100% of rendering). This walks through what actually happens from app
startup through an editor publishing a Product, since that's the one flow
that touches nearly every file in this repo. See the platform-level
`Lakbay.Docs/WALKTHROUGH.md` for how this fits with the other repos.

## Two trees, one Umbraco backoffice

- **Products tree** (`Catalog/`) - the holiday catalog: ProductLine,
  Country, Region, Destination, Accommodation, RoomType, Product,
  Activity. This is what `Lakbay.AvailabilityApi` stays in sync with.
- **Content tree** (`Content/`) - marketing pages: Home + landing pages
  per ProductLine/Region/Destination. Deliberately kept **independent**
  of the Products tree (plain text fields, not Content Pickers to
  Products-tree nodes) - see `ContentTreeSeeder.cs`'s own comment for the
  ordering-hazard reason.

Both trees are created **code-first**, not by hand in the backoffice - see
`CatalogContentTypeSeeder.cs`/`ContentTreeSeeder.cs`, both
`INotificationAsyncHandler<UmbracoApplicationStartedNotification>`
implementations wired up in `Catalog/CatalogComposer.cs` and
`Content/ContentComposer.cs`.

## Startup sequence

1. **`Program.cs`** builds the Umbraco pipeline
   (`AddBackOffice/AddWebsite/AddDeliveryApi/AddComposers`). `AddComposers()`
   is the real entry point for everything below - it auto-discovers every
   `IComposer` in this assembly.
2. **`Catalog/CatalogComposer.cs`** and **`Content/ContentComposer.cs`**
   each register their own notification handlers. `CmsSchemaBuilder`
   (`Shared/CmsSchemaBuilder.cs`) is registered once, scoped, and shared by
   both seeders - the one utility for creating Umbraco content
   types/properties in code rather than through backoffice UI clicks.
3. On `UmbracoApplicationStartedNotification`, `CatalogContentTypeSeeder`
   and `ContentTreeSeeder` both run (concurrently - Umbraco runs every
   handler via `Task.WhenAll`, a real ordering constraint documented in
   `ContentTreeSeeder`'s own comment). Each is idempotent: create the
   content types/seed content if missing, no-op on every later boot.

## Following "an editor publishes a Product" end to end

1. **The editor clicks Publish** in the Umbraco backoffice on a Product
   node. This fires a real `ContentPublishedNotification` - the same
   event a human backoffice edit and this repo's own programmatic seeding
   both go through (there's no separate "seed path" vs. "real edit path").
2. **`Catalog/CatalogPublishSyncHandler.cs`** handles it. `entity.ContentType.Alias`
   is switched on to figure out which catalog type just published (see
   `CatalogContentTypeSeeder`'s `*Alias` constants), then builds the
   matching `Lakbay.Contracts` record - `BuildProductEvent` for a Product,
   walking its Content Picker fields (`destination`, `accommodation`) via
   `ResolvePickedContent`, which always resolves to the picked node's
   **published** state, never a possibly-unpublished draft.
3. **`Catalog/ServiceBusCatalogSyncPublisher.cs`** serializes the resulting
   `CatalogSyncEvent` to JSON and sends it to the `lakbay-catalog-sync`
   Azure Service Bus queue - the connection string comes from
   `dotnet user-secrets` (`ConnectionStrings:ServiceBus`), never
   `appsettings.json`.
4. From here, this repo's job is done. `Lakbay.AvailabilityApi.Sync`
   (a different repo) picks the message up from the queue and applies it
   to MongoDB - see that repo's own `WALKTHROUGH.md`.

## A gotcha worth knowing before touching `CatalogPublishSyncHandler.cs`

Content Picker properties store the picked node's Umbraco UDI string
(`umb://document/...`), not a usable value directly - `ResolveProductLineCode`'s
own comment documents finding this "only by actually publishing and
watching it fail." Every picker field in this handler goes through
`ResolvePickedContent` for exactly this reason; reading a picker property
with a plain `entity.GetValue<string>(...)` will hand you the raw UDI, not
the thing it points to.

## Delivery API - how `Lakbay.Web` reads the Content tree

`Program.cs`'s `.AddDeliveryApi()` plus `Umbraco:CMS:DeliveryApi:Enabled`
in `appsettings.Development.json` together expose both trees as a REST
API (`/umbraco/delivery/api/v2/...`). CORS (also wired in `Program.cs`,
config-driven via `Cors:AllowedOrigins`) is what lets `Lakbay.Web` call it
cross-origin in local dev.
