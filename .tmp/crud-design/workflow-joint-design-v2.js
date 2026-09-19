export const meta = {
  name: 'crud-stack-joint-design-v2',
  description: 'Lean resumption of the CRUD stack joint design: reuse the twelve surviving proposals, single designers for the five empty themes, judges, ledger + seams synthesis, one critique/revise round, at most four agents in flight',
  phases: [
    { title: 'Design', detail: 'single all-lens designer where no proposal survived' },
    { title: 'Judge', detail: 'one judge per theme over whatever proposals exist' },
    { title: 'Synthesize', detail: 'decision ledger + seam contracts' },
    { title: 'Critique', detail: 'security-correctness + completeness' },
    { title: 'Revise', detail: 'apply findings' },
  ],
}

const ROOT = 'C:/Users/ahmad/workspace/tellma/tellma-platform'
const D = `${ROOT}/.tmp/crud-design`
const BRIEF = `${D}/briefing.md`
const COMMON = `The repository root is \`${ROOT}\`; every relative path in the briefing is relative to it, and every path in this prompt is absolute. Start by reading \`${BRIEF}\` in full and follow it — its §9 tells you exactly which ranges to read and forbids reading whole long files; token budget is the binding constraint, so read each input once, in as few tool calls as possible, and write your output file in one Write call (edit only if you must). Work only under \`${D}/\`; never modify any other file. If WebSearch/WebFetch are deferred, load them with ToolSearch ("select:WebSearch,WebFetch") — but only for a fact the research file lacks. Today is 2026-09-01.`

// Proposals that survived the earlier run (complete files, sections 1–8).
const EXISTING = {
  'host-tenancy': ['lens-a-simplicity', 'lens-b-performance', 'lens-c-correctness'],
  'service-pipeline': ['lens-a-simplicity', 'lens-b-performance', 'lens-c-correctness'],
  'users-roles-permissions': ['lens-a-simplicity', 'lens-b-performance', 'lens-c-correctness'],
  'settings-cache-l10n': ['lens-a-simplicity', 'lens-b-performance'],
  'web-api-mcp': ['lens-a-simplicity'],
}

const T = {
  'host-tenancy': { title: 'Distribution host and multi-tenancy', spec: '0010' },
  'data-access': { title: 'Entity contract and data access', spec: '0011' },
  'settings-cache-l10n': { title: 'Tenant settings, localization, and the version cache', spec: '0012' },
  'users-roles-permissions': { title: 'Users, roles, and permissions', spec: '0013' },
  'service-pipeline': { title: 'CRUD service pipeline and capabilities', spec: '0014' },
  'web-api-mcp': { title: 'Web API surface and the MCP seam', spec: '0015' },
  'blobs': { title: 'Blob storage and the record-plus-blobs pattern', spec: '0016' },
  'core-gl-stacks': { title: 'Core and GL reference stacks', spec: '0017' },
  'excel': { title: 'Excel codec: export and import', spec: '0018' },
  'background-inbox': { title: 'Background tasks, scheduler, and the inbox', spec: '0019' },
}

const KEYS = Object.keys(T)

// design: null = no designer needed; 'all' = one all-lens designer writing proposal.md;
// 'bc' = a performance+correctness designer complementing the surviving lens-A proposal.
const CHUNKS = [
  [
    { key: 'data-access', design: 'all' },
    { key: 'host-tenancy', design: null },
    { key: 'service-pipeline', design: null },
    { key: 'users-roles-permissions', design: null },
  ],
  [
    { key: 'background-inbox', design: 'all' },
    { key: 'blobs', design: 'all' },
    { key: 'settings-cache-l10n', design: null },
    { key: 'web-api-mcp', design: 'bc' },
  ],
  [
    { key: 'core-gl-stacks', design: 'all' },
    { key: 'excel', design: 'all' },
  ],
]

const JUDGE_SCHEMA = {
  type: 'object',
  properties: {
    decisions: { type: 'integer', description: 'number of numbered decisions in decisions.md' },
    reviewFlags: { type: 'array', items: { type: 'string' }, description: 'each judgment call with its plausible alternative, one line each' },
    conflicts: { type: 'array', items: { type: 'string' }, description: 'positions other themes must reconcile' },
    departures: { type: 'array', items: { type: 'string' }, description: 'departures from ARCHITECTURE.md' },
    doubts: { type: 'array', items: { type: 'string' } },
  },
  required: ['decisions', 'reviewFlags', 'conflicts', 'departures'],
}

