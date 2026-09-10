using Lakbay.Cms.Web.Shared;
using Umbraco.Cms.Core.Composing;
using Umbraco.Cms.Core.DependencyInjection;
using Umbraco.Cms.Core.Notifications;

namespace Lakbay.Cms.Web.Catalog;

/// <summary>
/// Wires up Phase 3's Products-tree seeding and the ADR-0013 Cms-&gt;
/// AvailabilityApi sync publisher. Auto-discovered via Program.cs's
/// existing AddComposers() call.
/// </summary>
public sealed class CatalogComposer : IComposer
{
    public void Compose(IUmbracoBuilder builder)
    {
        // Scoped, not singleton — it depends on Umbraco's own core
        // services (IContentTypeService etc.), which are scoped; a
        // singleton here would capture them past their real lifetime.
        // Shared with Content.ContentTreeSeeder (registered once here so
        // there's exactly one registration to reason about).
        builder.Services.AddScoped<CmsSchemaBuilder>();

        builder.AddNotificationAsyncHandler<UmbracoApplicationStartedNotification, CatalogContentTypeSeeder>();
        builder.AddNotificationAsyncHandler<ContentPublishedNotification, CatalogPublishSyncHandler>();
        builder.Services.AddSingleton<ICatalogSyncPublisher, ServiceBusCatalogSyncPublisher>();
    }
}
