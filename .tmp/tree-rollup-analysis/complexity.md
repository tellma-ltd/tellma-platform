# Tree rollups in pivots — contract and code complexity (Part A)

Compares two ways to roll a tree dimension up to its ancestors. Performance is Part B's.

- **(L) Level navigations**, as specced: `Level{j}` path segments resolved on every tree entity,
  one grouping key per shown level, a depth cap (`levels` ≤ 10).
- **(C) Virtual closure with masked self-twins**, as designed in review: a host-only
  `QuerySpec.TreeRollups` option; `OUTER APPLY` over `k < GetLevel()` with a seek on
  `GetAncestor(k)`; engine-appended twin measures; the cube builder picks the measure or its twin by
  the level indicator. No language change.

Spec links: [0008](../../docs/specs/0008-queryex.md), [0010](../../docs/specs/0010-amendments-to-implemented-specs.md),
[0012](../../docs/specs/0012-data-access-layer.md), [0013](../../docs/specs/0013-settings-localization-and-cache.md),
[0015](../../docs/specs/0015-crud-service-pipeline.md), [0029](../../docs/specs/0029-client-entity-model.md),
[0033](../../docs/specs/0033-report-definitions-and-engine.md), [0034](../../docs/specs/0034-report-renderers-and-studio.md).

## 1. Contract inventory

