export const meta = {
  name: 'crud-stack-ledger-synthesis',
  description: 'Synthesize the ten judged theme designs into the decision ledger and seam contracts, then one adversarial critique/revise round',
  phases: [
    { title: 'Synthesize', detail: 'decision ledger, then seam contracts' },
    { title: 'Critique', detail: 'security-correctness-performance + completeness' },
    { title: 'Revise', detail: 'apply findings' },
  ],
}

const ROOT = 'C:/Users/ahmad/workspace/tellma/tellma-platform'
const D = `${ROOT}/.tmp/crud-design`
const BRIEF = `${D}/briefing.md`
const COMMON = `The repository root is \`${ROOT}\`; every relative path in the briefing is relative to it, and every path in this prompt is absolute. Start by reading \`${BRIEF}\` in full and follow it — its §9 tells you exactly which ranges to read and forbids reading whole long files; token budget is the binding constraint, so read each input once, in as few tool calls as possible (page long files with offset/limit at about 400 lines per call). Work only under \`${D}/\`; never modify any other file. If WebSearch/WebFetch are deferred, load them with ToolSearch ("select:WebSearch,WebFetch") — but only for a fact the research files lack. Today is 2026-09-04.`

const KEYS = ['host-tenancy', 'data-access', 'settings-cache-l10n', 'users-roles-permissions', 'service-pipeline', 'web-api-mcp', 'blobs', 'core-gl-stacks', 'excel', 'background-inbox']
const DECISION_FILES = KEYS.map(k => `\`${D}/themes/${k}/decisions.md\``).join(', ')

const CRITIQUE_SCHEMA = {
  type: 'object',
  properties: {
    findings: { type: 'integer' },
    blocking: { type: 'integer', description: 'findings a spec author could not work around' },
    top: { type: 'array', items: { type: 'string' }, description: 'the five most consequential findings, one line each' },
  },
  required: ['findings', 'blocking', 'top'],
}

const SYNTH_PROMPT = `${COMMON}

You are the synthesizer. Read the ten theme decision files in full — ${DECISION_FILES} (each 800–1900 lines; read every section, they are your only source) — then the brain dump \`${ROOT}/.tmp/crud-stack.md\` and the breakdown \`${ROOT}/.tmp/crud-stack-specs.md\` (especially its seams and misalignments lists); consult ARCHITECTURE.md ranges and research files only to check a specific claim. Read \`${D}/notation.md\` and use its contract notation (never C#) wherever you write a shape.

Produce \`${D}/ledger.md\`. The ledger is an **overlay**, not a restatement: each theme's \`decisions.md\` remains the detailed design for its spec, and a spec author reads the ledger, \`seams.md\`, its own theme's decisions file, and its neighbours' decisions files for the seams it consumes. Where the ledger and a decisions file disagree, the ledger wins, and the ledger says explicitly which decisions change. Structure:

0. How to read (that rule, in one paragraph) and a table of the ten specs: number, title, theme key, one-line scope.
1. Consolidated critique of the brain dump — the general design, the detailed choices (names, columns, shapes), the gaps the design fills — merged from the ten section-1 critiques, deduplicated, specific and honest.
2. Global vocabulary and conventions: one settled answer for every naming and convention question the themes answered differently or left open — table plural vs singular; schema names; the entity base shapes (base classes vs marker interfaces: TopLevelEntity/ChildEntity/TreeEntity vs IAudited/ISystemVersioned/IMultilingual) and ownership markers ([ServerOwned]/[WriteOnce]/[SelfEditable]/[Child]/[ParentKey]); the audit column set and the concurrency token; whether IsActive is write-once, server-owned, or editable; id types per table; the batch abstraction's name (IDataBatch vs IDbBatch) and member set including after-commit callbacks; the lease column set and token type (the four-column ILeasable vs the seventeen-column IJobRow, long vs Guid token); the tree node scheme (id-path vs sibling ordinals) and where nodes are computed; Type vs CenterType; the natural-key ranking; the SignalR hub route; the dedicated Excel task table vs the generic core.Jobs; the export artifact's owner column; resource and action naming; package names and their dependency edges; version-tag names and storage; header names; exception type names and the reserved THROW number bands; enum storage; the term for securables; the reference distribution's folder and slug; the DbContext ownership (platform TellmaDbContext vs distro subclass) — each with the decision, the why, and the list of decisions-file passages that must be read as corrected.
3. Seams: for each of the 17 seams in briefing §4 plus every seam a theme added — the decision, the owner spec, the consumer specs, the member names of the contract in the notation (the full contract is written to seams.md by the next agent, so name every type and member here), and per theme the decisions-file items that must change to conform.
4. Conflict resolutions: every item of every theme's section 10, quoted briefly, with the resolution, the why, and which decisions change. Nothing may be left "for the spec author to decide".
5. Review flags: the consolidated list per spec, from every section 9 plus any judgment call you made here (mark yours "[synthesizer]").
6. ARCHITECTURE.md changes required: from every section 7 and the breakdown's misalignment list — the section, the gist of its current text, the replacing decision.
7. Deferred items and items left to implementation.
8. Errata per theme: for each theme key, a numbered list of corrections a spec author must apply while reading that decisions file (a pointer to the passage, the corrected statement) — the union of everything sections 2–4 changed.

Rules: one name per concept across the whole ledger; resolve, never defer; keep every review flag; mark every decision the themes were silent on with "[synthesizer]". Write the file incrementally so a cut-off run leaves a usable prefix: create it with sections 0–2 in one Write call, then append sections 3–4 with one Edit, then 5–8 with one Edit. Return a summary of at most 300 words: the conflicts you resolved and the decisions you made yourself.`

