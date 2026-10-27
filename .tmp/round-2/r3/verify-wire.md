# Verify: wire cluster (decisions 18, 20, 21, 22; R-10..R-13, R-15, R-25, R-28)

Findings V4-nn, grouped by owner (editor-notes.md §5). Format per fix-notes.md §3.

Checked and found consistent (no finding): 0016 §3.2 scalar table, the string rule, inbound
string-or-number and Wire ids; every JSON example in 0013–0035 (decimals and longs are strings, ints
numbers: 0016 §3.2/§3.5–§3.7/§7.4 aside from V4-02, 0028 §4.3 `targetId`, 0031 §8.2 and §10.4,
0033, 0034, 0035); 0013 §5.3 setting values; 0017 `size`; 0021 and 0028 `TargetId`; 0024 §10.1
`TmDecimal`, `TmId`, `TmDecimals` (all seven helpers), `TmJson` without an object member, §10.4,
Decision 6, Review flag 2; 0029 `long?: true`, §12.2, §15, `TmId` on generic APIs; 0025, 0026,
0030–0032, 0034 client types; no `TmDecimal` class or lossless parser except as rejected
alternatives; 0016 Decision 5. `Long`/`Idempotent` on 0015 §2.2–§2.3 and 0016 §4.1; 0016 §2.2
(activate/deactivate retried, R-10), §2.4, §4.3, §4.5, §8.3, §13.3; 0019's four Excel actions;
0033 `run`, `export`, `audience` and the coded route (`Idempotent`, `Long`); 0035 `audience`;
every `TmApi.read` call site in 0025–0035 targets a GET, a stack read or an `Idempotent = true`
action; 0030/0034 dropped `long: true`; 0034 §2.2's hatch rule (R-28). `forbidden`: 0013
`Problem_forbidden_Detail`, 0014 §5.3, every 0015 raise site and §14.1, 0016 §3.8/§5.6/§7.1/§13.3,
0018 §9, 0024 §2.4/§10.6/§11, 0029 `TmPermissions.label`, 0032 §4.6, 0033 §8.2. 412: 0016 §3.10,
0024 §5.5/§5.6/§11, 0026 §7 (`compare` semantics and vectors), 0027 §11.1; R-15 names in my scope
(`TmContracts`, `TmUpdateCheck`, `TmErrorView.actions`, `TmAppVersion.serverNewer`).

## 0015-22

- [V4-01] (must) 0016 §1.1, §3.10 (three places), §14 — "It holds `IContractFingerprints`
  (§3.10)"; "**The fingerprint.** `IContractFingerprints` (`Tellma.Core`, framework-free) computes";
  "public interface IContractFingerprints"; "Beside the fingerprints, `IContractFingerprints`
  answers two facts"; DoD "`IContractFingerprints` and its client-asset contributor" → rename
  `IContractFingerprints` to `IRouteContracts` at all five places (lines ~116, 792, 806, 820, 2100).
  Why: R-12.
