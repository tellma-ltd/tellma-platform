# Spec designer brief

Each round-2 spec gets one design note, `.tmp/round-2/design/<NNNN>.md`, written by one
designer. A later author turns the note into the spec, so the note must contain every decision,
contract and behavior the spec needs. It is decision-dense and may be terse; the polish comes
later. Target 500–900 lines.

## Read first

1. `.tmp/round-2/README.md`, `.tmp/round-2/style-guide.md`.
2. `.tmp/round-2/design/00-architecture.md` — the cross-cutting skeleton. Its names, owners,
   routes, seams and open-question answers are binding. If you believe one is wrong, follow it
   anyway and raise it in your note's "Challenges to the architecture" section with the
   alternative and the evidence.
3. `.tmp/round-2-brief.md` — the requirements, especially your spec's parts.
4. The digests in `.tmp/round-2/facts/` relevant to your spec, then the existing specs' sections
   you build on or change (open them; the digests point at the sections).
5. The sketches in `.tmp/images/` and the design canvases in `.tmp/ui-design/` for your pages
   (authoritative for visual style, not architecture).
6. The design notes of specs already designed (listed in your task), so yours fits theirs.

## The note's sections

1. **Scope.** Owns / consumes (with owner) / out of scope (with who owns it).
2. **Decisions.** Numbered. Each: the decision, a one-line why, the alternatives considered.
3. **Design.** Organized as the future spec's numbered sections. For each: what it decides,
   contracts as TypeScript sketches (client) or C# sketches (server) with `//` comments, behavior
   rules as bullets, flows as numbered steps or mermaid, and at least one concrete example for
   every non-obvious contract (a distribution's code, a JSON wire pair, a URL, a walk-through).
   Include states, edge cases, error handling, limits, accessibility (keyboard model, ARIA roles,
   focus), RTL, and responsive behavior per breakpoint where they apply.
4. **Open questions owned.** Each brief question this spec owns: the answer, the alternatives, why.
5. **Edits to existing specs 0010–0022.** Exact list: target spec and section, the change (old →
   new, concretely enough that an editor can apply it), and why. Only changes this spec needs.
6. **Amendments to frozen specs 0001–0009.** Target spec and section, the delta, why. These go to
   spec 0010.
7. **Requests to other round-2 specs.** What you need another round-2 spec to provide; name it.
8. **UI rules.** How the design meets: instant warm launch, instant feedback, non-blocking,
   stable layout, RTL, WCAG AA (front-end specs), each with the test that pins it.
9. **Assistant seam, telemetry, reference distribution.** What the page publishes to the page
   context registry; the client events and metrics it emits; what `Tellma.Distro.Acme.Client`
   (and the server reference distribution) adds to demonstrate it.
10. **Testing.** Suites, tiers, what each pins.
11. **Review flags.** Judgment calls for Ahmad: the call, the alternative, what would flip it.
12. **Challenges to the architecture.** Anything in 00-architecture.md you think is wrong.
13. **Verified facts.** Web facts you relied on, with URLs.

## Rules

- **Design rule 1 above all for distribution-facing APIs:** show what a distribution writes for the
  common case (few lines), and name the escape hatch for the uncommon one.
- **One owner per concept.** Never redefine a concept another spec owns; consume it by name and
  cite its owner. If you need it changed, say so in section 5 or 7.
- **Existing specs:** 0001–0009 frozen (section 6). 0010–0022 changeable (section 5). Never design a
  workaround around an existing spec when changing it is the better design.
- **Verify** browser, Angular and library facts you depend on with a web search; record them in
  section 13.
- **Do not edit any spec or any file other than your note.** Write the note early and extend it.
