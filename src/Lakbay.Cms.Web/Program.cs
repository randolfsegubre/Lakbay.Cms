// Composition root for the whole Umbraco solution (ADR-0006: this app owns
// zero Razor presentation - AddWebsite() below only wires Umbraco's own
// routing/backoffice, never renders the public site; that's Lakbay.Web's
// job entirely). AddComposers() is what auto-discovers and runs every
// IComposer in this assembly (CatalogComposer, ContentComposer) - that's
// the actual entry point for this repo's seeders and the publish-sync
// handler, not anything visible in this file.
WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.CreateUmbracoBuilder()
    .AddBackOffice()
    .AddWebsite()
    // Powers the Content Delivery API Lakbay.Web reads both trees through
    // (see appsettings.Development.json's DeliveryApi:Enabled) - config
    // alone doesn't register its DI services, this call is also required.
    .AddDeliveryApi()
    .AddComposers()
    .Build();

// Lakbay.Web calls the Content Delivery API cross-origin, same reasoning
// and same allow-list pattern as Lakbay.AvailabilityApi.Api's CORS setup
// — see appsettings.Development.json for the local default.
var allowedOrigins = builder.Configuration
    .GetSection("Cors:AllowedOrigins")
    .Get<string[]>() ?? [];

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
        policy.WithOrigins(allowedOrigins)
            .AllowAnyHeader()
            .AllowAnyMethod());
});

WebApplication app = builder.Build();


await app.BootUmbracoAsync();

app.UseCors();

app.UseUmbraco()
    .WithMiddleware(u =>
    {
        u.UseBackOffice();
        u.UseWebsite();
    })
    .WithEndpoints(u =>
    {
        u.UseBackOfficeEndpoints();
        u.UseWebsiteEndpoints();
    });

await app.RunAsync();