Hardness, worst first: **P** persisted · **S** language syntax users can type · **W** wire/API
(HTTP JSON, TS public types, NuGet C# API, problem codes, string keys) · **I** internal.

### (L) Level navigations

| # | Hard | Surface | Where |
|---|---|---|---|
| L1 | **S→P** | `Level{j}` is a path segment "usable wherever a navigation is: every clause". Users can type `Center.Level1.Code = 'HQ'` in a report `filter`, `having` or `expr`, in a search `?filter=`, or in a permission criterion (all Queryex text, stored and recompiled later). Hidden from the path picker, but legal in the expression editor. | [0010 §F.9](../../docs/specs/0010-amendments-to-implemented-specs.md#L2698), [0029 §11.2](../../docs/specs/0029-client-entity-model.md#L829), [0008 stored expressions](../../docs/specs/0008-queryex.md#L370) |
| L2 | **P** | A report term `path: "Center.Level1"` resolves (0029 path resolution includes levels; aggregate mode keeps a key through one), so "group by top-level ancestor" can be stored as a flat dimension. | [0033 §3.4 rule 4](../../docs/specs/0033-report-definitions-and-engine.md#L321), [0029 §4.3](../../docs/specs/0029-client-entity-model.md#L379), [0029 §6.2](../../docs/specs/0029-client-entity-model.md#L502) |
| L3 | **P** | Report document `levels?: number` (1–10, default `min(MaxDepth, 6)`) sets the query shape. Changing its meaning needs a format upgrade in the migrator. | [0033 §3.1](../../docs/specs/0033-report-definitions-and-engine.md#L251), [format upgrades](../../docs/specs/0033-report-definitions-and-engine.md#L271) |
| L4 | **S/P** | Resolution order: property, then FK navigation, then `Level{j}`. A property or navigation named `Level<n>` (n ≤ maxDepth) is a `Build()` error, so the name is reserved on every tree entity for good. | [0010 §F.9](../../docs/specs/0010-amendments-to-implemented-specs.md#L2680) |
| L5 | W | Engine NuGet API: `EntityBuilder.TreeLevels(int)`, `EntityDescriptor.TreeLevels`. `PathUse.Segments` and `QueryexColumn.Path` carry `Level2`. | [0010 §F.9](../../docs/specs/0010-amendments-to-implemented-specs.md#L2662) |
| L6 | W | Problem code `Query.LevelNotSelectable` (argument `path`) on the query route. | [0015 §5.2](../../docs/specs/0015-crud-service-pipeline.md#L891), [0015 codes](../../docs/specs/0015-crud-service-pipeline.md#L1464) |
| L7 | W | Client metadata: `TmEntityMetadata.treeLevels`, `TmNavigationDescriptor.level`, synthetic `kind: 'reference'` descriptor with `foreignKey` `Level{j}.Id`. | [0029 §2.2](../../docs/specs/0029-client-entity-model.md#L155), [0029 §4.1](../../docs/specs/0029-client-entity-model.md#L333), [0029 §4.3](../../docs/specs/0029-client-entity-model.md#L379) |
| L8 | W | Core string key `Entity_Level` ("Level {j}"). | [0013](../../docs/specs/0013-settings-localization-and-cache.md#L1555) |
| L9 | W | Cube wire: `ReportCube.Truncated`, `CubeTruncation(Dimension, Levels, Depth)`; `CubeLevel.Level` is a fixed level count per tree dimension. | [0033 §9.3](../../docs/specs/0033-report-definitions-and-engine.md#L1076) |
| L10 | W | Studio Levels field (1–10); the truncation notice "Center shows 4 of 6 levels…". | [0034 §12.3](../../docs/specs/0034-report-renderers-and-studio.md#L1029), [0034 §3.3](../../docs/specs/0034-report-renderers-and-studio.md#L293) |
| L11 | I | Schema provider declares `TreeLevels([Tree].MaxDepth)`; compose-time refusal of a level path in an entity `Select`. | [0012 §11.1](../../docs/specs/0012-data-access-layer.md#L1772), [0012 §11.4](../../docs/specs/0012-data-access-layer.md#L1866) |
| L12 | I | Compiler: a key group per level `[Center.Level{j}.Id]`, label items per level, a truncation probe `max(level(Center.Node))`. | [0033 §8.5](../../docs/specs/0033-report-definitions-and-engine.md#L888), [0033 §8.6](../../docs/specs/0033-report-definitions-and-engine.md#L941) |
| L13 | — | Architecture text: "tree dimensions aggregate through to-one level navigations". | [ARCHITECTURE.md](../../ARCHITECTURE.md#L738) |

### (C) Virtual closure

| # | Hard | Surface | Where (to amend) |
|---|---|---|---|
| C1 | P | Report document: `tree: true` is unchanged. `levels` is dropped, or becomes a display-only "expanded to depth". Either works before ship; after ship it's a meaning change, but a safe one (it no longer shapes the query). | 0033 §3.1 |
| C2 | W | Engine NuGet API: `QuerySpec.TreeRollups` (key ordinal per rollup). Caller errors: the key is its own key group and an `Id` path to a `TreeNode` entity; at most *n* rollups. **Host-only**, like `KeyGroups`/`GroupingSets`, never user text. | 0010 §F.8-style ([caller errors](../../docs/specs/0010-amendments-to-implemented-specs.md#L2624)) |
| C3 | W | `QueryexColumn` marks appended twins (e.g. `TwinOf: int?`, `Mask: int`, a bit per rollup the twin masks). Twins are `Nullable`. | 0010 §F.8 ([`IsGroupingLevel` precedent](../../docs/specs/0010-amendments-to-implemented-specs.md#L2598)) |
| C4 | W | **Open: ancestor labels.** (a) `TreeRollup.Attributes`: select ordinals whose rollup-path prefix rebinds to the ancestor; (b) property names the engine appends as `MAX([A].[x])`; (c) none, with labels and `ParentId` read by a follow-up lookup of the ancestor ids. (a) has the oddest public semantics: the same path text means different rows in different items. (c) adds nothing to the engine API. | — |
| C5 | W | Cube wire: `Truncated` / `CubeTruncation` removed (or always empty). Tree `CubeLevel`s become data-dependent (the deepest present). `tm-pivot` is member-tree based and unaffected. | [0033 §9.3](../../docs/specs/0033-report-definitions-and-engine.md#L1076), [0034 §6.1](../../docs/specs/0034-report-renderers-and-studio.md#L509) |
| C6 | I | Compiler: one key per tree dimension; twins; a rule for `Having` over ancestor rows. | 0033 §8.5–§8.6 |
| — | removed | L1, L2, L4, L6, L7, L8, L10's Levels field (or repurposed), L11, L13. | — |

**Feature lost under C:** L2's "flat dimension at a fixed depth" (group by region = level 1) is
not expressible. It would need its own construct (e.g. a host option, or an `ancestorAt`
function, which is a language addition).

## 2. Internal implementation complexity

| Area | (L) | (C) |
|---|---|---|
| Binder / resolver | A third resolution step; a synthetic navigation per `j` not in `Navigations`; `descendantOf` key paths, `Via` and `Exists` exclusion through it. | QuerySpec validation only. Variant (a) needs a **second join identity for the same path prefix** (`Center` = the fact's center in `Filter`/measures, the ancestor in the key and attributes). The join trie is keyed by path, so this is the hardest binder change. (b)/(c) avoid it. |
| Emitter | One `LEFT JOIN … ON Node = CASE WHEN GetLevel() >= j THEN GetAncestor(GetLevel() − j) END` per level. `j` is an engine token. | `OUTER APPLY` of a 0…MaxDepth−1 numbers source per rollup, plus one ancestor join. Twins: each non-idempotent aggregate re-emitted with the mask ANDed into its condition argument (§2.1). The engine needs a depth bound (a schema fact or a constant 32), or `GENERATE_SERIES` (needs compat-level verification). |
| Nullity | Every path through a level is `Nullable`. | Twins `Nullable`. Keys unchanged (F.8's rules). |
| Pruning / `MaxJoins` | `levels` joins per tree dimension, plus labels sharing them. 2 trees × 10 levels = 20 of `ReportLimits.MaxJoins` 64. | 2 per rollup (apply + ancestor), independent of depth. |
| QX5008 budget | `levels` keys per tree, against 32 keys / 31 groups. | 1 key per tree. |
| Cube decoder | §8.6's m/j table: phantom from the null pattern, (None), redundancy dropping across sets, the truncation probe. | Each row of a set holding the key is a member. Parent from `ParentId`. Phantom = twin when the member has children. No redundancy, no truncation. **New:** orphan handling when `Having` drops an ancestor row but keeps its descendants. |
| Pre-aggregation (optional) | n/a | A derived table grouped by (node, column keys) before the fan-out. A new emission shape in 0008. Needs decomposable measures (`avg` → sum + count). Adopt only if Part B shows it's needed. |
| Client (0029) | Resolution, descriptor synthesis, `reachable` through levels, select-plan aggregate mode. | None. |
| Studio (0034) | Levels field, truncation notice. | None, or a display-only "expand to depth". |

### 2.1 Twin rule (refines the review design)

The twin of a non-idempotent aggregate is **the same aggregate with the mask added to its
condition argument**:

- `sum(x, c)` → `SUM(CASE WHEN c AND k = 0 THEN x END)`;
- `count(…)` and `avg(x, c)` the same way. `AVG` ignores nulls, so the masked `AVG` is exact and
  needs no sum/count decomposition. This also keeps 0008's `avg` typing and scale.

`min`/`max` need no twin. With *t* tree rollups, each non-idempotent measure needs 2^t − 1 twins,
one per non-empty subset of masked rollups. A pivot allows 5 row + 3 column dimensions
([0033 §3.4](../../docs/specs/0033-report-definitions-and-engine.md#L318)), so C needs a new cap
(≤ 2 tree rollups keeps twins ≤ 3 per measure).

### 2.2 Test burden

| Suite | (L) | (C) |
|---|---|---|
| Engine goldens | F.9: child root, tree root, `descendantOf` through a level, build errors. | APPLY + twins: 1 and 2 rollups; with conditions; `avg`; null keys. |
| Differential interpreter ([0008 §18](../../docs/specs/0008-queryex.md#L2449)) | A to-one per-row ancestor function: small. Fixture `TreeLevels(4)` ([0010 Part F](../../docs/specs/0010-amendments-to-implemented-specs.md#L2378)). | Must model the **fan-out** (one row per ancestor), the twin masks per grouping set, and the indicator-driven choice. It's also the guard that a twin equals a naive per-set computation. Medium. |
| Host / client | 0012 compose refusal ([0012 tests](../../docs/specs/0012-data-access-layer.md#L1990)); 0015 code; 0029 resolve units ([0029 §17](../../docs/specs/0029-client-entity-model.md#L1122)). | None. |
| Cube builder ([0033 §15](../../docs/specs/0033-report-definitions-and-engine.md#L1319)) | Phantom, (None), leaves, redundancy, truncation. | Phantom via twin, (None), leaves, orphans under `Having`, member order. |

## 3. Reversibility

- **L locks in the language.** `Level{j}` is user-typeable syntax (L1) and a storable dimension
  path (L2). A stored definition that stops compiling answers `400 query-invalid`
  ([0033 §8.3](../../docs/specs/0033-report-definitions-and-engine.md#L835)), and a permission
  criterion breaks the same way. So once shipped, `Level{j}` stays even if pivots later move to C.
  **L → C later means maintaining both** (L's resolver/emitter/client surfaces, plus C). `levels`
  would turn into a display setting and `CubeTruncation` into dead wire, which only an API
  version can drop.
- **C keeps fan-out out of the language.** `TreeRollups` is host-built structure, like
  `GroupingSets`, so 0008's "no collections" invariant still holds for every user-typed clause
  ([0008 decision 8](../../docs/specs/0008-queryex.md#L2768)). Nothing persisted names it (documents
  keep `tree: true`). **C → L later** touches only the 0033 compiler, the cube decoder, and an
  obsoleted `QuerySpec` member, then adds L's surfaces fresh with an additive `levels` field.
- **Asymmetry:** choosing C first is cheap to reverse. Choosing L first is not, because of L1/L2.

## 4. Variants that reduce lock-in

| Variant | What changes | Lock-in | Cost |
|---|---|---|---|
| **C without attributes** (C4 c) | `TreeRollups` = key ordinals only. Labels and `ParentId` come from a keyed lookup of the ancestor ids. | Smallest engine contract. | +1 round trip unless the batch can capture keys from an aggregate statement (0012's `CaptureKeys` exists only for `Rows` ([0012](../../docs/specs/0012-data-access-layer.md#L876))). |
| **C in C#** (cube-builder rollup) | No engine change. SQL groups by leaf `Center.Id`. The cube builder sums leaves into ancestors fetched with labels. | Zero engine/language contract. Only 0033 internals. | Measures must decompose (`avg` emitted as sum + count). A larger SQL result (leaf × column rows). Ancestors need a second statement. Grand totals still come from SQL. Note: 0033 decision 6 forbids *client* aggregation, not server-side cube aggregation ([0033](../../docs/specs/0033-report-definitions-and-engine.md#L1360)). |
| **L as a host option** | Keep level joins, but drive them from a `QuerySpec` option instead of `Level{j}` syntax. | Removes L1/L2/L4/L6/L7/L8/L11. | Keeps the depth cap, the per-level key budget and the m/j decoder. |

## 5. Bottom line

| | (L) as specced | (C) closure + twins | C in C# |
|---|---|---|---|
| Persisted / typeable lock-in | `Level{j}` in user text and stored paths; reserved `Level<n>` names; `levels` shapes queries | none (`tree: true` only) | none |
| Public API surfaces added | ~9 (engine 2, 0015 code, 0029 ×2, 0013 key, cube truncation, studio field, schema provider) | ~2–3 (`TreeRollups`, twin column marker, maybe `Attributes`) | 0 |
| Internal complexity (rough, unverified) | low–medium engine; medium decoder; client work | medium–high engine (twins; join identity under C4 a); low decoder | none in engine; medium decoder + measure decomposition |
| Test burden | moderate, spread across 0010/0012/0015/0029/0033 | concentrated in the engine's differential suite (fan-out + masks) | cube-builder units + integration |
| Depth | ≤ 10 shown, truncation notice | full `MaxDepth` | full `MaxDepth` |
| Extra capability | flat "ancestor at depth j" dimension | — | — |
