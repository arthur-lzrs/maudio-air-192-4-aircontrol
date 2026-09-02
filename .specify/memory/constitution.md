<!--
Sync Impact Report
- Version change: [TEMPLATE] → 1.0.0 (initial ratification)
- Modified principles: n/a (first concrete version; template placeholders replaced)
- Added sections:
  - Core Principles: I. Code Quality, II. Testing Standards, III. User Experience Consistency, IV. Performance Requirements
  - Quality Gates
  - Development Workflow
  - Governance
- Removed sections: none
- Templates checked for alignment:
  - .specify/templates/plan-template.md ⚠ pending manual review (Constitution Check section should reference these four principles)
  - .specify/templates/spec-template.md ⚠ pending manual review (no direct principle references found; no changes required)
  - .specify/templates/tasks-template.md ⚠ pending manual review (task categorization should reflect testing/performance gates)
  - .specify/templates/checklist-template.md ⚠ pending manual review (no direct principle references found; no changes required)
- Follow-up TODOs:
  - TODO(RATIFICATION_DATE): Original adoption date not supplied by user; set to today's date (2026-09-01) as the effective ratification date since this is the first concrete version. Confirm or correct if an earlier date applies.
-->

# AIR Control Constitution

## Core Principles

### I. Code Quality
Code MUST be readable, consistent, and maintainable before it is considered done.
- All code MUST pass linting and static analysis with zero errors before merge; warnings MUST
  be justified in the PR description or resolved.
- Every function, module, and component MUST have a single, clear responsibility; if a change
  requires explaining "and" more than once when describing what a unit does, it MUST be split.
- Naming MUST be descriptive enough that reviewers do not need external context to understand
  intent; abbreviations and unexplained magic values are NOT permitted.
- Code review by at least one other contributor is REQUIRED before merging to the main branch;
  the reviewer MUST verify adherence to this principle, not just correctness.
- Dead code, commented-out blocks, and unused dependencies MUST be removed, not left "in case
  they're needed later."

**Rationale**: Code is read far more often than it is written. Consistent, self-explanatory code
reduces onboarding time, lowers defect rates, and keeps future changes cheap.

### II. Testing Standards
Automated tests are the primary evidence that the system behaves as intended; manual
verification alone is NOT sufficient for merge approval.
- New features and bug fixes MUST include automated tests that fail without the change and pass
  with it.
- Unit tests are REQUIRED for business logic; integration tests are REQUIRED for any change that
  crosses a component, service, or data-store boundary.
- A bug fix MUST include a regression test that reproduces the original defect before the fix is
  considered complete.
- The test suite MUST remain green on the main branch at all times; a red main branch blocks all
  other merges until fixed.
- Test coverage MUST NOT decrease on any pull request; coverage-reducing changes require explicit
  written justification from the reviewer.

**Rationale**: Untested behavior is unverified behavior. A disciplined test suite catches
regressions early and gives contributors confidence to change code without fear.

### III. User Experience Consistency
Every user-facing surface (UI, CLI, API responses, error messages) MUST behave and look as if it
were designed by a single team, even when built by different contributors over time.
- Shared design tokens, components, and interaction patterns MUST be reused rather than
  reimplemented; a new pattern requires explicit justification for why existing ones don't fit.
- Terminology, iconography, and tone MUST be consistent across all screens and messages; the same
  concept MUST use the same word everywhere in the product.
- Error messages MUST be actionable: they MUST state what happened and what the user can do next,
  never a raw stack trace or opaque code alone.
- Any UI or interaction change MUST be validated against the golden path and at least one edge
  case (empty state, error state, or loading state) before being marked complete.
- Accessibility basics (keyboard navigation, sufficient color contrast, screen-reader-readable
  labels) are REQUIRED, not optional polish.

**Rationale**: Inconsistency erodes user trust and increases support burden. Predictable, coherent
experiences let users transfer what they've learned in one part of the product to another.

### IV. Performance Requirements
Performance is a feature and MUST be measured, not assumed.
- Every feature that touches a request/response path, data query, or rendering loop MUST define
  an expected performance budget (e.g., response time, frame rate, memory footprint) before
  implementation begins.
- Changes that regress a measured performance budget by more than 10% MUST be flagged in review
  and either justified or fixed before merge.
- Expensive operations (network calls, large data processing, heavy rendering) MUST be identified
  and, where user-facing, MUST provide feedback (loading state, progress indicator) rather than
  appearing frozen.
- Performance-sensitive code paths MUST include a benchmark or load test that can be re-run to
  detect regressions over time.
- Optimization work MUST be driven by measurement (profiling data, benchmarks), not speculation.

**Rationale**: Performance problems compound silently and are expensive to retrofit. Setting
budgets up front and measuring continuously keeps the system fast as it grows.

## Quality Gates

The following gates MUST pass before any change is merged to the main branch:
1. Linting and static analysis: zero unjustified errors or warnings.
2. Automated tests: full suite green, no coverage regression, new behavior covered.
3. UX review: user-facing changes verified against golden path and at least one edge case, and
   checked for consistency with existing patterns and terminology.
4. Performance check: any change to a performance-sensitive path is measured against its budget.

A pull request that fails any gate MUST NOT be merged until the failure is resolved or an explicit,
documented exception is granted per the Governance section below.

## Development Workflow

- All changes MUST go through pull request review by at least one other contributor; no direct
  pushes to the main branch.
- PR descriptions MUST state what changed and why; for user-facing or performance-sensitive
  changes, the PR MUST also state how it was verified (tests run, manual checks performed,
  benchmarks captured).
- Breaking changes to APIs, data schemas, or shared components MUST be called out explicitly in
  the PR description along with a migration note.
- When a gate exception is necessary (e.g., an urgent hotfix), the exception MUST be documented in
  the PR with the reason and a follow-up task to bring the change back into compliance.

## Governance

This constitution supersedes all other informal practices and prior undocumented conventions for
this project. Where a team decision conflicts with this document, this document wins unless it is
formally amended.

**Amendment procedure**: Any contributor may propose an amendment via pull request against this
file. The PR MUST include the proposed change, its rationale, and the resulting version bump per
the versioning policy below. Amendments require review and approval before merge, same as any
other change.

**Versioning policy**: This constitution follows semantic versioning:
- MAJOR: Backward-incompatible governance changes, or removal/redefinition of a core principle.
- MINOR: A new principle or section is added, or existing guidance is materially expanded.
- PATCH: Clarifications, wording fixes, typo corrections, or other non-semantic refinements.

**Compliance review**: Every pull request MUST be checked against the Quality Gates above during
review. Any deviation from a principle MUST be either fixed before merge or explicitly documented
as a justified, time-bound exception in the PR description.

**Version**: 1.0.0 | **Ratified**: 2026-09-01 | **Last Amended**: 2026-09-01
