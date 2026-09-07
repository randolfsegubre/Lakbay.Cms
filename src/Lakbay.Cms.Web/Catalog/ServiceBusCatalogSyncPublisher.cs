using System.Text.Json;
using System.Text.Json.Serialization;
using Azure.Messaging.ServiceBus;
using Lakbay.Contracts;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Lakbay.Cms.Web.Catalog;

/// <summary>
/// ADR-0013: sends to the same <c>lakbay-catalog-sync</c> queue
/// Lakbay.AvailabilityApi.Sync's CatalogSyncFunction consumes. Connection
/// string comes from user-secrets (ConnectionStrings:ServiceBus), same
/// pattern as this repo's SQL Server connection string — never
/// appsettings, never committed.
/// </summary>
public sealed class ServiceBusCatalogSyncPublisher : ICatalogSyncPublisher, IAsyncDisposable
{
    private const string QueueName = "lakbay-catalog-sync";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly ServiceBusClient _client;
    private readonly ServiceBusSender _sender;
    private readonly ILogger<ServiceBusCatalogSyncPublisher> _logger;

    public ServiceBusCatalogSyncPublisher(IConfiguration configuration, ILogger<ServiceBusCatalogSyncPublisher> logger)
    {
        var connectionString = configuration.GetConnectionString("ServiceBus")
            ?? throw new InvalidOperationException(
                "Missing ConnectionStrings:ServiceBus — see Docs/DEVELOPER_HANDBOOK.md for the local Service Bus emulator setup (ADR-0013).");

        _client = new ServiceBusClient(connectionString);
        _sender = _client.CreateSender(QueueName);
        _logger = logger;
    }

    public async Task PublishAsync(CatalogSyncEvent syncEvent, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(syncEvent, JsonOptions);
        var message = new ServiceBusMessage(json)
        {
            ContentType = "application/json",
            Subject = syncEvent.EntityType.ToString(),
        };

        await _sender.SendMessageAsync(message, ct);
        _logger.LogInformation("Published catalog sync event: {EntityType}", syncEvent.EntityType);
    }

    public async ValueTask DisposeAsync()
    {
        await _sender.DisposeAsync();
        await _client.DisposeAsync();
    }
}
