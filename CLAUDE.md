# Lakbay.Cms — Start Here

This file is intentionally short. It exists so any Claude Code session (or
other AI coding assistant) rooted here auto-loads it and is pointed at the
real documentation before touching anything.

**Read, in this order, before writing any code:**

1. [../Lakbay.Docs/docs/01_CLAUDE.md](../Lakbay.Docs/docs/01_CLAUDE.md) —
   the platform AI operating manual. Constitution for the whole Lakbay
   estate; if anything else conflicts with it, it wins unless the user
   explicitly overrides it in the current conversation.
2. [../Lakbay.Docs/docs/02_BUILD_PLAN.md](../Lakbay.Docs/docs/02_BUILD_PLAN.md)
   — **Phase 0** (foundation/scaffolding, this repo's Umbraco solution
   must boot to the install wizard before anything is customized) and
   **Phase 3** (real CMS — content + product trees, Content Delivery API,
   GraphQL layer) are this repo's phases. Phase 4 involves this repo only
   for Service Bus event consumption.
3. [../Lakbay.Docs/docs/04_TASKS.md](../Lakbay.Docs/docs/04_TASKS.md) —
   current status across the whole platform.
4. Decisions this repo must honor:
   [ADR-0001](../Lakbay.Docs/docs/adr/ADR-0001-unify-ecms-pcms.md) (why
   ECMS and PCMS are one Umbraco solution, not two) and
   [ADR-0003](../Lakbay.Docs/docs/adr/ADR-0003-separate-booking-data.md)
   (why booking data is deliberately *not* stored here).
5. [../Lakbay.Docs/docs/03_ARCHITECTURE_AND_PATTERNS_GUIDE.md](../Lakbay.Docs/docs/03_ARCHITECTURE_AND_PATTERNS_GUIDE.md)
   — OOP/SOLID/pattern tables this repo's code should follow (Repository
   pattern over Umbraco queries, Dependency Inversion at the composition
   root, etc.).

## What this repo is

Umbraco 17 (.NET), single solution/startup. Two content trees in one
backoffice: **Content** (pages, landing pages, block-list components) and
**Products** (holiday/tour records — itinerary, price bands, departure
dates, inclusions, media, geo, product-line taxonomy). Content nodes
reference product nodes natively via Content Picker — no cross-service API
call. Exposes both trees via Umbraco's Content Delivery API plus a GraphQL
layer matching `Lakbay.Contracts`.

**Not this repo's job:** orders, baskets, live availability, or payment —
those live in `Lakbay.Booking`, on purpose (ADR-0003). Also not this
repo's job: rendering a page. No Razor views for the public site, ever —
`Lakbay.Web` owns 100% of presentation (ADR-0006). If you're adding a
`Views/` folder here for anything other than Umbraco's own backoffice,
stop and re-read that ADR.

## Local setup

Not yet proven — Phase 0 is not complete. Once the Umbraco solution boots
locally, the exact commands go in `Docs/DEVELOPER_HANDBOOK.md` (create
that file the moment setup actually works, not from memory afterward).

**Database:** SQL Server (Docker, Developer Edition) locally — never
Azure SQL Database, which has no local/offline edition. Azure SQL
Database is only used once a live/staging environment exists. See
[ADR-0005](../Lakbay.Docs/docs/adr/ADR-0005-local-sql-server-not-azure-sql.md).

## End of session

Update `../Lakbay.Docs/docs/04_TASKS.md` and append an entry to
`../Lakbay.Docs/docs/05_DEVLOG.md` for anything that changed phase status
or made a new structural decision. A new structural decision gets its own
ADR under `../Lakbay.Docs/docs/adr/`.
