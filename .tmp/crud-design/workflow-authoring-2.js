export const meta = {
  name: 'crud-stack-spec-authoring-2',
  description: 'Finish spec 0015, write specs 0017, 0018, 0020; then two cross-spec reviewers and per-spec fixers over all eleven specs',
  phases: [
    { title: 'Author', detail: 'three authors and one completion, four in flight' },
    { title: 'Review', detail: 'two cross-spec reviewers' },
    { title: 'Fix', detail: 'one fixer per spec, at most four in flight' },
  ],
}

const ROOT = 'C:/Users/ahmad/workspace/tellma/tellma-platform'
const D = `${ROOT}/.tmp/crud-design`
const SPECS_DIR = `${ROOT}/docs/specs`

const SPECS = [
  { n: '0010', key: 'host-tenancy', file: '0010-distribution-host-and-multitenancy.md', title: 'Distribution host and multi-tenancy' },
  { n: '0011', key: 'data-access', file: '0011-data-access-layer.md', title: 'Entity contract and data access' },
  { n: '0012', key: 'settings-cache-l10n', file: '0012-settings-localization-and-cache.md', title: 'Tenant settings, localization, and the version cache' },
  { n: '0013', key: 'users-roles-permissions', file: '0013-users-roles-and-permissions.md', title: 'Users, roles, and permissions' },
  { n: '0014', key: 'service-pipeline', file: '0014-crud-service-pipeline.md', title: 'CRUD service pipeline and capabilities' },
  { n: '0015', key: 'web-api-mcp', file: '0015-crud-web-api.md', title: 'Web API surface and the Tellma Tenant MCP server' },
  { n: '0016', key: 'blobs', file: '0016-blob-storage.md', title: 'Blob storage and the record-plus-blobs pattern' },
  { n: '0017', key: 'core-gl-stacks', file: '0017-core-and-gl-reference-stacks.md', title: 'Core and GL reference stacks' },
  { n: '0018', key: 'excel', file: '0018-excel-codec.md', title: 'Excel codec: export and import' },
  { n: '0019', key: 'background-inbox', file: '0019-background-jobs-and-scheduler.md', title: 'Background jobs and the scheduler', part: 'This spec covers the job machinery, the worker, handlers, the scheduler and the first consumers; the same decisions file also designs notifications, the inbox and the hub, which belong to spec 0020 — the ledger §0 says which decisions each spec reads. Cite spec 0020 for the notification and hub contracts you consume.' },
  { n: '0020', key: 'background-inbox', file: '0020-notifications-inbox-and-hub.md', title: 'Notifications, the inbox, and the hub', part: 'This spec covers notifications, notification preferences, the inbox and its counters, the hub and client events; the same decisions file also designs the job machinery and scheduler, which belong to spec 0019 (already written at `' + ROOT + '/docs/specs/0019-background-jobs-and-scheduler.md` — read its §1 placement table, §10 and §14 so the two specs agree on names) — the ledger §0 says which decisions each spec reads. Cite spec 0019 for the job contracts you consume (completion notifications, retention schedules).' },
]

const byN = Object.fromEntries(SPECS.map(s => [s.n, s]))
const GROUPS = [
  { key: 'A', specs: ['0010', '0011', '0012', '0013', '0014'] },
  { key: 'B', specs: ['0015', '0016', '0017', '0018', '0019', '0020'] },
]
const FIX_CHUNKS = [['0011', '0014', '0013', '0010'], ['0012', '0015', '0016', '0019'], ['0017', '0018', '0020']]

const COMMON = `The repository root is \`${ROOT}\`; every path in this prompt is absolute. Token budget is the binding constraint: read each input once, page long files at about 300 lines per Read call, and do not explore beyond the reading list. Today is 2026-09-04.`

const REVIEW_SCHEMA = {
  type: 'object',
  properties: {
    findings: { type: 'integer' },
    blocking: { type: 'integer' },
    perSpec: { type: 'array', items: { type: 'object', properties: { spec: { type: 'string' }, findings: { type: 'integer' }, blocking: { type: 'integer' } }, required: ['spec', 'findings', 'blocking'] } },
    top: { type: 'array', items: { type: 'string' } },
  },
  required: ['findings', 'blocking', 'perSpec', 'top'],
}

const RULES = `Entities are column tables; contracts are \`contract\` blocks in the notation with no XML doc comments; SQL statements are SQL; C# appears only in labelled illustrations of at most five lines. Never mention the ledger, seams, themes, briefing, brain dump, synthesizer, reviewers, or this exercise; never narrate history; never reference ARCHITECTURE.md sections or any code file; cite frozen specs 0001–0009 by number and section, and sibling specs 0010–0020 (written concurrently) by number and contract name, never by a sibling's section number. Public-facing text (XML docs, errors, logs, test names) never cites docs or spec numbers. Everything runs on Windows and Linux.`

