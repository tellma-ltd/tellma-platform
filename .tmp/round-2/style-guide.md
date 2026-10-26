# Style guide for round-2 specs

Ahmad, a lead engineer with 15 years of experience, found the round-1 specs (0010–0022) painful to
review. He had to ask an agent to re-explain many paragraphs. His three headline items:

1. **Less repetition.** Changing one piece of design must not need dozens of edits across many specs.
2. **Easier language.** Bullet points over big-ball-of-mud paragraphs.
3. **Examples** where they illustrate a contract or a hard concept.

The round-2 specs must read easily on the first pass. Every rule below serves that goal.

## Structure (keep it; it matches the family)

```markdown
# Spec: <Title>

- **Author:** Ahmad Akra
- **Date:** 20 October 2026

**Status:** Ready for implementation. Frozen once merged: revised only if implementation forces a
design change, then kept as the historical record of what shipped — never updated thereafter as the
code or its dependencies evolve.

## Context
## Goals / Non-goals
## 1. <first design section>      (numbered sections with ### n.m subsections)
...
## n. Testing
## n+1. Definition of done
## Decisions record
## Review flags
## Appendix A — … (only for closed lists: codes, keys, vectors)
```

- **Context:** 2–4 short paragraphs or a short paragraph plus bullets. What the spec ships, why,
  and what it builds on. End with a short list "Other specs that carry parts of this design" when
  this design lives partly in another spec's sections (for example "spec 0016 §2.4 — the
  `metadata` route"). State facts; never narrate how the design was reached.
- **Goals / Non-goals:** bullet lists under bold **Goals** and **Non-goals (explicitly out of
  scope)**. Each non-goal names who owns it or why it waits.
- **Section 1** is placement: packages or projects, entry points, folders, dependencies, plus a
  short **Vocabulary** list that defines every term the spec uses more than once.
- **Every numbered section opens with one to three plain sentences** saying what the section
  decides, before any detail. A reader who skims only those openers should understand the design.
- **Testing:** suites, what each pins, which run on PR and which nightly. Table or bullets.
- **Definition of done:** bold-led bullets. Point at sections; never restate them. For a
  front-end spec it states how the design meets each UI rule (instant warm launch, instant
  feedback, non-blocking, stable layout, RTL, WCAG AA) and names the test that pins it.
- **Decisions record:** numbered. Each item is **the decision** — one-line why — (§ pointer). An
  open question from the brief that this spec owns is answered here, with the alternatives
  considered in a short clause.
- **Review flags:** numbered. Each names the judgment call (§ pointer), the plausible alternative,
  and what evidence or preference would flip it.

## Plain language

- **One idea per sentence.** Aim for sentences under 25 words. Split any sentence with two
  semicolons or three commas of qualification.
- **Paragraphs of at most four sentences.** Anything longer becomes bullets.
- **Bullets with a bold lead** for rules, steps, options and properties:
  `- **Cancelled on navigation.** A read the user abandons is aborted …`
- **Numbered lists** for sequences (startup steps, a flow).
- **Plain words.** "uses" not "leverages", "before" not "prior to", "shows" not "surfaces"
  (unless it is a UI surface). Prefer the everyday word to the clever one.
- **Define, then use.** Define a term once (Vocabulary or first use), then use exactly that term.
  Never use two names for one thing.
- **Say who does what.** Prefer "The shell reads the cache" to "The cache is read".
- **Normative voice.** "must", "is", "never". No hedging ("should probably", "may want to").
- **No dense parentheses.** A parenthetical longer than a few words becomes its own sentence or
  bullet.
- **Backticks only for code names** (types, members, routes, keys, files). Don't stack five
  identifiers in one sentence; spread them over bullets.

## No repetition

- **Each fact lives in one place**, the section that owns it. Everywhere else, link to it:
  "see §4.2" or "spec 0024 §3.1".
