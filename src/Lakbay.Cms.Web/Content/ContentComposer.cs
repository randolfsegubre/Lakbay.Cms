using Umbraco.Cms.Core.Composing;
using Umbraco.Cms.Core.DependencyInjection;
using Umbraco.Cms.Core.Notifications;

namespace Lakbay.Cms.Web.Content;

/// <summary>
/// Wires up the Content tree seeder (ADR-0001). CmsSchemaBuilder itself
/// is registered once, in Catalog.CatalogComposer — see that file's
/// comment for why.
/// </summary>
public sealed class ContentComposer : IComposer
{
    public void Compose(IUmbracoBuilder builder)
    {
        builder.AddNotificationAsyncHandler<UmbracoApplicationStartedNotification, ContentTreeSeeder>();
    }
}