function authorPrompt(s) {
  const out = `${SPECS_DIR}/${s.file}`
  return `${COMMON}

You are writing **spec ${s.n} — ${s.title}** to \`${out}\`, a document handed to a coder to implement. It must stand on its own: its reader never sees the working files you read.

**Read, in this order:**
1. \`${D}/spec-style.md\` (the mandatory house format) and \`${D}/notation.md\` (the mandatory contract notation), then \`${ROOT}/docs/AGENTS.md\`.
2. \`${D}/briefing.md\`: only §2 "Fixed facts" and §8 "Research digest" (find them by heading; skip everything else).
3. \`${D}/ledger.md\` in full (866 lines). It is the decision overlay: where it disagrees with any other file, the ledger wins. Its §8 lists, per theme, the corrections to apply while reading that theme's decisions file; §5 lists the review flags your spec must carry; §6 lists the ARCHITECTURE.md changes your Definition of done must name.
4. \`${D}/seams.md\`: §0 and §23 (the per-spec defines/consumes matrix) first; then every seam your spec **defines** and every seam it **consumes**, in full; skip the rest. A seam you define appears in your spec in full (contracts, Members tables, SQL); a seam you consume is cited as "spec NNNN's <contract name>" with only the members you use restated verbatim.
5. \`${D}/themes/${s.key}/decisions.md\` in full, read with the ledger's §8 errata for \`${s.key}\` applied. ${s.part || ''}
6. \`${ROOT}/.tmp/crud-stack-specs.md\` (the breakdown; your spec's "Owns:" list is the scope checklist) and \`${ROOT}/.tmp/crud-stack.md\` (the brain dump; every open question in your scope must be answered by the design in your body — never as a question-and-answer list).
7. Style exemplars: \`${ROOT}/docs/specs/0008-queryex.md\` lines 1–130 and 2563–2600; \`${ROOT}/docs/specs/0009-marmin-ae-connector.md\` lines 420–476. Sibling specs already written under \`${SPECS_DIR}/\` (0010–0016, 0019): Grep them for the names of contracts you consume so spellings agree; read only the ranges you need.
8. Only when a consumed seam needs detail seams.md lacks: the owning theme's decisions file, the section the ledger names.

**Write the spec** per spec-style.md: header (Author Ahmad Akra, Date 4 September 2026, the Status paragraph), Context, Goals / Non-goals, numbered sections starting with placement and architecture, a Testing section, a Definition of done (its Docs bullet names the ARCHITECTURE.md rows of ledger §6 that this spec touches), a Decisions record, and a Review flags section carrying every flag ledger §5 assigns to this spec (its theme's numbered list plus every S-flag whose subject this spec owns), each with the chosen position, the plausible alternative, and what would flip it. Every "Owns:" item is fully specified — names, types, statements, columns, limits, error codes, telemetry — at the density of spec 0008. ${RULES}

Write the file incrementally so a cut-off leaves a usable prefix: one Write with the header through the first three numbered sections, then two or three Edit appends for the rest. Do not re-read the file after writing. Return at most 200 words: the line count, the number of review flags, any contradiction you found between the ledger and seams.md (quote both), and any "Owns:" item you could not fully specify and why.`
}

function completePrompt(s) {
  const out = `${SPECS_DIR}/${s.file}`
  return `${COMMON}

You are **completing spec ${s.n} — ${s.title}** at \`${out}\`. Its author was cut off after section 12; the file ends inside or just after "## 12. OpenAPI and the \`v1\` seam" and lacks the Testing section, the Definition of done, the Decisions record, and the Review flags.

**Read, in this order:** \`${D}/spec-style.md\` and \`${D}/notation.md\`; the existing spec in full (1,390 lines; page it) — note every section number, name, contract and decision it states; \`${D}/ledger.md\` §5 (the review flags for 0015 — the theme's numbered list plus every S-flag whose subject 0015 owns) and §6 (the ARCHITECTURE.md rows 0015 touches) and §8's "web-api-mcp" errata; \`${D}/themes/web-api-mcp/decisions.md\` sections 8 ("Verification") and 9 ("Review flags") only; \`${ROOT}/docs/specs/0008-queryex.md\` lines 2563–2600 (the exemplar's Definition of done and Decisions record).

**Then:** if section 12 ends mid-sentence or mid-list, finish it consistently with what precedes. Append, in this order: "## 13. Testing" (test projects mirroring \`src/\`, what each suite pins, the two trait tiers \`Category=Integration\` and \`Live=true\`, fixtures, PR versus nightly — drawn from the spec's own sections and the decisions file's test intent); "## 14. Definition of done" (bold-led bullets: Projects, Behavior, Observability, CI, Docs naming the ARCHITECTURE.md rows, Not in scope of done); "## Decisions record" ("The load-bearing decisions, where not already evident above:" then numbered bold decision — one-line why — § pointers, drawn only from what the spec's body already states); "## Review flags" (numbered; every flag assigned to 0015 plus any judgment call the body makes that the ledger did not settle; each with the chosen position, the alternative, and what would flip it). ${RULES} Use Edit appends; do not rewrite existing sections except to finish a truncated section 12. Return at most 150 words: what you appended, the number of review flags, and any inconsistency you noticed in the existing body (do not fix it).`
}