const SEAMS_PROMPT = `${COMMON}

You are the contracts agent. Read \`${D}/notation.md\` first, then \`${D}/ledger.md\` in full, then the section 3 (Contracts) and section 4 (Schema) of each theme decisions file (${DECISION_FILES}; find the sections by their "## 3." and "## 4." headings) for the detail the ledger references. Write \`${D}/seams.md\` in the **contract notation of notation.md — no C#, no XML doc comments**: for every seam in ledger section 3, the complete contract — every service, contract, record, data shape, base shape, enum and annotation with its members, a Members table where semantics need more than a remark — and, where the contract is SQL, the exact statements (standard column sets with types, the lease statements, the tag-bump statement, the standalone table types, the tree recompute statement). Consistency rules: one name per concept, identical to the ledger; every type referenced by any contract is defined somewhere in the file; batch-shaped inputs are \`list<T>\`; state which package and namespace owns each contract, and note where a Tellma.Core.Abstractions contract would need EF Core, ASP.NET Core, or Queryex types (the ledger says where that is allowed). Add a final section: per spec 0010–0019, exactly which contracts it defines and which it consumes. Where the ledger was ambiguous, resolve it and list the resolution under a "Resolutions" heading at the end so the ledger can be corrected. Write the file incrementally (create with the first half of the seams, then append the rest with one Edit). Return a summary of at most 200 words including every resolution.`

const CRITIC_LENS = 'security, correctness, and performance together: fail-closed access control, stale-cache leaks, concurrency soundness, write paths that bypass invariants (tag bumps, audit stamps, RLS post-checks), transaction boundaries, DB round trips per operation in common and cold-cache cases, N+1 shapes, locks held across I/O, multi-instance races, schema evolution under N−1 apps, and seams whose two sides cannot both be implemented as specified'

