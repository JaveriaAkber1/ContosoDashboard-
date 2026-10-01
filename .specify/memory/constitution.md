<!--
Sync Impact Report
===================
Version change: [TEMPLATE] → 1.0.0 (initial ratification)
Modified principles: N/A (first concrete adoption of the template; all five
  principle slots filled for the first time)
Added sections:
  - Core Principles I–V (Security by Default, Infrastructure Abstraction for
    Cloud Migration, Clean Separation of Concerns, Educational Clarity &
    Simplicity, Spec-Driven Development Workflow)
  - Technology Stack & Compliance Constraints
  - Development Workflow & Quality Gates
  - Governance
Removed sections: None
Templates requiring updates:
  - .specify/templates/plan-template.md ⚠ pending manual review (Constitution
    Check gate should reference these five principles explicitly)
  - .specify/templates/spec-template.md ✅ no principle-specific language, no
    change required
  - .specify/templates/tasks-template.md ✅ no principle-specific language, no
    change required
  - .specify/templates/checklist-template.md ✅ no principle-specific language,
    no change required
Follow-up TODOs:
  - TODO(RATIFICATION_DATE): original adoption date predating this document
    was not recorded anywhere in the repository; 2026-10-01 is used as the
    effective ratification date since this is the first concrete version of
    the constitution.
-->

# ContosoDashboard Constitution

## Core Principles

### I. Security by Default (NON-NEGOTIABLE)
Every page, component, and service MUST enforce authentication and
authorization before returning or mutating data; `[Authorize]` attributes on
pages are necessary but never sufficient. Service-level checks MUST
independently re-verify that the current user owns or is a member of the
resource being accessed (Insecure Direct Object Reference protection) — a
missing or bypassed UI check MUST NOT result in unauthorized data exposure.
File and data handling MUST follow secure-by-construction patterns: generate
storage identifiers (e.g., GUIDs) before persistence, validate inputs against
explicit allow-lists (file type, size, category), never trust user-supplied
names or paths, and store uploaded content outside any publicly served
directory. Security headers and defense-in-depth (middleware, page
attributes, service checks) are mandatory layers, not optional hardening.
Rationale: ContosoDashboard teaches students production-grade security
habits using a deliberately mocked authentication system; the mock nature of
auth must never become an excuse for skipping authorization logic that would
be required in a real deployment.

### II. Infrastructure Abstraction for Cloud Migration
All infrastructure-facing dependencies (file storage, authentication,
external services) MUST be defined behind an interface (e.g.
`IFileStorageService`) with a local/offline implementation used for training.
Business logic and Razor components MUST depend only on the interface, never
on the concrete local implementation, so that a future cloud-backed
implementation (e.g. Azure Blob Storage, Microsoft Entra ID) can be swapped in
via dependency injection with zero changes to business logic. New features
MUST NOT introduce direct dependencies on cloud SDKs or require network
connectivity to run in the training environment.
Rationale: The project's explicit purpose is to demonstrate an offline-first
architecture with a clear, low-friction migration path to Azure; abstraction
leakage defeats that teaching goal.

### III. Clean Separation of Concerns
Code MUST be organized into the existing layered structure: `Models/` for
entities, `Data/` for `DbContext` and persistence concerns only, `Services/`
for business logic and authorization decisions, and `Pages/`/`Shared/` for
presentation. Razor pages and components MUST NOT contain business rules,
direct `DbContext` queries, or authorization logic inline — they call into
services. Services MUST NOT contain Blazor/UI-specific code. Each new
capability is added as a cohesive vertical slice across these layers rather
than bypassing a layer for convenience.
Rationale: Layering is itself a teaching artifact; blurring it undermines the
architectural lessons the project exists to demonstrate.