const CRITIQUE_SCHEMA = {
  type: 'object',
  properties: {
    findings: { type: 'integer' },
    blocking: { type: 'integer', description: 'findings a spec author could not work around' },
    top: { type: 'array', items: { type: 'string' }, description: 'the five most consequential findings, one line each' },
  },
  required: ['findings', 'blocking', 'top'],
}

function designPrompt(key, mode) {
  const t = T[key]
  const dir = `${D}/themes/${key}`
  const lensText = mode === 'bc'
    ? `You carry **Lens B — Tier-2 performance and operations** and **Lens C — Correctness, security, and long-term maintainability** together (briefing §6). A Lens A proposal already exists at \`${dir}/lens-a-simplicity.md\`: read it, and write a proposal that either strengthens or contradicts it on every point where B or C sees it differently — do not merely restate it. Write to \`${dir}/lens-bc-performance-correctness.md\`.`
    : `You carry **all three lenses** of briefing §6 at once. Add a section 0 titled **Lens checks** that answers every probe listed under each lens for this theme (line counts a distribution writes, DB calls per operation, where a stale cache could leak, and so on) before the numbered sections. Write to \`${dir}/proposal.md\`.`
  return `${COMMON}

You are designing theme **${t.title}** (key \`${key}\`, future spec ${t.spec}). Read the inputs the briefing's §9 table lists for this theme — and nothing more — plus \`${D}/research/${key}.md\`. Answer every question in the theme's paragraph (briefing §3), take a position on every seam you touch (§4), weigh the orchestrator's hints (§5) and say where you disagree, and respect every fact in §2 and §8. ${lensText}

Use the designer format of briefing §7 (sections 1–8). Be concrete: real names, real types, real C# signatures with XML-doc summaries, real SQL where a statement is load-bearing, complete column lists. Be honest in the critique: the brain dump is a draft and its author wants it challenged. Depth over breadth — a spec author must be able to write the spec from your file without asking you anything. Return a summary of at most 150 words: your three most consequential decisions and your two biggest doubts.`
}

function judgePrompt(key) {
  const t = T[key]
  const dir = `${D}/themes/${key}`
  const files = [...(EXISTING[key] || [])]
  files.push('proposal', 'lens-bc-performance-correctness')
  const list = files.map(f => `\`${f}.md\``).join(', ')
  return `${COMMON}

You are the judge for theme **${t.title}** (key \`${key}\`, future spec ${t.spec}). Read the proposals that exist under \`${dir}/\` (candidates: ${list} — list the folder first and read only files that exist; ignore \`decisions.md\` if present), the research file \`${D}/research/${key}.md\`, and the inputs the briefing's §9 table lists for this theme. For every question in the theme's paragraph (briefing §3) and every seam it touches (§4), pick the best answer: the strongest proposal is the spine, the best ideas of the others are grafted in, and you supply the correction where every proposal is wrong, silent, or asserts a fact you cannot confirm against the sources (verify against the repo, the specs, and the research file — never against a proposal's own claims). When only one proposal exists you are its adversarial reviewer as well as its judge: probe it with all three lenses of §6, fix what is wrong, and fill what is missing. Reject anything that contradicts a fixed fact (§2, §8) or silently narrows the theme's scope. Prefer the simplest design that satisfies all three lenses; when lenses genuinely conflict, say which wins here and why.

Write \`${dir}/decisions.md\` in the judge format (briefing §7: sections 1–8 as a designer file, plus 9 Review flags and 10 Conflicts). It must be self-contained — a reader who never sees the proposals gets the complete settled design for this theme, with every name, type, signature, statement, and column. Then return the structured result.`
}

const DECISION_FILES = KEYS.map(k => `\`${D}/themes/${k}/decisions.md\``).join(', ')