function reviewPrompt(g) {
  const list = g.specs.map(n => `${n} (\`${SPECS_DIR}/${byN[n].file}\`)`).join(', ')
  return `${COMMON}

You are the cross-spec reviewer for specs ${list}. Read \`${D}/spec-style.md\`, \`${D}/notation.md\`, \`${ROOT}/docs/AGENTS.md\`, \`${D}/ledger.md\` in full, \`${D}/seams.md\` §0 and §23 (then individual seams as needed to check a claim), \`${ROOT}/.tmp/crud-stack-specs.md\`, and each of your specs in full (page them). All eleven specs live under \`${SPECS_DIR}/\` as 0010-…md through 0020-…md; use Grep across that folder for cross-spec checks.

For each of your specs, produce numbered findings with severity (blocking / major / minor), exact location (file, section), the problem, the evidence, and a concrete fix, covering: (a) departures from the ledger or seams.md — a wrong name, shape, statement, number, or a resolved conflict re-litigated; (b) style and notation violations — C# beyond labelled illustrations of at most five lines, XML doc comments, narration, mentions of the ledger, themes, brain dump, briefing or reviewers, ARCHITECTURE.md section references, references to a sibling spec by section number, a missing mandatory section (Context, Goals / Non-goals, Testing, Definition of done, Decisions record, Review flags), the wrong header or date; (c) completeness — every "Owns:" item of the breakdown for that spec and every brain-dump open question in its scope must be specified in the body; list each GAP; (d) review flags — every ledger §5 flag for the spec must be present with its alternative; (e) cross-spec consistency — for every seam the spec defines, grep the other ten specs for its type and member names and report every spelling or shape drift; for every seam it consumes, check the restated members match the owner spec's; report contradictions between two specs as one finding placed under the spec that departs from seams.md. Default to reporting a doubt as a minor finding.

Write \`${D}/critique/specs-review-${g.key}.md\` with one "## Spec NNNN" section per spec (also add a "## Cross-spec" section for drift found in specs outside your group, keyed by spec number). Return the structured result.`
}

function fixPrompt(s) {
  const path = `${SPECS_DIR}/${s.file}`
  return `${COMMON}

You are fixing **spec ${s.n} — ${s.title}** at \`${path}\`. Read \`${D}/spec-style.md\` and \`${D}/notation.md\`; then the findings for spec ${s.n}: the "## Spec ${s.n}" section of \`${D}/critique/specs-review-A.md\` and of \`${D}/critique/specs-review-B.md\`, plus any "## Cross-spec" entry naming ${s.n} in either file (use Grep to locate them, then Read those ranges only); then the spec in full (page it). Consult \`${D}/ledger.md\` or \`${D}/seams.md\` only to verify a specific finding (Grep for the name, read the range).

Apply every finding you agree with by editing the spec in place: replace stale text, never append notes or history; keep the section structure and every review flag; keep the notation (no C# beyond labelled illustrations of at most five lines, no XML doc comments); keep names identical to seams.md. Record every finding you rejected, with the reason, in \`${D}/critique/specs-fix-${s.n}.md\` (one line per finding; also list the ones applied). Return at most 150 words: what changed materially and what you rejected.`
}

phase('Author')
const authored = await parallel([
  () => agent(authorPrompt(byN['0017']), { label: 'author:0017', phase: 'Author', effort: 'high' }),
  () => agent(authorPrompt(byN['0018']), { label: 'author:0018', phase: 'Author', effort: 'high' }),
  () => agent(authorPrompt(byN['0020']), { label: 'author:0020', phase: 'Author', effort: 'high' }),
  () => agent(completePrompt(byN['0015']), { label: 'complete:0015', phase: 'Author', effort: 'high' }),
])
log(`Author phase: ${authored.filter(Boolean).length}/4 finished`)

phase('Review')
const reviews = await parallel(GROUPS.map(g => () => agent(reviewPrompt(g), { label: `review:${g.key}`, phase: 'Review', schema: REVIEW_SCHEMA, effort: 'high' })))
const found = reviews.filter(Boolean)
log(`Review: ${found.reduce((n, r) => n + r.findings, 0)} findings (${found.reduce((n, r) => n + r.blocking, 0)} blocking)`)

phase('Fix')
const fixed = []
for (const chunk of FIX_CHUNKS) {
  const results = await parallel(chunk.map(n => () => agent(fixPrompt(byN[n]), { label: `fix:${n}`, phase: 'Fix', effort: 'high' })))
  results.forEach((r, i) => fixed.push({ spec: chunk[i], summary: r }))
  log(`Fixed ${fixed.filter(f => f.summary).length}/11 specs so far`)
}

return { authored, reviews: found, fixed }