- **Never restate another spec's contract.** Cite it. If this spec uses three members of another
  spec's type, name those three members and cite the owner; don't copy the type.
- **Don't restate goals** in the Definition of done, or the body in the Decisions record.
- **Tables replace repeated sentences.** If five items share a pattern, use one table.
- **The test:** if a design detail changed, how many places would need an edit? The answer should
  be one, plus at most a pointer elsewhere.

## Examples

Use an example whenever a contract or concept is not obvious from its declaration. Good examples:

- A short TypeScript snippet showing how a distribution uses an API (the 5 lines it writes).
- A JSON request/response pair for a wire shape.
- A URL showing the route grammar.
- A numbered walk-through of a flow ("Farah opens a second tab: 1. … 2. …").
- A small table of inputs and outputs (for a rule with cases).

Rules:

- Label it: "**Example —** …" or a code block captioned by the sentence before it.
- Use the reference distribution (Acme: English + Arabic, Gregorian + Umm Al-Qura, ar-SA) and
  familiar entities (Users, Roles, Centers, Customers, Sales invoices).
- Keep each example short (under ~20 lines). One clear example beats three partial ones.

## Contract blocks

- **TypeScript sketches** for client contracts (```ts fences): real TypeScript declarations
  (interfaces, types, function and class signatures) with short `//` comments. No `/** */` TSDoc,
  no imports, no function bodies. A sketch shows names and shapes; it is never pasted into code.
- **C# sketches** for server contracts (```csharp fences): real C# declarations with `//`
  comments; no `using` directives, no XML docs, no method bodies, no cancellation-token
  parameters.
- **SQL** blocks are the exact statements to emit. **Column tables** are the storage truth.
- Never put a table under a sketch that restates its members; comment the members instead.
- Section 1 states once: "Contract blocks are TypeScript and C# sketches: names and shapes are
  normative; imports, documentation comments, bodies and cancellation tokens are omitted."

## Diagrams

- Use a mermaid `sequenceDiagram` or `flowchart` when a flow spans more than three actors or has
  branches (warm launch, leader election, save flow). Keep each under ~15 nodes.

## Never

- Never mention the brief, the breakdown, rounds, reviewers, agents, the ledger, or how a decision
  was reached ("previously", "now changed to", "we decided"). The spec states the design as it is.
- Never cite `docs/` paths or spec numbers in text that ships: TSDoc, XML docs, error messages,
  log messages, UI strings, test names. Only specs and inline code comments may cite them.
- Never reference ARCHITECTURE.md sections from a spec body.
- Never use all-caps UI copy in examples (the design rule: sentence case everywhere).
- Never pad. Length follows scope: aim for 600–1,200 lines. A long spec is a smell; a short one
  that leaves an implementer guessing is a defect.

## Before / after

**Before (round-1 style):**

> SQL blocks are the exact statements to emit, each run through spec 0012's
> `IDataBatch.Sql(sql, options)` with `SqlOptions.Writes` naming the tables written. Local names
> take the reserved `@tb{b}_` prefix, `{b}` being the batch ordinal. The pre-mark, settle and
> delivery update write `core.OutgoingEmails` from `Tellma.Core` through spec 0012's internal
> channel, as `IJobQueue` and `INotifier` do (spec 0015 §13.3); `retry` and `cancel` use the row's
> stack.

**After (round-2 style):**

> SQL blocks show the exact statements to emit.
>
> - **How they run.** Each statement goes through `IDataBatch.Sql(sql, options)` (spec 0012 §6),
>   with `SqlOptions.Writes` listing the tables it writes.
> - **Local names.** Variables and temp tables start with `@tb{b}_`, where `{b}` is the batch
>   ordinal, so two batches never collide.
> - **Who writes the table.** The job's own statements (pre-mark, settle, delivery update) write
>   `core.OutgoingEmails` directly, the same way `IJobQueue` does (spec 0015 §13.3). The user
>   actions `retry` and `cancel` go through the row's stack like any other save.
