# r8 — editor A: H-1 applied

Files: `docs/specs/0010-amendments-to-implemented-specs.md` (Part F, plus two Part F-scoped
lines outside its heading), `docs/specs/0012-data-access-layer.md`. Unstaged.

## Member name

`QuerySpec.UndeclaredColumns` (`IReadOnlyList<string>`, default `[]`), not the proposed
`RawColumns`. Why: 0008 §3 says a column the language cannot use "is simply not declared", and
`PropertyDescriptor.Column` is 0008's term for an unquoted physical column name. The name says
what the list holds (root columns the schema does not declare) without suggesting raw SQL text.

## Changes — spec 0010 Part F

1. **Part F intro**: one sentence added after the §F.8–§F.15 sentence: the data access layer needs
   one last change to read the columns its schema omits (§F.16).
2. **§F.0 Order**: §F.16 ships first with §F.1–§F.4, §F.6, §F.7 (spec 0012 needs it).
3. **§F.0 Cache keys**: L3 gains "the undeclared columns in order".
4. **§F.0 Reference interpreter**: lists undeclared columns; `LedgerFixture` gains "an
   `nvarchar(max)` column its schema does not declare, for §F.16".
5. **New §F.16 `QuerySpec.UndeclaredColumns`**, after §F.15, in the Target / Requested by / Why,
   sketch, bullets, example, Compatibility/Tests shape:
   - names are unquoted physical columns of the root's `Source`, matching
     `[A-Za-z_][A-Za-z0-9_]*` (the identifier rule §F.1 uses for `TableSource`); not looked up in
     the schema; existence and character type are the host's obligation; the fourth host-authored
     text to reach SQL after `TableSource` (0008 §3);
   - emission: one more select item each, after the compiled ones, in list order, `[T].[<name>]`
     on the root alias (0008 §12.2); `Body` carries them (§F.3);
   - columns: `QueryexColumn` per name after the compiled ones, `Text` = name, `QxString`,
     `Nullable`, `Path` null, `IsGroupingKey` false (0008's actual member and enum names);
   - nothing reads them: joins, slots, ordering, tiebreakers unchanged;
   - row grain only: requires `Aggregate` unset; `Measures` requires `Aggregate` (§F.14), so it
     never meets the list;
   - discovery: `QueryDiscoverySpec` gains nothing;
   - caller errors: bad identifier, duplicate ignoring case, non-empty with `Aggregate`;
   - example over `acme.SalesInvoiceLine` with a hypothetical `ExtrasJson` (same device as
     §F.14's `Cost`/`Discount`), SQL block and the resulting `Columns[3]`;
   - compatibility (empty → today's SQL and columns) and tests.
6. **Part F — Testing**: goldens list gains "undeclared columns"; L3 splits gain them.
7. **Part F — Definition of done**: Engine §F.6–§F.16; Tool accepts undeclared columns; Not in
   scope adds "spec 0012 … passing its `[JsonColumn]` columns".
8. **Part F — Decisions record**: #1 reworded ("the data access layer's items live here", refs
   §F.1–§F.7, §F.16) since "seven items" was no longer the count; new #14 (names through
   `CompileQuery`, not splicing into `Body` before `FROM`).
9. **Part F — Review flags**: new #9 (undeclared columns are text only; alternative a
   `QueryexType` per column).
10. **Outside the Part F heading, Part F-scoped only**: the Goals "Part F" bullet gains
    "and undeclared root columns (§F.16)"; the "Other specs" bullet for spec 0012 becomes
    "§11.1–§11.2, §11.4 … and the `[JsonColumn]` columns it passes to §F.16". No other part touched.

## Changes — spec 0012

1. **§11.2**: amendment list gains "undeclared root columns (§F.16)"; "Where the amendments are
   used" gains an **Undeclared columns** bullet pointing at §11.4.
