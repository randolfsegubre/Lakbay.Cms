# Lakbay.Cms

Unified editorial CMS + product catalog for the Lakbay platform — ECMS and
PCMS merged into one Umbraco 18 solution (corrected from "17" — see
[Docs/DEVELOPER_HANDBOOK.md](Docs/DEVELOPER_HANDBOOK.md)). See
[ADR-0001](../Lakbay.Docs/docs/adr/ADR-0001-unify-ecms-pcms.md) for why.

Phase 0 scaffolding is done, and as of 2026-09-08 it's running against a
real local SQL Server (Docker) database — boots to the install wizard
with a live DB connection, no errors. Only the wizard's admin-account step
is left, deliberately manual (real credential choice); see
[Docs/DEVELOPER_HANDBOOK.md](Docs/DEVELOPER_HANDBOOK.md) for exact
commands and current status, and [CLAUDE.md](CLAUDE.md) /
[../Lakbay.Docs/docs/02_BUILD_PLAN.md](../Lakbay.Docs/docs/02_BUILD_PLAN.md)
for what's next (Phase 3).
