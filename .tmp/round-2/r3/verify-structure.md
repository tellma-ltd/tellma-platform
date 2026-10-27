# Verify: structure (spec hygiene across the round's diff)

Findings V7-nn, grouped by owner (editor-notes.md §5). Format per fix-notes.md §3.

Checked against baseline `063e313`, over the whole diff of 0010–0035 and ARCHITECTURE.md, and found
right (no finding):

- **History narration.** No "previously", "no longer", "now uses", "was changed", "instead of the
  old", "this round", "review round", "per Ahmad" or decision numbers of the review in any added
  line. The remaining "now"/"no longer" hits are user copy ("Your device is now on …", "This report
  no longer runs") or behaviour ("a tenant that no longer exists"). 0010's "becomes", "replaces" and
  "today" describe deltas to frozen specs, as the amendment format requires.
- **Working files.** No spec or ARCHITECTURE.md cites `.tmp/`, design-measures, design-locale,
  g1–g5, review-r1, editor-notes, fix-notes, decisions.md, R-nn rulings, or R1–R9 / F1–F6 as names
  (the `.tmp` hits in 0017 are the blob store's folder; the `F2` hits are the key).
- **Headings.** No existing section of 0011–0035 is renumbered. New sections sit at the end of
  their parent: 0010 §B.7, §B.8, §C.13, §D.11, §E.5, §F.14, §F.15; 0024 §7.7, §7.8; 0033 §8.11.
  Renamed in place: 0010 §C.12, §D.4; 0024 §9.3, §10.4 (Plain JSON), Appendix A (Build problem
  codes); 0032 §4.2; 0033 §8.5, §8.6. 0010 §F.8/§F.9 cut and §F.10–§F.15 → §F.8–§F.13 per
  decision 5. Every citation of a renamed or cut section was grepped: no `§F.CUT-`, no stale
  §F number (every `§F.8`–`§F.15` citation in 0010, 0012, 0013, 0029, 0033, 0034, 0035 matches
  today's topic), no "lossless" citation of 0024 §10.4 outside rejected alternatives, no
  "Configuration problem codes", `tellma-client init` or `tellma-client check`; every citation of
  0033 §8.5/§8.6 fits the leaf statement/rollup. Every spec-qualified `spec NNNN §x` citation in
  0010–0035 resolves to an existing heading, and every § reference inside a Decisions row or
  Review flag resolves.
- **Decisions and flags.** Every Decisions record and Review-flag list of the 24 changed specs is
  numbered 1…n with no gap or duplicate (0010 Parts A–F checked separately). Renumbered lists:
  0010 Part F decisions (old 10–14 → 6–10, new 11–13) and flags (old 7 → 5, 8 → 6, new 7–9);
  0016 flags (3–22 → 2–21); 0023 flags (11 → 10, new 11). Cross-checked every citation by number
  (paragraph-joined grep of "flag N", "Review flag N", "decision N", "Part X decision N"): 0010
  "flag 4" (Part F Testing, DoD) is the hoisted `'uq'` flag; "Part C flag 6", "Part C decision 9",
  "Part D decision 11" match; 0033 §9.4 "(flag 5)" is the drill flag; 0013 "Review flag 7" is the
  Latin-digits flag; 0014 → 0021 flag 6, 0032 → 0030 flag 14, 0035 → 0034 flag 12 all match.
  Nothing anywhere cites a 0016, 0023, 0024 or 0010 Part F flag or decision by number, and no
  research doc does either. No Decisions row or flag still describes a removed concept (levels,
  grouping sets, lossless parsing, `init`, `check`, aging) except as a rejected alternative.
- **Testing and Definition of done.** Each new section has a test and a DoD line: §B.7 (Playwright
  row; DoD Glyphs), §B.8 (unit and `tokens:check` rows; DoD Tokens), §C.13 (Playwright; DoD
  Glyphs), §D.11 (Playwright; DoD Glyphs), §E.5 (Playwright; DoD Glyphs), §F.14 and §F.15 (unit
  vectors, the measure identities, rank exactness; DoD "§F.6–§F.15"), 0024 §7.7 (`TmLocalization`
  row, offered pairs, `regional-formats.spec`; DoD server pieces and the Instant-feedback row),
  0024 §7.8 (`TmDeviceZone` row, `device-zone.spec`, `zone-labels.spec`; DoD Non-blocking and
  Stable-layout rows), 0033 §8.11 (the Runs suite's 422s; DoD "Every section (§2–§14)").

## 0010-AE

- [V7-01] (should) 0010 Part B — Decisions record 10 — "**Every built-in glyph draws through `tm-icon`
  with `lucide` data; `tm-spinner` and the grid's per-cell masks are the exceptions**" duplicates
  Part C decision 17 "**The grid's per-cell check and dash stay CSS masks** — a grid paints
  thousands of them; a `tm-icon` per cell was the alternative (§C.13)" → in Part B decision 10,
  replace "`tm-spinner` and the grid's per-cell masks are the exceptions" with "apart from
  `tm-spinner` and the grid's masks (Part C decision 17)", so the masks' decision is recorded once,
  in the part that owns §C.13. Why: docs/AGENTS.md "state each fact once"; no duplicate Decisions
  entries.

## 0024

- [V7-02] (should) 0024 §9.3, first bullet — "redirects straight to `/{id}` (spec 0011 §5.5). The
  tenant shell paints first, with no tenantless skeleton and no shell swap." → delete the second
  sentence. It is Decision 22's Why word for word ("the tenant shell paints first, with no
  tenantless skeleton and no shell swap"), and §14's Stable-layout row already pins it. Why:
  docs/AGENTS.md (rationale only in the Decisions record; each fact once).

## 0033

- [V7-03] (should) 0033 §9.3, "Which members exist" — "A member exists when at least one surviving
  leaf row sits under it; a phantom child when its node's own row and something below it survived
  (§8.6)." restates §8.6's "Members" and "The phantom child" bullets → "**Which members exist.**
  §8.6 decides, over the surviving leaf rows. The axis also has the `Total` member (id 0, depth 0)
  when it has totals or no dimension." Why: docs/AGENTS.md "state each fact once, in the section
  that owns it, and cross-reference it elsewhere".

## 0023-29

- [V7-04] (should) 0023 §3.1 — the intro "Every glyph a library component draws goes through
  `tm-icon`." is contradicted by the closing bullet's "Two exceptions keep their own drawing:
  `tm-spinner`'s animated arc, and the grid's per-cell check and dash masks", and that bullet's "So
  every glyph shares one stroke, one size token and one RTL rule." repeats Decision 19's Why ("one
  stroke, one size token and one RTL rule instead of hand copies") → delete the intro sentence
  (the bullet states the rule with its exceptions) and delete "So every glyph shares one stroke,
  one size token and one RTL rule." Why: docs/AGENTS.md (each fact once; rationale only in
  Decisions). Independent of V2-03, which rewords the stroke value in the same section.
- [V7-05] (should) 0023 §4.2, "Which key" — "never an email or a `sub`: it is stable, reveals
  nothing, and arrives with every row." → "never an email or a `sub`." Decision 20 holds the why ("a
  record key is stable, private and on every row, which an email or `sub` is not"). Why:
  docs/AGENTS.md (rationale only in the Decisions record).

## 0011-14

- [V7-06] (should) 0011 §5.5, "The post-sign-in landing" — "A sign-in that returns to `/` lands on
  the user's one live tenant, so a single-tenant user sees the tenant shell first, with no
  tenantless skeleton and no shell swap." → "A sign-in that returns to `/` lands on the user's one
  live tenant." Decision 29 states the why ("a single-tenant user sees the tenant shell first, with
  no tenantless skeleton"). Why: docs/AGENTS.md (rationale only in the Decisions record).

## 0015-22

- [V7-07] (should) 0016 Review flags — flag 2 "**Decimals as JSON numbers with a lossless client
  parser** (§3.2) versus decimals as JSON strings" was deleted and flags 3–22 renumbered to 2–21,
  so the spec that owns the wire rule (§3.2, Decision 5) carries no flag for it; only 0024 flag 2
  does ("`decimal` and `long` as JSON strings (§10.4) | JSON numbers with a lossless client parser
  | users of generic tools (Power Query, BI) …") → rewrite 0016 flag 2 in place instead: "2.
  **`decimal` and `long` as JSON strings** (§3.2) versus JSON numbers with a lossless client
  parser. Flips if users of generic tools (Power Query, BI) need numeric columns without setting a
  type.", and give today's flags 2–21 back their numbers 3–22 (nothing cites them). 0024 flag 2 may
  then point at it ("the wire call is spec 0016's flag 2") rather than restate it. Why:
  docs/AGENTS.md "a reversed decision rewritten in place"; decision 18; each fact once.
- [V7-08] (should) ARCHITECTURE.md, "An installable PWA on the Angular service worker" — "Angular's
  worker takes security fixes only; the platform stays on it because it already ties each tab to
  its version" → "Angular's worker takes security fixes only; the platform uses it because it ties
  each tab to its version". "Stays on it" and "already" narrate a reconsidered choice. Why:
  docs/AGENTS.md (state the decision as it stands, no narration); decision 14.