2. **§11.4 "Every mapped property"** rewritten: the host passes the root's `[JsonColumn]` names as
   `QuerySpec.UndeclaredColumns` (spec 0010 §F.16); the engine reads them on the root alias after
   the compiled items and reports a `String`, `Nullable`, no-`Path` column each; reader buffers
   `string?[]` (§11.3). Child rule kept: each child query takes the flag and passes its own
   table's `[JsonColumn]`s. Dropped: "before its `FROM` line", "writes them at the end of the
   compiled `Body`'s select list", "adds one `QueryColumn`", and the `[T].[DumpJson]` /
   0008 §13.4 citation.
3. **§11.5**: "carry no `QueryexColumn`" was false under §F.16 (they now have one); reworded to
   "carry no `Path`; the materializer sets each by its column name (`QueryexColumn.Text`)".
4. **§13.1 Unit row**: the golden-SQL wording still holds in substance, but the phrase
   "and §11.4's projections in an `AllProperties` query's" was ungrammatical; it now reads "and an
   `AllProperties` query's, root and child `[JsonColumn]` columns included (§11.4)".
5. **Context**: does not enumerate Part F items (only "spec 0010 Part F (§11.2)"); unchanged.
   §13.3's `AllProperties` integration line still holds; unchanged.

## Deviations from the pinned detail

- Name `UndeclaredColumns` instead of `RawColumns` (above).
- "With grouping, `Measures` or aggregates present" phrased in 0008's terms: `Aggregate` is the
  single switch onto the Group axis (0008 §2.1, §11), and aggregations under `Aggregate = false`
  are already QX4002, so the only caller error is a non-empty list with `Aggregate`.
- `DiscoverQuery` ignores the list: `QueryDiscoverySpec` gains no counterpart. 0012 §11.4 runs
  discovery on the select text only to learn paths; the `[JsonColumn]` names come from metadata.
- Select-item aliases: 0008 §13.4 does not fix them; the §F.16 SQL block follows Part F's existing
  `AS [c<n>]` convention (§F.9, §F.11, §F.14).

## Notes for Ahmad

- The identifier rule (`[A-Za-z_][A-Za-z0-9_]*`) is stricter than `PropertyDescriptor.Column`,
  which accepts any non-empty name and is bracket-escaped. I chose it to match §F.1's
  `TableSource` rule; every `[JsonColumn]` in specs 0012–0035 satisfies it. Loosen it to 0008 §3's
  `Column` rule if a pack may name columns otherwise.
- The type is fixed at `QxString` (flag 9). Every current `[JsonColumn]` is `nvarchar(max)`, so it
  holds; a non-text undeclared column would hit 0012 §11.3's `InvalidOperationException`.
- I edited two lines of 0010 outside the Part F heading (Goals' Part F bullet, the "Other specs"
  line for 0012), since both enumerate Part F's items/hosts. Revert if Part F-only meant the
  heading strictly.

## Fix pass

- **R8-1 applied.** Spec 0010 Goals, Part F bullet: dropped the first "and" ("…`ComparedWith`,
  measure partials with their C# evaluation (§F.8–§F.15), and undeclared root columns (§F.16).")
  and rewrapped.
- **R8-2 applied.** Spec 0010 §F.0 reference-interpreter bullet and Part F Definition of done "Not
  in scope of done" bullet rewrapped to ≤100 columns; the orphan "spec" line is gone. Wording
  unchanged.
- **R8-3 applied.** Spec 0025 §13 intro: "`TmRecents` lists the pages the user last visited in this
  tenant, at most ten, for the launcher (spec 0027 §9.3) and the home page (spec 0035 §15.3)." The
  Access bullet owns the filter; the over-long second line was rewrapped.
- **R8-4 applied.** Spec 0012 §11.4: the engine's emission position and column shape are §F.16's
  (Emission, Columns bullets), so 0012 now only cites §F.16 and keeps its own fact: "the reader
  buffers each of their `String` columns as `string?[]` (§11.3)". The child-query and no-flag
  sentences are unchanged. §11.5's "carry no `Path`; the materializer sets each by its column
  name" still holds and is the materializer's own fact.

Checks: all touched files LF, no BOM; the index is unchanged (no file newly staged).

### Notes for Ahmad

- The three notes above (identifier rule stricter than `PropertyDescriptor.Column`, type fixed
  at `QxString`, the two 0010 lines edited outside the Part F heading) remain open.