const SYNTH_PROMPT = `${COMMON}

You are the synthesizer. Read the ten theme decision files in full — ${DECISION_FILES} — then the brain dump and the breakdown (especially its seams and misalignments lists); consult ARCHITECTURE.md ranges and research files only to check a specific claim. Read \`${D}/notation.md\` and use its contract notation (never C#) wherever you write a shape.

Produce \`${D}/ledger.md\`. The ledger is an **overlay**, not a restatement: each theme's \`decisions.md\` remains the detailed design for its spec, and a spec author reads the ledger, \`seams.md\`, its own theme's decisions file, and its neighbours' decisions files for the seams it consumes. Where the ledger and a decisions file disagree, the ledger wins, and the ledger says explicitly which decisions change. Structure:

0. How to read (that rule, in one paragraph) and a table of the ten specs: number, title, theme key, one-line scope.
1. Consolidated critique of the brain dump — the general design, the detailed choices (names, columns, shapes), the gaps the design fills — merged from the ten section-1 critiques, deduplicated, specific and honest.
2. Global vocabulary and conventions: one settled answer for every naming and convention question the themes answered differently or left open (table plural vs singular; schema names; audit column set and concurrency token; id types; resource and action naming; package names and their dependency edges; version-tag names; header names; exception type names; enum storage; the term for securables; "version tag" vs "etag"; folder and slug of the reference distribution) — each with the decision, the why, and the list of decisions-file passages that must be read as corrected.
3. Seams: for each of the 17 seams in briefing §4 plus every seam a theme added — the decision, the owner spec, the consumer specs, the member names of the contract in the notation (the full contract is written to seams.md by the next agent, so name every type and member here), and per theme the decisions-file items that must change to conform.
4. Conflict resolutions: every item of every theme's section 10, quoted briefly, with the resolution, the why, and which decisions change. Nothing may be left "for the spec author to decide".
5. Review flags: the consolidated list per spec, from every section 9 plus any judgment call you made here (mark yours "[synthesizer]").
6. ARCHITECTURE.md changes required: from every section 7 and the breakdown's misalignment list — the section, the gist of its current text, the replacing decision.
7. Deferred items and items left to implementation.
8. Errata per theme: for each theme key, a numbered list of corrections a spec author must apply while reading that decisions file (a pointer to the passage, the corrected statement) — the union of everything sections 2–4 changed.

Rules: one name per concept across the whole ledger; resolve, never defer; keep every review flag; mark every decision the themes were silent on with "[synthesizer]"; write the file in one Write call. Return a summary of at most 300 words: the conflicts you resolved and the decisions you made yourself.`

const SEAMS_PROMPT = `${COMMON}

You are the contracts agent. Read \`${D}/notation.md\` first, then \`${D}/ledger.md\` in full, then the section 3 (Contracts) and section 4 (Schema) of each theme decisions file (${DECISION_FILES}) for the detail the ledger references. Write \`${D}/seams.md\` in the **contract notation of notation.md — no C#, no XML doc comments**: for every seam in ledger section 3, the complete contract — every service, contract, record, data shape, base shape, enum and annotation with its members, a Members table where semantics need more than a remark — and, where the contract is SQL, the exact statements (standard column sets with types, the lease statements, the tag-bump statement, the standalone table types, the tree recompute statement). Consistency rules: one name per concept, identical to the ledger; every type referenced by any contract is defined somewhere in the file; batch-shaped inputs are \`list<T>\`; state which package and namespace owns each contract, and note where a Tellma.Core.Abstractions contract would need EF Core, ASP.NET Core, or Queryex types (the ledger says where that is allowed). Add a final section: per spec 0010–0019, exactly which contracts it defines and which it consumes. Where the ledger was ambiguous, resolve it and list the resolution under a "Resolutions" heading at the end so the ledger can be corrected. Write the file in one Write call. Return a summary of at most 200 words including every resolution.`

const CRITICS = [
  { key: 'security-correctness', lens: 'security, correctness, and performance together: fail-closed access control, stale-cache leaks, concurrency soundness, write paths that bypass invariants (tag bumps, audit stamps, RLS post-checks), transaction boundaries, DB round trips per operation in common and cold-cache cases, N+1 shapes, locks held across I/O, multi-instance races, schema evolution under N−1 apps, and seams whose two sides cannot both be implemented as specified' },
]

function critiquePrompt(c) {
  return `${COMMON}

You are an adversarial reviewer. Read \`${D}/notation.md\`, then \`${D}/ledger.md\` and \`${D}/seams.md\` in full, then the brain dump and the breakdown; open a theme decision file, a research file, or a spec range only to check a specific claim. Attack the ledger and the seam contracts from this lens: ${c.lens}. Also report, regardless of lens: contradictions between sections; decisions that violate a fixed fact (briefing §2, §8) or a guiding principle of ARCHITECTURE.md without recording the departure; conflicts (ledger section 4) resolved by deferral rather than decision; missing pieces a spec author would have to invent; names that clash or drift between the ledger and seams.md; review flags that were silently resolved instead of flagged; any C# or XML-doc comment in seams.md or the ledger's shapes (they must use the notation); anything a distribution author would find ambiguous when adding a plain entity, an IsActive entity, a tree entity, a custom endpoint, a custom validator, or an image.

Write \`${D}/critique/round1-${c.key}.md\`: numbered findings, each with severity (blocking / major / minor), the exact location (ledger section or seam), the problem, the evidence, and a concrete proposed fix. Default to reporting a doubt as a minor finding rather than staying silent. Return the structured result.`
}

