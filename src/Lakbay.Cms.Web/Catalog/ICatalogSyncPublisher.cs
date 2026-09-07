using Lakbay.Contracts;

namespace Lakbay.Cms.Web.Catalog;

/// <summary>
/// Publishes a catalog change to Lakbay.AvailabilityApi.Sync (ADR-0013).
/// An Adapter over the actual transport (Service Bus) so
/// <see cref="CatalogPublishSyncHandler"/> has no direct dependency on
/// Azure.Messaging.ServiceBus — matches this repo's Dependency-Inversion
/// convention (03_ARCHITECTURE_AND_PATTERNS_GUIDE.md).
/// </summary>
public interface ICatalogSyncPublisher
{
    Task PublishAsync(CatalogSyncEvent syncEvent, CancellationToken ct);
}
