# AGENTS.md for docs/

Rules for the documentation in this folder.

## Rules
- Docs inside `docs/` are frozen once merged into main: they are never updated to reflect later changes in whatever they describe.
- Docs must not reference specific sections of [ARCHITECTURE.md](../ARCHITECTURE.md), or any other living document or code file, since those may change out from under them.
- Docs may reference other docs inside `docs/`, since those are frozen too.

## Revising specs across review rounds

Specs accrete bloat when revised round by round: each round tends to *append* prose defending the
change instead of folding it in cleanly, so redundancy, narration, and rebuttals pile up over
successive rounds. Revise as if writing the final document fresh for a reader who never saw the
review — not as a reply to the reviewer.

- Revise by **replacing** stale text, not appending to it. A review round should usually leave the
  spec about the same size, not visibly larger; if it grew, check whether the growth is a genuinely
  new requirement or just accreted justification.
- State the decision **as it stands now**; do not narrate how it got there. Cut self-referential
  phrases like "now validated by…", "previously X, now Y", "the … framing is dropped", "yes,
  supported", "to answer the common case directly".
- State each fact **once**, in the section that owns it, and cross-reference it elsewhere rather than
  restating it.
- Do not **re-litigate** alternatives. Record the chosen design and a proportionate "why" — not a
  rebuttal of every option a reviewer raised.
- Keep the design separate from the conversation that produced it. If decision history is worth
  keeping, confine it to one dedicated "Decisions"/rationale section (or a separate doc), never
  interleaved through the spec.
