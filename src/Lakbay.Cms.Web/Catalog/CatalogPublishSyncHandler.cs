using System.Text.Json;
using Lakbay.Contracts;
using Microsoft.Extensions.Logging;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Events;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Notifications;
using Umbraco.Cms.Core.Services;

namespace Lakbay.Cms.Web.Catalog;

/// <summary>
/// ADR-0013: on publish of a ProductLine/Destination/Product node, builds
/// a <see cref="CatalogSyncEvent"/> matching Lakbay.Contracts' shapes and
/// sends it via <see cref="ICatalogSyncPublisher"/>. Ignores publishes of
/// any other content type (the future Content tree's pages, once built).
///
/// A Product's embedded Destination/ProductLine are resolved from their
/// *published* values via <see cref="IContentService"/>, not from
/// <paramref name="entity"/>'s own possibly-unpublished picker target —
/// syncing a Product should never accidentally leak a destination's draft
/// edit that hasn't been published yet.
/// </summary>
public sealed class CatalogPublishSyncHandler(
    ICatalogSyncPublisher publisher,
    IContentService contentService,
    ILogger<CatalogPublishSyncHandler> logger)
    : INotificationAsyncHandler<ContentPublishedNotification>
{
    public async Task HandleAsync(ContentPublishedNotification notification, CancellationToken ct)
    {
        foreach (var entity in notification.PublishedEntities)
        {
            CatalogSyncEvent? syncEvent;

            try
            {
                syncEvent = entity.ContentType.Alias switch
                {
                    CatalogContentTypeSeeder.ProductLineAlias => BuildProductLineEvent(entity),
                    CatalogContentTypeSeeder.CountryAlias => new CatalogSyncEvent
                    {
                        EntityType = CatalogEntityType.Country,
                        Country = BuildCountry(entity),
                    },
                    CatalogContentTypeSeeder.RegionAlias => new CatalogSyncEvent
                    {
                        EntityType = CatalogEntityType.Region,
                        Region = BuildRegion(entity),
                    },
                    CatalogContentTypeSeeder.AccommodationAlias => new CatalogSyncEvent
                    {
                        EntityType = CatalogEntityType.Accommodation,
                        Accommodation = BuildAccommodation(entity),
                    },
                    CatalogContentTypeSeeder.DestinationAlias => new CatalogSyncEvent
                    {
                        EntityType = CatalogEntityType.Destination,
                        Destination = BuildDestination(entity),
                    },
                    CatalogContentTypeSeeder.RoomTypeAlias => new CatalogSyncEvent
                    {
                        EntityType = CatalogEntityType.RoomType,
                        RoomType = BuildRoomType(entity),
                    },
                    CatalogContentTypeSeeder.ActivityAlias => new CatalogSyncEvent
                    {
                        EntityType = CatalogEntityType.Activity,
                        Activity = BuildActivity(entity),
                    },
                    CatalogContentTypeSeeder.ProductAlias => BuildProductEvent(entity),
                    _ => null,
                };
            }
            catch (Exception ex)
            {
                // A malformed catalog node (missing required field, bad
                // enum text) should never take down the whole publish
                // operation for an editor — log and skip that one node.
                logger.LogError(ex, "Skipping catalog sync for {ContentType} {Key}: {Message}",
                    entity.ContentType.Alias, entity.Key, ex.Message);
                continue;
            }

            if (syncEvent is null)
            {
                continue; // Not a catalog content type — e.g. a future Content-tree page.
            }

            await publisher.PublishAsync(syncEvent, ct);
        }
    }

    private static CatalogSyncEvent BuildProductLineEvent(IContent entity) => new()
    {
        EntityType = CatalogEntityType.ProductLine,
        ProductLine = BuildProductLine(entity),
    };

    private static ProductLine BuildProductLine(IContent entity)
    {
        var codeText = entity.GetValue<string>("code")
            ?? throw new InvalidOperationException($"ProductLine {entity.Key}: missing 'code'.");

        var code = ParseProductLineCode(codeText, entity.Key);

        return new ProductLine
        {
            Code = code,
            Name = entity.Name ?? codeText,
            Tagline = entity.GetValue<string>("tagline") ?? string.Empty,
            Countries = (entity.GetValue<string>("countries") ?? "PH")
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
            SourceUpdatedUtc = entity.UpdateDate,
        };
    }

    private static Country BuildCountry(IContent entity) => new()
    {
        Id = entity.Key.ToString(),
        Name = entity.Name ?? entity.Key.ToString(),
        Code = entity.GetValue<string>("code") ?? "PH",
        Description = entity.GetValue<string>("description") ?? string.Empty,
        Highlights = ParseActivityLines(entity.GetValue<string>("highlights")),
        SourceUpdatedUtc = entity.UpdateDate,
    };

    private Region BuildRegion(IContent entity)
    {
        var countryEntity = ResolvePickedContent(entity, "country")
            ?? throw new InvalidOperationException($"Region {entity.Key}: 'country' picker is empty or points at an unpublished/missing node.");

        return new Region
        {
            Id = entity.Key.ToString(),
            Name = entity.Name ?? entity.Key.ToString(),
            Slug = entity.GetValue<string>("slug") ?? entity.Key.ToString(),
            Country = BuildCountry(countryEntity),
            ProductLine = ResolveProductLineCode(entity),
            Description = entity.GetValue<string>("description") ?? string.Empty,
            Highlights = ParseActivityLines(entity.GetValue<string>("highlights")),
            SourceUpdatedUtc = entity.UpdateDate,
        };
    }

    private Accommodation BuildAccommodation(IContent entity)
    {
        var destinationEntity = ResolvePickedContent(entity, "destination")
            ?? throw new InvalidOperationException($"Accommodation {entity.Key}: 'destination' picker is empty or points at an unpublished/missing node.");

        return new Accommodation
        {
            Id = entity.Key.ToString(),
            Name = entity.Name ?? entity.Key.ToString(),
            Description = entity.GetValue<string>("description") ?? string.Empty,
            Highlights = ParseActivityLines(entity.GetValue<string>("highlights")),
            HeroImageUrl = entity.GetValue<string>("heroImageUrl"),
            Destination = BuildDestination(destinationEntity),
            Type = ParseAccommodationType(entity.GetValue<string>("type"), entity.Key),
            Tags = ParseActivityLines(entity.GetValue<string>("tags")),
            OfficialRating = entity.GetValue<double?>("officialRating"),
            SourceUpdatedUtc = entity.UpdateDate,
        };
    }

    private Destination BuildDestination(IContent entity)
    {
        var regionEntity = ResolvePickedContent(entity, "region")
            ?? throw new InvalidOperationException($"Destination {entity.Key}: 'region' picker is empty or points at an unpublished/missing node.");

        return new Destination
        {
            Id = entity.Key.ToString(),
            Name = entity.Name ?? entity.Key.ToString(),
            Slug = entity.GetValue<string>("slug") ?? entity.Key.ToString(),
            Country = entity.GetValue<string>("country") ?? "PH",
            Region = BuildRegion(regionEntity),
            ProductLine = ResolveProductLineCode(entity),
            Description = entity.GetValue<string>("description") ?? string.Empty,
            Latitude = entity.GetValue<double?>("latitude"),
            Longitude = entity.GetValue<double?>("longitude"),
            IncludedPerks = ParseActivityLines(entity.GetValue<string>("includedPerks")),
            OptionalAddOns = ParseActivityLines(entity.GetValue<string>("optionalAddOns")),
            SourceUpdatedUtc = entity.UpdateDate,
        };
    }

    /// <summary>
    /// RoomType carries a plain <c>AccommodationId</c> reference, not an
    /// embedded Accommodation — same "queried separately by parent id"
    /// shape ProductFilter.destinationId already uses. Resolving the
    /// picker here only to read its key (not to build the full object)
    /// avoids the reverse-lookup complexity a Accommodation.RoomTypes
    /// embedded list would otherwise need.
    /// </summary>
    private RoomType BuildRoomType(IContent entity)
    {
        var accommodationEntity = ResolvePickedContent(entity, "accommodation")
            ?? throw new InvalidOperationException($"RoomType {entity.Key}: 'accommodation' picker is empty or points at an unpublished/missing node.");

        return new RoomType
        {
            Id = entity.Key.ToString(),
            Name = entity.Name ?? entity.Key.ToString(),
            Description = entity.GetValue<string>("description") ?? string.Empty,
            SizeSqm = entity.GetValue<string>("sizeSqm"),
            BedConfiguration = entity.GetValue<string>("bedConfiguration") ?? string.Empty,
            MaxOccupancy = entity.GetValue<int?>("maxOccupancy") ?? 1,
            BoardBasis = ParseBoardBasis(entity.GetValue<string>("boardBasis"), entity.Key),
            PriceBands = ParsePriceBands(entity.GetValue<string>("priceBands"), entity.Key),
            HeroImageUrl = entity.GetValue<string>("heroImageUrl"),
            MonthlyRatePhp = entity.GetValue<decimal?>("monthlyRatePhp"),
            AccommodationId = accommodationEntity.Key.ToString(),
            SourceUpdatedUtc = entity.UpdateDate,
        };
    }

    /// <summary>
    /// Activity embeds a full Destination (like Product/Accommodation),
    /// not a thin id reference (unlike RoomType) — Activities are
    /// independently cross-destination browsable (ADR-0020), so they need
    /// the richer object the same way Accommodation now does.
    /// </summary>
    private Activity BuildActivity(IContent entity)
    {
        var destinationEntity = ResolvePickedContent(entity, "destination")
            ?? throw new InvalidOperationException($"Activity {entity.Key}: 'destination' picker is empty or points at an unpublished/missing node.");

        return new Activity
        {
            Id = entity.Key.ToString(),
            Name = entity.Name ?? entity.Key.ToString(),
            Description = entity.GetValue<string>("description") ?? string.Empty,
            DurationLabel = entity.GetValue<string>("durationLabel") ?? string.Empty,
            PricePhp = entity.GetValue<decimal?>("pricePhp") ?? 0m,
            Includes = ParseActivityLines(entity.GetValue<string>("includes")),
            HeroImageUrl = entity.GetValue<string>("heroImageUrl"),
            Destination = BuildDestination(destinationEntity),
            SourceUpdatedUtc = entity.UpdateDate,
        };
    }

    private CatalogSyncEvent BuildProductEvent(IContent entity)
    {
        var destinationEntity = ResolvePickedContent(entity, "destination")
            ?? throw new InvalidOperationException($"Product {entity.Key}: 'destination' picker is empty or points at an unpublished/missing node.");

        var accommodationEntity = ResolvePickedContent(entity, "accommodation");

        return new CatalogSyncEvent
        {
            EntityType = CatalogEntityType.Product,
            Product = new Product
            {
                Id = entity.Key.ToString(),
                Slug = entity.GetValue<string>("slug") ?? entity.Key.ToString(),
                Name = entity.Name ?? entity.Key.ToString(),
                ProductLine = ResolveProductLineCode(entity),
                Destination = BuildDestination(destinationEntity),
                Summary = entity.GetValue<string>("summary") ?? string.Empty,
                ItineraryDays = entity.GetValue<int?>("itineraryDays") ?? 0,
                BoardBasis = ParseBoardBasis(entity.GetValue<string>("boardBasis"), entity.Key),
                PriceBands = ParsePriceBands(entity.GetValue<string>("priceBands"), entity.Key),
                Accommodation = accommodationEntity is null ? null : BuildAccommodation(accommodationEntity),
                IncludedActivities = ParseActivityLines(entity.GetValue<string>("includedActivities")),
                OptionalActivities = ParseActivityLines(entity.GetValue<string>("optionalActivities")),
                // AvailableCount deliberately absent (ADR-0014) — the
                // Product record's constructor requires it, but the sync
                // event carries a placeholder; Lakbay.AvailabilityApi.Sync
                // never reads it off this event for that field (see
                // CatalogSyncEvent's own doc comment).
                AvailableCount = 0,
                HeroImageUrl = entity.GetValue<string>("heroImageUrl"),
                SourceUpdatedUtc = entity.UpdateDate,
            },
        };
    }

    private static ProductLineCode ParseProductLineCode(string? text, Guid contentKey)
    {
        if (string.IsNullOrWhiteSpace(text) || !Enum.TryParse<ProductLineCode>(text, ignoreCase: true, out var code))
        {
            throw new InvalidOperationException($"Content {contentKey}: '{text}' is not a valid ProductLineCode.");
        }

        return code;
    }

    /// <summary>
    /// Destination and Product's own "productLine" field is a Content
    /// Picker pointing at a ProductLine node — not free text. Real bug,
    /// found only by actually publishing and watching it fail (not by
    /// reading the code): the raw property value is the picker's UDI
    /// string (e.g. "umb://document/..."), not a code like "ALON", so it
    /// must be resolved to the picked ProductLine node first and its own
    /// `code` field read from there, the same way BuildProductEvent
    /// already resolves the `destination` picker.
    /// </summary>
    private ProductLineCode ResolveProductLineCode(IContent entity)
    {
        var productLineEntity = ResolvePickedContent(entity, "productLine")
            ?? throw new InvalidOperationException(
                $"Content {entity.Key}: 'productLine' picker is empty or points at an unpublished/missing node.");

        return ParseProductLineCode(productLineEntity.GetValue<string>("code"), productLineEntity.Key);
    }

    private static readonly JsonSerializerOptions PriceBandJsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// v0 simplification (CatalogContentTypeSeeder): priceBands is a
    /// Textarea holding a JSON array shaped like Lakbay.Contracts.PriceBand,
    /// not a proper Block List yet. Malformed/empty JSON syncs as an
    /// empty list rather than failing the whole product's sync — an
    /// editor mid-typing an unfinished value shouldn't block a publish.
    /// </summary>
    private static IReadOnlyList<PriceBand> ParsePriceBands(string? json, Guid contentKey)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<List<PriceBand>>(json, PriceBandJsonOptions) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    /// <summary>
    /// includedActivities/optionalActivities: a Textarea holding one
    /// activity per line — plain strings, no nesting, so newline-split is
    /// simpler than the JSON-array pattern priceBands uses and needs no
    /// escaping for a content editor to type.
    /// </summary>
    private static IReadOnlyList<string> ParseActivityLines(string? text) =>
        string.IsNullOrWhiteSpace(text)
            ? []
            : text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static BoardBasis ParseBoardBasis(string? text, Guid contentKey)
    {
        var normalized = text?.Replace("_", string.Empty);

        if (string.IsNullOrWhiteSpace(normalized) || !Enum.TryParse<BoardBasis>(normalized, ignoreCase: true, out var basis))
        {
            throw new InvalidOperationException($"Product {contentKey}: '{text}' is not a valid BoardBasis.");
        }

        return basis;
    }

    /// <summary>Same normalize-and-parse shape as ParseBoardBasis (ADR-0020).</summary>
    private static AccommodationType ParseAccommodationType(string? text, Guid contentKey)
    {
        var normalized = text?.Replace("_", string.Empty);

        if (string.IsNullOrWhiteSpace(normalized) || !Enum.TryParse<AccommodationType>(normalized, ignoreCase: true, out var type))
        {
            throw new InvalidOperationException($"Accommodation {contentKey}: '{text}' is not a valid AccommodationType.");
        }

        return type;
    }

    /// <summary>
    /// Resolves a Content Picker property to the picked node's *published*
    /// content — never its possibly-unpublished draft state.
    /// </summary>
    private IContent? ResolvePickedContent(IContent entity, string propertyAlias)
    {
        var raw = entity.GetValue<string>(propertyAlias);

        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        if (!UdiParser.TryParse(raw, out var udi) || udi is not GuidUdi guidUdi)
        {
            return null;
        }

        var picked = contentService.GetById(guidUdi.Guid);
        return picked is { Published: true } ? picked : null;
    }
}
