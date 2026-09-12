# Lakbay.Cms

Unified editorial CMS + product catalog for the Lakbay platform — ECMS and
PCMS merged into one Umbraco 18 solution (corrected from "17" — see
[Docs/DEVELOPER_HANDBOOK.md](Docs/DEVELOPER_HANDBOOK.md)). See
[ADR-0001](../Lakbay.Docs/docs/adr/ADR-0001-unify-ecms-pcms.md) for why.

Phase 3 (real CMS — Content and Products trees, Content Delivery API,
GraphQL layer) is built and running against a real local SQL Server
(Docker) database — see [Docs/DEVELOPER_HANDBOOK.md](Docs/DEVELOPER_HANDBOOK.md)
for exact commands, and [CLAUDE.md](CLAUDE.md) /
[../Lakbay.Docs/docs/02_BUILD_PLAN.md](../Lakbay.Docs/docs/02_BUILD_PLAN.md)
for full phase status.

## E2E testing

Verified live 2026-09-12: `docker compose up -d` (real SQL Server), booted
clean (100 seed keys, 264 cached document URLs). Content Delivery API
(`/umbraco/delivery/api/v2/content`) returned 132 real content items
across the full destination hierarchy (14 real products, region/destination
landing pages); backoffice loads and is fully populated. `/` itself 404s
correctly — this is a headless CMS (ADR-0006), nothing should render there
directly; `Lakbay.Web` is the only presentation layer. Full trail:
[`../Lakbay.Docs/docs/05_DEVLOG.md`](../Lakbay.Docs/docs/05_DEVLOG.md)'s
2026-09-12 entry.