### IV. Educational Clarity & Simplicity
Code MUST optimize for readability and pedagogical clarity over cleverness,
premature optimization, or speculative generality (YAGNI). Simpler, more
explicit code is preferred over abstractions that are not yet justified by a
second real use case. Known limitations (e.g., mock authentication, no
external cloud dependency) MUST be documented inline or in project docs
rather than silently worked around. Any deviation from production-grade
practice that exists solely to keep the project offline and dependency-free
MUST be called out as such.
Rationale: The audience is students learning Spec-Driven Development; code
that is clever but opaque defeats the training purpose even if it is
technically correct.

### V. Spec-Driven Development Workflow
Every non-trivial feature MUST originate from a specification produced via
the Spec Kit workflow (`/speckit.specify` → `/speckit.clarify` →
`/speckit.plan` → `/speckit.tasks` → `/speckit.implement`) before
implementation begins. Plans MUST include a Constitution Check against the
principles in this document, and any violation MUST be explicitly justified
in the plan's Complexity Tracking section or the approach MUST be revised.
Ad hoc implementation that bypasses spec/plan artifacts is permitted only for
trivial fixes (typos, formatting, non-behavioral refactors).
Rationale: This repository's primary purpose is teaching Spec-Driven
Development; the workflow itself is a first-class project requirement, not
just a process suggestion.

## Technology Stack & Compliance Constraints

- Framework: ASP.NET Core with Blazor Server; Razor Pages for
  non-interactive flows (e.g., login/logout) that need direct HTTP semantics.
- Data access: Entity Framework Core against SQL Server LocalDB in the
  training environment; schema changes go through `ApplicationDbContext` and
  must remain compatible with automatic database creation/seeding on first
  run.
- Styling: Bootstrap 5.3 with Bootstrap Icons; no additional UI framework may
  be introduced without updating this constitution.
- Authentication/Authorization: Cookie-based mock authentication with
  claims-based identity and role-based access control is the ONLY
  authentication mechanism permitted in this training codebase. Real
  identity providers (Entra ID, Auth0, etc.) MUST NOT be wired in; the
  `IFileStorageService`-style abstraction pattern is how future production
  identity is intended to be introduced, not by modifying this repository
  directly.
- Offline requirement: The application MUST run fully offline (no required
  external network calls, no cloud service credentials) so it remains usable
  in disconnected training environments.
- Explicit non-goals: This codebase is NOT production-hardened. It MUST NOT
  be presented, packaged, or deployed as a production-ready application.

## Development Workflow & Quality Gates

- Every feature branch follows the Spec Kit lifecycle (spec → clarify → plan
  → tasks → implement) with artifacts committed under `specs/<feature>/`.
- Plan review MUST include an explicit Constitution Check section that maps
  the design to Principles I–V; unresolved violations block progression to
  `/speckit.tasks`.
- Manual security verification (authentication required, user isolation,
  IDOR protection, role-based feature gating) as documented in the project
  README MUST be re-run for any change touching authorization, data access,
  or file handling, since no automated test suite currently exists in this
  repository.
- When automated tests are introduced for a feature, they MUST live
  alongside the convention established at that time and MUST be run before
  the feature is considered complete; this constitution does not currently
  mandate a specific test framework but does mandate that any introduced
  test suite be kept green.
- Pull requests/change reviews MUST verify compliance with this constitution
  before merge; any accepted deviation MUST be recorded with rationale in
  the relevant plan's Complexity Tracking section.

## Governance

This constitution supersedes all other informal practices for this
repository. Amendments require: (1) a documented rationale for the change,
(2) an update to this file including a refreshed Sync Impact Report, and (3)
a version bump following semantic versioning — MAJOR for backward-incompatible
governance or principle removals/redefinitions, MINOR for new principles or
materially expanded guidance, PATCH for clarifications and non-semantic
wording fixes. All plans produced via `/speckit.plan` MUST include a
Constitution Check gate; complexity or deviations MUST be justified in that
plan's Complexity Tracking section rather than silently introduced. Reviewers
MUST treat unexplained deviation from Principles I–V as a blocking issue.

**Version**: 1.0.0 | **Ratified**: 2026-10-01 | **Last Amended**: 2026-10-01