const COMPLETENESS_PROMPT = `${COMMON}

You are the completeness critic. Build an explicit coverage map and write it to \`${D}/critique/round1-completeness.md\`: (a) every open question and every "??" in the brain dump \`${ROOT}/.tmp/crud-stack.md\`, quoted briefly, with the location that answers it (ledger section, or theme decisions file and section) or GAP; (b) every "Owns:" item of every spec in \`${ROOT}/.tmp/crud-stack-specs.md\`, with the location or GAP; (c) every seam in the breakdown's "Seams to fix" list and in briefing §4, with the ledger section-3 entry and the seams.md contract or GAP; (d) every "Misalignments with ARCHITECTURE.md to settle" item, with the ledger section-6 entry or GAP; (e) every conflict listed in any theme decisions file's section 10, with the ledger section-4 resolution or GAP; (f) every type or member the ledger names for a seam that is absent from \`${D}/seams.md\` or defined differently there. List the gaps at the end as numbered findings with a proposed resolution each. Return the structured result (findings = number of gaps; blocking = gaps in seams, conflicts, or the "Owns" lists).`

const REVISE_PROMPT = `${COMMON}

You are the reviser. Read \`${D}/notation.md\`, \`${D}/ledger.md\`, \`${D}/seams.md\`, and every \`${D}/critique/round1-*.md\`. Apply every finding you agree with by editing the ledger and seams.md in place — replace stale text, never append rebuttals or history; keep one name per concept across both files; keep every review flag (add new ones where a fix was itself a judgment call); keep every shape in the contract notation. Where you need a fact to decide, check the repo, the spec ranges, or the research files; do not guess. Record every finding you rejected or deferred, with the reason, in \`${D}/critique/round1-resolutions.md\` (one line per finding keyed by file and number; also list the ones you applied). Return a summary of at most 250 words: what changed materially, what you rejected and why.`

// Per-theme stages; chunks bound the number of agents in flight. A stage that returns null
// drops the item from the pipeline, so the no-design case returns a sentinel instead.
const designStage = (item) => item.design
  ? agent(designPrompt(item.key, item.design), { label: `design:${item.key}`, phase: 'Design' })
  : Promise.resolve({ skipped: true })
const judgeStage = (_, item) => agent(judgePrompt(item.key), { label: `judge:${item.key}`, phase: 'Judge', schema: JUDGE_SCHEMA, effort: 'high' })

const judged = []
for (const chunk of CHUNKS) {
  const results = await pipeline(chunk, designStage, judgeStage)
  results.forEach((r, i) => { if (r) judged.push({ theme: chunk[i].key, ...r }) })
  log(`Judged ${judged.length}/10 themes so far`)
}

phase('Synthesize')
const ledgerSummary = await agent(SYNTH_PROMPT, { label: 'synthesize:ledger', phase: 'Synthesize', effort: 'high' })
const seamsSummary = await agent(SEAMS_PROMPT, { label: 'synthesize:seams', phase: 'Synthesize', effort: 'high' })

phase('Critique')
const critiques = await parallel([
  ...CRITICS.map(c => () => agent(critiquePrompt(c), { label: `critique:${c.key}`, phase: 'Critique', schema: CRITIQUE_SCHEMA, effort: 'high' })),
  () => agent(COMPLETENESS_PROMPT, { label: 'critique:completeness', phase: 'Critique', schema: CRITIQUE_SCHEMA, effort: 'high' }),
])
const found = critiques.filter(Boolean)
log(`Critique: ${found.reduce((n, c) => n + c.findings, 0)} findings (${found.reduce((n, c) => n + c.blocking, 0)} blocking)`)

phase('Revise')
const revision = await agent(REVISE_PROMPT, { label: 'revise', phase: 'Revise', effort: 'high' })

return { judged, ledgerSummary, seamsSummary, critiques: found, revision }