- [V4-02] (must) 0015 §14.1 — "`public sealed record ConcurrencyConflict(long Id, string?
  ModifiedAt, long? ModifiedById, string? ModifiedByName, bool IsMissing);`" → `long? ModifiedById`
  becomes `int? ModifiedById`. A `core.Users` id is an `int` (0012's `int ModifiedById`, 0016 §3.4
  `UserProfileView(int Id …)`); typed `long`, 0016 §3.2's per-CLR-type rule writes it as a JSON
  string, which contradicts 0016 §7.4's example `"modifiedById": 3` and 0031 §9's
  `TmConflictInfo.modifiedById?: number`. Why: decision 18 (`int` stays a number).
- [V4-03] (should) 0016 §3.10, "The contributor" and the example — "writes the `contracts` section
  of the Contracts chunk from `All`, `Long` and `Idempotent`" and "**Example —** the `contracts`
  section, shortened:" → "writes the Contracts chunk's three sections `fingerprints`, `long` and
  `idempotent` from `All`, `Long` and `Idempotent`" and "**Example —** the Contracts chunk,
  shortened:"; in §13.3 Contract "The emitted `contracts` section equals `contracts.json`" → "The
  emitted Contracts chunk equals `contracts.json`". As written the chunk is `{ contracts: {…} }`,
  while 0024 §3.1's loader returns `TmContracts` ("the Contracts chunk (spec 0016 §3.10)") with
  `fingerprints`, `long` and `idempotent` at the top. Why: decision 20 (one chunk shape in 0016 and
  0024).
- [V4-04] (should) 0019 §12.3 step 2 — "`Fail(JobError("forbidden", { resource, action, reason
  }))`, the code and arguments of the denial (spec 0015 §14.1)" → "`Fail(JobError("forbidden", {
  resource, action, reason, path }))` (`path` only for `restricted-path`)". The report case's
  refusal in step 3 "fails the item the same way", and 0033 §8.2 raises `restricted-path` with the
  path; 0015 §14.1's `Arguments` carry `path`. Why: decision 21.
- [V4-05] (should) 0015 §14.1 and 0016 §7.1 — "`LimitExceededException(string Limit, long Actual,
  long Maximum)`" / "`arguments.limit`, `.actual`, `.maximum`" → state the wire form: either type
  `Actual` and `Maximum` as `int` where every ceiling fits, or add to 0016 §7.1's row "`.actual` and
  `.maximum` are `long`s, so JSON strings (§3.2)". 0016 §3.2 encodes `arguments` "by the declared
  type", so they are strings today while `CountMismatchException(int Expected, int Actual)` sends
  numbers; no spec says so. Why: decision 18 (every server table agrees with the string rule).
- [V4-06] (should) 0016 §3.2 (or §12.1) — §12.1 says "schemas are the real types of §4.2", and no
  spec says how a schema describes a `decimal` or `long` that travels as a string (OpenAPI §12.1,
  MCP `inputSchema` §11.5, the action metadata's `arguments` JSON Schema of 0029 §2.2) → add one
  bullet under "`decimal` and `long` are JSON strings": "**Schemas.** OpenAPI, MCP `inputSchema` and
  the client metadata's argument schemas describe a `decimal` as `{ "type": ["string", "number"],
  "format": "decimal" }` and a `long` as `{ "type": ["string", "integer"], "format": "int64" }`;
  outbound is always the string." Why: decision 18 (exact for agents and scripts); a custom
  converter gives the generated schema no type of its own.

## 0024

- [V4-07] (must) 0024 §14, "Server pieces" — "spec 0016's string decimals (§3.2) and
  `IContractFingerprints` (§3.10)" → "spec 0016's string decimals (§3.2) and `IRouteContracts`
  (§3.10)". Why: R-12.
- [V4-08] (must) 0024 §14, "What Acme adds" — "the hand-mapped `acme-reports/credit-exposure`,
  called through `TmApi.read` with `long`." → delete this bullet and extend the `acmeFeature` bullet:
  "the credit-exposure coded report (`acme-reports/credit-exposure`, a projected `[ApiAction]` with
  `Long = true`, spec 0033 §11, which the Contracts chunk lists, so no call passes `long`)". 0033
  §11, 0034 §2.2 and 0018 §1.4 make it a projected action, not a hand-mapped route. Why: R-28.
- [V4-09] (should) 0024 §3.4, `long` bullet — "the actions declared `Long = true` (spec 0015 §2.3)
  and the Excel operations" → "the actions declared `Long = true` (spec 0015 §2.3), spec 0019's
  four Excel operations among them". 0019 §1.2 declares `Long = true` on `export`,
  `export-for-import`, `inspect-import` and `import`; one flag drives the list. Why: decision 20 /
  request from 0015-22.
- [V4-10] (should) 0024 §10.1 — "`interface TmWriteOptions { tenantId?: number; long?: boolean }
  // no signal: a write is never aborted`" → give `long` the same comment as `TmReadOptions.long`:
  "only for a route outside the Contracts chunk: the watchdog allows 6 min (§3.4)". Why: decision 20
  (the `long` option stays only for hand-mapped routes), R-28.
- [V4-11] (should) 0024 §10.4 "Arithmetic", §2.1 lint list, §12 gate row — "`+` on a `TmDecimal`
  would join text, so typescript-eslint's `restrict-plus-operands` refuses it"; gate "no `+` on a
  `TmDecimal` (§10.4)" → `restrict-plus-operands` refuses `TmDecimal + number` but allows `string +
  string`, so `total + amount` on two `TmDecimal`s passes and concatenates. Keep the rule and add a
  type-aware `@tellma/core-tools/eslint` rule (for example `tm/no-decimal-operators`) that refuses
  `+`, `+=`, `<`, `<=`, `>`, `>=` when an operand is a `TmDecimal` ("use `TmDecimals.add` /
  `compare`"); name it in §2.1, §10.4 and the §12 gate row. Why: decision 18 (the lint exists to
  stop text arithmetic; as written the spec claims a guarantee the rule does not give).

## 0028-32

- [V4-12] (should) 0031 §10.4 `TmEntityActionOutcome` — "`| { readonly kind: 'entities'; readonly result:
  TmEntitiesResult }  // spec 0016 §3.6's envelope`": `TmEntitiesResult` is declared in no spec, so
  its `ids` type is open → declare it beside the sketch: "`interface TmEntitiesResult<T =
  TmJsonObject> { readonly ids: readonly TmId[]; readonly entities: readonly T[]; readonly related?:
  Readonly<Record<string, readonly TmJsonObject[]>>; readonly extras?: TmJsonObject; readonly rows?:
  TmQueryRowSet }`". Why: R-11 (generic id lists follow the key type; client types use `TmId`).
