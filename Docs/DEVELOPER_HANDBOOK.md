# Lakbay.Cms — Developer Handbook

Written the moment local setup actually worked (2026-09-06) — split into
what's proven and what's still pending Docker on this machine. The test
for this document: could you follow it on a plane, no internet, no AI
agent (once Docker images are pulled once, per the project's offline bar)?

## Layout

```
Lakbay.Cms.sln
src/Lakbay.Cms.Web/    Umbraco 18 (net10.0) — the CMS itself
Database/              SQL Server container build (Developer Edition),
                        adapted from the official `dotnet new
                        umbraco-compose` template — trimmed to
                        database-only, see docker-compose.yml's comment
docker-compose.yml      SQL Server only — Lakbay.Cms.Web runs natively
                        (`dotnet run`), never in this compose file
.env.example            copy to .env, fill in a real local password
```

References `../Lakbay.Contracts/csharp/Lakbay.Contracts.csproj` directly
(project reference, no package registry needed yet).

## Proven working, 2026-09-06 (no database needed for this much)

```bash
dotnet build     # 0 Warning(s), 0 Error(s)
dotnet run --project src/Lakbay.Cms.Web
# → http://localhost:<port> shows the real "Install Umbraco" wizard
#   (confirmed via browser screenshot, Umbraco 18.1.1)
```

This is Phase 0's actual exit criterion — a booting, unmodified install
wizard, nothing customized. The wizard's later steps (admin user, database
connection) weren't completed, on purpose — that needs a real database and
is closer to Phase 3's "real CMS" scope than Phase 0's "does it boot" bar.

**Important version correction:** every earlier planning document
(Blueprint, `Lakbay.Docs`) said "Umbraco 17." Checking `dotnet new install
Umbraco.Templates` on 2026-09-06 shows the actual latest is **18.1.1** —
the plan was written before checking, the same category of mistake as the
original "MockApi" framing. Corrected here and being propagated back to
`Lakbay.Docs` in the same session.

## Still pending Docker (not installed on this machine as of 2026-09-06)

```bash
# 1. Docker Desktop must be installed and running first.

# 2. Copy the env template and set a real local-only password:
cp .env.example .env
# edit .env, set DB_PASSWORD to something real (matches step 3 below)

# 3. Start SQL Server:
docker compose up -d
# creates umbracoDb (this repo) and lakbayBookingDb (Lakbay.Booking) —
# see Database/setup.sql. Same SQL Server *instance* for local-dev
# convenience; still two fully separate databases (ADR-0003).

# 4. Point Lakbay.Cms.Web at it via .NET user-secrets — NOT appsettings —
#    so the password never lands in a committed file:
cd src/Lakbay.Cms.Web
dotnet user-secrets set "ConnectionStrings:umbracoDbDSN" \
  "Server=localhost,1433;Database=umbracoDb;User Id=sa;Password=<same DB_PASSWORD from .env>;TrustServerCertificate=true"
dotnet user-secrets set "ConnectionStrings:umbracoDbDSN_ProviderName" "Microsoft.Data.SqlClient"

# 5. dotnet run again, complete the install wizard through the browser —
#    this time it can actually create the schema.
```

`dotnet user-secrets init` has already been run for
`Lakbay.Cms.Web` (added a `<UserSecretsId>` GUID to the `.csproj` — safe
to commit, it's just a reference, not the secret itself).

## Adding a new Document Type / content tree — worked walkthrough (once Phase 3 starts)

Not applicable yet — no content types exist as of Phase 0. This section
gets filled in with a real, proven walkthrough the moment Phase 3 actually
builds the Content + Products trees (ADR-0001) — not written
speculatively now.
