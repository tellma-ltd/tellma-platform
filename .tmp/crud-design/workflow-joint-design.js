export const meta = {
  name: 'crud-stack-joint-design',
  description: 'Joint design of the CRUD stack (specs 0010-0019): per-theme research, three-lens design panels, judges, ledger synthesis, seam contracts, two rounds of adversarial critique and revision',
  phases: [
    { title: 'Research', detail: 'one web-research agent per theme' },
    { title: 'Design', detail: 'three lenses per theme' },
    { title: 'Judge', detail: 'one judge per theme' },
    { title: 'Synthesize', detail: 'decision ledger + seam contracts' },
    { title: 'Critique', detail: 'adversarial lenses + completeness, two rounds' },
    { title: 'Revise', detail: 'apply findings, two rounds' },
  ],
}

const BRIEF = '.tmp/crud-design/briefing.md'
const COMMON = `Start by reading \`${BRIEF}\` in full and follow it. Work only under \`.tmp/crud-design/\`; never modify any other file. The Read tool caps each call at ~25k tokens, so page through long files with offset/limit rather than giving up. If WebSearch/WebFetch are deferred, load them with ToolSearch ("select:WebSearch,WebFetch"). Today is 2026-09-01.`

const THEMES = [
  {
    key: 'host-tenancy', title: 'Distribution host and multi-tenancy', spec: '0010',
    research: `(1) ASP.NET Core 10 OpenID Connect handler and BFF: what shipped in .NET 9/10 (PAR support, any first-party BFF package or template, token refresh in the cookie handler, SaveTokens patterns), and the recommended antiforgery posture for a cookie-authenticated JSON API in .NET 10 (SameSite, custom-header check, minimal-API antiforgery). (2) Session revocation for cookie auth: ITicketStore vs security-stamp validation intervals; current guidance. (3) Multi-tenancy in .NET in 2026: state of Finbuckle.MultiTenant (version, .NET 10 support, EF Core 10 support) and whether a platform should depend on it or roll its own tenant resolution; per-tenant DbContext connection patterns with EF Core 10 (IDbContextFactory, pooled contexts with per-tenant connection). (4) Secrets: Azure App Service Key Vault references, Azure.Extensions.AspNetCore.Configuration.Secrets, Azure SQL with managed identity (Authentication=Active Directory Default / Managed Identity) so that tenant databases need no stored passwords at all; how per-database connection strings are typically stored for a catalog of hundreds of tenant databases; on-prem equivalents (DPAPI, environment, file). (5) Azure SQL elastic pool facts relevant to a per-tenant database model: max databases per pool, connection limits, cross-database access restrictions (no cross-db queries in Azure SQL). (6) AsyncLocal-based ambient context vs scoped DI services for carrying tenant/user context into background work in .NET 10 — current guidance and pitfalls. (7) Minimal API route groups with a {tenantId} prefix and endpoint filters in .NET 10.`,
  },
  {
    key: 'data-access', title: 'Entity contract and data access', spec: '0011',
    research: `(1) EF Core 10 and SQL Server hierarchyid: is Microsoft.EntityFrameworkCore.SqlServer.HierarchyId still a separate package or folded into the provider; the HierarchyId .NET type API (GetLevel, GetAncestor, GetDescendant, GetReparentedValue, IsDescendantOf, Parse/ToString); migration and index support; LINQ translation coverage. (2) EF Core 10 temporal tables: IsTemporal() mapping, period column and history table conventions, what happens on bulk MERGE/UPDATE/DELETE of a system-versioned table, restrictions on OUTPUT with temporal tables, and any open EF 10 issues. (3) Microsoft.Data.SqlClient 6.x: the SqlBatch (DbBatch) API — does it support table-valued parameters and multiple result sets per batch command; SqlRetryLogicBaseProvider and the transient error numbers it covers; streaming TVPs with IEnumerable<SqlDataRecord>; SqlBulkCopy vs TVP guidance for tens of thousands of rows. (4) SQL Server MERGE in 2026: the current known issues list (Aaron Bertrand / Microsoft docs), HOLDLOCK guidance, and whether separate UPDATE/INSERT/DELETE statements are still recommended over MERGE for upsert-with-TVP. (5) sp_sequence_get_range: semantics, required permissions, sequence CACHE behaviour and gaps on restart/failover, and NEXT VALUE FOR in INSERT ... SELECT. (6) SQL Server 2025 and Azure SQL: native json data type availability/GA status on Azure SQL, JSON_OBJECTAGG/JSON_ARRAYAGG, regex functions, and any implication for storing JSON columns from EF Core 10 (the json type mapping in EF 10). (7) EF Core 10 conventions for enum-as-string (ConfigureConventions Properties<T>().HaveConversion<string>()) and for max length on those columns. (8) Azure SQL default isolation (READ_COMMITTED_SNAPSHOT on by default) and on-prem defaults; implications for validation reads before a write. (9) OpenTelemetry SqlClient instrumentation 1.16 tag set and the db.* semantic conventions version it emits; the recommended way to count DB round trips per request.`,
  },
  {
    key: 'settings-cache-l10n', title: 'Tenant settings, localization, and the version cache', spec: '0012',
    research: `(1) HybridCache in .NET 10: stampede protection, L1-only usage without a distributed backend, tag-based invalidation (added when?), size limits, serialization of custom types, current package version; versus IMemoryCache with SizeLimit; the recommended choice for an in-process per-tenant cache validated by version tags read from the database. (2) The MessageFormat NuGet package (ICU MessageFormat for .NET, version 8.0.0 pinned in this repo): maintainer, supported syntax (plural, select, selectordinal, nested), plural-rule source, caching/performance, thread-safety. (3) .NET 10 localization: IStringLocalizer with .resx vs JSON resources; satellite-assembly deployment; RequestLocalizationMiddleware culture providers; whether CultureInfo accepts BCP 47 -u-ca- calendar extensions (e.g. ar-SA-u-ca-islamic-umalqura) and what happens to them. (4) Calendars in .NET 10 with ICU: UmAlQuraCalendar supported range, HijriCalendar, whether any Ethiopian (Ge'ez) calendar exists in System.Globalization or ICU-backed CultureInfo (am-ET), and the usual .NET libraries that implement Ethiopian calendar conversion. (5) Time zones: TimeZoneInfo IANA/Windows id conversion on .NET 10, and whether any standard HTTP header or client hint conveys the client time zone or calendar (Sec-CH-* proposals, Accept-Language extensions); browser Temporal API status in 2026 for the client side. (6) Guidance and examples for version-tag / stamp-validated in-memory caches (opaque tag comparison on every request) and for a "cache format version" constant to invalidate on deploy.`,
  },
  {
    key: 'users-roles-permissions', title: 'Users, roles, and permissions', spec: '0013',
    research: `(1) SQL Server native row-level security (security policies, SESSION_CONTEXT) versus application-composed filters: current guidance, performance characteristics, and why an application that forbids logic in the database would still avoid it — collect the arguments on both sides. (2) ASP.NET Core 10 authorization: resource-based authorization handlers, authorization policies with endpoint metadata, the "require authorization by default" fallback policy pattern for minimal APIs, and how to make it hard to leave an endpoint unsecured. (3) Common ERP/SaaS permission models: resource-action pairs with row filters (e.g. how Odoo record rules, Salesforce sharing rules, and Dynamics security roles express filters), the treatment of "public" permissions, and admin self-lockout protections. (4) Opaque version tags: uniqueidentifier vs rowversion vs monotonic bigint for change stamps in SQL Server — comparison and indexing implications. (5) The Tellma identity server's bulk invite and delivery-status API shapes as implemented in this repo (read src/apps/Tellma.Identity controllers/services for the invite endpoint request/response types) so the user state model can be designed against the real contract; report the exact statuses and error shapes.`,
  },
  {
    key: 'service-pipeline', title: 'CRUD service pipeline and capabilities', spec: '0014',
    research: `(1) .NET 10 minimal API validation: the built-in AddValidation / source-generated validation for DataAnnotations, IValidatableObject support, how errors are shaped (ValidationProblemDetails / RFC 9457), and its limits for batch/child-collection validation; FluentValidation current version and .NET 10 status; guidance on which to use for bulk entity validation with property paths like Lines[3].Quantity. (2) Transactions with Microsoft.Data.SqlClient 6 in .NET 10: TransactionScope with async flow versus explicit SqlTransaction, whether SqlBatch participates in a SqlTransaction, and the fact that distributed transactions are unavailable cross-platform. (3) Transient-fault retry: SqlClient configurable retry logic (SqlRetryLogicBaseProvider) and its default transient error list; EF Core EnableRetryOnFailure semantics; guidance on retrying batches that contain non-idempotent statements. (4) The DataLoader pattern for batching and deduplicating context loads (GreenDonut in .NET, Facebook DataLoader semantics) as a reference for a validation-context loader. (5) OpenTelemetry database semantic conventions (db.client.operation.duration, db.query.text, db.system.name) as of 2026 and the .NET SqlClient instrumentation's conformance; patterns for per-request counters (DB calls per request) as histogram/tag on the request span. (6) SQL Server snapshot/RCSI behaviour when a batch reads then writes in one transaction (write-skew considerations for uniqueness validation done in C#) and the standard mitigation (unique indexes as the last line of defence, UPDLOCK/HOLDLOCK range checks).`,
  },
  {
    key: 'web-api-mcp', title: 'Web API surface and the MCP seam', spec: '0015',
    research: `(1) ASP.NET Core 10 minimal APIs: route groups, endpoint filters, TypedResults, built-in OpenAPI (3.1) document generation, [AsParameters], JSON options with source generation (JsonSerializerContext, TypeInfoResolverChain, polymorphism attributes), IProblemDetailsService and RFC 9457 output, AddRateLimiter (partitioned policies, sliding/token bucket, in-memory), request body size limits (MaxRequestBodySize, RequestSizeLimit), request timeouts, output caching, response compression, antiforgery for minimal APIs. (2) System.Text.Json in .NET 10: source-generation coverage for records/init-only/nullable, performance vs reflection, known limitations. (3) The Model Context Protocol as of 2026-09: the current spec revision date(s) and headline changes since 2025-06-18 (authorization: OAuth 2.1 resource-server role, RFC 9728 protected resource metadata, RFC 8414, dynamic client registration, client ID metadata documents / CIMD, resource indicators RFC 8707; streamable HTTP transport; tool annotations readOnlyHint/destructiveHint/idempotentHint; structured tool output / outputSchema; elicitation; tasks; server-side pagination). (4) The official C# SDK (ModelContextProtocol / ModelContextProtocol.AspNetCore NuGet): current version, ASP.NET Core hosting model, authentication integration with ASP.NET Core authentication (bearer), tool declaration API. (5) OpenIddict 7.x support for RFC 9728 protected resource metadata, dynamic client registration, and resource indicators — what a platform identity server needs to do so Claude Code, Claude Cowork/Desktop, ChatGPT/Codex, and Cursor can connect to a remote MCP server behind OAuth; the documented requirements of each of those clients (DCR? pre-registered client? PKCE only?). (6) Guidance on MCP tool-set design for large applications (tool count limits in practice, intent-based tools, namespacing, progressive disclosure/discovery tools). (7) Public REST API versioning for ASP.NET Core 10 (Asp.Versioning.Http current version, URL-segment versioning) — only to note the seam.`,
  },
  {
    key: 'blobs', title: 'Blob storage and the record-plus-blobs pattern', spec: '0016',
    research: `(1) Azure.Storage.Blobs (current version, .NET 10 support): ETag and conditional request options (IfMatch/IfNoneMatch) on upload/download, BlobHttpHeaders (content type, cache control), blob index tags and lifecycle-management rules filtered by tags (e.g. delete staging blobs older than N days), soft delete, container naming rules and limits, per-tenant containers vs virtual directories, DefaultAzureCredential auth, Azurite for local development. (2) Server-side image processing libraries for .NET in 2026: SixLabors.ImageSharp (current license terms — Six Labors Split License thresholds), SkiaSharp (license, maintenance, native dependencies on Linux App Service), Magick.NET (license, native deps), and decompression-bomb protections; the usual choice for resizing profile pictures and thumbnails. (3) ASP.NET Core: serving a stream with ETag/Last-Modified and 304 handling (Results.Stream / Results.File entityTag parameters), Cache-Control choices for private, authenticated, immutable-by-id blobs. (4) File-system blob stores: atomic write patterns (temp file + rename), path traversal safety, and Linux/Windows portability considerations. (5) Client upload patterns for records with attachments in modern SaaS apps (staged upload with token vs multipart with the record) — collect the trade-offs as documented by major APIs (Stripe file uploads, GitHub, Google Drive resumable, Slack).`,
  },
  {
    key: 'core-gl-stacks', title: 'Core and GL reference stacks', spec: '0017',
    research: `(1) JSON Merge Patch (RFC 7396) vs JSON Patch (RFC 6902) support in .NET 10 / ASP.NET Core 10 (Microsoft.AspNetCore.JsonPatch.SystemTextJson status) and the typical choice for "edit a few settings out of many" APIs. (2) SQL Server hierarchyid maintenance for bulk reparenting: GetReparentedValue usage, computing new child nodes without collisions (GetDescendant with siblings), recursive CTE patterns to recompute subtree counts, and indexing (depth-first index on Node, breadth-first on level+ParentId). (3) Identity server integration facts from this repo: read src/apps/Tellma.Identity for the bulk invite and delivery-status endpoints' routes, request/response records, statuses, and the scope required (tellma_identity); report them precisely so UserService can be designed against them. (4) SignalR in ASP.NET Core 10: IUserIdProvider, per-user groups, sending from a background service via IHubContext, and Azure SignalR Service's Microsoft.Azure.SignalR current version. (5) Cost-center / responsibility-center taxonomies in accounting (service, operation, sale, investment/profit centers) to sanity-check the CenterType values.`,
  },
  {
    key: 'excel', title: 'Excel codec: export and import', spec: '0018',
    research: `(1) .NET Excel libraries as of 2026-09: ClosedXML (version, license, memory model, streaming support, .NET 10), EPPlus (version, the Polyform Noncommercial / commercial license situation), MiniExcel (version, Apache-2.0, streaming read/write, memory at 1M rows, templates), DocumentFormat.OpenXml 3.x (version, MIT, OpenXmlWriter/OpenXmlReader SAX streaming), NPOI (version, license); benchmarks for writing/reading 1M rows; recommendation for a platform that must export up to the Excel row limit with bounded memory and re-import the same file. (2) Excel facts: row/column limits, number-format codes for dates and numbers, locale-tagged formats ([$-xxxx]), calendar support in number formats (Hijri via B2 prefix / Um Al Qura, whether any Ethiopian calendar rendering exists), date serial semantics (1900 vs 1904 date systems), shared strings vs inline strings for streaming writers, custom XML parts and defined names for embedding metadata, data validation lists, sheet protection. (3) Patterns other systems use for round-trippable import/export sheets (Odoo export/import, Dynamics 365 data management, Salesforce Data Loader): header conventions, natural-key columns for lookups, child-row layouts (repeated parent columns vs separate sheets), error reporting per row.`,
  },
  {
    key: 'background-inbox', title: 'Background tasks, scheduler, and the inbox', spec: '0019',
    research: `(1) SQL Server work-queue patterns: UPDATE TOP (n) ... WITH (READPAST, UPDLOCK, ROWLOCK) ... OUTPUT, lease/visibility-timeout semantics, filtered indexes for pending rows, poison-message handling, and the known pitfalls (lock escalation, deadlocks with multiple pollers). (2) .NET 10 hosting for background work: BackgroundService, IHostedLifecycleService, PeriodicTimer, System.Threading.Channels for in-process nudges, HostOptions.ShutdownTimeout and graceful drain, App Service "Always On" and idle behaviour, Azure App Service multi-instance considerations. (3) CRON parsing libraries: Cronos (current version, seconds support, time-zone and DST handling semantics, .NET 10), NCrontab, Quartz cron — recommendation. (4) OpenTelemetry for asynchronous work: span links (ActivityLink) vs parent-child for jobs enqueued by a request, messaging semantic conventions, baggage propagation; how Azure Monitor renders links. (5) Azure SignalR Service and ASP.NET Core 10 SignalR: Microsoft.Azure.SignalR current version, IUserIdProvider, per-user messaging from IHubContext in background services, the REST API to close a user's connections, self-hosted multi-instance backplane options (Redis), and the hub-only token facts. (6) Scheduler replay policies in established schedulers (Quartz misfire instructions, Kubernetes CronJob startingDeadlineSeconds / concurrencyPolicy, Airflow catchup, Hangfire recurring jobs) — collect the semantics to design a replay policy and a backup-restore safeguard.`,
  },
]

