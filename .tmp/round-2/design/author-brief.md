# Spec author brief

Each round-2 spec is written by one author from its design note. The author's job is to produce
the final, implementation-ready spec that reads easily on the first pass.

## Inputs, in precedence order

1. `.tmp/round-2/reviews/addenda.md` (ten short binding addenda) and `.tmp/round-2/reviews/rulings.md` — Part 0 (cascading rulings), Part 1 (skeleton amendments),
   your spec's Part 2 subsection, the Part 2.15–2.17 rows that name your spec, and every Part 5
   question that names your spec (each becomes a review flag). Binding; overrides everything below.
2. `.tmp/round-2/design/00-architecture.md` — the cross-cutting skeleton. Binding where the
   rulings do not override it.
3. `.tmp/round-2/design/<NNNN>.md` — your design note: the content. Every contract, rule, behavior
   and example an implementer needs comes from it, adjusted by the rulings.
4. `.tmp/round-2/design/outlines.md` — the binding section numbering of every new spec. Keep your
   own numbering exactly; cite other new specs by it.
5. `.tmp/round-2/style-guide.md` — how the spec reads. This is the quality bar: Ahmad found the
   round-1 specs too dense and repetitive.
6. Existing specs 0011–0022, when you cite them. Their sections were just edited for this round
   (logs in `.tmp/round-2/edits/applied-*.md`); open the file and cite real section numbers. Spec
   0010's parts use letter-dotted numbers (`§A.1`…`§A.10`, `§B.1`…, `§F.15`).

## What to write

- **Header** exactly as the style guide shows, dated 20 October 2026.
- **Context:** 2–4 short paragraphs, then "Other specs that carry parts of this design": the
  sections of other specs (existing or new) where parts of this design live, one line each.
- **Goals / Non-goals.**
- **Numbered sections** per the outline. Each opens with one to three plain sentences that say
  what it decides. Section 1 includes the sketch-notation sentence and a short Vocabulary list.
- **Testing**, then **Definition of done**. For a front-end spec the Definition of done states how
  the design meets each UI rule (instant warm launch, instant feedback, non-blocking, stable
  layout, RTL, WCAG AA) and names the pinning test; it also lists what the page publishes to the
  page-context registry, its telemetry, and what the reference distribution (Acme) adds.
- **Decisions record:** numbered; **decision** — one-line why — (§ pointer). Answer each open
  question this spec owns with the alternatives considered in a short clause.
- **Review flags:** numbered; the call (§ pointer), the plausible alternative, what would flip it.

## Length

At most 1,100 lines (0033: 1,200; 0010: no fixed cap, but every amendment tight). The notes run
1,200–1,800 lines. Get there by:

- applying the rulings' cuts;
- stating each fact once and citing it elsewhere ("see §4.2", "spec 0024 §10.3");
- never restating another spec's contract — name the members you use and cite the owner;
- deleting process narration, verification notes, "challenges" and design history;
- turning prose into bullets and tables.

Never drop a contract, rule, state, limit or error an implementer needs.

## Rules

- **No process words.** Never mention the brief, the breakdown, design notes, rulings, the
  skeleton, rounds, reviewers, agents, item IDs (`C-…`, `R-…`, `M-…`, `Q-…`, `X-…`, `G…`) or how a
  decision was reached. The spec states the design as it is.
- **No `A§` pointers.** The skeleton is not a document a reader has. Cite the spec that owns the
  concept (the skeleton's ownership map tells you which).
- **Sketches:** TypeScript (```ts) for client contracts, C# (```csharp) for server contracts, real
  declarations with `//` comments, no doc comments, no imports or `using`, no bodies. SQL exact.
- **Examples:** at least one concrete example for every non-obvious contract or flow (a
  distribution's few lines, a JSON pair, a URL, a walk-through). Use Acme and familiar entities.
- **Public text never cites specs:** UI copy, error messages, log text and test names in examples
  carry no `docs/` paths or spec numbers.
- **UI copy is sentence case.** Arabic examples are welcome where RTL matters.
- **Write the file early** (header, Context, Goals, then section by section) and extend it, so
  work survives an interruption. Edit no other file.
