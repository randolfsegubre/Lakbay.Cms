using System.Text.Json;
using Lakbay.Cms.Web.Shared;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Events;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Notifications;
using Umbraco.Cms.Core.Services;

namespace Lakbay.Cms.Web.Content;

/// <summary>
/// Creates the Content tree (ADR-0001) — a Home Page and one landing page
/// per product line, each with a "sections" field describing a
/// repeatable list of page blocks that <c>Lakbay.Web</c>'s block registry
/// (ADR-0012) renders.
///
/// `sections` is JSON-in-Textarea, not a real Umbraco Block List — see
/// ADR-0015 for why: a real Block List was built and the stored value
/// round-tripped correctly through direct SQL inspection, but Umbraco's
/// own Content Delivery API failed to read it back (empty items, no
/// error), and the exact cause wasn't found within this session's time
/// budget. This mirrors the same, already-proven pattern
/// `Catalog.CatalogContentTypeSeeder` uses for `Product.priceBands`.
///
/// Deliberately independent of the Products tree: a landing page names
/// its product line with a plain <c>productLineCode</c> text field, not a
/// Content Picker to the actual ProductLine node — avoids an ordering
/// hazard, since Umbraco runs every <c>UmbracoApplicationStartedNotification</c>
/// handler concurrently (Task.WaitAll), so this seeder cannot assume
/// Catalog's ProductLine content nodes already exist by the time it runs.
///
/// Same v0-simplification stance as the Products tree, stated plainly:
/// hero/section images are plain URL text fields, not a Media Picker —
/// real photography/licensing is a Phase 5 concern, not this one.
/// </summary>
public sealed class ContentTreeSeeder(
    CmsSchemaBuilder schema,
    IContentTypeService contentTypeService,
    IContentService contentService)
    : INotificationAsyncHandler<UmbracoApplicationStartedNotification>
{
    public const string HomePageAlias = "homePage";
    public const string ProductLineLandingPageAlias = "productLineLandingPage";
    public const string RegionLandingPageAlias = "regionLandingPage";
    public const string DestinationLandingPageAlias = "destinationLandingPage";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task HandleAsync(UmbracoApplicationStartedNotification notification, CancellationToken ct)
    {
        // One-time migration off the abandoned real-Block-List attempt
        // (ADR-0015). Gated on heroBanner/imageTextBlock still existing —
        // those two are never created by this class anymore, so their
        // presence is the unique tell that this database still has V1
        // leftovers. Self-limiting: once this runs once and deletes them,
        // the gate is false on every later boot and this whole block
        // no-ops — critical, since homePage/productLineLandingPage *do*
        // legitimately exist after a normal run, and must never be wiped
        // just because they exist.
        if (contentTypeService.Get("heroBanner") is not null || contentTypeService.Get("imageTextBlock") is not null)
        {
            await DeleteStaleTypeIfPresentAsync("heroBanner");
            await DeleteStaleTypeIfPresentAsync("imageTextBlock");
            await DeleteStaleTypeIfPresentAsync(ProductLineLandingPageAlias);
            await DeleteStaleTypeIfPresentAsync(HomePageAlias);
        }

        // ADR-0018 migration: same self-limiting shape as the block above
        // — gated on regionLandingPage not existing yet, the tell that
        // this database predates the nested Region/Destination pages.
        if (contentTypeService.Get(HomePageAlias) is not null && contentTypeService.Get(RegionLandingPageAlias) is null)
        {
            await DeleteStaleTypeIfPresentAsync(DestinationLandingPageAlias);
            await DeleteStaleTypeIfPresentAsync(RegionLandingPageAlias);
            await DeleteStaleTypeIfPresentAsync(ProductLineLandingPageAlias);
            await DeleteStaleTypeIfPresentAsync(HomePageAlias);
        }

        if (contentTypeService.Get(HomePageAlias) is null)
        {
            var textstring = await schema.GetBuiltInDataTypeAsync(Constants.PropertyEditors.Aliases.TextBox);
            var textarea = await schema.GetBuiltInDataTypeAsync(Constants.PropertyEditors.Aliases.TextArea);

            var homePage = schema.NewContentType(HomePageAlias, "Home Page", "icon-home", allowedAsRoot: true);
            schema.AddProperties(homePage,
                (textstring, "heroHeading", "Hero Heading"),
                (textarea, "heroSubtext", "Hero Subtext"),
                (textstring, "heroImageUrl", "Hero Image URL"),
                (textarea, "sections", "Page Sections (JSON array — see Docs/DEVELOPER_HANDBOOK.md)"));
            await schema.SaveNewAsync(homePage);

            var landingPage = schema.NewContentType(ProductLineLandingPageAlias, "Product Line Landing Page", "icon-flag-alt", allowedAsRoot: false);
            schema.AddProperties(landingPage,
                (textstring, "productLineCode", "Product Line Code (ALON / AMIHAN / PARUL / PAMANA)"),
                (textstring, "heading", "Heading"),
                (textstring, "heroImageUrl", "Hero Image URL"),
                (textarea, "sections", "Page Sections (JSON array — see Docs/DEVELOPER_HANDBOOK.md)"));
            await schema.SaveNewAsync(landingPage);

            // ADR-0018: real nested pages under each ProductLine landing
            // page, matching ECMS's actual page-tree depth (confirmed via
            // the read-only D:\_DEV\HPUK reference clone) — Country →
            // Region → Resort → Accommodation in ECMS maps onto
            // ProductLine landing → Region landing → Destination landing
            // here. Same shape as productLineLandingPage (heading,
            // heroImageUrl, sections) plus a plain-text `slug` join key —
            // same "plain text, not a picker" reasoning as
            // productLineCode above, so this seeder never has to assume
            // the Products tree's Region/Destination nodes already exist.
            var regionLandingPage = schema.NewContentType(RegionLandingPageAlias, "Region Landing Page", "icon-map-alt", allowedAsRoot: false);
            schema.AddProperties(regionLandingPage,
                (textstring, "slug", "Region Slug"),
                (textstring, "heading", "Heading"),
                (textstring, "heroImageUrl", "Hero Image URL"),
                (textarea, "sections", "Page Sections (JSON array — see Docs/DEVELOPER_HANDBOOK.md)"));
            await schema.SaveNewAsync(regionLandingPage);

            var destinationLandingPage = schema.NewContentType(DestinationLandingPageAlias, "Destination Landing Page", "icon-map-location", allowedAsRoot: false);
            schema.AddProperties(destinationLandingPage,
                (textstring, "slug", "Destination Slug"),
                (textstring, "heading", "Heading"),
                (textstring, "heroImageUrl", "Hero Image URL"),
                (textarea, "sections", "Page Sections (JSON array — see Docs/DEVELOPER_HANDBOOK.md)"));
            await schema.SaveNewAsync(destinationLandingPage);
        }

        // Same reasoning as CatalogContentTypeSeeder: content-seeding stays
        // in this same HandleAsync, after type-creation, not a second
        // handler — no ordering guarantee exists between them otherwise.
        await SeedRealContentAsync();
    }

    /// <summary>
    /// Real copy and real photos — the same four destinations the
    /// Products tree seeds, reused here as each landing page's hero (they
    /// already are that product line's actual seeded holiday, so this is
    /// accurate, not filler). Idempotent: skips if a Home Page already
    /// exists. Honors the same LAKBAY_FORCE_RESEED_CATALOG escape hatch
    /// as the Products tree, so one env var resets both trees together.
    /// </summary>
    private Task SeedRealContentAsync()
    {
        if (Environment.GetEnvironmentVariable("LAKBAY_FORCE_RESEED_CATALOG") == "true")
        {
            DeleteAllOfType(DestinationLandingPageAlias);
            DeleteAllOfType(RegionLandingPageAlias);
            DeleteAllOfType(ProductLineLandingPageAlias);
            DeleteAllOfType(HomePageAlias);
        }
        else if (contentService.CountPublished(HomePageAlias) > 0)
        {
            return Task.CompletedTask;
        }

        const string boracayImage = "https://commons.wikimedia.org/wiki/Special:FilePath/WV%20banner%20Visayas%20Boracay%20white%20beach.jpg";

        var home = contentService.Create("Home", -1, HomePageAlias);
        home.SetValue("heroHeading", "Four ways to see the Philippines, chosen for what makes each place worth the trip");
        home.SetValue("heroSubtext", "Philippines-first holidays");
        home.SetValue("heroImageUrl", boracayImage);
        home.SetValue("sections", BuildSectionsJson(
            Hero("Welcome to Lakbay",
                "Curated Philippine holidays across four themed collections — islands, highlands, festivals, and heritage towns.",
                boracayImage),
            ImageText("Why a Philippines-first platform",
                "Every holiday on Lakbay is built around a real place and a real reason to go — not a generic package. Four collections, one country, chosen for what actually makes each destination worth the trip.",
                "https://commons.wikimedia.org/wiki/Special:FilePath/Kayangan%20Lake%2C%20Coron%20-%20Palawan.jpg",
                "right")));
        PublishOrThrow(home);

        // Name deliberately carries a "Landing Page" suffix, not just the
        // English line name ("Islands", etc.) — CatalogContentTypeSeeder's
        // ProductLine nodes now use that plain name, and giving both trees
        // an identically-named node is exactly the URL-routing collision
        // ADR-0015/cmsContentApi.ts's getLandingPageContent already had to
        // work around once (see that file's comment). The suffix keeps
        // the two trees unambiguous by name regardless of which Umbraco
        // route resolution ever prefers.
        var landingPages = new (string LineCode, string Name, string Heading, string HeroImage, string WelcomeText, string SecondaryText, string SecondaryImage)[]
        {
            ("ALON", "Islands Landing Page", "Islands & water adventure",
                "https://commons.wikimedia.org/wiki/Special:FilePath/Kayangan%20Lake%2C%20Coron%20-%20Palawan.jpg",
                "From limestone karsts to WWII wreck dives, Islands collects the Philippines' best island-hopping and water adventure — starting with Coron, Palawan.",
                "Coron alone has three lagoons worth a full day each, a sunken Japanese fleet for wreck diving, and a town that never feels overrun.",
                "https://commons.wikimedia.org/wiki/Special:FilePath/WV%20banner%20Visayas%20Boracay%20white%20beach.jpg"),
            ("AMIHAN", "Highlands Landing Page", "Highland & cool-climate escapes",
                "https://commons.wikimedia.org/wiki/Special:FilePath/Baguio%20City%2C%20Philippines(landscape%20view).jpg",
                "Pine forests and cool mountain air, without leaving the tropics — Highlands is built around the Philippines' highland escapes, led by Baguio.",
                "At 1,540 meters, Baguio earned its nickname as the Summer Capital honestly — Session Road, Burnham Park, and sunrise at Mines View Park.",
                "https://commons.wikimedia.org/wiki/Special:FilePath/Kayangan%20Lake%2C%20Coron%20-%20Palawan.jpg"),
            ("PARUL", "Festivals Landing Page", "Festive & light tourism",
                "https://commons.wikimedia.org/wiki/Special:FilePath/WV%20banner%20San%20Fernando%20Pampanga%20giant%20Christmas%20lanterns.JPG",
                "San Fernando, Pampanga is the Christmas Capital of the Philippines for a reason — the Giant Lantern Festival is genuinely one of a kind.",
                "Giant, motorized, kaleidoscopic lanterns competing barangay against barangay — Festivals is light tourism the Philippines does better than anywhere.",
                "https://commons.wikimedia.org/wiki/Special:FilePath/Baguio%20City%2C%20Philippines(landscape%20view).jpg"),
            ("PAMANA", "Heritage Landing Page", "Heritage & culture",
                "https://commons.wikimedia.org/wiki/Special:FilePath/The%20Calle%20Crisologo%20in%20Vigan%2C%20Ilocos%20Sur.jpg",
                "Cobblestone streets, Spanish-era houses, kalesa rides — Vigan is a UNESCO World Heritage town and the anchor of Heritage's collection.",
                "Calle Crisologo at golden hour, the Bantay Bell Tower, and a working pottery workshop — Vigan rewards walking it slowly.",
                "https://commons.wikimedia.org/wiki/Special:FilePath/WV%20banner%20San%20Fernando%20Pampanga%20giant%20Christmas%20lanterns.JPG"),
        };

        var lineLandingPageIds = new Dictionary<string, int>();

        foreach (var lp in landingPages)
        {
            var content = contentService.Create(lp.Name, home.Id, ProductLineLandingPageAlias);
            content.SetValue("productLineCode", lp.LineCode);
            content.SetValue("heading", lp.Heading);
            content.SetValue("heroImageUrl", lp.HeroImage);
            content.SetValue("sections", BuildSectionsJson(
                Hero(lp.Heading, lp.WelcomeText, lp.HeroImage),
                ImageText("On the ground", lp.SecondaryText, lp.SecondaryImage, "left")));
            PublishOrThrow(content);
            lineLandingPageIds[lp.LineCode] = content.Id;
        }

        // ADR-0018: real nested Region/Destination pages under each
        // ProductLine landing page, matching ECMS's actual page-tree
        // depth (confirmed via the read-only D:\_DEV\HPUK reference
        // clone). Slugs match the Products tree's own Region/Destination
        // slugs (CatalogContentTypeSeeder) — the join key, plain text for
        // the same ordering reason productLineCode above already is.
        // Copy here is intentionally shorter than the Products tree's own
        // descriptions/highlights (this is the editorial overlay, not the
        // structured data) but covers the same real facts, not filler.
        var regionPages = new (string LineCode, string Slug, string Name, string Heading, string HeroImage, string WelcomeText, string SecondaryText, string SecondaryImage)[]
        {
            ("ALON", "palawan", "Palawan Landing Page", "Palawan",
                "https://commons.wikimedia.org/wiki/Special:FilePath/Kayangan%20Lake%2C%20Coron%20-%20Palawan.jpg",
                "The Philippines' last ecological frontier — limestone karst islands, hidden lagoons, and some of the clearest water in the country.",
                "Regularly ranked among the world's best islands by international travel press. Coron and El Nido are separate towns, roughly 4 hours apart by boat or van.",
                "https://commons.wikimedia.org/wiki/Special:FilePath/Big_Lagoon_at_El_Nido%2C_Palawan%2C_Philippines.jpg"),
            ("ALON", "western-visayas-alon", "Western Visayas Landing Page (Islands)", "Western Visayas",
                "https://commons.wikimedia.org/wiki/Special:FilePath/Boracay_White_Beach_in_day_%28985286231%29.jpg",
                "Home to Boracay, the Philippines' most internationally famous beach destination.",
                "White Beach's powdery sand comes from pulverized coral and shells, not imported sand — a 2018 rehabilitation reset strict development rules still in force today.",
                "https://commons.wikimedia.org/wiki/Special:FilePath/Boracay_White_Beach_in_day_%28985286231%29.jpg"),
            ("ALON", "caraga", "Caraga Landing Page", "Caraga",
                "https://commons.wikimedia.org/wiki/Special:FilePath/Cloud%209%20Boardwalk%2C%20Siargao.jpg",
                "A teardrop-shaped island on the country's Pacific side, built around one of the world's best-known surf breaks.",
                "Cloud 9's reef break hosts an annual international surfing cup. Best surf season: August-November.",
                "https://commons.wikimedia.org/wiki/Special:FilePath/Cloud%209%20Boardwalk%2C%20Siargao.jpg"),
            ("ALON", "ilocos-alon", "Ilocos Region Landing Page (Islands)", "Ilocos Region",
                "https://commons.wikimedia.org/wiki/Special:FilePath/Surfing%20Capital%20of%20the%20Northern%20Philippines.jpg",
                "The Philippines' most accessible surf coast — a string of beach towns three hours north of Manila by road.",
                "San Juan's Urbiztondo Beach is the country's most established beginner-surf town — close enough to Manila for a weekend trip, no flight required.",
                "https://commons.wikimedia.org/wiki/Special:FilePath/Surfing%20Capital%20of%20the%20Northern%20Philippines.jpg"),
            ("AMIHAN", "cordillera-amihan", "Cordillera Administrative Region Landing Page (Highlands)", "Cordillera Administrative Region",
                "https://commons.wikimedia.org/wiki/Special:FilePath/Baguio%20City%2C%20Philippines(landscape%20view).jpg",
                "A mountainous region in northern Luzon, several degrees cooler than the lowlands year-round.",
                "Baguio sits at 1,540m elevation, earning its nickname as the Philippines' Summer Capital. Sagada is a smaller, quieter mountain town known for caving.",
                "https://commons.wikimedia.org/wiki/Special:FilePath/Hanging_Coffins_of_Sagada%2C_Mountain_Province.JPG"),
            ("AMIHAN", "calabarzon", "Calabarzon Landing Page", "Calabarzon",
                "https://commons.wikimedia.org/wiki/Special:FilePath/Taal_Lake_and_Volcano%2C_Tagaytay%2C_Philippines.jpg",
                "The ridge just south of Manila overlooking Taal Lake and Volcano, the Philippines' most accessible highland day trip.",
                "Less than two hours from Manila — no overnight stay strictly required. Taal is one of the world's smallest active volcanoes.",
                "https://commons.wikimedia.org/wiki/Special:FilePath/Taal_Lake_and_Volcano%2C_Tagaytay%2C_Philippines.jpg"),
            ("PARUL", "central-luzon", "Central Luzon Landing Page", "Central Luzon",
                "https://commons.wikimedia.org/wiki/Special:FilePath/WV%20banner%20San%20Fernando%20Pampanga%20giant%20Christmas%20lanterns.JPG",
                "Home to the Giant Lantern Festival, the Philippines' best-known Christmas-season celebration.",
                "San Fernando is officially recognized as the Christmas Capital of the Philippines — lanterns up to 20 feet wide with thousands of synchronized lights.",
                "https://commons.wikimedia.org/wiki/Special:FilePath/WV%20banner%20San%20Fernando%20Pampanga%20giant%20Christmas%20lanterns.JPG"),
            ("PARUL", "central-visayas", "Central Visayas Landing Page", "Central Visayas",
                "https://commons.wikimedia.org/wiki/Special:FilePath/Sinulog_Festival_of_Cebu_City.jpg",
                "The Visayas' commercial and cultural hub, home to the country's grandest religious festival.",
                "Sinulog Festival draws millions of visitors every third Sunday of January. Cebu is also the Philippines' oldest city, founded in 1565.",
                "https://commons.wikimedia.org/wiki/Special:FilePath/Sinulog_Festival_of_Cebu_City.jpg"),
            ("PARUL", "western-visayas-parul", "Western Visayas Landing Page (Festivals)", "Western Visayas",
                "https://commons.wikimedia.org/wiki/Special:FilePath/Iloilo_Dinagyang_Festival.jpg",
                "A heritage port city on Panay island, home to the Dinagyang Festival.",
                "Dinagyang's tribal street-dance competition runs every fourth Sunday of January, a week after Cebu's Sinulog.",
                "https://commons.wikimedia.org/wiki/Special:FilePath/Iloilo_Dinagyang_Festival.jpg"),
            ("PAMANA", "ilocos-pamana", "Ilocos Region Landing Page (Heritage)", "Ilocos Region",
                "https://commons.wikimedia.org/wiki/Special:FilePath/The%20Calle%20Crisologo%20in%20Vigan%2C%20Ilocos%20Sur.jpg",
                "The Philippines' best-preserved Spanish colonial town, a UNESCO World Heritage Site.",
                "Calle Crisologo's cobblestone streets and ancestral houses date to the 16th-18th centuries.",
                "https://commons.wikimedia.org/wiki/Special:FilePath/The%20Calle%20Crisologo%20in%20Vigan%2C%20Ilocos%20Sur.jpg"),
            ("PAMANA", "cordillera-pamana", "Cordillera Administrative Region Landing Page (Heritage)", "Cordillera Administrative Region",
                "https://commons.wikimedia.org/wiki/Special:FilePath/Banaue_Rice_Terraces_of_the_Philippines.jpg",
                "Home to the Ifugao Rice Terraces, carved into the mountains over 2,000 years ago.",
                "Often called the 'Eighth Wonder of the World', a UNESCO World Heritage Site, still farmed by Ifugao communities using traditional methods.",
                "https://commons.wikimedia.org/wiki/Special:FilePath/Banaue_Rice_Terraces_of_the_Philippines.jpg"),
            ("PAMANA", "ncr", "National Capital Region Landing Page", "National Capital Region",
                "https://commons.wikimedia.org/wiki/Special:FilePath/Fort_Santiago%2C_Intramuros.JPG",
                "Metro Manila's historic walled city, the oldest district in the Philippine capital.",
                "Built by Spanish colonizers in the 16th century as the seat of government — Fort Santiago, Manila Cathedral, and San Agustin Church all sit within its walls.",
                "https://commons.wikimedia.org/wiki/Special:FilePath/Fort_Santiago%2C_Intramuros.JPG"),
        };

        var regionLandingPageIds = new Dictionary<string, int>();

        foreach (var rp in regionPages)
        {
            var content = contentService.Create(rp.Name, lineLandingPageIds[rp.LineCode], RegionLandingPageAlias);
            content.SetValue("slug", rp.Slug);
            content.SetValue("heading", rp.Heading);
            content.SetValue("heroImageUrl", rp.HeroImage);
            content.SetValue("sections", BuildSectionsJson(
                Hero(rp.Heading, rp.WelcomeText, rp.HeroImage),
                ImageText("Worth knowing", rp.SecondaryText, rp.SecondaryImage, "left")));
            PublishOrThrow(content);
            regionLandingPageIds[rp.Slug] = content.Id;
        }

        // Destination pages — nested under their Region landing page.
        // SecondaryText deliberately names the real Accommodation from
        // the Products tree (CatalogContentTypeSeeder) — this is the
        // "give accommodation real visual presence" half of ADR-0018,
        // prompted directly by the user pointing at Inntravel's own
        // dedicated Accommodation section as the bar to match.
        var destinationPages = new (string RegionSlug, string Slug, string Name, string Heading, string HeroImage, string WelcomeText, string AccommodationName, string AccommodationText)[]
        {
            ("palawan", "coron", "Coron Landing Page", "Coron, Palawan",
                "https://commons.wikimedia.org/wiki/Special:FilePath/Kayangan%20Lake%2C%20Coron%20-%20Palawan.jpg",
                "Island-hopping among limestone karsts, WWII wreck diving, and the Big and Small Lagoons.",
                "Coron Bayside Inn", "A simple harborside inn a five-minute walk from the public market and the town's island-hopping jetty — walking distance to dive shops and restaurants."),
            ("palawan", "el-nido", "El Nido Landing Page", "El Nido, Palawan",
                "https://commons.wikimedia.org/wiki/Special:FilePath/Big_Lagoon_at_El_Nido%2C_Palawan%2C_Philippines.jpg",
                "Limestone cliffs, hidden lagoons, and the Bacuit archipelago — the crown jewel of Palawan's island-hopping circuit.",
                "Bacuit Bay Guesthouse", "A family-run guesthouse in El Nido town, a few minutes' walk from the island-hopping jetty — simple, clean rooms, not a resort."),
            ("western-visayas-alon", "boracay", "Boracay Landing Page", "Boracay, Aklan",
                "https://commons.wikimedia.org/wiki/Special:FilePath/Boracay_White_Beach_in_day_%28985286231%29.jpg",
                "The Philippines' most famous white-sand beach island — powdery sand and turquoise water off the coast of Panay.",
                "Station 2 Beachfront Inn", "A short walk from White Beach's main strip, rooms facing a quiet garden courtyard — daily breakfast included."),
            ("caraga", "siargao", "Siargao Landing Page", "Siargao Island, Surigao del Norte",
                "https://commons.wikimedia.org/wiki/Special:FilePath/Cloud%209%20Boardwalk%2C%20Siargao.jpg",
                "Cloud 9's world-class reef break made Siargao the country's surfing capital.",
                "Cloud 9 Surf Lodge", "A short walk from the Cloud 9 boardwalk, with board storage and a resident surf coach on call."),
            ("ilocos-alon", "la-union", "La Union Landing Page", "San Juan, La Union",
                "https://commons.wikimedia.org/wiki/Special:FilePath/Surfing%20Capital%20of%20the%20Northern%20Philippines.jpg",
                "The Philippines' most accessible surf town, three hours from Manila.",
                "Urbiztondo Beachfront Lodge", "Steps from Urbiztondo Beach's beginner-friendly surf break, with a rooftop deck facing the sunset."),
            ("cordillera-amihan", "baguio", "Baguio Landing Page", "Baguio",
                "https://commons.wikimedia.org/wiki/Special:FilePath/Baguio%20City%2C%20Philippines(landscape%20view).jpg",
                "The Philippines' Summer Capital — pine forests and cool air at 1,540m, without leaving the tropics.",
                "Session Road Pine House", "A short tricycle ride from Burnham Park, with a fireplace lounge for Baguio's cool evenings."),
            ("cordillera-amihan", "sagada", "Sagada Landing Page", "Sagada, Mountain Province",
                "https://commons.wikimedia.org/wiki/Special:FilePath/Hanging_Coffins_of_Sagada%2C_Mountain_Province.JPG",
                "Hanging coffins, limestone caves, and misty pine ridgelines in the heart of the Cordillera.",
                "Echo Valley Inn", "A guesthouse on Sagada's ridge road, a short walk from the Sumaguing Cave trailhead."),
            ("calabarzon", "tagaytay", "Tagaytay Landing Page", "Tagaytay, Cavite",
                "https://commons.wikimedia.org/wiki/Special:FilePath/Taal_Lake_and_Volcano%2C_Tagaytay%2C_Philippines.jpg",
                "Ridge-top views of Taal Volcano and Taal Lake, a cool-climate escape less than two hours from Manila.",
                "Ridgeline View Suites", "Rooms facing Taal Lake and Volcano directly, a short drive from People's Park in the Sky."),
            ("central-luzon", "san-fernando-pampanga", "San Fernando Pampanga Landing Page", "San Fernando, Pampanga",
                "https://commons.wikimedia.org/wiki/Special:FilePath/WV%20banner%20San%20Fernando%20Pampanga%20giant%20Christmas%20lanterns.JPG",
                "The Christmas Capital of the Philippines — home of the Giant Lantern Festival.",
                "Lantern City Homestay", "A homestay in San Fernando's lantern-making district, footsteps from the festival grounds."),
            ("central-visayas", "cebu", "Cebu Landing Page", "Cebu City, Cebu",
                "https://commons.wikimedia.org/wiki/Special:FilePath/Sinulog_Festival_of_Cebu_City.jpg",
                "Home of the Sinulog Festival, the Philippines' grandest religious and cultural celebration.",
                "Colon Heritage Hotel", "A hotel near Cebu City's historic Colon Street, a short walk from the Sinulog parade route."),
            ("western-visayas-parul", "iloilo", "Iloilo Landing Page", "Iloilo City, Iloilo",
                "https://commons.wikimedia.org/wiki/Special:FilePath/Iloilo_Dinagyang_Festival.jpg",
                "Home of the Dinagyang Festival — tribal street dancing and drumbeats honoring the Santo Niño.",
                "Calle Real Suites", "A hotel on Iloilo's Calle Real heritage strip, minutes from the Dinagyang tribe competition grounds."),
            ("ilocos-pamana", "vigan", "Vigan Landing Page", "Vigan",
                "https://commons.wikimedia.org/wiki/Special:FilePath/The%20Calle%20Crisologo%20in%20Vigan%2C%20Ilocos%20Sur.jpg",
                "A UNESCO World Heritage colonial-era town of cobblestone streets and preserved Spanish-era houses.",
                "Calle Crisologo Heritage House", "A restored ancestral house on Calle Crisologo's cobblestone strip, steps from the kalesa stand."),
            ("cordillera-pamana", "banaue", "Banaue Landing Page", "Banaue, Ifugao",
                "https://commons.wikimedia.org/wiki/Special:FilePath/Banaue_Rice_Terraces_of_the_Philippines.jpg",
                "The 2,000-year-old Banaue Rice Terraces, carved into the Cordillera mountains by the Ifugao people.",
                "Ifugao View Lodge", "A lodge overlooking the rice terraces directly, run by a local Ifugao family."),
            ("ncr", "intramuros", "Intramuros Landing Page", "Intramuros, Manila",
                "https://commons.wikimedia.org/wiki/Special:FilePath/Fort_Santiago%2C_Intramuros.JPG",
                "The Walled City — Fort Santiago, Manila Cathedral, and cobblestone streets from Spanish colonial Manila.",
                "Plaza Suites Intramuros", "A boutique hotel inside the Walled City itself, steps from Fort Santiago."),
        };

        foreach (var dp in destinationPages)
        {
            var content = contentService.Create(dp.Name, regionLandingPageIds[dp.RegionSlug], DestinationLandingPageAlias);
            content.SetValue("slug", dp.Slug);
            content.SetValue("heading", dp.Heading);
            content.SetValue("heroImageUrl", dp.HeroImage);
            content.SetValue("sections", BuildSectionsJson(
                Hero(dp.Heading, dp.WelcomeText, dp.HeroImage),
                ImageText($"Where you'll stay: {dp.AccommodationName}", dp.AccommodationText, dp.HeroImage, "right")));
            PublishOrThrow(content);
        }

        return Task.CompletedTask;
    }

    private static Dictionary<string, object?> Hero(string heading, string subtext, string imageUrl) => new()
    {
        ["type"] = "hero",
        ["heading"] = heading,
        ["subtext"] = subtext,
        ["imageUrl"] = imageUrl,
    };

    private static Dictionary<string, object?> ImageText(string heading, string text, string imageUrl, string imagePosition) => new()
    {
        ["type"] = "imageText",
        ["heading"] = heading,
        ["text"] = text,
        ["imageUrl"] = imageUrl,
        ["imagePosition"] = imagePosition,
    };

    private static string BuildSectionsJson(params Dictionary<string, object?>[] blocks) =>
        JsonSerializer.Serialize(blocks, JsonOptions);

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
    /// Deletes a content type and all of its content (content must go
    /// first — a type can't be deleted while content of that type still
    /// exists). Used only by the one-time ADR-0015 migration above.
    /// </summary>
    private async Task DeleteStaleTypeIfPresentAsync(string alias)
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

        await contentTypeService.DeleteAsync(contentType.Key, Constants.Security.SuperUserKey);
    }

    private void PublishOrThrow(IContent content)
    {
        var result = contentService.SaveAndPublish(content, culturesToPublish: []);

        if (!result.Success)
        {
            throw new InvalidOperationException(
                $"Failed to publish seed content '{content.Name}' ({content.ContentType.Alias}): {result.Result}");
        }
    }
}
