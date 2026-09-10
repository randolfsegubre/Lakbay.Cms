using Lakbay.Cms.Web.Shared;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Events;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Notifications;
using Umbraco.Cms.Core.Services;

namespace Lakbay.Cms.Web.Catalog;

/// <summary>
/// Creates the Products tree's document types (ADR-0001, Phase 3) the
/// first time this app boots against an empty schema — code-first, not
/// backoffice clicking, so the schema is reviewable and reproducible on a
/// fresh machine (this project's "off-grid, no AI agent" maintainability
/// bar, see Lakbay.Docs/docs/01_CLAUDE.md §"Engineering practice"). Runs
/// once; a no-op on every later boot once the types already exist.
///
/// Scope: the Products tree only (ProductLine, Destination, Product). The
/// Content tree lives in <c>Lakbay.Cms.Web.Content.ContentTreeSeeder</c> —
/// same pattern, a separate class, matching ADR-0001's own split between
/// the two trees.
///
/// v0 simplifications, stated plainly rather than hidden — all fixable
/// later as backoffice-UX polish, not data-shape changes:
/// - ProductLine's `code` and Product's `boardBasis` are plain text, not
///   configured dropdowns. CatalogPublishSyncHandler validates/parses
///   them against Lakbay.Contracts' enums and skips+logs an invalid
///   value rather than trusting UI-level constraints.
/// - Product's `priceBands` is a Textarea holding a JSON array (matching
///   Lakbay.Contracts.PriceBand's shape) rather than a proper Block List
///   — deliberately not upgraded even now that Block List is proven
///   working elsewhere (Content tree's `sections`): PriceBand isn't
///   Content-tree UI, and changing its stored shape would be a breaking
///   change for CatalogPublishSyncHandler's ParsePriceBands with no
///   corresponding benefit yet.
/// - Product's `heroImageUrl` is a plain text URL field, not a Media
///   Picker — deliberate, not just deferred: this platform's product
///   photography isn't real media-library content yet (real photos get
///   sourced/licensed properly by Phase 5), so pointing straight at an
///   external URL is the honest shape for what's actually here, matching
///   Lakbay.Contracts.Product.HeroImageUrl's own `string?` shape exactly.
/// - `includedActivities`/`optionalActivities` are Textareas holding one
///   activity per line, not a JSON array — same reasoning as
///   `priceBands` for structure vs. simplicity, but newline-split is
///   simpler still since these are plain strings with no nested shape to
///   preserve (see CatalogPublishSyncHandler.ParseActivityLines).
/// </summary>
public sealed class CatalogContentTypeSeeder(
    CmsSchemaBuilder schema,
    IContentTypeService contentTypeService,
    IContentService contentService)
    : INotificationAsyncHandler<UmbracoApplicationStartedNotification>
{
    public const string ProductLineAlias = "productLine";
    public const string CountryAlias = "country";
    public const string RegionAlias = "region";
    public const string DestinationAlias = "destination";
    public const string AccommodationAlias = "accommodation";
    public const string RoomTypeAlias = "roomType";
    public const string ActivityAlias = "activity";
    public const string ProductAlias = "product";

    public async Task HandleAsync(UmbracoApplicationStartedNotification notification, CancellationToken ct)
    {
        // One-time migration, same self-limiting shape as ADR-0015 through
        // ADR-0019 before it: gated on a specific tell that this database
        // predates the current shape, deletes-and-recreates so the seed
        // loop below never hits "No PropertyType exists with the supplied
        // alias". Four tells checked together: the Region type not
        // existing at all (pre-ADR-0017), existing but missing `slug`
        // (ADR-0017 shape, pre-ADR-0018), Accommodation missing
        // `destination` (pre-ADR-0019 shape), or Accommodation missing
        // `type` (pre-ADR-0020 shape — accommodation types/long-stay/
        // Activities pass).
        var regionType = contentTypeService.Get(RegionAlias);
        var accommodationType = contentTypeService.Get(AccommodationAlias);
        var needsMigration = regionType is null
            || regionType.PropertyTypes.All(pt => pt.Alias != "slug")
            || accommodationType is null
            || accommodationType.PropertyTypes.All(pt => pt.Alias != "destination")
            || accommodationType.PropertyTypes.All(pt => pt.Alias != "type");

        if (contentTypeService.Get(ProductLineAlias) is not null && needsMigration)
        {
            await DeleteContentAndTypeAsync(ActivityAlias);
            await DeleteContentAndTypeAsync(RoomTypeAlias);
            await DeleteContentAndTypeAsync(ProductAlias);
            await DeleteContentAndTypeAsync(AccommodationAlias);
            await DeleteContentAndTypeAsync(DestinationAlias);
            await DeleteContentAndTypeAsync(RegionAlias);
            await DeleteContentAndTypeAsync(CountryAlias);
            await DeleteContentAndTypeAsync(ProductLineAlias);
        }

        var typesAlreadyExist = contentTypeService.Get(ProductLineAlias) is not null;

        if (!typesAlreadyExist)
        {
            var textstring = await schema.GetBuiltInDataTypeAsync(Constants.PropertyEditors.Aliases.TextBox);
            var textarea = await schema.GetBuiltInDataTypeAsync(Constants.PropertyEditors.Aliases.TextArea);
            var numeric = await schema.GetBuiltInDataTypeAsync(Constants.PropertyEditors.Aliases.Integer);
            var decimalType = await schema.GetBuiltInDataTypeAsync(Constants.PropertyEditors.Aliases.Decimal);
            var contentPicker = await schema.GetBuiltInDataTypeAsync(Constants.PropertyEditors.Aliases.ContentPicker);

            var productLine = schema.NewContentType(ProductLineAlias, "Product Line", "icon-flag");
            schema.AddProperties(productLine,
                (textstring, "code", "Code (ALON / AMIHAN / PARUL / PAMANA)"),
                (textstring, "tagline", "Tagline"),
                (textstring, "countries", "Countries (comma-separated ISO 3166-1 alpha-2)"));
            await schema.SaveNewAsync(productLine);

            // ADR-0017: Country → Region → Destination → Accommodation,
            // matching the real ECMS geography tree shape (confirmed via
            // the read-only D:\_DEV\HPUK reference clone), adapted for
            // Lakbay's single-country business model — see the ADR for
            // why Country is one shared node rather than duplicated per
            // ProductLine the way Region/Destination are.
            var country = schema.NewContentType(CountryAlias, "Country", "icon-globe");
            schema.AddProperties(country,
                (textstring, "code", "Code (ISO 3166-1 alpha-2)"),
                (textarea, "description", "Description"),
                (textarea, "highlights", "Highlights (one per line)"));
            await schema.SaveNewAsync(country);

            var region = schema.NewContentType(RegionAlias, "Region", "icon-map-alt");
            schema.AddProperties(region,
                (textstring, "slug", "Slug"),
                (contentPicker, "country", "Country"),
                (contentPicker, "productLine", "Product Line"),
                (textarea, "description", "Description"),
                (textarea, "highlights", "Highlights (one per line)"));
            await schema.SaveNewAsync(region);

            var destination = schema.NewContentType(DestinationAlias, "Destination", "icon-map-location");
            schema.AddProperties(destination,
                (textstring, "slug", "Slug"),
                (textstring, "country", "Country (ISO 3166-1 alpha-2)"),
                (contentPicker, "region", "Region"),
                (contentPicker, "productLine", "Product Line"),
                (textarea, "description", "Description"),
                (decimalType, "latitude", "Latitude"),
                (decimalType, "longitude", "Longitude"),
                // ADR-0019: resort-wide perks/add-ons bundled into any stay
                // at this destination, regardless of which Accommodation —
                // see the Inghams "Included in your ski holiday" pattern.
                (textarea, "includedPerks", "Included Perks (one per line)"),
                (textarea, "optionalAddOns", "Optional Add-Ons (one per line)"));
            await schema.SaveNewAsync(destination);

            var accommodation = schema.NewContentType(AccommodationAlias, "Accommodation", "icon-home");
            schema.AddProperties(accommodation,
                (textarea, "description", "Description"),
                (textarea, "highlights", "Highlights (one per line)"),
                (textstring, "heroImageUrl", "Hero Image URL"),
                // ADR-0019: Accommodation becomes independently browsable
                // (the Stays search page), so it needs its own Destination
                // reference — previously only reachable indirectly via a
                // Product picking both as siblings.
                (contentPicker, "destination", "Destination"),
                (textstring, "type", "Type (HOTEL / RESORT / APARTEL / PENSION_HOUSE / HOSTEL / HOMESTAY / VACATION_RENTAL)"),
                (textarea, "tags", "Tags (one per line — e.g. Budget-Friendly, Family-Friendly)"),
                (decimalType, "officialRating", "Official Rating (stars, optional)"));
            await schema.SaveNewAsync(accommodation);

            // ADR-0019: a real "pick your room" option within an
            // Accommodation (Inghams' "Room Types" pattern). Flat + picker,
            // same convention as the rest of the Products tree (ADR-0017) —
            // not nested under its Accommodation.
            var roomType = schema.NewContentType(RoomTypeAlias, "Room Type", "icon-bed");
            schema.AddProperties(roomType,
                (contentPicker, "accommodation", "Accommodation"),
                (textarea, "description", "Description"),
                (textstring, "sizeSqm", "Size (e.g. 18-22m²)"),
                (textstring, "bedConfiguration", "Bed Configuration"),
                (numeric, "maxOccupancy", "Max Occupancy"),
                (textstring, "boardBasis", "Board Basis (ROOM_ONLY / BREAKFAST / HALF_BOARD / FULL_BOARD / ALL_INCLUSIVE)"),
                (textarea, "priceBands", "Price Bands (JSON array — see Docs/DEVELOPER_HANDBOOK.md)"),
                // ADR-0020: rooms get their own photo (falls back to the
                // Accommodation's own in the UI when unset), and an
                // optional flat monthly rate for open-ended/long-stay
                // travelers — a different price shape than the dated
                // seasonal priceBands above.
                (textstring, "heroImageUrl", "Hero Image URL"),
                (decimalType, "monthlyRatePhp", "Monthly Rate PHP (long-stay, optional)"));
            await schema.SaveNewAsync(roomType);

            // ADR-0020: a real, independently bookable local activity at a
            // fixed, agreed-upfront price — Lakbay's answer to "buying
            // directly from a local risks getting scammed or overpriced."
            // Flat + picker, same Products-tree convention; embeds its
            // Destination (not a thin id) since Activities are
            // cross-destination browsable, same reasoning as Accommodation.
            var activity = schema.NewContentType(ActivityAlias, "Activity", "icon-map-marker");
            schema.AddProperties(activity,
                (contentPicker, "destination", "Destination"),
                (textarea, "description", "Description"),
                (textstring, "durationLabel", "Duration (e.g. Half-day (4 hours))"),
                (decimalType, "pricePhp", "Price PHP (fixed, per person)"),
                (textarea, "includes", "Includes (one per line)"),
                (textstring, "heroImageUrl", "Hero Image URL"));
            await schema.SaveNewAsync(activity);

            var product = schema.NewContentType(ProductAlias, "Product", "icon-luggage");
            schema.AddProperties(product,
                (textstring, "slug", "Slug"),
                (contentPicker, "productLine", "Product Line"),
                (contentPicker, "destination", "Destination"),
                (textarea, "summary", "Summary"),
                (numeric, "itineraryDays", "Itinerary Days"),
                (textstring, "boardBasis", "Board Basis (ROOM_ONLY / BREAKFAST / HALF_BOARD / FULL_BOARD / ALL_INCLUSIVE)"),
                (textarea, "priceBands", "Price Bands (JSON array — see Docs/DEVELOPER_HANDBOOK.md)"),
                (contentPicker, "accommodation", "Accommodation"),
                (textarea, "includedActivities", "Included Activities (one per line)"),
                (textarea, "optionalActivities", "Optional Activities (one per line)"),
                (textstring, "heroImageUrl", "Hero Image URL"));
            await schema.SaveNewAsync(product);
        }

        // Deliberately in the same HandleAsync, after the type-creation
        // above, not a second UmbracoApplicationStartedNotification
        // handler: Umbraco's EventAggregator runs every handler for a
        // given notification concurrently (Task.WaitAll), so a separate
        // content-seeding handler could race the type-creation above and
        // try to create content of a type that doesn't exist yet.
        // Sequential within one method is the only ordering guarantee.
        await SeedRealContentAsync();
    }

    /// <summary>
    /// Real Philippine seed content — same four destinations/products
    /// <c>Lakbay.AvailabilityApi.Api</c>'s Phase 1 <c>CatalogSeeder</c>
    /// already hand-seeds, authored here instead through
    /// <see cref="IContentService"/> so publishing them exercises the
    /// real ADR-0013 pipe end to end (this triggers a genuine
    /// ContentPublishedNotification per node — CatalogPublishSyncHandler
    /// does the rest). Standing in for backoffice authoring only because
    /// no session has interactive backoffice login credentials (see
    /// 05_DEVLOG.md — deliberately never recorded, even locally);
    /// content authored this way is otherwise indistinguishable from
    /// content an editor typed in by hand. Idempotent: skips entirely if
    /// any Destination content already exists.
    ///
    /// Set LAKBAY_FORCE_RESEED_CATALOG=true to delete and recreate all
    /// seed content instead of skipping — a deliberate, explicit opt-in
    /// escape hatch for early development (e.g. after fixing a bug in
    /// this seeder itself), never the default. Unset/false in every real
    /// environment; this must never silently delete an editor's actual
    /// work.
    /// </summary>
    private Task SeedRealContentAsync()
    {
        if (Environment.GetEnvironmentVariable("LAKBAY_FORCE_RESEED_CATALOG") == "true")
        {
            DeleteAllOfType(ActivityAlias);
            DeleteAllOfType(RoomTypeAlias);
            DeleteAllOfType(ProductAlias);
            DeleteAllOfType(AccommodationAlias);
            DeleteAllOfType(DestinationAlias);
            DeleteAllOfType(RegionAlias);
            DeleteAllOfType(CountryAlias);
            DeleteAllOfType(ProductLineAlias);
        }
        else if (contentService.CountPublished(DestinationAlias) > 0)
        {
            return Task.CompletedTask;
        }

        var lines = new (string Code, string Name, string Tagline, string Countries)[]
        {
            ("ALON", "Islands", "Islands & water adventure", "PH"),
            ("AMIHAN", "Highlands", "Highland & cool-climate escapes", "PH"),
            ("PARUL", "Festivals", "Festive & light tourism", "PH"),
            ("PAMANA", "Heritage", "Heritage & culture", "PH"),
        };

        var lineUdis = new Dictionary<string, string>();

        foreach (var line in lines)
        {
            var content = contentService.Create(line.Name, -1, ProductLineAlias);
            content.SetValue("code", line.Code);
            content.SetValue("tagline", line.Tagline);
            content.SetValue("countries", line.Countries);
            PublishOrThrow(content);
            lineUdis[line.Code] = ToUdi(content);
        }

        // ADR-0017: one shared Country node (confirmed with the user —
        // Lakbay's whole catalog is one country today, unlike ECMS's
        // real multi-country scale), then Regions scoped one-per-
        // ProductLine the same way Destination already is. Region names
        // repeat across lines on purpose (e.g. "Cordillera Administrative
        // Region" appears under both Highlands and Heritage) — each is a
        // distinct node with its own highlights tailored to that line's
        // angle on the same place, matching ECMS's own real per-product
        // duplication of Country/Region.
        var countryContent = contentService.Create("Philippines", -1, CountryAlias);
        countryContent.SetValue("code", "PH");
        countryContent.SetValue("description",
            "An archipelago of over 7,000 islands in Southeast Asia — warm year-round, English widely spoken, and home to some of the region's best diving, surfing, and mountain scenery within a few hours of each other.");
        countryContent.SetValue("highlights", string.Join('\n', new[]
        {
            "Tropical climate year-round, with a drier peak season November-May",
            "English is an official language alongside Filipino — no language barrier for most travelers",
            "Currency: Philippine Peso (PHP)",
        }));
        PublishOrThrow(countryContent);
        var countryUdi = ToUdi(countryContent);

        var regions = new RegionSeed[]
        {
            new("region-palawan", "Palawan", "ALON",
                "The Philippines' last ecological frontier — limestone karst islands, hidden lagoons, and some of the clearest water in the country.",
                ["Regularly ranked among the world's best islands by international travel press", "Best visited November-May, outside typhoon season", "Coron and El Nido are separate towns, roughly 4 hours apart by boat or van"]),
            new("region-western-visayas-alon", "Western Visayas", "ALON",
                "Home to Boracay, the Philippines' most internationally famous beach destination.",
                ["White Beach's powdery sand comes from pulverized coral and shells, not imported sand", "A 2018 six-month environmental rehabilitation reset strict development rules still in force today", "Nearest airport: Caticlan (close) or Kalibo (further, often cheaper)"]),
            new("region-caraga", "Caraga", "ALON",
                "A teardrop-shaped island on the country's Pacific side, built around one of the world's best-known surf breaks.",
                ["Cloud 9's reef break hosts an annual international surfing cup", "Also known for its lagoons, rock pools, and a slower pace than Boracay", "Best surf season: August-November"]),
            new("region-ilocos-alon", "Ilocos Region", "ALON",
                "The Philippines' most accessible surf coast — a string of beach towns three hours north of Manila by road.",
                ["San Juan's Urbiztondo Beach is the country's most established beginner-surf town", "Close enough to Manila for a weekend trip, no flight required", "Also home to Vigan's heritage old town further north (Heritage line)"]),
            new("region-cordillera-amihan", "Cordillera Administrative Region", "AMIHAN",
                "A mountainous region in northern Luzon, several degrees cooler than the lowlands year-round.",
                ["Baguio sits at 1,540m elevation, earning its nickname as the Philippines' Summer Capital", "Sagada is a smaller, quieter mountain town known for caving and centuries-old hanging coffins", "Roughly 5-6 hours by road from Manila"]),
            new("region-calabarzon", "Calabarzon", "AMIHAN",
                "The ridge just south of Manila overlooking Taal Lake and Volcano, the Philippines' most accessible highland day trip.",
                ["Less than two hours from Manila — no overnight stay strictly required", "Taal is one of the world's smallest active volcanoes, sitting on an island within a lake", "Cooler than Manila year-round thanks to the elevation"]),
            new("region-central-luzon", "Central Luzon", "PARUL",
                "Home to the Giant Lantern Festival, the Philippines' best-known Christmas-season celebration.",
                ["San Fernando is officially recognized as the Christmas Capital of the Philippines", "The Giant Lantern Festival runs mid-December, with lanterns up to 20 feet wide and thousands of synchronized lights", "About 2 hours from Manila by road"]),
            new("region-central-visayas", "Central Visayas", "PARUL",
                "The Visayas' commercial and cultural hub, home to the country's grandest religious festival.",
                ["Sinulog Festival draws millions of visitors every third Sunday of January", "Cebu is also the Philippines' oldest city, founded in 1565", "A major international and domestic air gateway to the central Philippines"]),
            new("region-western-visayas-parul", "Western Visayas", "PARUL",
                "A heritage port city on Panay island, home to the Dinagyang Festival.",
                ["Dinagyang's tribal street-dance competition runs every fourth Sunday of January, a week after Cebu's Sinulog", "Known for a well-preserved Spanish-era heritage strip along Calle Real", "A short flight or ferry connection from Boracay/Aklan"]),
            new("region-ilocos-pamana", "Ilocos Region", "PAMANA",
                "The Philippines' best-preserved Spanish colonial town, a UNESCO World Heritage Site.",
                ["Calle Crisologo's cobblestone streets and ancestral houses date to the 16th-18th centuries", "One of the best-preserved examples of a planned Spanish colonial town in Asia", "Also the gateway region to La Union's surf towns further south (Islands line)"]),
            new("region-cordillera-pamana", "Cordillera Administrative Region", "PAMANA",
                "Home to the Ifugao Rice Terraces, carved into the mountains over 2,000 years ago.",
                ["Often called the 'Eighth Wonder of the World', a UNESCO World Heritage Site", "Still farmed by Ifugao communities using traditional methods", "Batad, a nearby amphitheater-shaped terrace cluster, is reachable only on foot"]),
            new("region-ncr", "National Capital Region", "PAMANA",
                "Metro Manila's historic walled city, the oldest district in the Philippine capital.",
                ["Built by Spanish colonizers in the 16th century as the seat of government", "Fort Santiago, Manila Cathedral, and San Agustin Church (a UNESCO World Heritage Site) all sit within its walls", "A short trip from Ninoy Aquino International Airport"]),
        };

        var regionUdis = new Dictionary<string, string>();

        foreach (var r in regions)
        {
            var content = contentService.Create(r.Name, -1, RegionAlias);
            // Slug derived from the already-unique seed Id (strip the
            // "region-" prefix) rather than a separate field — the Id
            // already disambiguates same-named regions across product
            // lines (e.g. "region-western-visayas-alon" vs "-parul").
            content.SetValue("slug", r.Id.Replace("region-", string.Empty));
            content.SetValue("country", countryUdi);
            content.SetValue("productLine", lineUdis[r.LineCode]);
            content.SetValue("description", r.Description);
            content.SetValue("highlights", string.Join('\n', r.Highlights));
            PublishOrThrow(content);
            regionUdis[r.Id] = ToUdi(content);
        }

        var destinations = new DestinationSeed[]
        {
            new("dest-coron", "Coron, Palawan", "PH", "region-palawan", "ALON",
                "Island-hopping among limestone karsts, WWII wreck diving, and the Big and Small Lagoons.", 11.9973, 120.2046,
                ["Local destination host on call during your stay", "Return airport/pier transfers", "Digital island-hopping route guide"],
                ["Private van transfer upgrade", "Kayak rental for the lagoons"]),
            new("dest-boracay", "Boracay, Aklan", "PH", "region-western-visayas-alon", "ALON",
                "The Philippines' most famous white-sand beach island — powdery sand and turquoise water off the coast of Panay.", 11.9674, 121.9248,
                ["Local destination host on call during your stay", "Return Caticlan/Kalibo airport transfers", "Beach umbrella and lounger set-up assistance"],
                ["Private van transfer upgrade", "Sunset paraw boat add-on"]),
            new("dest-el-nido", "El Nido, Palawan", "PH", "region-palawan", "ALON",
                "Limestone cliffs, hidden lagoons, and the Bacuit archipelago — the crown jewel of Palawan's island-hopping circuit.", 11.1949, 119.4079,
                ["Local destination host on call during your stay", "Return airport transfers", "Digital island-hopping route guide"],
                ["Private boat transfer upgrade", "Snorkel gear rental for the week"]),
            new("dest-siargao", "Siargao Island, Surigao del Norte", "PH", "region-caraga", "ALON",
                "Cloud 9's world-class reef break made Siargao the country's surfing capital — a laid-back island of lagoons and reliable swells nearly year-round.", 9.8000, 126.1500,
                ["Local destination host on call during your stay", "Return airport transfers", "Board storage arrangement"],
                ["Scooter rental for the week", "Extra night pre/post stay"]),
            new("dest-la-union", "San Juan, La Union", "PH", "region-ilocos-alon", "ALON",
                "The Philippines' most accessible surf town, three hours from Manila — beginner-friendly beach breaks that turned this fishing town into the surf capital of the north.", 16.5167, 120.3167,
                ["Local destination host on call during your stay", "Return transfer from San Fernando", "Surf conditions and tide update briefing"],
                ["Board rental for the week", "Private van transfer upgrade"]),
            new("dest-baguio", "Baguio", "PH", "region-cordillera-amihan", "AMIHAN",
                "The Philippines' Summer Capital — pine forests and cool air at 1,540m, without leaving the tropics.", 16.4023, 120.5960,
                ["Local destination host on call during your stay", "Return transfers from the bus terminal", "Cool-season packing tips briefing"],
                ["Strawberry farm day-trip add-on", "Private van transfer upgrade"]),
            new("dest-sagada", "Sagada, Mountain Province", "PH", "region-cordillera-amihan", "AMIHAN",
                "Hanging coffins, limestone caves, and misty pine ridgelines in the heart of the Cordillera.", 17.0837, 120.8992,
                ["Local destination host on call during your stay", "Return transfers from Baguio", "Spelunking safety briefing"],
                ["Extra guided cave add-on", "Private van transfer upgrade"]),
            new("dest-tagaytay", "Tagaytay, Cavite", "PH", "region-calabarzon", "AMIHAN",
                "Ridge-top views of Taal Volcano and Taal Lake, a cool-climate escape less than two hours from Manila.", 14.1153, 120.9621,
                ["Local destination host on call during your stay", "Return transfers from Manila", "Volcano-viewpoint recommendation briefing"],
                ["Horseback ride add-on", "Late checkout (subject to availability)"]),
            new("dest-san-fernando-pampanga", "San Fernando, Pampanga", "PH", "region-central-luzon", "PARUL",
                "The Christmas Capital of the Philippines — home of the Giant Lantern Festival.", 15.0286, 120.6898,
                ["Local destination host on call during your stay", "Return transfer from Clark/Manila", "Festival-week crowd navigation tips"],
                ["Lantern-making souvenir kit", "Private van transfer upgrade"]),
            new("dest-cebu", "Cebu City, Cebu", "PH", "region-central-visayas", "PARUL",
                "Home of the Sinulog Festival, the Philippines' grandest religious and cultural celebration, held every third Sunday of January.", 10.3157, 123.8854,
                ["Local destination host on call during your stay", "Return airport transfers", "Sinulog-week crowd navigation tips"],
                ["Lechon food-crawl add-on", "Private van transfer upgrade"]),
            new("dest-iloilo", "Iloilo City, Iloilo", "PH", "region-western-visayas-parul", "PARUL",
                "Home of the Dinagyang Festival — tribal street dancing and drumbeats honoring the Santo Niño every fourth Sunday of January.", 10.7202, 122.5621,
                ["Local destination host on call during your stay", "Return airport transfers", "Dinagyang-week crowd navigation tips"],
                ["Guimaras mango-farm add-on", "Private van transfer upgrade"]),
            new("dest-vigan", "Vigan", "PH", "region-ilocos-pamana", "PAMANA",
                "A UNESCO World Heritage colonial-era town of cobblestone streets and preserved Spanish-era houses.", 17.5747, 120.3869,
                ["Local destination host on call during your stay", "Return transfers from Laoag/Manila", "Heritage-town walking map"],
                ["Kalesa ride upgrade", "Extra night pre/post stay"]),
            new("dest-banaue", "Banaue, Ifugao", "PH", "region-cordillera-pamana", "PAMANA",
                "The 2,000-year-old Banaue Rice Terraces, carved into the Cordillera mountains by the Ifugao people — a UNESCO World Heritage site.", 16.9166, 121.0575,
                ["Local destination host on call during your stay", "Return transfers from the bus terminal", "Trekking safety briefing"],
                ["Batad overnight extension", "Private van transfer upgrade"]),
            new("dest-intramuros", "Intramuros, Manila", "PH", "region-ncr", "PAMANA",
                "The Walled City — Fort Santiago, Manila Cathedral, and cobblestone streets from Spanish colonial Manila.", 14.5895, 120.9750,
                ["Local destination host on call during your stay", "Return transfers from your Manila hotel/airport", "Walled City historical map"],
                ["Bambike heritage ride add-on", "Extended guided walking tour"]),
        };

        var destinationUdis = new Dictionary<string, string>();

        foreach (var dest in destinations)
        {
            var content = contentService.Create(dest.Name, -1, DestinationAlias);
            content.SetValue("slug", dest.Id.Replace("dest-", string.Empty));
            content.SetValue("country", dest.Country);
            content.SetValue("region", regionUdis[dest.RegionId]);
            content.SetValue("productLine", lineUdis[dest.LineCode]);
            content.SetValue("description", dest.Description);
            // The Umbraco.Decimal editor stores as decimal, not double —
            // an explicit cast rather than relying on SetValue's Object
            // parameter to convert a boxed double correctly.
            content.SetValue("latitude", (decimal)dest.Lat);
            content.SetValue("longitude", (decimal)dest.Lng);
            content.SetValue("includedPerks", string.Join('\n', dest.IncludedPerks));
            content.SetValue("optionalAddOns", string.Join('\n', dest.OptionalAddOns));
            PublishOrThrow(content);
            destinationUdis[dest.Id] = ToUdi(content);
        }

        // ADR-0017: Accommodation promoted off Product's two flat strings
        // into a real, reusable content type with its own highlights —
        // one node per product today (1:1), but now structurally able to
        // be shared across multiple products at the same destination
        // without duplicating copy, the "revisit only if..." case
        // ADR-0016 explicitly flagged.
        // HeroImageUrl reuses the same real Wikimedia photo already
        // sourced for that destination's Product — never a fabricated
        // photo of the specific (invented) property name. Honest: it's
        // genuinely a photo of the place; dishonest would be claiming it
        // depicts a specific building that doesn't exist. See ADR-0018.
        var accommodations = new AccommodationSeed[]
        {
            new("accom-coron-bayside-inn", "Coron Bayside Inn",
                "A simple harborside inn a five-minute walk from the public market and the town's island-hopping jetty.",
                ["Walking distance to the town's main strip of dive shops and restaurants", "Simple fan/AC rooms, not a resort-style property", "Front desk can arrange the island-hopping boat pickup directly"],
                "https://commons.wikimedia.org/wiki/Special:FilePath/Kayangan%20Lake%2C%20Coron%20-%20Palawan.jpg",
                "dest-coron", "PENSION_HOUSE", ["Budget-Friendly", "Convenient Location"], null),
            new("accom-session-road-pine-house", "Session Road Pine House",
                "A short tricycle ride from Burnham Park, with a fireplace lounge for Baguio's cool evenings — furnished units also welcome guests staying the whole season.",
                ["Fireplace lounge open to guests on cool Baguio evenings", "Short tricycle ride to Burnham Park and Session Road", "Continental breakfast included daily", "Monthly rates available for remote workers extending their stay"],
                "https://commons.wikimedia.org/wiki/Special:FilePath/Baguio%20City%2C%20Philippines(landscape%20view).jpg",
                "dest-baguio", "APARTEL", ["Family-Friendly", "Cozy", "Long-Stay Friendly"], 3.0),
            new("accom-lantern-city-homestay", "Lantern City Homestay",
                "A homestay in San Fernando's lantern-making district, footsteps from the festival grounds.",
                ["Run by a local family in San Fernando's lantern-making district", "Footsteps from the main festival grounds — no transport needed on festival night", "Home-cooked Kapampangan breakfast included"],
                "https://commons.wikimedia.org/wiki/Special:FilePath/WV%20banner%20San%20Fernando%20Pampanga%20giant%20Christmas%20lanterns.JPG",
                "dest-san-fernando-pampanga", "HOMESTAY", ["Homestay", "Budget-Friendly"], null),
            new("accom-calle-crisologo-heritage-house", "Calle Crisologo Heritage House",
                "A restored ancestral house on Calle Crisologo's cobblestone strip, steps from the kalesa stand — the whole house is yours, not individual rooms.",
                ["A genuinely restored 19th-century ancestral house, not a modern replica", "Directly on Calle Crisologo's cobblestone strip", "Antique furnishings throughout, capiz-shell windows"],
                "https://commons.wikimedia.org/wiki/Special:FilePath/The%20Calle%20Crisologo%20in%20Vigan%2C%20Ilocos%20Sur.jpg",
                "dest-vigan", "VACATION_RENTAL", ["Heritage Stay", "Boutique"], 3.5),
            new("accom-station-2-beachfront-inn", "Station 2 Beachfront Inn",
                "A short walk from White Beach's main strip, rooms facing a quiet garden courtyard.",
                ["A short walk from White Beach's main strip", "Garden courtyard rooms, quieter than beachfront-facing units", "Daily breakfast included"],
                "https://commons.wikimedia.org/wiki/Special:FilePath/Boracay_White_Beach_in_day_%28985286231%29.jpg",
                "dest-boracay", "PENSION_HOUSE", ["Budget-Friendly", "Family-Friendly", "Beachfront"], 2.5),
            new("accom-bacuit-bay-guesthouse", "Bacuit Bay Guesthouse",
                "A family-run guesthouse in El Nido town, a few minutes' walk from the island-hopping jetty.",
                ["Family-run, a few minutes' walk from the island-hopping jetty", "Simple, clean rooms — not a resort", "Staff can help book any of El Nido's A-D island-hopping tours"],
                "https://commons.wikimedia.org/wiki/Special:FilePath/Big_Lagoon_at_El_Nido%2C_Palawan%2C_Philippines.jpg",
                "dest-el-nido", "HOMESTAY", ["Budget-Friendly", "Family-Run"], null),
            new("accom-echo-valley-inn", "Echo Valley Inn",
                "A guesthouse on Sagada's ridge road, a short walk from the Sumaguing Cave trailhead.",
                ["On Sagada's ridge road, walking distance to the Sumaguing Cave trailhead", "Basic mountain-town accommodations, hot water available", "Can arrange a licensed local spelunking guide"],
                "https://commons.wikimedia.org/wiki/Special:FilePath/Hanging_Coffins_of_Sagada%2C_Mountain_Province.JPG",
                "dest-sagada", "PENSION_HOUSE", ["Budget-Friendly", "Mountain View"], null),
            new("accom-ridgeline-view-suites", "Ridgeline View Suites",
                "Rooms facing Taal Lake, a short drive from People's Park in the Sky.",
                ["Rooms face Taal Lake and Volcano directly", "A short drive from People's Park in the Sky", "On-site restaurant serves ridge-top dinners with a volcano view"],
                "https://commons.wikimedia.org/wiki/Special:FilePath/Taal_Lake_and_Volcano%2C_Tagaytay%2C_Philippines.jpg",
                "dest-tagaytay", "RESORT", ["Scenic View", "Family-Friendly"], 3.5),
            new("accom-colon-heritage-hotel", "Colon Heritage Hotel",
                "A hotel near Cebu City's historic Colon Street, a short walk from the Sinulog parade route.",
                ["Near Cebu City's historic Colon Street, the oldest street in the Philippines", "A short walk from the Sinulog parade route", "Book well ahead for Sinulog week — the city fills up fast", "Monthly corporate rates available"],
                "https://commons.wikimedia.org/wiki/Special:FilePath/Sinulog_Festival_of_Cebu_City.jpg",
                "dest-cebu", "HOTEL", ["Heritage Stay", "City Center", "Long-Stay Friendly"], 3.0),
            new("accom-calle-real-suites", "Calle Real Suites",
                "A hotel on Iloilo's Calle Real heritage strip, minutes from the Dinagyang tribe competition grounds.",
                ["On Iloilo's Calle Real heritage strip", "Minutes from the Dinagyang tribe competition grounds", "Rooftop view of the Iloilo River Esplanade"],
                "https://commons.wikimedia.org/wiki/Special:FilePath/Iloilo_Dinagyang_Festival.jpg",
                "dest-iloilo", "HOTEL", ["Heritage Stay", "City Center"], 3.0),
            new("accom-ifugao-view-lodge", "Ifugao View Lodge",
                "A lodge overlooking the rice terraces, run by a local Ifugao family.",
                ["Overlooks the Banaue viewpoint rice terraces directly", "Run by a local Ifugao family — home-cooked meals available", "Can arrange the trek to Batad and its own amphitheater terraces"],
                "https://commons.wikimedia.org/wiki/Special:FilePath/Banaue_Rice_Terraces_of_the_Philippines.jpg",
                "dest-banaue", "HOMESTAY", ["Family-Run", "Scenic View"], null),
            new("accom-plaza-suites-intramuros", "Plaza Suites Intramuros",
                "A boutique hotel inside the Walled City itself, steps from Fort Santiago.",
                ["Inside the Walled City itself, a rarity for Intramuros accommodations", "Steps from Fort Santiago", "Colonial-era architecture throughout the property"],
                "https://commons.wikimedia.org/wiki/Special:FilePath/Fort_Santiago%2C_Intramuros.JPG",
                "dest-intramuros", "HOTEL", ["Boutique", "Heritage Stay"], 4.0),
            new("accom-cloud-9-surf-lodge", "Cloud 9 Surf Lodge",
                "A short walk from the Cloud 9 boardwalk, with board storage and a resident surf coach on call.",
                ["A short walk from the Cloud 9 boardwalk and reef break", "Board storage and a resident surf coach on call", "Popular with repeat surfers — book ahead in peak season (Aug-Nov)", "A real hub for surfers extending their stay for a season"],
                "https://commons.wikimedia.org/wiki/Special:FilePath/Cloud%209%20Boardwalk%2C%20Siargao.jpg",
                "dest-siargao", "HOSTEL", ["Surf-Friendly", "Budget-Friendly", "Long-Stay Friendly"], 3.0),
            new("accom-urbiztondo-beachfront-lodge", "Urbiztondo Beachfront Lodge",
                "Steps from Urbiztondo Beach's surf break, with a rooftop deck facing the sunset.",
                ["Steps from Urbiztondo Beach's beginner-friendly surf break", "Rooftop deck facing the sunset", "Walking distance to San Juan's cafe and restaurant strip", "Popular with surfers staying the whole season"],
                "https://commons.wikimedia.org/wiki/Special:FilePath/Surfing%20Capital%20of%20the%20Northern%20Philippines.jpg",
                "dest-la-union", "HOSTEL", ["Surf-Friendly", "Beachfront", "Long-Stay Friendly"], 2.5),
        };

        var accommodationUdis = new Dictionary<string, string>();
        var accommodationHeroImages = new Dictionary<string, string>();

        foreach (var a in accommodations)
        {
            var content = contentService.Create(a.Name, -1, AccommodationAlias);
            content.SetValue("description", a.Description);
            content.SetValue("highlights", string.Join('\n', a.Highlights));
            content.SetValue("heroImageUrl", a.HeroImageUrl);
            content.SetValue("destination", destinationUdis[a.DestinationId]);
            content.SetValue("type", a.Type);
            content.SetValue("tags", string.Join('\n', a.Tags));
            if (a.OfficialRating is { } rating)
            {
                content.SetValue("officialRating", (decimal)rating);
            }
            PublishOrThrow(content);
            accommodationUdis[a.Id] = ToUdi(content);
            // ADR-0020: RoomType.HeroImageUrl reuses this same real photo —
            // an honest choice (a real photo of the place, never a
            // fabricated distinct-room interior), captured here so the
            // room-seeding loop below doesn't need its own lookup.
            accommodationHeroImages[a.Id] = a.HeroImageUrl;
        }

        // ADR-0019: 2 real room options per Accommodation — a
        // cheaper/simpler one and a pricier/larger one — the "find rooms
        // in that hotel that can be an option" half of the Inghams
        // research. Flat + picker, queried by accommodationId (see
        // RoomType.cs), not embedded.
        var roomTypes = new RoomTypeSeed[]
        {
            new("Standard Fan Room", "A simple fan-cooled room facing the inn's inner corridor.", "14-16m²", "One double bed", 2, "ROOM_ONLY",
                """[{"label":"Standard rate","startDate":"2026-09-01T00:00:00Z","endDate":"2027-08-31T00:00:00Z","pricePhp":1400}]""", "accom-coron-bayside-inn"),
            new("Family AC Room", "An air-conditioned room with extra sleeping space for a family group.", "20-24m²", "One double bed and a single bed", 3, "ROOM_ONLY",
                """[{"label":"Standard rate","startDate":"2026-09-01T00:00:00Z","endDate":"2027-08-31T00:00:00Z","pricePhp":2200}]""", "accom-coron-bayside-inn"),

            // ADR-0020: Baguio's cool climate and remote-work-friendly
            // reputation (confirmed via research as a real Philippine
            // digital-nomad destination) make this the room reframed for
            // long-stay guests — monthly rate ≈ 35% off the nightly rate
            // over 30 nights, matching the real-world discount pattern.
            new("Pine House Standard Room", "A cozy room with mountain-town charm, walking distance to Session Road.", "16-18m²", "One queen bed", 2, "BREAKFAST",
                """[{"label":"Standard rate","startDate":"2026-09-01T00:00:00Z","endDate":"2027-08-31T00:00:00Z","pricePhp":1800}]""", "accom-session-road-pine-house",
                MonthlyRatePhp: 35000m),
            new("Pine House Family Room", "A larger room with fireplace-lounge access, good for families escaping the heat.", "24-28m²", "One queen bed and a double sofa bed", 4, "BREAKFAST",
                """[{"label":"Standard rate","startDate":"2026-09-01T00:00:00Z","endDate":"2027-08-31T00:00:00Z","pricePhp":3200}]""", "accom-session-road-pine-house"),

            new("Homestay Standard Room", "A simple homestay room in the family's own house.", "12-14m²", "One double bed", 2, "BREAKFAST",
                """[{"label":"Standard rate","startDate":"2026-09-01T00:00:00Z","endDate":"2027-08-31T00:00:00Z","pricePhp":1200}]""", "accom-lantern-city-homestay"),
            new("Homestay Family Room", "A larger shared-house room, good for festival-week family groups.", "18-20m²", "Two double beds", 4, "BREAKFAST",
                """[{"label":"Standard rate","startDate":"2026-09-01T00:00:00Z","endDate":"2027-08-31T00:00:00Z","pricePhp":2000}]""", "accom-lantern-city-homestay"),

            new("Heritage Room", "An antique-furnished room in the restored 19th-century house.", "18-20m²", "One four-poster double bed", 2, "BREAKFAST",
                """[{"label":"Standard rate","startDate":"2026-09-01T00:00:00Z","endDate":"2027-08-31T00:00:00Z","pricePhp":2800}]""", "accom-calle-crisologo-heritage-house"),
            new("Heritage Suite", "A larger heritage room with a sitting area, capiz-shell windows on two sides.", "28-32m²", "One four-poster double bed and a daybed", 3, "BREAKFAST",
                """[{"label":"Standard rate","startDate":"2026-09-01T00:00:00Z","endDate":"2027-08-31T00:00:00Z","pricePhp":4500}]""", "accom-calle-crisologo-heritage-house"),

            new("Garden View Room", "A budget-friendly room facing the inn's quiet garden courtyard.", "16-18m²", "One double bed", 2, "BREAKFAST",
                """[{"label":"Standard rate","startDate":"2026-09-01T00:00:00Z","endDate":"2027-08-31T00:00:00Z","pricePhp":2200}]""", "accom-station-2-beachfront-inn",
                MonthlyRatePhp: 43000m),
            new("Beachfront Family Room", "A larger room with extra beds, a short walk from White Beach's main strip.", "24-26m²", "One double bed and two single beds", 4, "BREAKFAST",
                """[{"label":"Standard rate","startDate":"2026-09-01T00:00:00Z","endDate":"2027-08-31T00:00:00Z","pricePhp":3800}]""", "accom-station-2-beachfront-inn"),

            new("Bacuit Bay Standard Room", "A simple, clean room a few minutes' walk from the jetty.", "14-16m²", "One double bed", 2, "ROOM_ONLY",
                """[{"label":"Standard rate","startDate":"2026-09-01T00:00:00Z","endDate":"2027-08-31T00:00:00Z","pricePhp":1600}]""", "accom-bacuit-bay-guesthouse"),
            new("Bacuit Bay Family Room", "A larger room for families booking El Nido's island-hopping tours together.", "22-24m²", "One double bed and a bunk bed", 4, "ROOM_ONLY",
                """[{"label":"Standard rate","startDate":"2026-09-01T00:00:00Z","endDate":"2027-08-31T00:00:00Z","pricePhp":2800}]""", "accom-bacuit-bay-guesthouse"),

            new("Echo Valley Standard Room", "A basic mountain-town room with hot water available.", "14-16m²", "One double bed", 2, "ROOM_ONLY",
                """[{"label":"Standard rate","startDate":"2026-09-01T00:00:00Z","endDate":"2027-08-31T00:00:00Z","pricePhp":1300}]""", "accom-echo-valley-inn"),
            new("Echo Valley Family Room", "A larger room near the ridge road, good for group caving trips.", "20-22m²", "Two double beds", 4, "ROOM_ONLY",
                """[{"label":"Standard rate","startDate":"2026-09-01T00:00:00Z","endDate":"2027-08-31T00:00:00Z","pricePhp":2400}]""", "accom-echo-valley-inn"),

            new("Lake View Room", "A room facing Taal Lake and Volcano directly.", "18-20m²", "One queen bed", 2, "BREAKFAST",
                """[{"label":"Standard rate","startDate":"2026-09-01T00:00:00Z","endDate":"2027-08-31T00:00:00Z","pricePhp":2600}]""", "accom-ridgeline-view-suites"),
            new("Ridgeline Family Suite", "A larger suite with a sitting area, still facing the volcano view.", "28-30m²", "One queen bed and a double sofa bed", 4, "BREAKFAST",
                """[{"label":"Standard rate","startDate":"2026-09-01T00:00:00Z","endDate":"2027-08-31T00:00:00Z","pricePhp":4200}]""", "accom-ridgeline-view-suites"),

            new("Colon Heritage Standard Room", "A comfortable room near Cebu's historic Colon Street.", "18-20m²", "One queen bed", 2, "BREAKFAST",
                """[{"label":"Standard rate","startDate":"2026-09-01T00:00:00Z","endDate":"2027-08-31T00:00:00Z","pricePhp":2400}]""", "accom-colon-heritage-hotel",
                MonthlyRatePhp: 47000m),
            new("Colon Heritage Family Room", "A larger room, useful for booking well ahead of Sinulog week.", "26-28m²", "Two queen beds", 4, "BREAKFAST",
                """[{"label":"Standard rate","startDate":"2026-09-01T00:00:00Z","endDate":"2027-08-31T00:00:00Z","pricePhp":4000}]""", "accom-colon-heritage-hotel"),

            new("Calle Real Standard Room", "A room on Iloilo's Calle Real heritage strip.", "18-20m²", "One queen bed", 2, "BREAKFAST",
                """[{"label":"Standard rate","startDate":"2026-09-01T00:00:00Z","endDate":"2027-08-31T00:00:00Z","pricePhp":2300}]""", "accom-calle-real-suites"),
            new("Calle Real Family Suite", "A rooftop-facing suite with river-esplanade views.", "26-28m²", "One queen bed and a double sofa bed", 4, "BREAKFAST",
                """[{"label":"Standard rate","startDate":"2026-09-01T00:00:00Z","endDate":"2027-08-31T00:00:00Z","pricePhp":3900}]""", "accom-calle-real-suites"),

            new("Ifugao View Standard Room", "A simple room overlooking the Banaue viewpoint terraces.", "14-16m²", "One double bed", 2, "HALF_BOARD",
                """[{"label":"Standard rate","startDate":"2026-09-01T00:00:00Z","endDate":"2027-08-31T00:00:00Z","pricePhp":1800}]""", "accom-ifugao-view-lodge"),
            new("Ifugao View Family Room", "A larger room, good for groups trekking to Batad together.", "20-22m²", "Two double beds", 4, "HALF_BOARD",
                """[{"label":"Standard rate","startDate":"2026-09-01T00:00:00Z","endDate":"2027-08-31T00:00:00Z","pricePhp":3000}]""", "accom-ifugao-view-lodge"),

            new("Colonial Room", "A colonial-era-styled room inside the Walled City itself.", "20-22m²", "One queen bed", 2, "BREAKFAST",
                """[{"label":"Standard rate","startDate":"2026-09-01T00:00:00Z","endDate":"2027-08-31T00:00:00Z","pricePhp":3200}]""", "accom-plaza-suites-intramuros"),
            new("Colonial Suite", "A larger suite with period furnishings, steps from Fort Santiago.", "32-36m²", "One king bed and a daybed", 3, "BREAKFAST",
                """[{"label":"Standard rate","startDate":"2026-09-01T00:00:00Z","endDate":"2027-08-31T00:00:00Z","pricePhp":5200}]""", "accom-plaza-suites-intramuros"),

            new("Cloud 9 Standard Room", "A simple room a short walk from the Cloud 9 boardwalk.", "16-18m²", "One double bed", 2, "BREAKFAST",
                """[{"label":"Standard rate","startDate":"2026-09-01T00:00:00Z","endDate":"2027-08-31T00:00:00Z","pricePhp":2000}]""", "accom-cloud-9-surf-lodge",
                MonthlyRatePhp: 39000m),
            new("Surf Family Room", "A larger room with board storage, popular with repeat surfer families.", "24-26m²", "One double bed and two single beds", 4, "BREAKFAST",
                """[{"label":"Standard rate","startDate":"2026-09-01T00:00:00Z","endDate":"2027-08-31T00:00:00Z","pricePhp":3400}]""", "accom-cloud-9-surf-lodge"),

            new("Urbiztondo Standard Room", "A simple room steps from Urbiztondo Beach's surf break.", "14-16m²", "One double bed", 2, "ROOM_ONLY",
                """[{"label":"Standard rate","startDate":"2026-09-01T00:00:00Z","endDate":"2027-08-31T00:00:00Z","pricePhp":1500}]""", "accom-urbiztondo-beachfront-lodge",
                MonthlyRatePhp: 29000m),
            new("Urbiztondo Family Room", "A larger room with rooftop-deck access, good for a family surf weekend.", "22-24m²", "One double bed and a bunk bed", 4, "ROOM_ONLY",
                """[{"label":"Standard rate","startDate":"2026-09-01T00:00:00Z","endDate":"2027-08-31T00:00:00Z","pricePhp":2600}]""", "accom-urbiztondo-beachfront-lodge"),
        };

        foreach (var rt in roomTypes)
        {
            var content = contentService.Create(rt.Name, -1, RoomTypeAlias);
            content.SetValue("accommodation", accommodationUdis[rt.AccommodationId]);
            content.SetValue("description", rt.Description);
            content.SetValue("sizeSqm", rt.SizeSqm);
            content.SetValue("bedConfiguration", rt.BedConfiguration);
            content.SetValue("maxOccupancy", rt.MaxOccupancy);
            content.SetValue("boardBasis", rt.BoardBasis);
            content.SetValue("priceBands", rt.PriceBandsJson);
            // ADR-0020: reuse the owning Accommodation's own real photo —
            // never a fabricated distinct-room interior (same honesty
            // rule ADR-0018 established for Accommodation itself).
            content.SetValue("heroImageUrl", accommodationHeroImages[rt.AccommodationId]);
            if (rt.MonthlyRatePhp is { } monthlyRate)
            {
                content.SetValue("monthlyRatePhp", monthlyRate);
            }
            PublishOrThrow(content);
        }

        // ADR-0020: a real, independently bookable local activity per
        // destination at a fixed, agreed-upfront price — Lakbay's answer
        // to "buying directly from a local risks getting scammed or
        // overpriced." Every inclusion itemized in writing, matching the
        // real Klook/GetYourGuide anti-scam pattern confirmed via
        // research, with realistic PHP pricing grounded in that same
        // research (a vetted island-hopping trip runs ₱1,500-2,500/person).
        var activities = new ActivitySeed[]
        {
            new("Ultimate Island Hopping Tour", "Kayangan Lake, Barracuda Lake, and Twin Lagoon in one full day out of Coron town.", "Full-day (7 hours)", 1800m,
                ["Licensed boatman and guide", "Snorkeling gear", "Packed lunch", "All entrance/environmental fees", "Life jackets"],
                "https://commons.wikimedia.org/wiki/Special:FilePath/Kayangan%20Lake%2C%20Coron%20-%20Palawan.jpg", "dest-coron"),
            new("Sunset Sailing & Island Hopping", "A traditional paraw boat sail with a snorkeling stop at Crystal Cove.", "Half-day (4 hours)", 1200m,
                ["Licensed paraw boat crew", "Snorkeling stop at Crystal Cove", "Bottled water", "Life jackets"],
                "https://commons.wikimedia.org/wiki/Special:FilePath/Boracay_White_Beach_in_day_%28985286231%29.jpg", "dest-boracay"),
            new("Bacuit Bay Island Hopping (Tour C)", "Hidden Beach, Matinloc Shrine, and Secret Beach across El Nido's Bacuit Bay.", "Full-day (7 hours)", 1900m,
                ["DENR environmental fee", "Licensed boat crew", "Snorkeling gear", "Set lunch", "Life jackets"],
                "https://commons.wikimedia.org/wiki/Special:FilePath/Big_Lagoon_at_El_Nido%2C_Palawan%2C_Philippines.jpg", "dest-el-nido"),
            new("Cloud 9 Surf Lesson (Beginner)", "A beginner surf lesson at Siargao's world-famous Cloud 9 reef break.", "2 hours", 1000m,
                ["Certified surf instructor", "Board rental", "Rashguard", "Photos of your session"],
                "https://commons.wikimedia.org/wiki/Special:FilePath/Cloud%209%20Boardwalk%2C%20Siargao.jpg", "dest-siargao"),
            new("Urbiztondo Surf Lesson (Beginner)", "A beginner-friendly surf lesson at La Union's most established surf beach.", "2 hours", 800m,
                ["Certified surf instructor", "Board and rashguard rental", "Bottled water"],
                "https://commons.wikimedia.org/wiki/Special:FilePath/Surfing%20Capital%20of%20the%20Northern%20Philippines.jpg", "dest-la-union"),
            new("Burnham Park & Session Road Heritage Walk", "A guided walk through Baguio's Burnham Park, Session Road, and Mines View Park.", "Half-day (3 hours)", 600m,
                ["Licensed local guide", "Mines View Park stop", "Transport between stops"],
                "https://commons.wikimedia.org/wiki/Special:FilePath/Baguio%20City%2C%20Philippines(landscape%20view).jpg", "dest-baguio"),
            new("Sumaguing Cave Spelunking (Guided)", "A guided descent into Sagada's best-known limestone cave system.", "Half-day (4 hours)", 800m,
                ["DENR permit fee", "Licensed spelunking guide", "Headlamp rental"],
                "https://commons.wikimedia.org/wiki/Special:FilePath/Hanging_Coffins_of_Sagada%2C_Mountain_Province.JPG", "dest-sagada"),
            new("Taal Lake Volcano Crater Hike & Boat Crossing", "A round-trip boat crossing and crater hike at one of the world's smallest active volcanoes.", "Half-day (5 hours)", 1500m,
                ["Round-trip boat crossing", "Horse or hike option", "Local guide", "Environmental fee"],
                "https://commons.wikimedia.org/wiki/Special:FilePath/Taal_Lake_and_Volcano%2C_Tagaytay%2C_Philippines.jpg", "dest-tagaytay"),
            new("Giant Lantern Festival Grounds Guided Visit", "A guided walkthrough of San Fernando's lantern-making district and festival grounds.", "Half-day (3 hours)", 500m,
                ["Licensed local guide", "Lantern-making district walkthrough", "Festival-night viewing spot arrangement"],
                "https://commons.wikimedia.org/wiki/Special:FilePath/WV%20banner%20San%20Fernando%20Pampanga%20giant%20Christmas%20lanterns.JPG", "dest-san-fernando-pampanga"),
            new("Sinulog Grand Parade Viewing Deck Pass", "A reserved viewing area for Cebu's grandest religious and cultural celebration.", "Full-day (6 hours)", 1200m,
                ["Reserved viewing area", "Licensed local guide", "Bottled water", "Basilica del Santo Niño guided visit"],
                "https://commons.wikimedia.org/wiki/Special:FilePath/Sinulog_Festival_of_Cebu_City.jpg", "dest-cebu"),
            new("Dinagyang Tribe Competition Viewing Pass", "A reserved viewing area for Iloilo's tribal street-dance competition.", "Full-day (6 hours)", 1100m,
                ["Reserved viewing area", "Licensed local guide", "Molo Church heritage stop"],
                "https://commons.wikimedia.org/wiki/Special:FilePath/Iloilo_Dinagyang_Festival.jpg", "dest-iloilo"),
            new("Calle Crisologo Kalesa Heritage Tour", "A horse-cart tour of Vigan's cobblestone heritage strip.", "Half-day (3 hours)", 700m,
                ["Licensed kalesa driver-guide", "Bantay Bell Tower stop", "Pottery workshop visit"],
                "https://commons.wikimedia.org/wiki/Special:FilePath/The%20Calle%20Crisologo%20in%20Vigan%2C%20Ilocos%20Sur.jpg", "dest-vigan"),
            new("Batad Rice Terraces Guided Trek", "A guided trek to the amphitheater-shaped Batad rice terrace cluster.", "Full-day (7 hours)", 1300m,
                ["Licensed Ifugao trekking guide", "DENR/community fee", "Packed lunch"],
                "https://commons.wikimedia.org/wiki/Special:FilePath/Banaue_Rice_Terraces_of_the_Philippines.jpg", "dest-banaue"),
            new("Walled City Guided Walking Tour", "Fort Santiago and San Agustin Church on a guided walk through Intramuros.", "Half-day (3 hours)", 650m,
                ["Licensed local guide", "Fort Santiago entrance fee", "Calesa ride segment"],
                "https://commons.wikimedia.org/wiki/Special:FilePath/Fort_Santiago%2C_Intramuros.JPG", "dest-intramuros"),
        };

        foreach (var act in activities)
        {
            var content = contentService.Create(act.Name, -1, ActivityAlias);
            content.SetValue("destination", destinationUdis[act.DestinationId]);
            content.SetValue("description", act.Description);
            content.SetValue("durationLabel", act.DurationLabel);
            content.SetValue("pricePhp", act.PricePhp);
            content.SetValue("includes", string.Join('\n', act.Includes));
            content.SetValue("heroImageUrl", act.HeroImageUrl);
            PublishOrThrow(content);
        }

        var products = new ProductSeed[]
        {
            new("Coron Island Hopping, 3 Days 2 Nights", "coron-island-hopping-3d2n", "ALON", "dest-coron",
                "Big Lagoon, Kayangan Lake, and Skeleton Wreck — three days of island-hopping out of Coron town.", 3, "HALF_BOARD",
                """[{"label":"Regular season","startDate":"2026-11-01T00:00:00Z","endDate":"2027-04-30T00:00:00Z","pricePhp":12500}]""",
                "accom-coron-bayside-inn",
                ["Big Lagoon and Kayangan Lake island-hopping boat tour", "Skeleton Wreck snorkeling stop", "Packed lunch on the boat", "Return airport transfers"],
                ["Twin-tank wreck diving for certified divers", "Calauit Safari Park day trip", "Private boat upgrade (skip the shared tour)"],
                "https://commons.wikimedia.org/wiki/Special:FilePath/Kayangan%20Lake%2C%20Coron%20-%20Palawan.jpg"),
            new("Baguio Cool-Weather Weekend, 2 Days 1 Night", "baguio-cool-weekend-2d1n", "AMIHAN", "dest-baguio",
                "Session Road, Burnham Park, and a Mines View Park sunrise — a short highland escape from Manila heat.", 2, "BREAKFAST",
                """[{"label":"Weekend rate","startDate":"2026-09-01T00:00:00Z","endDate":"2027-02-28T00:00:00Z","pricePhp":6800}]""",
                "accom-session-road-pine-house",
                ["Guided Burnham Park & Session Road walking tour", "Mines View Park sunrise viewing", "Daily breakfast"],
                ["Strawberry picking day trip to La Trinidad", "Tam-awan Village art tour", "Camp John Hay pine forest walk"],
                "https://commons.wikimedia.org/wiki/Special:FilePath/Baguio%20City%2C%20Philippines(landscape%20view).jpg"),
            new("Giant Lantern Festival Day Trip", "giant-lantern-festival-day-trip", "PARUL", "dest-san-fernando-pampanga",
                "A guided day trip to San Fernando's Giant Lantern Festival, the Philippines' answer to a light-tourism holiday.", 1, "ROOM_ONLY",
                """[{"label":"December season","startDate":"2026-12-01T00:00:00Z","endDate":"2026-12-24T00:00:00Z","pricePhp":2200}]""",
                "accom-lantern-city-homestay",
                ["Guided Giant Lantern Festival viewing", "Lantern-making workshop visit", "Round-trip transfer from Clark/Manila"],
                ["Pampanga culinary trail (sisig, Susie's cuisine)", "Make-your-own lantern souvenir workshop"],
                "https://commons.wikimedia.org/wiki/Special:FilePath/WV%20banner%20San%20Fernando%20Pampanga%20giant%20Christmas%20lanterns.JPG"),
            new("Vigan Heritage Walk, 2 Days 1 Night", "vigan-heritage-walk-2d1n", "PAMANA", "dest-vigan",
                "Calle Crisologo by kalesa, Bantay Bell Tower, and a pottery-making visit in a UNESCO World Heritage town.", 2, "BREAKFAST",
                """[{"label":"Regular season","startDate":"2026-09-01T00:00:00Z","endDate":"2027-08-31T00:00:00Z","pricePhp":7400}]""",
                "accom-calle-crisologo-heritage-house",
                ["Kalesa (horse-cart) heritage tour", "Bantay Bell Tower visit", "Pottery-making workshop at a local burnayan"],
                ["Baluarte Zoo add-on", "Longganisa & bagnet cooking class", "Sunset photography tour"],
                "https://commons.wikimedia.org/wiki/Special:FilePath/The%20Calle%20Crisologo%20in%20Vigan%2C%20Ilocos%20Sur.jpg"),
            new("Boracay White Beach Getaway, 3 Days 2 Nights", "boracay-white-beach-getaway-3d2n", "ALON", "dest-boracay",
                "Sunbathing on the world-famous White Beach, sunset sailing, and watersports off Station 1 and 2.", 3, "BREAKFAST",
                """[{"label":"Regular season","startDate":"2026-11-01T00:00:00Z","endDate":"2027-05-31T00:00:00Z","pricePhp":15800}]""",
                "accom-station-2-beachfront-inn",
                ["Sunset sailing on a traditional paraw boat", "Full-day island hopping (Crystal Cove, Crocodile Island, Puka Shell Beach)", "Daily breakfast"],
                ["Parasailing", "Helmet diving", "Cliff diving at Ariel's Point (with lunch)"],
                "https://commons.wikimedia.org/wiki/Special:FilePath/Boracay_White_Beach_in_day_%28985286231%29.jpg"),
            new("El Nido Island Hopping, 4 Days 3 Nights", "el-nido-island-hopping-4d3n", "ALON", "dest-el-nido",
                "The Big and Small Lagoons, Secret Beach, and Bacuit Bay's limestone islands across four island-hopping tours.", 4, "HALF_BOARD",
                """[{"label":"Regular season","startDate":"2026-11-01T00:00:00Z","endDate":"2027-05-31T00:00:00Z","pricePhp":19500}]""",
                "accom-bacuit-bay-guesthouse",
                ["Tour A: Big & Small Lagoon, Secret Lagoon, Shimizu Island", "Tour C: Hidden Beach, Matinloc Shrine, Secret Beach", "Daily set lunch on the boat", "Snorkeling gear"],
                ["Tour D: Bat Cave and Ubugon Rock", "Sunset sailing cruise", "Private boat charter"],
                "https://commons.wikimedia.org/wiki/Special:FilePath/Big_Lagoon_at_El_Nido%2C_Palawan%2C_Philippines.jpg"),
            new("Sagada Caves & Hanging Coffins, 3 Days 2 Nights", "sagada-caves-hanging-coffins-3d2n", "AMIHAN", "dest-sagada",
                "Sumaguing Cave spelunking, the centuries-old hanging coffins, and sunrise at Kiltepan viewpoint.", 3, "HALF_BOARD",
                """[{"label":"Regular season","startDate":"2026-09-01T00:00:00Z","endDate":"2027-05-31T00:00:00Z","pricePhp":9200}]""",
                "accom-echo-valley-inn",
                ["Sumaguing Cave guided spelunking", "Hanging Coffins viewing walk", "Sunrise viewing at Kiltepan"],
                ["Cave connection (Sumaguing-to-Lumiang) for experienced spelunkers", "Bomod-ok Falls trek", "Weaving workshop with a local cooperative"],
                "https://commons.wikimedia.org/wiki/Special:FilePath/Hanging_Coffins_of_Sagada%2C_Mountain_Province.JPG"),
            new("Tagaytay Taal Volcano Weekend, 2 Days 1 Night", "tagaytay-taal-volcano-weekend-2d1n", "AMIHAN", "dest-tagaytay",
                "Ridge-top dining with a Taal Volcano view, People's Park in the Sky, and a Taal Lake boat crossing.", 2, "BREAKFAST",
                """[{"label":"Weekend rate","startDate":"2026-09-01T00:00:00Z","endDate":"2027-08-31T00:00:00Z","pricePhp":6200}]""",
                "accom-ridgeline-view-suites",
                ["Ridge-top dinner with a Taal Volcano view", "People's Park in the Sky visit", "Taal Lake boat crossing"],
                ["Taal Volcano crater hike (weather-permitting)", "Sky Ranch amusement park add-on", "Picnic Grove zip line"],
                "https://commons.wikimedia.org/wiki/Special:FilePath/Taal_Lake_and_Volcano%2C_Tagaytay%2C_Philippines.jpg"),
            new("Sinulog Festival Experience, 3 Days 2 Nights", "sinulog-festival-experience-3d2n", "PARUL", "dest-cebu",
                "Front-row access to the Sinulog grand parade, a Basilica del Santo Niño visit, and Cebu City heritage sites.", 3, "BREAKFAST",
                """[{"label":"January season","startDate":"2027-01-15T00:00:00Z","endDate":"2027-01-22T00:00:00Z","pricePhp":13500}]""",
                "accom-colon-heritage-hotel",
                ["Front-row Sinulog grand parade viewing area", "Basilica del Santo Niño guided visit", "Fort San Pedro heritage walk"],
                ["Sinulog street party pass", "Cebu City food crawl (lechon, dried mangoes)", "Mactan Island day trip add-on"],
                "https://commons.wikimedia.org/wiki/Special:FilePath/Sinulog_Festival_of_Cebu_City.jpg"),
            new("Dinagyang Festival Experience, 3 Days 2 Nights", "dinagyang-festival-experience-3d2n", "PARUL", "dest-iloilo",
                "Tribal street-dance competitions, drumbeats, and Iloilo City's own festival food trail.", 3, "BREAKFAST",
                """[{"label":"January season","startDate":"2027-01-20T00:00:00Z","endDate":"2027-01-27T00:00:00Z","pricePhp":12800}]""",
                "accom-calle-real-suites",
                ["Dinagyang tribe competition viewing pass", "Molo Church & Jaro Cathedral heritage tour", "Iloilo River Esplanade walk"],
                ["Dinagyang street food trail", "Guimaras Island day trip (mango farm visit)"],
                "https://commons.wikimedia.org/wiki/Special:FilePath/Iloilo_Dinagyang_Festival.jpg"),
            new("Banaue Rice Terraces Trek, 3 Days 2 Nights", "banaue-rice-terraces-trek-3d2n", "PAMANA", "dest-banaue",
                "A guided trek through the 2,000-year-old Banaue Rice Terraces and Ifugao villages, a UNESCO World Heritage site.", 3, "HALF_BOARD",
                """[{"label":"Regular season","startDate":"2026-09-01T00:00:00Z","endDate":"2027-05-31T00:00:00Z","pricePhp":10600}]""",
                "accom-ifugao-view-lodge",
                ["Guided trek through the Banaue viewpoint terraces", "Batad village and waterfall trek", "Ifugao village cultural visit"],
                ["Overnight homestay extension in Batad", "Wood-carving workshop with a local artisan", "Kiangan War Memorial day trip"],
                "https://commons.wikimedia.org/wiki/Special:FilePath/Banaue_Rice_Terraces_of_the_Philippines.jpg"),
            new("Intramuros Heritage Walk, 1 Day", "intramuros-heritage-walk-1d", "PAMANA", "dest-intramuros",
                "A walking tour of the Walled City — Fort Santiago, Manila Cathedral, San Agustin Church, and Calle Real.", 1, "ROOM_ONLY",
                """[{"label":"Year-round","startDate":"2026-09-01T00:00:00Z","endDate":"2027-08-31T00:00:00Z","pricePhp":1800}]""",
                "accom-plaza-suites-intramuros",
                ["Guided Fort Santiago & Rizal Shrine tour", "Manila Cathedral and San Agustin Church visit", "Calesa ride along the walls"],
                ["Bambike (bamboo bike) heritage ride", "Museo de Intramuros add-on", "Night walking ghost tour"],
                "https://commons.wikimedia.org/wiki/Special:FilePath/Fort_Santiago%2C_Intramuros.JPG"),
            new("Siargao Surf Camp, 5 Days 4 Nights", "siargao-surf-camp-5d4n", "ALON", "dest-siargao",
                "Daily surf lessons at Cloud 9 and Jacking Horse, an island-hopping day, and a scooter to explore lagoons and waterfalls between sessions.", 5, "BREAKFAST",
                """[{"label":"Regular season","startDate":"2026-09-01T00:00:00Z","endDate":"2027-05-31T00:00:00Z","pricePhp":14000}]""",
                "accom-cloud-9-surf-lodge",
                ["Daily guided surf lessons (beginner to intermediate)", "Board rental", "Naked Island, Daku Island & Guyam Island hopping tour", "Airport transfers"],
                ["Sugba Lagoon day trip with stand-up paddleboarding", "Magpupungko rock pools day trip", "Video-analysis coaching session for advanced surfers"],
                "https://commons.wikimedia.org/wiki/Special:FilePath/Cloud%209%20Boardwalk%2C%20Siargao.jpg"),
            new("La Union Surf Weekend, 3 Days 2 Nights", "la-union-surf-weekend-3d2n", "ALON", "dest-la-union",
                "Beginner-friendly beach breaks, a full weekend of guided surf lessons, and San Juan's beach-town cafe strip within walking distance.", 3, "ROOM_ONLY",
                """[{"label":"Regular season","startDate":"2026-09-01T00:00:00Z","endDate":"2027-08-31T00:00:00Z","pricePhp":5500}]""",
                "accom-urbiztondo-beachfront-lodge",
                ["2 guided surf lessons", "Board and rashguard rental", "Airport transfer from San Fernando"],
                ["Third-day add-on surf lesson", "Bacnotan waterfalls day trip", "Surfboard purchase discount voucher"],
                "https://commons.wikimedia.org/wiki/Special:FilePath/Surfing%20Capital%20of%20the%20Northern%20Philippines.jpg"),
        };

        foreach (var p in products)
        {
            var content = contentService.Create(p.Name, -1, ProductAlias);
            content.SetValue("slug", p.Slug);
            content.SetValue("productLine", lineUdis[p.LineCode]);
            content.SetValue("destination", destinationUdis[p.DestinationId]);
            content.SetValue("summary", p.Summary);
            content.SetValue("itineraryDays", p.Days);
            content.SetValue("boardBasis", p.BoardBasis);
            content.SetValue("priceBands", p.PriceBandsJson);
            content.SetValue("accommodation", accommodationUdis[p.AccommodationId]);
            content.SetValue("includedActivities", string.Join('\n', p.IncludedActivities));
            content.SetValue("optionalActivities", string.Join('\n', p.OptionalActivities));
            content.SetValue("heroImageUrl", p.HeroImageUrl);
            PublishOrThrow(content);
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Seed-data shape for one Product — a record instead of an unnamed
    /// tuple once accommodation/activity fields pushed the field count to
    /// 13; a positional tuple literal that wide stops being reviewable at
    /// a glance. Carries an AccommodationId reference (ADR-0017) into the
    /// accommodations dictionary, same pattern DestinationId already used.
    /// </summary>
    private sealed record ProductSeed(
        string Name, string Slug, string LineCode, string DestinationId,
        string Summary, int Days, string BoardBasis, string PriceBandsJson,
        string AccommodationId,
        string[] IncludedActivities, string[] OptionalActivities,
        string HeroImageUrl);

    /// <summary>Seed-data shape for one Region (ADR-0017).</summary>
    private sealed record RegionSeed(
        string Id, string Name, string LineCode, string Description, string[] Highlights);

    /// <summary>Seed-data shape for one Destination — a record, not a positional tuple, once ADR-0019's perk fields pushed the field count to 10 (same reasoning as ProductSeed).</summary>
    private sealed record DestinationSeed(
        string Id, string Name, string Country, string RegionId, string LineCode,
        string Description, double Lat, double Lng,
        string[] IncludedPerks, string[] OptionalAddOns);

    /// <summary>Seed-data shape for one Accommodation. DestinationId/Tags/OfficialRating added in ADR-0019; Type added in ADR-0020 — a real Philippine accommodation category, not every stay is a Hotel.</summary>
    private sealed record AccommodationSeed(
        string Id, string Name, string Description, string[] Highlights, string HeroImageUrl,
        string DestinationId, string Type, string[] Tags, double? OfficialRating);

    /// <summary>
    /// Seed-data shape for one RoomType (ADR-0019) — a real "pick your
    /// room" option within an Accommodation. MonthlyRatePhp added in
    /// ADR-0020, only set where a longer stay is realistic. HeroImageUrl
    /// isn't authored here — it's derived by reusing the owning
    /// Accommodation's own photo (see the seeding loop below), not
    /// distinct per-room seed data.
    /// </summary>
    private sealed record RoomTypeSeed(
        string Name, string Description, string SizeSqm, string BedConfiguration,
        int MaxOccupancy, string BoardBasis, string PriceBandsJson, string AccommodationId,
        decimal? MonthlyRatePhp = null);

    /// <summary>Seed-data shape for one Activity (ADR-0020) — a real, independently bookable local activity at a fixed price.</summary>
    private sealed record ActivitySeed(
        string Name, string Description, string DurationLabel, decimal PricePhp,
        string[] Includes, string HeroImageUrl, string DestinationId);

    /// <summary>
    /// LAKBAY_FORCE_RESEED_CATALOG's cleanup step — goes through
    /// IContentService.Delete for every node, not a raw SQL DELETE, so
    /// Umbraco's own cache/Examine-index/recycle-bin bookkeeping stays
    /// consistent rather than being bypassed.
    /// </summary>
    private void DeleteAllOfType(string alias)
    {
        var contentType = contentTypeService.Get(alias);

        if (contentType is null)
        {
            return;
        }

        var items = contentService.GetPagedOfType(contentType.Id, 0, 1000, out _, null);

        foreach (var item in items)
        {
            contentService.Delete(item);
        }
    }

    /// <summary>
    /// Used only by the accommodation/activities schema migration above —
    /// deletes a content type's content, then the type itself, so a
    /// stale-shaped type can be recreated fresh a few lines later.
    /// </summary>
    private async Task DeleteContentAndTypeAsync(string alias)
    {
        DeleteAllOfType(alias);

        if (contentTypeService.Get(alias) is { } contentType)
        {
            await contentTypeService.DeleteAsync(contentType.Key, Constants.Security.SuperUserKey);
        }
    }

    private void PublishOrThrow(IContent content)
    {
        // Confirmed against the real API, not assumed — two wrong guesses
        // first: null throws ArgumentNullException, and ["*"] throws
        // "wildcards or nulls are not allowed" (that message is
        // misleading — it actually means "not for this invariant content
        // type"). An empty array is what invariant content (none of this
        // platform's catalog types have culture variance configured)
        // actually wants.
        var result = contentService.SaveAndPublish(content, culturesToPublish: []);

        if (!result.Success)
        {
            throw new InvalidOperationException(
                $"Failed to publish seed content '{content.Name}' ({content.ContentType.Alias}): {result.Result}");
        }
    }

    private static string ToUdi(IContent content) =>
        new GuidUdi(Constants.UdiEntityType.Document, content.Key).ToString();
}