const LENSES = [
  { key: 'A', file: 'lens-a-simplicity', title: 'Distro-author simplicity and AI-native authoring' },
  { key: 'B', file: 'lens-b-performance', title: 'Tier-2 performance and operations' },
  { key: 'C', file: 'lens-c-correctness', title: 'Correctness, security, and long-term maintainability' },
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

function researchPrompt(t) {
  return `${COMMON}

You are the research agent for theme **${t.title}** (key \`${t.key}\`, future spec ${t.spec}). Read the briefing's §1 inputs that matter to this theme (the brain dump sections for the theme, the breakdown entry for spec ${t.spec}, the relevant ARCHITECTURE.md sections) so you understand what the designers will need, then answer these questions with the web (and with the repo where the question says so):

${t.research}

Write \`.tmp/crud-design/research/${t.key}.md\` in the research format (briefing §7): one section per question; each finding states what was verified, the verification date, the source URLs (primary sources first), and a one-line implication for the design. Distinguish verified facts from inferences. Where versions matter, give the exact latest stable version and its release date. Return a summary of at most 200 words listing the findings most likely to change a design decision.`
}

function designPrompt(t, l) {
  return `${COMMON}

You are designing theme **${t.title}** (key \`${t.key}\`, future spec ${t.spec}) under **Lens ${l.key} — ${l.title}** (briefing §6). Read every input the briefing lists in §1 — the brain dump and the breakdown in full, the relevant parts of ARCHITECTURE.md, the relevant sections of the frozen specs, and the existing code — plus the research findings at \`.tmp/crud-design/research/${t.key}.md\` (if the file is missing, do the research yourself). Answer every question in the theme's paragraph (briefing §3) and take a position on every seam you touch (§4). Weigh the orchestrator's hints (§5) and say where you disagree. Where a fact is fast-moving and the research file does not cover it, verify it yourself with WebSearch/WebFetch and record what you verified.

Write your proposal to \`.tmp/crud-design/themes/${t.key}/${l.file}.md\` in the designer format (briefing §7, sections 1–8). Be concrete: real names, real types, real C# signatures, real SQL where a statement is load-bearing, real column lists. Be honest in the critique: the brain dump is a draft and its author wants it challenged. Aim for the depth that lets a spec author write the spec without asking you anything; long is fine, vague is not. Return a summary of at most 200 words: your three most consequential decisions and your two biggest doubts.`
}

function judgePrompt(t) {
  return `${COMMON}

You are the judge for theme **${t.title}** (key \`${t.key}\`, future spec ${t.spec}). Read the three proposals under \`.tmp/crud-design/themes/${t.key}/\` (lens-a-simplicity.md, lens-b-performance.md, lens-c-correctness.md — judge whichever exist), the research file \`.tmp/crud-design/research/${t.key}.md\`, the brain dump, the breakdown, and the relevant sections of ARCHITECTURE.md and the frozen specs. For every question in the theme's paragraph (briefing §3) and every seam it touches (§4), pick the best answer: use the strongest proposal as the spine, graft the best ideas from the others, and supply your own correction where all three are wrong or where a proposal asserts a fact you cannot confirm against the sources (verify against the repo, the specs, and the research file — not against the proposals' claims). Reject anything that contradicts a fixed fact (briefing §2) or that silently narrows the theme's scope. Prefer the simplest design that satisfies all three lenses; when lenses genuinely conflict, say which one wins here and why.

Write \`.tmp/crud-design/themes/${t.key}/decisions.md\` in the judge format (briefing §7: sections 1–8 as a designer file, plus 9 Review flags and 10 Conflicts). It must be self-contained — a reader who never sees the three proposals gets the complete settled design for this theme. Then return the structured result.`
}

const SYNTH_PROMPT = `${COMMON}

You are the synthesizer. Read all ten theme decision files (\`.tmp/crud-design/themes/*/decisions.md\`), the research files, the brain dump, the breakdown (especially its seams and misalignments lists), and the relevant sections of ARCHITECTURE.md. Produce \`.tmp/crud-design/ledger.md\` — the single decision ledger from which ten spec authors will write specs 0010–0019 without seeing the theme files.

Structure:
0. How to read this ledger (one paragraph) and a table of the ten specs with a one-line scope each.
1. Consolidated critique of the brain dump: the general design, then the detailed choices (names, columns, shapes), then the gaps the ledger fills. Specific, honest, no hedging.
2. Cross-cutting seams — one settled shape each for the 17 seams in briefing §4 and any the themes added: the decision, the owner spec, the consumer specs, and the concrete contract in prose (C# and SQL shapes belong in seams.md, which another agent writes from this ledger — but name every type and member here so the two files cannot drift).
3. Per-spec decisions, one subsection per spec 0010…0019, each containing: numbered decisions with rationale; the schema (every table and column, typed); the answers to every brain-dump open question in the spec's scope (quote the question briefly); departures from ARCHITECTURE.md; review flags (judgment calls with their plausible alternatives — keep all of them, they are a required deliverable); explicit non-goals and what the spec leaves to a later spec.
4. ARCHITECTURE.md changes required: a precise list — the section, the gist of the current text, the new decision that replaces it — covering at least the misalignments in the breakdown and every departure recorded in section 3.
5. Open items deliberately left to implementation, and items deliberately deferred beyond these ten specs.

Rules: resolve every conflict from the themes' section 10 explicitly (state the resolution and why); where two themes disagree on a name, a type, or an owner, choose one and record it in the seam; names are final — one name per concept across the whole ledger; do not lose detail — the ledger is the spec authors' only source of decisions, so completeness beats brevity, and C# shapes stay where they carry the decision; keep every review flag; mark any decision you had to make yourself (because the themes were silent) with "[synthesizer]". Return a summary of at most 300 words: the conflicts you resolved and the decisions you had to make yourself.`

const SEAMS_PROMPT = `${COMMON}

You are the contracts agent. Read \`.tmp/crud-design/ledger.md\` in full, then the ten theme decision files (\`.tmp/crud-design/themes/*/decisions.md\`) for the C# and SQL detail the ledger references. Write \`.tmp/crud-design/seams.md\`: for every cross-cutting seam in ledger section 2, the complete contract — C# (namespaces as the ledger places them; interfaces, records, abstract base classes, attributes, enums, options types, extension-method signatures, with an XML-doc summary on every type and member) and, where the contract is SQL, the exact shapes (standard column sets with types, the lease statements, the tag-bump statement, the standalone table types, the tree recompute statement). Consistency rules: one name per concept, identical to the ledger; every type referenced by any contract is defined somewhere in the file; async members take a CancellationToken last; batch-shaped inputs are IReadOnlyList<T>; nothing in Tellma.Core.Abstractions references EF Core, ASP.NET Core, or Queryex types unless the ledger explicitly allows it (say where it does). Add a final section: per spec 0010–0019, exactly which contracts it defines and which it consumes. Where the ledger was ambiguous, resolve it and list the resolution under a "Resolutions" heading at the end so the ledger can be corrected. Return a summary of at most 200 words including every resolution.`

const CRITICS = [
  { key: 'security-correctness', lens: 'security and correctness: fail-closed access control, stale-cache leaks, concurrency soundness, write paths that bypass invariants (tag bumps, audit stamps, RLS post-checks), transaction boundaries, schema evolution under N−1 apps, seams whose two sides cannot both be implemented as specified' },
  { key: 'performance-operations', lens: 'Tier-2 performance and operations: count the DB round trips per operation and per request in the common and cold-cache cases, find N+1 shapes, locks held across I/O, plan-cache fragmentation, unbounded caches or payloads, multi-instance races, missing telemetry, and anything that will not scale to hundreds of tenants and millions of rows' },
  { key: 'author-ergonomics', lens: 'distro-author ergonomics and AI-native authoring: how much a distribution writes to add a plain entity, an entity with IsActive, a tree entity, a custom endpoint, a custom validator, an image; whether every capability is declared once; whether the MCP seam is honoured by every design; whether names are consistent and guessable; whether anything is left for the author to invent' },
]

function critiquePrompt(c, round) {
  const extra = round === 1
    ? ''
    : ` This is round ${round}: the previous round's findings are under \`.tmp/crud-design/critique/round1-*.md\` with the reviser's resolutions in \`.tmp/crud-design/critique/round1-resolutions.md\`. Verify that each accepted round-1 finding was actually fixed (a fix that introduced a new inconsistency counts as a finding), then hunt for residual problems with fresh eyes.`
  return `${COMMON}

You are an adversarial reviewer. Read \`.tmp/crud-design/ledger.md\` and \`.tmp/crud-design/seams.md\` in full, then the brain dump and the breakdown, and the theme decision files and frozen specs as needed to check claims. Attack the ledger and the seam contracts from this lens: ${c.lens}. Also report, regardless of lens: contradictions between sections; decisions that violate a fixed fact (briefing §2) or a guiding principle of ARCHITECTURE.md without recording the departure; missing pieces a spec author would have to invent; names that clash or drift between the ledger and seams.md; review flags that were silently resolved instead of flagged.${extra}

Write \`.tmp/crud-design/critique/round${round}-${c.key}.md\`: numbered findings, each with severity (blocking / major / minor), the exact location (ledger section or seam), the problem, the evidence, and a concrete proposed fix. Default to reporting a doubt as a finding with severity minor rather than staying silent. Then return the structured result.`
}

function completenessPrompt(round) {
  return `${COMMON}

You are the completeness critic (round ${round}). Build an explicit coverage map and write it to \`.tmp/crud-design/critique/round${round}-completeness.md\`: (a) every open question and every "??" in the brain dump \`.tmp/crud-stack.md\`, quoted briefly, with the ledger location that answers it or GAP; (b) every "Owns:" item of every spec in \`.tmp/crud-stack-specs.md\`, with the ledger location or GAP; (c) every seam in the breakdown's "Seams to fix" list and in briefing §4, with the ledger section and the seams.md contract or GAP; (d) every "Misalignments with ARCHITECTURE.md to settle" item, with the ledger section 4 entry or GAP; (e) every decision in the ledger that names a type or member absent from \`.tmp/crud-design/seams.md\` when it belongs to a seam, or defined differently there. List the gaps at the end as numbered findings with a proposed resolution each. Return the structured result (findings = number of gaps; blocking = gaps in seams or in the "Owns" lists).`
}

function revisePrompt(round) {
  return `${COMMON}

You are the reviser (round ${round}). Read \`.tmp/crud-design/ledger.md\`, \`.tmp/crud-design/seams.md\`, and every \`.tmp/crud-design/critique/round${round}-*.md\`. Apply every finding you agree with by editing the ledger and seams.md in place — replace stale text, never append rebuttals or history; keep one name per concept across both files; keep every review flag (add new ones where a fix was itself a judgment call). Where you need a fact to decide, check the repo, the frozen specs, or the research files; do not guess. For each finding you reject or defer, record it with the reason in \`.tmp/crud-design/critique/round${round}-resolutions.md\` (one line per finding, keyed by the critique file and number; also list the ones you applied). Return a summary of at most 250 words: what changed materially, what you rejected and why.`
}

// Per-theme pipeline: research → three-lens design panel → judge. No barrier until synthesis.
const researchStage = (t) => agent(researchPrompt(t), { label: `research:${t.key}`, phase: 'Research' })
const designStage = (_, t) => parallel(LENSES.map(l => () =>
  agent(designPrompt(t, l), { label: `design:${t.key}:${l.key}`, phase: 'Design' })))
const judgeStage = (_, t) => agent(judgePrompt(t), { label: `judge:${t.key}`, phase: 'Judge', schema: JUDGE_SCHEMA, effort: 'high' })

const judged = await pipeline(THEMES, researchStage, designStage, judgeStage)
const judgedOk = judged.filter(Boolean)
log(`Judged ${judgedOk.length}/${THEMES.length} themes; ${judgedOk.reduce((n, j) => n + j.reviewFlags.length, 0)} review flags, ${judgedOk.reduce((n, j) => n + j.conflicts.length, 0)} cross-theme conflicts`)

// Synthesis needs every theme's decisions together.
phase('Synthesize')
const ledgerSummary = await agent(SYNTH_PROMPT, { label: 'synthesize:ledger', phase: 'Synthesize', effort: 'high' })
const seamsSummary = await agent(SEAMS_PROMPT, { label: 'synthesize:seams', phase: 'Synthesize', effort: 'high' })

// Two rounds of adversarial critique + revision.
const rounds = []
for (const round of [1, 2]) {
  phase('Critique')
  const critiques = await parallel([
    ...CRITICS.map(c => () => agent(critiquePrompt(c, round), { label: `critique:r${round}:${c.key}`, phase: 'Critique', schema: CRITIQUE_SCHEMA, effort: 'high' })),
    () => agent(completenessPrompt(round), { label: `critique:r${round}:completeness`, phase: 'Critique', schema: CRITIQUE_SCHEMA, effort: 'high' }),
  ])
  const found = critiques.filter(Boolean)
  const total = found.reduce((n, c) => n + c.findings, 0)
  const blocking = found.reduce((n, c) => n + c.blocking, 0)
  log(`Round ${round}: ${total} findings (${blocking} blocking)`)
  phase('Revise')
  const revision = await agent(revisePrompt(round), { label: `revise:r${round}`, phase: 'Revise', effort: 'high' })
  rounds.push({ round, total, blocking, top: found.flatMap(c => c.top), revision })
}

return {
  judged: judgedOk.map((j, i) => ({ theme: THEMES[i] ? THEMES[i].key : i, decisions: j.decisions, reviewFlags: j.reviewFlags, conflicts: j.conflicts, departures: j.departures, doubts: j.doubts || [] })),
  ledgerSummary,
  seamsSummary,
  rounds,
}