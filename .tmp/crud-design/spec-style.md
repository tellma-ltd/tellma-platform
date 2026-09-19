# House style for specs 0010–0021

Derived from the merged specs in `docs/specs/` (0007, 0008, 0009 are the closest exemplars) and
from `docs/AGENTS.md`. Follow it exactly; the twelve specs must read as one family.

## File names

| Spec | File |
|---|---|
| 0010 | `docs/specs/0010-distribution-host-and-multitenancy.md` |
| 0011 | `docs/specs/0011-data-access-layer.md` |
| 0012 | `docs/specs/0012-settings-localization-and-cache.md` |
| 0013 | `docs/specs/0013-users-roles-and-permissions.md` |
| 0014 | `docs/specs/0014-crud-service-pipeline.md` |
| 0015 | `docs/specs/0015-crud-web-api.md` |
| 0016 | `docs/specs/0016-blob-storage.md` |
| 0017 | `docs/specs/0017-core-and-gl-reference-stacks.md` |
| 0018 | `docs/specs/0018-excel-codec.md` |
| 0019 | `docs/specs/0019-background-jobs-and-scheduler.md` |
| 0020 | `docs/specs/0020-notifications-inbox-and-hub.md` |
| 0021 | `docs/specs/0021-identity-server-amendments.md` |

## Header (verbatim shape)

```markdown
# Spec: <Title>

- **Author:** Ahmad Akra
- **Date:** 4 September 2026 (0021: 11 September 2026)

**Status:** Ready for implementation. Frozen once merged: revised only if implementation forces a
design change, then kept as the historical record of what shipped — never updated thereafter as the
code or its dependencies evolve.
```

## Section skeleton

1. `## Context` — three to six paragraphs: what this spec ships, why now, how it relates to the
   frozen specs it builds on (cite them as "spec 0008 §13.1" — references to other docs inside
   `docs/` are allowed because they are frozen too), and what it deliberately leaves to a later
   spec. No history of how the design was reached.
2. `## Goals / Non-goals` — two bullet lists under bold **Goals** / **Non-goals (explicitly out
   of scope)** headings. Non-goals name the spec that owns each excluded item.
3. `## 1. …`, `## 2. …` — numbered sections with `### n.m` subsections. The first section is
   usually placement and architecture: projects, packages, namespaces, folder paths, and the
   dependency edges each project takes, in a table.
4. A `## n. Testing` section: test projects (mirroring `src/`), what each suite pins, the two
   trait tiers (`Category=Integration`, `Live=true`), fixture entities/databases, and what runs
   on PR versus nightly.
5. A `## n. Definition of done` section with bold-led bullets: **Projects** (each with README,
   XML docs on every member, building and testing on Windows and Linux under warnings-as-errors,
   wired into `Tellma.slnx`); **Behavior** (which sections' semantics are implemented and pinned
   by which suites); **Observability** (instruments and log events asserted); **CI**; **Docs**
   (the ARCHITECTURE.md updates this spec requires, listed concretely; "public XML docs and error
   messages reference no `docs/` paths, per repo rule"); **Not in scope of done**.
6. `## Decisions record` — opens with "The load-bearing decisions, where not already evident
   above:" followed by a numbered list; each item is **bold decision** — one-line why — (§
   pointers). Decision plus why plus pointer only; the record never re-argues the body.
7. `## Review flags` — required in every one of these twelve specs. Numbered list; each item names
   the judgment call as made in the body (with a § pointer), the equally plausible alternative,
   and what evidence or preference would flip it. This is where Ahmad reviews design calls; keep
   every flag the ledger assigned to this spec and add any the author made while writing.
8. Appendices (`## Appendix A — …`) only for closed enumerations that would clutter the body
   (diagnostic codes, reserved names, conformance vectors).

## Prose rules

- State the design as it stands. Never narrate ("previously", "now", "the reviewer", "the brain
  dump", "the ledger", "the lens", "we decided"). Never mention the review process or this
  design exercise; the ledger and theme files do not exist to the spec's reader.
- Each fact once, in the section that owns it; elsewhere cross-reference by § number. A seam
  owned by another spec is cited ("the batch contract of spec 0011 §3"), never restated in full
  — restate only the members this spec uses, verbatim from the seam contract.
- Never reference ARCHITECTURE.md sections, code files, or any living document. Referring to
  "the architecture document" by name in passing is acceptable in Context; pointing at its
  sections is not.
- Normative voice: "is", "must", "never"; present tense; no "we"/"you"; no hedging words.
  Requirements are bullet lists led by a bold phrase. Tables for matrices (surfaces × verbs,
  exceptions × statuses, tiers × filters).
- **Contract blocks are C# sketches.** Entities and public contracts are documented in fenced
  `csharp` blocks written as real C# declarations with no `using` directives, no `namespace`
  declaration, no XML documentation comments, no method bodies and no cancellation-token
  parameters; short `//` remarks are fine. The rules and a conversion table are in
  `.tmp/crud-design/notation.md`. Section 1 states once: "Contract blocks are C# sketches: names
  and shapes are normative; `using` directives, XML documentation, cancellation-token parameters,
  method bodies and accessibility details are omitted, so a block is never pasted into code."
  Column tables remain the storage truth for entities; SQL statements are the exact shape to
  emit. Implementers once copied full C# with XML docs verbatim, and later found a
  language-neutral notation harder to read than C#; the sketch is the rule that survives both.
- Names are final. One name per concept, identical to the seam contract in every spec.
- Density like spec 0008: precise, compact paragraphs; no filler, no restated goals, no
  motivational prose. Length follows scope (the exemplars run 500–2900 lines); do not pad and
  do not truncate — every "Owns:" item of the breakdown must be fully specified.
- Public-facing text rule (repo-wide): XML docs, error messages, log messages, and test names
  never cite `docs/` or "spec NNNN"; only inline code comments may.
- Cross-platform: every command and path works on Windows and Linux.