const CRITIQUE_PROMPT = `${COMMON}

You are an adversarial reviewer. Read \`${D}/notation.md\`, then \`${D}/ledger.md\` and \`${D}/seams.md\` in full, then the brain dump and the breakdown; open a theme decision file, a research file, or a spec range only to check a specific claim. Attack the ledger and the seam contracts from this lens: ${CRITIC_LENS}. Also report, regardless of lens: contradictions between sections; decisions that violate a fixed fact (briefing §2, §8) or a guiding principle of ARCHITECTURE.md without recording the departure; conflicts (ledger section 4) resolved by deferral rather than decision; missing pieces a spec author would have to invent; names that clash or drift between the ledger and seams.md; review flags that were silently resolved instead of flagged; any C# or XML-doc comment in seams.md or the ledger's shapes (they must use the notation); anything a distribution author would find ambiguous when adding a plain entity, an IsActive entity, a tree entity, a custom endpoint, a custom validator, or an image.

Write \`${D}/critique/round1-security-correctness.md\`: numbered findings, each with severity (blocking / major / minor), the exact location (ledger section or seam), the problem, the evidence, and a concrete proposed fix. Default to reporting a doubt as a minor finding rather than staying silent. Return the structured result.`

const COMPLETENESS_PROMPT = `${COMMON}

You are the completeness critic. Build an explicit coverage map and write it to \`${D}/critique/round1-completeness.md\`: (a) every open question and every "??" in the brain dump \`${ROOT}/.tmp/crud-stack.md\`, quoted briefly, with the location that answers it (ledger section, or theme decisions file and section) or GAP; (b) every "Owns:" item of every spec in \`${ROOT}/.tmp/crud-stack-specs.md\`, with the location or GAP; (c) every seam in the breakdown's "Seams to fix" list and in briefing §4, with the ledger section-3 entry and the seams.md contract or GAP; (d) every "Misalignments with ARCHITECTURE.md to settle" item, with the ledger section-6 entry or GAP; (e) every conflict listed in any theme decisions file's section 10, with the ledger section-4 resolution or GAP; (f) every type or member the ledger names for a seam that is absent from \`${D}/seams.md\` or defined differently there. List the gaps at the end as numbered findings with a proposed resolution each. Return the structured result (findings = number of gaps; blocking = gaps in seams, conflicts, or the "Owns" lists).`

const REVISE_PROMPT = `${COMMON}

You are the reviser. Read \`${D}/notation.md\`, \`${D}/ledger.md\`, \`${D}/seams.md\`, and every \`${D}/critique/round1-*.md\`. Apply every finding you agree with by editing the ledger and seams.md in place — replace stale text, never append rebuttals or history; keep one name per concept across both files; keep every review flag (add new ones where a fix was itself a judgment call); keep every shape in the contract notation. Where you need a fact to decide, check the repo, the spec ranges, or the research files; do not guess. Record every finding you rejected or deferred, with the reason, in \`${D}/critique/round1-resolutions.md\` (one line per finding keyed by file and number; also list the ones you applied). Return a summary of at most 250 words: what changed materially, what you rejected and why.`

phase('Synthesize')
const ledgerSummary = await agent(SYNTH_PROMPT, { label: 'synthesize:ledger', phase: 'Synthesize', effort: 'high' })
const seamsSummary = await agent(SEAMS_PROMPT, { label: 'synthesize:seams', phase: 'Synthesize', effort: 'high' })

phase('Critique')
const critiques = await parallel([
  () => agent(CRITIQUE_PROMPT, { label: 'critique:security-correctness', phase: 'Critique', schema: CRITIQUE_SCHEMA, effort: 'high' }),
  () => agent(COMPLETENESS_PROMPT, { label: 'critique:completeness', phase: 'Critique', schema: CRITIQUE_SCHEMA, effort: 'high' }),
])
const found = critiques.filter(Boolean)
log(`Critique: ${found.reduce((n, c) => n + c.findings, 0)} findings (${found.reduce((n, c) => n + c.blocking, 0)} blocking)`)

phase('Revise')
const revision = await agent(REVISE_PROMPT, { label: 'revise', phase: 'Revise', effort: 'high' })

return { ledgerSummary, seamsSummary, critiques: found, revision }