# Platform-core checkpoints

One entry per checkpoint, in priority order (PLAN.md Appendix A). The entries come from the planning
analysis (twelve agents, each reading one to four specs in full), corrected by an adversarial plan
review. They are a starting map, **not a substitute for reading the spec sections**: the spec is the
authority. Where an entry disagrees with the spec, follow the spec and note it in the review file.

- **Gated on** is the authoritative readiness list. It is also in DEPS.md, which
  `tools/check_deps.py --ready` reads. Every gate points to an earlier checkpoint.
- **Later seams** are dependencies on later checkpoints. Ship a seam (an interface, token, default
  or placeholder); the later checkpoint fills it.
- **Analyst dependency notes** is the analysts' original text. Treat it as reading guidance.
- **Planner note** records ordering, tier, ownership and seam decisions. It binds.
- Tier 1 = MVP, 2 = completion, 3 = may spill (PLAN.md §4).

## Phase 1 Foundations and amendments

### PC-000 — Run infrastructure: PR and nightly test tiers in CI, tag conventions

- **Analysis id:** `SETUP` · **Spec:** plan · **Layer:** infra · **Tier:** 1 · **Est. LOC:** 500
- **Sections:** PLAN.md §6, PROCESS.md §5.3 and §11
- **Gated on (DEPS.md):** none
- **Analyst dependency notes:** none
- **Deliverables:** ci.yml: (1) every dotnet test filter excludes the nightly tier: build-test `Category!=Integration&Category!=E2E&Category!=Nightly&Live!=true`, integration `Category=Integration&Category!=Nightly&Live!=true`, identity-e2e `Category=E2E&Category!=Nightly`; (2) the client job runs the PR tier only: Chromium installed, `node scripts/e2e.mjs --project chromium` with tests tagged `@nightly`, `@cross-engine` or `@perf` excluded (grep-invert); (3) a concurrency group per head sha with `cancel-in-progress: false`, so every integration push gets its own verdict; (4) integration and client job timeouts raised to 45 min. `.github/workflows/nightly-tier.yml` on platform-core with the real Nightly-tier jobs (dotnet `Category=Nightly`, Playwright `@nightly`, `@cross-engine`, `@perf`, all browsers) guarded by `if: github.ref == 'refs/heads/platform-core'`; the scheduled trampoline on main dispatches it (SETUP.md §1). CONTRIBUTING.md: the PR-tier and Nightly-tier commands, verbatim. `.gitattributes` keeps plans/ LF.
- **Tests:** a CI run on the platform-core PR is green; `gh workflow run nightly-tier.yml --ref platform-core` starts the platform-core version and reports (an empty tier is fine)
- **Done when:** Every integration push gets its own PR-tier CI verdict; the nightly tier can be dispatched and is scheduled through main; the tier commands are written down.

### PC-001 — Package pins and the composition root with both gates

- **Analysis id:** `0011-a` · **Spec:** 0011 · **Layer:** backend · **Tier:** 1 · **Est. LOC:** 4,000
- **Sections:** §1.1 (pins, package edges), §1.2 taxonomy.json, §2.1 (contract core + only items realised now), §2.2, §2.3, §2.4 core.entity-shapes; 0024 §3.2 TellmaRoutes/IClientAssetContributor contracts
- **Gated on (DEPS.md):** none
- **Analyst dependency notes:** none
- **Deliverables:** CPM bumps (EF 10.0.11, SqlClient 6.1.6, Testcontainers 4.14.0, new ASP.NET/config pins). Tellma.Core.Abstractions.Composition: ITellmaFeature, RequiresAttribute, FeatureDeclaration, FeatureContributionItem + ServiceContributionItem/TableTypeContributionItem/ShapeRequirementContributionItem, ServiceLifetimeKind, FeatureContribution (Add, Feature, Singleton/Scoped/Transient, TableType, RequiresShape), IStartupCheck, CompositionProblem, TellmaCompositionException, DeploymentVersions, TellmaRoutes, IClientAssetContributor/ClientAssetContext/ClientMetadataChunk. Tellma.Core.Composition: TellmaBuilder (AddFeature, UseEntity, Services/Configuration/Environment), AddTellma(IServiceCollection,…), IContributionRealizer<T>, RealizationContext, internal ModelContributionItem + Model() extensions, CoreFeature (empty), TellmaComposition.Validate, graph gate (unknown Requires, duplicate names, cycles, type twice with different instances, options bound twice, slug rule), topological realisation with service override and collection-service exemption, TellmaStartupGate (IHostedLifecycleService.StartingAsync), checks core.options/deployment-identity/globalization/key-vault-literals/public-origin/taxonomy/entity-shapes. taxonomy.json at repo root. New test/core/Tellma.Core.Tests.
- **Tests:** unit (Tellma.Core.Tests): graph gate aggregation and Fix text, cycles, unknown item type, duplicate names, double options binding, pack-service override, Validate host-free, taxonomy.json consistency, each built-in check
- **Done when:** Repo builds under warnings-as-errors with new pins; every existing suite (identity, table types, queryex, email) green after the bump; Tellma.Core.Tests green on Windows and Linux; a composition with a cycle or unknown Requires throws one TellmaCompositionException listing every problem before Build(); the startup gate aggregates check problems before the host serves.

### PC-002 — Identity store on the platform collation + Distribution seed kind

- **Analysis id:** `0010-a1` · **Spec:** 0010-A · **Layer:** backend · **Tier:** 1 · **Est. LOC:** 5,000
- **Sections:** §A.11, §A.1 (Part A Testing/DoD rows for them)
- **Gated on (DEPS.md):** none
- **Analyst dependency notes:** none
- **Deliverables:** DatabaseCollations in Tellma.Core.Abstractions/Hosting with a README entry. TellmaIdentityDbContext calls UseCollation(DatabaseCollations.Platform). The re-baselined Initial migration and snapshot. TellmaIdentityConstants.LatestMigration. TellmaIdentityStore.MigrateAsync, built like the design-time factory. Seed.MigrateCommand, set in Tellma.Identity.Web. The store check in IdentitySeeder: the collation read (plus catalog_collation_type_desc on Azure), optional MigrateAsync, then the dbo.__EFMigrationsHistory check that names MigrateCommand. Identity integration and E2E fixtures move to EF-created databases, or to the platform COLLATE. The collation pin test over Tellma.Identity.Migrations, with the unit project referencing Migrations. The Distribution seed kind: options, startup validation (slug of 15 or fewer characters, https origin, secrets outside Development), convergence through ClientDescriptorFactory.Distribution/DistributionService with token exchange off, the origin added to tellma_api resources and granted to seeded CLI/native clients, never deleting, and the DistributionSeedApplied audit event.
- **Tests:** unit (pin, LatestMigration equals the last migration, Distribution descriptors equal onboarding's, idempotence, secret rotation, never-delete, validation failures); integration (SQL Server): a missing database created in Latin1_General_100_CI_AS with RCSI=1; a foreign-collation database refused with ApplyMigrations on and off; three failing stores with ApplyMigrations off (missing, empty, missing LatestMigration row), with an extra later row passing; a context without a migrations assembly failing; MigrateAsync bringing an empty platform-collation database current; the Distribution seed applied twice
- **Done when:** Every existing identity suite is green on Windows and Linux against EF-created platform-collation databases. A developer database in the server default collation is refused with a message naming both collations. The Distribution entry converges on restart. Warnings-as-errors build is clean, and the READMEs are updated.

### PC-003 — Queryex host-composition basics: rename, zones, restrictions, level, Prologue/Body, paging slots, versioned leaves, undeclared columns

- **Analysis id:** `0010-f1` · **Spec:** 0010-F · **Layer:** backend · **Tier:** 1 · **Est. LOC:** 5,500
- **Sections:** §F.0 (codes table, cache-key rule, fixture growth for these items), §F.18, §F.22, §F.1, §F.2, §F.3, §F.4, §F.6, §F.16
- **Gated on (DEPS.md):** none
- **Analyst dependency notes:** none
- **Deliverables:** The QueryexVersion rename across all 21 files (done first). QueryexTimeZones.Ids and Aliases; CldrTimeZones split into Zones (current ids) and Aliases (the 19 rows), with Etc/GMT±n and deprecated ids removed; a checked-in zone.tab id list. KeySetRestriction with Restrictions emitted as IN (SELECT [Id] FROM @tb…), plus caller errors. The level() registry entry. CompiledQuery Prologue and Body (Sql == Prologue + Body + ';\n'). Paging slots @qx{b}_skip and @qx{b}_take as Literal slots, with L3 keyed on their presence and Value re-stamped on a hit. FilterTree.Leaf(text, version) with per-leaf binding and L3 keys. UndeclaredColumns emitted on [T] with QxString/Nullable columns, plus caller errors. Interpreter and fixture support for these. The inspection tool's inputs for restrictions and undeclared columns. The README's amendments list.
- **Tests:** unit: goldens (zero, one and two restrictions with and without a filter and in an aggregate; level; undeclared columns beside a join and with paging; the rewritten paging goldens), corpus (level type, nullity and QX3005), caller-error vectors, Sql == Prologue+Body for every golden, the ProbeCacheKeys hit across pages 1 and 2, leaf-stamp L3 splits, Leaf refusing 0 and Version+1, zone-table tests (Zones ⊆ zone.tab ids, no Etc/GMT±n, aliases resolving, US/Alaska and Etc/GMT-4 being QX3101). integration (SQL Server): differential for restrictions, level and undeclared columns; SELECT COUNT(*) FROM (<Body>) over a paged and an unordered query; every Windows zone name in sys.time_zone_info.
- **Done when:** Every pre-existing corpus entry passes. Every pre-existing golden is byte-identical except the paging names. QueryexLanguage.Version is 1. Warnings-as-errors build is clean on Windows and Linux, with no new package or project.

### PC-004 — icon and kbd foundations

- **Analysis id:** `0023-a` · **Spec:** 0023 · **Layer:** client · **Tier:** 1 · **Est. LOC:** 3,500
- **Sections:** §1 (icon, kbd rows; lucide dependency), §2.1 (kbd parts), §3.1, §3.2, §9 (icon, kbd groups), §11, §12 (unit vectors)
- **Gated on (DEPS.md):** none
- **Analyst dependency notes:** 0002 foundation (exists)
- **Deliverables:** Two entry points. `icon/`: TmIcon (tm-icon) drawing with createElementNS, TmIconRegistry, provideTellmaIcons, TmIconNode, TmIconSource, mirrorInRtl, the guards. `kbd/`: TmKbd, TmKeys, tmFormatKeys, tmKeysText, tmKeysMatch with the event.code fallback and the AltGr rule, tmAriaKeyShortcuts, tmIsApplePlatform. Also: the `lucide` dependency in core-ui package.json plus allowedNonPeerDependencies; `icon` and `kbd` token groups; identity token CSS refresh; stories; examples; components.json; API goldens; budgets of 2 KB and 1 KB; the new `collection` docs group.
- **Tests:** Unit (vitest browser): tmKeysMatch vectors (Arabic layout Mod+D, Mod+Shift+D with key ']', Mod+/, Option on Apple, Polish AltGr+A, Mod+Alt+Plus from numpad and main keys), tmFormatKeys/tmKeysText on both platforms, refused icon attributes, glyph drawn after the first change detection, unknown name gives an empty box plus one dev error. E2E: mirror under RTL, glyph present in the first frame.
- **Done when:** Both entry points build and lint. api:check, size:check, docs:build (no '§'), tokens:check and the parity gate pass. Both are published so that 0010 Part B (§B.7) can migrate glyphs. No existing test regresses.

### PC-005 — Column collations on class-derived standalone table types

- **Analysis id:** `0010-g1` · **Spec:** 0010-G · **Layer:** backend · **Tier:** 1 · **Est. LOC:** 2,000
- **Sections:** §G.1, Part G Testing and DoD
- **Gated on (DEPS.md):** PC-002
- **Analyst dependency notes:** none
- **Deliverables:** UseCollation(column, collation) (case-insensitive name match, last call wins over Column(…, collation), order-independent, and an InvalidOperationException naming the type and column for an unknown name). The character-column check at finalization (char, varchar, nchar and nvarchar only; json refused). Canonical JSON unchanged for collation-free types. The integration fixture creates databases with the platform COLLATE. The migrations host gains a collated type, and its migrations are re-scaffolded with dotnet-ef. README updated.
- **Tests:** unit (a class column with UseCollation keeping its store type; a Column(…)-added column with the call before or after; last call wins; unknown column throws; int, datetimeoffset and json with a collation fail finalization; adding a collation emits a create plus cleanup with no drop; a type without one keeps its canonical JSON and hash); design (snapshot round trip); integration (sys.columns.collation_name reads Latin1_General_100_BIN2; a table variable of the type joins a BIN2 column without Msg 468)
- **Done when:** No existing definition's hash changes. Every existing EF test passes except the three places Part G names. Warnings-as-errors build is clean on Windows and Linux.

### PC-006 — Management API v1, tellma_kind, existingOnly

- **Analysis id:** `0010-a2` · **Spec:** 0010-A · **Layer:** backend · **Tier:** 1 · **Est. LOC:** 2,500
- **Sections:** §A.6, §A.7, §A.8
- **Gated on (DEPS.md):** PC-002
- **Analyst dependency notes:** 0010-a1
- **Deliverables:** All management routes move under api/identity/v1/... and the unversioned ones are removed. The tellma.identity.api.calls counter (tags version and endpoint) plus a log line naming the calling client. The tellma_kind claim (individual for authorization_code/device_code and their refreshes; service for client_credentials and machine token exchange), set at issuance and never client-supplied. InviteUserItem.ExistingOnly with the four outcomes (Active+sub, existing_user_has_no_credential, unknown_email, the existing disabled/purged refusal) and no side effects. Existing integration tests repointed to v1.
- **Tests:** unit (tellma_kind per grant type; existingOnly outcome table); integration (every route under v1 and 404 without the segment; a mixed-existingOnly bulk invite queues no mail for its existingOnly items)
- **Done when:** No caller in the repo uses an unversioned route. The counter is visible on the Tellma.Identity meter. A sandbox-style invite creates no user and sends no mail.

### PC-007 — FilterTree.Via and FilterTree.Exists

- **Analysis id:** `0010-f2` · **Spec:** 0010-F · **Layer:** backend · **Tier:** 1 · **Est. LOC:** 4,500
- **Sections:** §F.7, §F.12
- **Gated on (DEPS.md):** PC-003
- **Analyst dependency notes:** 0010-f1
- **Deliverables:** Via(navigation, inner) with ViaNode: a dotted to-one path, joins shared through the trie, a presence guard on LEFT paths, a depth count, discovery reporting prefixed paths, caller errors (and Via in Having), …Via[nav] locations. Exists(entity, navigation, inner) with ExistsNode: an EXISTS subquery with its own trie, bindings and lookups; aliases [X{n}]/[P{n}]/[B{n}]/[C{n}] continuing in pre-order; NOT EXISTS two-valued; MaxJoins counting; nothing reported to discovery from inside; caller errors. L3 keys include both nodes. The interpreter's Via and Exists. The fixture's child table with a parent navigation.
- **Tests:** unit goldens (Via through INNER with no guard and LEFT with a guard; Exists plain, nested, inside Via, Via inside, under Or and Not), alias uniqueness, caller errors, discovery prefixes, MaxJoins; integration differential (Not(Via) exactly complementary over lines with no center; Not(Exists); Exists over the child table; one SQL text for two users)
- **Done when:** A tree without the new nodes compiles byte-identically, and every new code and location is reachable by a corpus entry.

### PC-008 — Foundation seams: breakpoints, theme, translations and live direction, touch sizes, colour groups

- **Analysis id:** `0010-b1` · **Spec:** 0010-B · **Layer:** client · **Tier:** 1 · **Est. LOC:** 4,500
- **Sections:** §B.1, §B.2, §B.3, §B.5, §B.6, §B.8 (and the modal query migration that Part B DoD lists)
- **Gated on (DEPS.md):** none
- **Analyst dependency notes:** none
- **Deliverables:** layout/breakpoints.json, TM_BREAKPOINTS and TmBreakpoints (matchMedia only). The stylelint plugin tm/allowed-breakpoints, reading breakpoints.json through the package export. TmTheme, with an environment initializer when provideTellmaUi({theme}) is passed. TmUiOptions.translations as the Transloco loader with merge-not-replace, fallback loads, and TM_UI_TRANSLATE re-resolving. ɵTmLiveDirectionality, a MutationObserver on html and body dir. The tm/prefix-exports scope change. ɵtmControlSize, wired into form-field-hosted controls, tmButton and tm-select. The categorical and sequential token groups with emission and default values. The modal shell's media query rewritten to range syntax. Tokens CSS copied to the identity server. Budgets updated: primary to 7.
- **Tests:** unit (vitest browser): TmBreakpoints at 390/576/767/768/1440, memoization, atLeast('xs'); TmTheme set/effective, the listener attached only under system, the same-task write; loader merge in both orders, the en fallback, re-render after load, live Directionality on html and body; resolved size at 390 and 768. Tokens unit: onTint-on-tint ≥4.5:1 in both schemes; both blocks emit the 31 names. lint:test: accepted and refused preludes, prefix cases. Playwright: theme toggle (never data-theme=system), a live dir flip with a modal open and a menu closed, a 46 px field at 390 px. axe light and dark unchanged. Gates: primary golden, tokens:check, size:check.
- **Done when:** Every existing showcase suite is green. The showcase overlays follow <html dir> without the [dir] wrapper. The primary ceiling is 7 with a budgetNotes entry, and the identity token CSS is refreshed.
- **Planner note:** CSP check (0024 §6 serves script-src 'self' with require-trusted-types-for 'script' and no unsafe-eval): first verify whether the current ICU path (@jsverse/transloco-messageformat over @messageformat/core) compiles messages with new Function or eval. If it does, ship an eval-free path here (an interpreter over the parsed AST, e.g. intl-messageformat) used by every tmUi, tm and string-pack message, with a vitest browser test under that CSP rendering a plural and a select in en and ar, and report the ruling.

### PC-009 — Resource grant evaluator, control-plane audiences, ThirdParty seeds

- **Analysis id:** `0010-a3` · **Spec:** 0010-A · **Layer:** backend · **Tier:** 1 · **Est. LOC:** 3,500
- **Sections:** §A.2, §A.5, §A.4 (ThirdParty flag; seed entries optional)
- **Gated on (DEPS.md):** PC-006, PC-002
- **Analyst dependency notes:** 0010-a1
- **Deliverables:** IResourceGrantEvaluator (exact match, or <origin>/<positive int without leading zeros>/mcp), applied wherever spec 0003 validates resource: authorize, code exchange, refresh, client credentials, device code and token exchange. Refresh narrows and never widens. The ResourceRefused audit event, with invalid_target on refusal. Control-plane client_credentials with resource={origin} is checked against the distribution-origin list at request time, and an explicit resource becomes the whole aud. HasAudience beside HasScope. ApiPolicies.ControlPlaneScope requires aud urn:tellma:control-plane, and IdentityScope requires the issuer origin. The ThirdParty Native seed flag: no FirstParty marker and explicit consent.
- **Tests:** unit (the evaluator vectors of Part A Testing; machine audiences; policies refusing a principal whose aud lacks theirs; ThirdParty seed); integration (resource=origin/17/mcp through code exchange and refresh with aud asserted, refresh to /18/mcp and to origin refused; a control-plane token for a distribution answered 403 by the operator and management APIs; an off-list origin rejected with invalid_target; a token without resource accepted by the operator API)
- **Done when:** A token minted for one distribution is 403 at the identity server's own APIs and is invalid at another distribution's origin. Every evaluator vector passes.

### PC-010 — Omitted parameters and constant statements

- **Analysis id:** `0010-f3` · **Spec:** 0010-F · **Layer:** backend · **Tier:** 1 · **Est. LOC:** 3,500
- **Sections:** §F.8, §F.9 (QX4009 and the widened QX2009)
- **Gated on (DEPS.md):** PC-007
- **Analyst dependency notes:** 0010-f2
- **Deliverables:** QueryCompilationOptions.OmittedParameters, with a syntactic folder before binding: bare list elements dropped (in, descendantOf, ancestorOf), atoms folded by polarity counting FilterTree.Not, absorption to a fixed point, aggregation conditions in every mode, and the typed absent value elsewhere. The folder is reusable by §F.10 and §F.20. EvaluateConstant with ConstantItem and ConstantCompilationOptions: a FROM-less SELECT in Value mode, coercion, QX4009, QX4002, QX3007 and QX2009 at Constant[i], single-emission guards, and HasUser. Cache keys include the omitted set and constant items. Interpreter support. Inspection tool inputs.
- **Tests:** unit: a corpus entry per §F.8 table row; absorption through and, or and not; polarity through FilterTree.Not and nested not; list dropping; folding inside Having and a select measure's condition; the typed absent value in a select item; the caller error; constant goldens; HasUser=false; each diagnostic with its location; string-to-Date coercion. integration: the differential with the interpreter applying the folding rule itself; constant items evaluated on SQL Server against the interpreter.
- **Done when:** An empty omitted set compiles as today. The 0012 seams (QueryArguments.Omit and IDataBatch.EvaluateConstant) have the engine members they call.

### PC-011 — Public overlay, drawer and alert dialogs, popover sizes, button links and reasons, tab instantiate

- **Analysis id:** `0010-d1` · **Spec:** 0010-D · **Layer:** client · **Tier:** 1 · **Est. LOC:** 5,500
- **Sections:** §D.1, §D.2, §D.3, §D.9, §D.10, §D.12, §D.13
- **Gated on (DEPS.md):** PC-008, PC-004
- **Analyst dependency notes:** 0010-b1; 0023 §3.1 (icon)
- **Deliverables:** @tellma/core-ui/overlay (tmCreateAnchoredOverlay, tmLogicalPositions, tmPushEscapeDismissal and their types) with every importer migrated and private reduced. TmModal placement 'end' (drawer widths sm/md/lg 360/480/720, full width below sm, no motion), role 'alertdialog' with describedBy (dev warning when missing), no direction passed to CDK, and live setDirection plus updatePosition while open. The modal media query in range syntax, if 0010-b1 has not done it. TmPopover size (sm/md/lg 320/440/640, clamped to 100vw − 2×8 px, block size capped) and panelClass. tm-image placeholderOnError. The .tm-button --icon-size and tm-icon projection rules. a[tmButton] plus disabledReason (aria-disabled, focus kept, activation swallowed, a tooltip on hover, focus and tap via ɵshowOnTap, AriaDescriber), with an anchor plus reason as a dev error and disabled plus reason as a dev warning. TmTabGroup.instantiate(ids?). Showcase modal and popover stories, tokens, goldens, harnesses.
- **Tests:** unit (the overlay suites under the new entry point; placement classes, no direction passed, role and ariaDescribedBy reaching CDK; popover size classes and clamping; placeholderOnError; disabledReason semantics and anchor variants; instantiate: hidden and inert with selection unchanged and the same instance later); Playwright (the drawer at 390/768/1440 in LTR/RTL with a live flip moving its edge; the drawer present one frame after the click; fixed popover geometry while content loads; tm-icon in buttons at each size; a reason tooltip on hover, focus and tap); axe (drawer, alert dialog, popover sizes, a reason button in both themes and directions)
- **Done when:** The popover golden no longer leaks TmOverlaySide from private. ./overlay ≤2 and ./private ≤2, with every other ceiling unchanged. Existing modal and popover consumers render unchanged.

### PC-012 — Calendars: the 'uq' month table, startOfQuarter, 'et'

- **Analysis id:** `0010-f5` · **Spec:** 0010-F · **Layer:** backend · **Tier:** 1 · **Est. LOC:** 6,500
- **Sections:** Part F Testing's benchmark (first), §F.11, §F.17, §F.19
- **Gated on (DEPS.md):** PC-010
- **Analyst dependency notes:** 0010-f3
- **Deliverables:** The 'uq' join-shape benchmark at 10M rows (hoisted variable vs bound parameter vs scalar form, with and without a covering header date index), run once with its result recorded before §F.11 is built. QueryexSchemaBuilder.CalendarMap('uq', source) with Build() validation. 'uq' accepted only when mapped. The nullity registry rule for table calendars. Three emission forms: the lateral OUTER APPLY TOP(1) backward seek per distinct operand; the hoisted DECLARE @qx{b}_v{n} for path-free calls, deduplicated; the scalar subquery for aggregated operands and constant statements. Second lookups on (Calendar, Year, Month) for startOfYear, week, startOfQuarter, add and diff. Lateral sources emitted in creation order. Exists subqueries carry their own lookups. startOfQuarter under gc, et and uq. The 'et' Amete Mihret day count (offset 2922), inline arithmetic with an inverse, Pagume in Q4, and add and diff semantics. The fixture's uq month map seeded from UmAlQuraCalendar by spec 0013 §8.2's rule. An ICU ethiopic vector file. Interpreter support for both calendars.
- **Tests:** unit: lateral goldens (a Date operand, a DateTime operand, a second lookup, a grouping key, mixed row-grain and path-free operands), hoisted goldens (in WHERE, HAVING, an aggregation condition and a grouping key; a call used twice declaring one variable), scalar-form goldens (SELECT, HAVING, a constant statement), QX3101 without the map, corpus rows at quarter boundaries, 29 February, Pagume 5 and 6 and the fourth quarter, a golden per function and calendar. integration: differential for every function over every day 1900-04-30..2077-11-16 and beyond both ends ('uq'); every day 1900–2100 under 'gc' and 'et'; hoisted calls in every position executed on SQL Server; the ICU vectors read by both the corpus and the interpreter.
- **Done when:** A schema without the map keeps QX3101 for 'uq'. The benchmark result is recorded, and the hoisted design is either kept (variable ≤1.5× a bound parameter) or escalated per flag 4.

### PC-013 — l10n: instants, zone labels, period styles and names, bidi-free text, grouping

- **Analysis id:** `0010-d2` · **Spec:** 0010-D · **Layer:** client · **Tier:** 1 · **Est. LOC:** 6,000
- **Sections:** §D.4, §D.7, §D.8, §D.14, §D.15
- **Gated on (DEPS.md):** PC-011, PC-008
- **Analyst dependency notes:** 0010-b1
- **Deliverables:** The pure l10n functions of §D.4 (ISO instants with offsets, fractions truncated to ms, malformed input passed through with a dev warning; relative-time thresholds truncated; GMT offset labels compared at the instant shown), §D.7 (month yMMM, quarter via quarterPattern with Ethiopic month 13 in Q4, year y, names from fixed reference dates, out-of-range numbers formatted as numbers) and §D.8 (the weekday skeleton yMMMEEEEd; timeStyle 'none'). TM_TIME_ZONE, TM_DEVICE_TIME_ZONE and TM_NOW in the primary entry point. TmL10n timeZone, deviceTimeZone, zoneMismatch (recomputed on visibilitychange), zoneLabel, formatDateTime and formatTime with the auto/column/none modes, formatRelativeTime, formatMonthName, formatWeekdayName. TM_BIDI_CONTROLS and tmStripBidiControls applied in every formatter, every TmL10n method and the default TM_UI_TRANSLATE. The grid's number cells get a dir=ltr span and tmNumber gets dir=auto. The grouping option in tmFormatNumber and tmNumber. The tmUi.l10n.quarter string plus Arabic. Golden arrays dateTime, time, relative and zoneLabel; period and name rows over Gregorian, Umm al-Qura and Ethiopic; grouping rows; every Arabic row re-approved through golden:approve. The i18n showcase story with a fake clock. ./l10n ceiling at 10.
- **Tests:** Format golden (all new arrays; no expected string holding a bidi control); unit (every relative threshold boundary, truncation, a DST day boundary in Asia/Beirut, malformed input, the three label modes, zoneMismatch after a zone change, no formatter output with a bidi control for every golden locale, tmNumber grouping=false reformatting on blur while still parsing '2,026'); Playwright (a negative number cell's minus at the left in RTL; input[tmNumber] with Latin and Arabic-Indic digits; a copied Arabic date range pasting as plain text)
- **Done when:** The golden diff is reviewed and approved. Arabic outputs carry no U+200E, U+200F or U+061C. ./l10n ≤10 with the primary within 7. Existing date and number goldens are unchanged except for the stripped marks.

### PC-014 — Account entry URL and sign-in language

- **Analysis id:** `0010-a5` · **Spec:** 0010-A · **Layer:** backend · **Tier:** 1 · **Est. LOC:** 3,000
- **Sections:** §A.9, §A.10
- **Gated on (DEPS.md):** PC-009, PC-002
- **Analyst dependency notes:** 0010-a1
- **Deliverables:** AccountController GET {issuer}/account ([Authorize] on the identity cookie; the section map with profile as default; a 302 carrying client_id, return_url and ui_locales; no-store; the challenge forwarding ui_locales). ApplicationLinkResolver.ResolveAsync(clientId, returnUrl), which honours a same-origin return_url for a first-party client with a registered Origin. The Manage navigation keeps the three query values. The authorization endpoint's redirect to sign-in carries ui_locales from the query or the PAR request. The sign-in interaction pages (email code, external login, passkey registration, step-up) keep ui_locales.
- **Tests:** unit (the section mapping; back-link vectors: same origin, another port, userinfo, javascript:, relative URL, unknown client, third-party client); integration (/account with and without the cookie for each section, asserting Location and no-store; Manage back links; ui_locales from query and from PAR); E2E Playwright (ui_locales=ar through PAR renders sign-in in Arabic with dir=rtl and keeps it through the email-code step; the back link returns to return_url)
- **Done when:** A user opening Account from an Arabic client sees Arabic Manage pages, and the back link returns them only to a same-origin URL. The E2E job is green on Linux.

## Phase 2 Contracts and data-access core

### PC-015 — Cross-spec contract seed (wire seed, CRUD/validation contracts, endpoint and asset contracts)

- **Analysis id:** `SEED` · **Spec:** 0015/0016/0014/0024 · **Layer:** backend · **Tier:** 1 · **Est. LOC:** 6,500
- **Sections:** 0016 §1.1, §3.1-§3.4, §4.1, §10.1 (analysis 0016-a); 0015 §2.2-§2.3 annotations, §7.1-§7.3 validation contracts, §11.2 [EntityRef], §14.1 exceptions; 0014 §4.5 SecurableEndpointMetadata/MemberEndpointMetadata; 0024 §3.2 IClientAssetContributor, ClientAssetContext, ClientMetadataChunk, TellmaRoutes; the descriptor records that 0011 §2.1's FeatureContribution names (NotificationTypeDescriptor, NotificationChannelDescriptor)
- **Gated on (DEPS.md):** PC-001
- **Analyst dependency notes:** 0011-a
- **Deliverables:** Every type that specs 0011-0014 consume from later specs, created once under the owning spec's exact names and namespaces, so later checkpoints extend instead of re-declaring: Tellma.Core.Abstractions.Api (TellmaJson, scalar converters, declaration attributes incl. [ApiResource]/[RelatedSelect]/McpExposure, wire records whose member types exist); Tellma.Core.Abstractions.Crud annotations ([Stack], StackOperations, [ApiAction], [EntityAction], [EntityRef]); validation contracts (ValidationErrors, ValidationCodes, CodedError, ValidationException); TellmaException and the 0015 §14.1 subclasses (ForbiddenException/ForbiddenReason, IndividualRequiredException, ...). StepUpRequiredException is NOT seeded: 0011 §4.6 owns it in Tellma.Core.Abstractions.Tenancy and 0011-c ships it. 0014 §4.5 endpoint metadata records; 0024 §3.2 C# asset-contributor contracts; the descriptor records 0011's FeatureContribution names. Each type carries its full XML docs and the owner's semantics; no behaviour that needs later machinery.
- **Tests:** unit: every scalar of 0016 §3.2 round-trips; JSON options pinned; attribute usage validation
- **Done when:** All seeded types compile with XML docs and unit tests for their pure behaviour; later checkpoints extend them and never re-declare them.

### PC-016 — Caching/settings/localization contracts and the three calendar systems

- **Analysis id:** `0013-a` · **Spec:** 0013 · **Layer:** backend · **Tier:** 1 · **Est. LOC:** 5,500
- **Sections:** §1.1 (Abstractions namespaces), §2.2 contracts (VersionTag, VersionTagNames, UserVersionTagNames, snapshots, VersionTagDependency, VersionTagList), §3.1 contracts (CacheOutcome, CacheResult, CacheOptions), §4.3 contracts (CachedEntitySet, ICacheableEntities), §5.3/§5.6 pure records (SettingVisibility, MultilingualShape, SettingKey/SettingKey<T>, SettingValueType, descriptors, TenantSettings incl. Get<T>/Today, TenantSettingsForClient, ITenantSettingsCache), §7.1 records (LanguageInfo, DigitStyle, LanguageFormats, LocalizationContext), §8 (ICalendarSystem, CalendarCodes, DateStyle, ICalendarRegistry, Gregorian/UmAlQura/Ethiopian systems, formatting §8.3, TryParse), §11 telemetry name constants, Appendix A
- **Gated on (DEPS.md):** PC-003
- **Analyst dependency notes:** 0010 Part F §F.22 (only if value-set constants are included here; otherwise none)
- **Deliverables:** All Abstractions-level contract types of this spec that do not need 0012 entity bases (so 0011's RequestContext and 0012's IDataBatch can compile against them); Tellma.Core/Calendars: GregorianCalendarSystem, UmAlQuraCalendarSystem (HijriCalendar fallback beyond AH 1500, HijriAdjustment=0), EthiopianCalendarSystem (JDN port, 13 months, leap y%4==3, 1752-09-14..2500-01-01), CalendarRegistry; CacheTelemetryNames, LocalizationTelemetryNames; MonthName resource keys Calendar_uq_Month1..Calendar_et_Month13 declared (values land in 0013-c); test/core/Tellma.Core.Tests created if 0011/0012 have not yet created it
- **Tests:** unit: Ethiopian conformance vectors (Appendix A) and inverses, JDN round trips over the whole range; golden strings per calendar x DateStyle x culture (en-GB, ar-SA, am-ET); AH 1500 overflow via HijriCalendar; TryParse inverts Format(Short); VersionTag.ToWire form; SettingKey<T> construction (DefaultJson, LabelKey, group derivation rule of §5.3); TenantSettings.Get<T> parse/default/memoize and Today with FakeTimeProvider
- **Done when:** Abstractions builds with no EF/framework reference (only Queryex + BCL); calendar goldens and Appendix A vectors green on Windows and Linux; XML docs on every member; README of Tellma.Core.Abstractions lists the new namespaces

### PC-017 — Blob primitives, content sniffing and the file-system store

- **Analysis id:** `0017-a` · **Spec:** 0017 · **Layer:** backend · **Tier:** 1 · **Est. LOC:** 3,200
- **Sections:** §1.3 (FileSystemBlobStoreOptions, AddFileSystemBlobStore), §2.3, §2.4 (policy records + presets only), §3.2 step 1 (file-name sanitiser), §3.3, §3.6 (IImageProcessor contract), §6.1, §6.2, §10.1 (names, store meter)
- **Gated on (DEPS.md):** PC-001
- **Analyst dependency notes:** none
- **Deliverables:** Tellma.Core.Abstractions.Blobs: IBlobStore, BlobWrite, BlobContent, BlobStoreEntry, BlobStoreException, BlobAlreadyExistsException, BlobName (kind/variant/name grammar, Primary, Variant), BlobKindPolicy + Attachment/Avatar/Photo/Signature presets, ImagePolicy, ThumbnailPolicy, ImageFit, ImageFormat, IImageProcessor, ProcessedImage, ImageRejectedException, BlobTelemetryNames. Tellma.Core.Abstractions.Entities: BlobReferenceAttribute, BlobPreset, BlobReadAccess. Tellma.Core.Blobs: FileSystemBlobStore (.part temp + flush + atomic move, traversal check, lazy EnsureTenant retry-once, PurgeIncomplete), FileSystemBlobStoreOptions, BlobsComposition.AddFileSystemBlobStore, internal content-type sniffer and file-name sanitiser, Tellma.Blobs store instruments. Tellma.Core.Testing: abstract BlobStoreConformanceTests (xUnit v3). test/core/Tellma.Core.Tests/Blobs (create the project if 0011 has not).
- **Tests:** unit: BlobName grammar incl. traversal strings; sniffing over fixture byte prefixes (PK OOXML, MZ, ELF, Mach-O, HTML with leading whitespace, SVG behind XML prolog, text with NUL); sanitiser; FS store conformance in a temp dir (create-only conflict, missing read null, delete-missing success, list by prefix, purge .part, EnsureTenant idempotent, traversal rejected, missing tenant dir created+retried, seekable read of known length, primary+thumb in one call); store-instrument tag values
- **Done when:** Abstractions compile with only the Tellma.Core.Queryex edge; FS store passes the shared conformance suite on Windows and Linux; READMEs updated; warnings-as-errors green. 0012's metadata checkpoint and 0014's User entity can now reference [BlobReference] and BlobKindPolicy.

### PC-018 — Entity contract, annotations, conventions, table-type binder and the TellmaDbContext model

- **Analysis id:** `0012-a` · **Spec:** 0012 · **Layer:** backend · **Tier:** 1 · **Est. LOC:** 6,000
- **Sections:** §1.1, §1.2, §1.3, §1.4 (constants), §2.1–§2.4, §2.6, §2.8, §4.1, §4.2, §4.4, §6.2
- **Gated on (DEPS.md):** PC-001, PC-005, PC-015, PC-017
- **Analyst dependency notes:** 0011-a; 0010 §G.1
- **Deliverables:** Abstractions edge to Tellma.Core.Queryex; HierarchyId pin. Tellma.Core.Abstractions.Entities: Entity/TopLevelEntity/ChildEntity/TreeEntity/ActivatableTreeEntity (+ non-generic), IActivatable, IJobEntity, every annotation of §2.2 (TableType, ExcludeFromTableType, Collation, Tree, ParentKey, References, TableIndex, MaxChildren, ServerOwned, WriteOnce, Derived, Defaulted, NaturalKey, Unique, Searchable, PreserveWhitespace, JsonColumn, Multilingual, DisplayNameSource, Cacheable, BumpsVersionTag, BumpsUserVersionTag, ExcludeFromExcel, Sibling, TableTypeBinder marker), SearchKind). Abstractions.TableTypes: DateList, IdStampList, [TableType] on the bulk lists. Tellma.Core.Data: the 18 conventions of §1.3 in order, TellmaDbContext (model from composition: leaves, UseEntity substitution, standalone types, open Core configurations closed over leaves, UseCollation(Platform)), UseTellmaSqlServer (UseSqlServer, UseHierarchyId, UseTableTypes(scope), compat 160, Application Name), dbo.__TellmaSchema/__TellmaProvisioning model contribution, TableTypeBinder (SqlMetaData from TableTypeDefinition, lazy SqlDataRecord, enums as strings, datetimeoffset(7)), core.model-contributors check, IModel singleton registration.
- **Tests:** unit (Tellma.Core.Tests/Data): each convention against a test model (collation on entity and standalone UDTT, References FK+IX to the registered leaf, TableIndex order, enum sizing incl. nullable enums, temporal shadow periods excluded from UDTT, tree Node/UX/defaults, sequences START 1000, CK_Id > 0, unique filtered/child-scoped, sibling FK cascade and no sequence, blob FK + filtered UX, job FK SET NULL), open Core configuration closed over a registered leaf, binder metadata/ordering by name and type mapping
- **Done when:** A composed test model with a leaf substitution builds one mapped type per table, its migration scaffolds the expected constraint and index names, and the binder streams rows for entity and standalone types; repo and existing suites green on both OSes.
- **Planner note:** [BlobReference]/BlobPreset/BlobKindPolicy come from 0017-a and the 0016 attributes from the contract seed: do not re-declare them.

### PC-019 — Language catalogue, value sets and negotiation

- **Analysis id:** `0013-b` · **Spec:** 0013 · **Layer:** backend · **Tier:** 1 · **Est. LOC:** 5,000
- **Sections:** §7.1-§7.3, §7.7, §9.1-§9.2, §1.3 (language, calendar and value-set startup checks incl. core.time-zones warning)
- **Gated on (DEPS.md):** PC-016, PC-003, PC-001
- **Analyst dependency notes:** 0013-a; 0010 Part F §F.22; 0011 §2.2-§2.3 (TellmaBuilder.Languages/AddLanguage, FeatureContribution.Calendar/ValueSet, realised gate) — can be unit-tested with the registries constructed directly if 0011 is not yet in
- **Deliverables:** LanguageCatalog (15 entries with Symbol, NativeName, direction; frozen 243 language-region pairs generated once from .NET 10 predefined cultures and checked in as data; native digits ar->arab, ur->arabext, hi->deva; CLDR likely default regions); distribution declaration handling for TellmaBuilder.Languages(['en-GB','ar-SA']) and AddLanguage(info, formats); IsOffered/IsRegionOffered/Formats; ValueSetAttribute/ValueSetEntry/ValueSetDescriptor/IValueSetRegistry/CoreValueSets and ValueSetRegistry with the four core sets (core.TimeZone from QueryexTimeZones.Ids + Aliases); LocalizationNegotiator (q-ordered Accept-Language, extension stripping, region skip rule, MessageLanguage/MessageCalendar overrides, ContentLanguageIndex derivation, -u-nu-latn Gregorian-forced CultureInfo clone with bidi marks stripped from patterns); header-rejected counter; startup checks as CompositionProblems
- **Tests:** unit: catalogue invariants (unique symbols, every code predefined, every pair resolves with predefinedOnly:true, -u-nu-latn clone accepts GregorianCalendar and prints '1,234,567.89' for ar-SA); Formats/IsRegionOffered for declared region, catalogue default, unknown code, AddLanguage; full negotiation table (headers x preferences x tenant for language, region, calendar, zone; en-SA -> en-GB; ar-SA-u-ca-islamic-umalqura -> ar-SA; invalid header ignored and counted; MessageLanguage vs ContentLanguageIndex; MessageCalendar outside pair); value sets' values/order/labels, core.TimeZone == QueryexTimeZones.Ids, aliases, each startup gate of §7.7, core.time-zones warning with a fake resolver
- **Done when:** All negotiation/catalogue tests green on both OSes (the predefined-culture assertions are ICU-version sensitive: run on Windows ICU and Ubuntu libicu in CI); no runtime dependency on host ICU for the catalogue data itself

### PC-020 — Status and identity indicators

- **Analysis id:** `0023-b` · **Spec:** 0023 · **Layer:** client · **Tier:** 1 · **Est. LOC:** 5,500
- **Sections:** §2.2, §3.3, §4.1, §4.2, §4.4, §4.5, §5.3, §9 (badge, counter, avatar, progress, skeleton, emptyState groups), §10 (Copy, Counter keys), §11, §12
- **Gated on (DEPS.md):** PC-004, PC-008, PC-013, PC-011
- **Analyst dependency notes:** 0023-a; 0010 §B.1; 0010 §B.3; 0010 §B.6; 0010 §B.8; 0010 §D.4; 0010 §D.9; 0010 §D.10
- **Deliverables:** Entry points `badge` (TmBadge, TmCounter), `avatar` (TmAvatar, tmInitials, tmAvatarTone with FNV-1a vectors), `progress` (TmProgressBar with the 250 ms hold, TmProgressRing), `skeleton` (line/block/circle/table), `relative-time` (one shared minute-aligned ticker that pauses while hidden), `copy` (TmCopyButton with clipboard fallback, TmReference) and `empty-state`. Each with harnesses, stories, tokens, strings, goldens and budgets.
- **Tests:** Unit: tmInitials across Latin, Arabic, Ethiopic and CJK; tmAvatarTone fixed vectors ('core.User:17'→4, ':18'→3, verified); the progress hold with fake timers. Harness: copy, reference, counter, avatar, progress, relative-time, empty-state. E2E: copy fallback with clipboard permission denied; bounding boxes for counter 9→10 and for copy success; reduced motion makes the sweep and ring static; forced colors show the tinted avatar circle and counter tone; axe on every state.
- **Done when:** Seven entry points pass every gate. Bounding-box specs show no shift. The avatar tone vectors are pinned. Relative time ticks once per minute and stops while hidden.

### PC-021 — Entity metadata, startup validation, natural keys, display-name metadata and fingerprints

- **Analysis id:** `0012-b` · **Spec:** 0012 · **Layer:** backend · **Tier:** 1 · **Est. LOC:** 4,500
- **Sections:** §2.5 (metadata half), §2.7, §3.1 (EntityMetadata, records, IEntityMetadataProvider; IDisplayNames contract only), §3.2, §3.3 (contracts), §4.3 StorageFingerprint
- **Gated on (DEPS.md):** PC-018
- **Analyst dependency notes:** 0012-a
- **Deliverables:** Abstractions.Data: EntityMetadata, PropertyMetadata, PropertyOwnership, ChildCollectionMetadata, ReferenceMetadata, NaturalKeyMetadata, MultilingualGroup, UniqueIndexMetadata, TreeMetadata, BlobReferenceMetadata, DisplayNameMetadata, IEntityMetadataProvider, IDisplayNames (contract), QueryColumn, QueryRowSet (+Builder), RowPage, RelatedEntities/RelatedEntitySet, TableName. Tellma.Core.Data: metadata builder from IModel + reflection (ownership derivation incl. interface-declared members, compiled getters/setters, ResetServerOwned/ResetDerived, child pairing, navigation naming, natural-key inference order, display-name source resolution), every §3.2 rule reported into the realised gate (warnings for unkeyed referenced entities), SchemaFingerprint, StorageFingerprint.
- **Tests:** unit: every §3.2 rule as a failing model, a model configuration naming a replaced default refused, natural-key inference order, ownership derivation, ResetServerOwned/ResetDerived, SchemaFingerprint and StorageFingerprint stability and change detection
- **Done when:** IEntityMetadataProvider.Get resolves a leaf from any base. A malformed model fails startup listing every problem. The web-host and migrator compositions produce equal StorageFingerprint values. All tests are green.

### PC-022 — ICU localizer factory, LocalizationScope and Core resources

- **Analysis id:** `0013-c` · **Spec:** 0013 · **Layer:** backend · **Tier:** 1 · **Est. LOC:** 5,000
- **Sections:** §10.1-§10.3, §1.2 (AddTellma replaces IStringLocalizerFactory), §11 (tellma.localization.missing, MissingResourceString, MessageFormatMalformed)
- **Gated on (DEPS.md):** PC-019, PC-016
- **Analyst dependency notes:** 0013-a; 0013-b
- **Deliverables:** IcuStringLocalizerFactory decorating ResourceManagerStringLocalizerFactory; one shared MessageFormatter with CustomValueFormatter (date/time via ICalendarSystem + display zone of LocalizationScope.Current or the request; medium/long supplied); named-argument extraction (dictionary or anonymous type; second object -> ArgumentException); LocalizationScope (AsyncLocal, nesting, restore); missing-key telemetry once per key per culture; Tellma.Core Resources/Strings.resx (neutral English) + Strings.ar.resx satellite (at least for the reference distribution's declared 'ar'), ServerStrings.resx marker; Core keys enumerated in §10.1 (Calendar_<code>, Calendar_<code>_Month<n>, Region_<code> for every catalogued region, Problem_*, Settings.* codes, Action_*, Entity_New, Entity_DisplayNameFallback, Boot_*, SettingCategory_general, Access_Reason_self, report/dashboard keys, Queryex_<code>); a small generator for Region_ names per shipped language (e.g. a node script using Intl.DisplayNames in the client workspace, or CLDR JSON) checked into eng/; CoreStrings addressing
- **Tests:** unit: plural categories for ar and am; gender select; malformed pattern -> raw value + one log; {d, date, medium} in every calendar; resource fallback chain ar-SA -> ar -> neutral with no tenant hop; key absent everywhere returns key and counts; CoreStrings addressing from a test assembly; LocalizationScope: resolves/formats in scope culture, calendar and zone, disposal restores, concurrent flow isolation
- **Done when:** Every localizer in a composed host renders ICU; scope semantics pinned; resx keys follow §10.1 grammar; Region_ generation reproducible by a documented command that runs on Windows and Linux

### PC-023 — Chips and feedback surfaces

- **Analysis id:** `0023-c` · **Spec:** 0023 · **Layer:** client · **Tier:** 1 · **Est. LOC:** 5,500
- **Sections:** §2.1 (chip, toast rows), §4.3, §5.1, §5.2, §5.4, §9 (chip, toast, banner, busyOverlay), §10 (chip, toast, busy overlay, banner keys), §12
- **Gated on (DEPS.md):** PC-020, PC-004, PC-011, PC-008
- **Analyst dependency notes:** 0023-a; 0023-b; 0010 §D.1; 0010 §B.1
- **Deliverables:** Entry points `chip` (TmChip, TmChipSet with wrap/collapse/scroll, roving buttons, exit/focusLast), `toast` (TmToasts root service with a popover=manual host, two persistent live regions, a maxVisible queue, key replacement, timers paused on hover, focus or hidden page, focus(), Escape via the stack; plus provideTellmaToasts), `banner` (TmBannerBar, exactly 32 px) and `busy-overlay` (TmBusyOverlay: inert in the same task, cursor: progress, scrim at 200 ms, `immediate`, nested runs, live label, focus restore).
- **Tests:** Unit: toast queue (max visible, replace by key, pause and resume, sticky error) and chip collapse/scroll on resize only. Harness: chip/chip-set, toast (root loader), banner, busy-overlay nesting, live label, immediate. E2E: toast timers pause on hover and focus; focus() then Escape; the busy overlay blocks keys and pointer with cursor: progress; scrim at 200 ms or at once; focus restored; the focused chip scrolls into view. Use Playwright page.clock for the timing assertions.
- **Done when:** Four entry points pass every gate. chip/chip-set is published before 0010 Part E starts. The toast and overlay timing specs are deterministic under the fake clock.

### PC-024 — Catalog database, registry, connection resolution and catalog write side

- **Analysis id:** `0011-b` · **Spec:** 0011 · **Layer:** backend · **Tier:** 1 · **Est. LOC:** 6,500
- **Sections:** §3.1–§3.8 (not §3.9), §3.6 checks, §6.7 statement builder and DatabaseCollations, §7.1–§7.5 (catalog statements; session statements deferred to 0011-f), §9 tenancy instruments
- **Gated on (DEPS.md):** PC-001, PC-018, PC-005, PC-002, PC-015
- **Analyst dependency notes:** 0011-a; 0012-a; 0010 §G.1
- **Deliverables:** Abstractions.Tenancy: TenantCategory, TenantState, TenantDescriptor, TenantDisplayNames, TenantMembership, TenantMembershipRecord ([TableType]/[Collation]), ITenantMembershipDirectory, TenantLanding.SingleLiveTenant, TenantNotFoundException/TenantUnavailableException/TenantStateException, TenancyTelemetryNames. Tellma.Core.Tenancy: CatalogDbContext (platform collation, sweep scope, tellma_app grants), CatalogMigrations.Latest, TenantRegistry + refresh hosted service (PeriodicTimer over TimeProvider, single flight, != version compare, MaxStaleness, listener fan-out, 10 s first load, history check), TenantLocation/TenantInfo, ITenantConnectionFactory/ITenantConnectionProvider (SqlConnectionStringBuilder composition, Application Name, AllowedServers, TenantServerRefused), core.sql-profiles (incl. warm-profile warning), ITenantCatalog (Register/SetState/Rename/Relocate/List with THROW 50409 translation), TenantMembershipDirectory (TVP via TableTypeBinder), state-transition table, CREATE DATABASE statement builder (SQL Server vs EngineEdition 5 + CreateDatabaseOptions). New src/core/Tellma.Core.Migrator project holding catalog migrations + CatalogDesignTimeFactory. New test/core/Tellma.Core.IntegrationTests with the catalog fixture (role pre-created).
- **Tests:** unit: registry semantics with FakeTimeProvider (unchanged/changed/older GUID/single flight/listeners only for changed/staleness -> catalog-unavailable), connection composition and every profile rule, AllowedServers exclusion, transition table incl. illegal, creation statement variants, collation pin over catalog migrations, Latest == last migration, TenantLanding over launch vectors (file created here or in 0011-f); integration(SQL): catalog migrations from empty and idempotent, version bumps, concurrent transitions (one wins), RelocateAsync refusals, membership list and per-tenant reconcile TVP
- **Done when:** Catalog migrations apply from empty on LocalDB and Testcontainers; registry reloads only on version change and fails closed beyond MaxStaleness; a row outside AllowedServers is absent from the snapshot; all listed unit and integration tests green.
- **Planner note:** DatabaseCollations already exists (0010-a1) and TellmaException is in the contract seed: do not re-declare either.

### PC-025 — Request context, holder, access guard, scope factory and server links

- **Analysis id:** `0011-c` · **Spec:** 0011 · **Layer:** backend · **Tier:** 1 · **Est. LOC:** 3,000
- **Sections:** §3.9, §4.1–§4.6 (§4.3 without the hub front door), §5.10
- **Gated on (DEPS.md):** PC-024, PC-015, PC-016
- **Analyst dependency notes:** 0011-b
- **Deliverables:** Abstractions.Tenancy: PrincipalKind, AuthenticationAssurance, RequestContext, RequestContextSnapshot, IRequestContextAccessor, IRequestContextInitializer, RequestContextInputs, RequireAssuranceMetadata, ITenantScopeFactory/TenantScope, ITenantStateListener, ISessionTerminationListener, StepUpRequiredException; Abstractions.Composition.ITellmaLinks. Tellma.Core.Tenancy: scoped holder, TenantAccessGuard (state verdicts, ordered initializers, assurance -> StepUp/IndividualRequired), TenantScopeFactory (allowNonActive only for System), TenantSandboxContext, TellmaLinks (Target via an IStackRegistry seam), tellma.tenancy.resolutions/scopes meters.
- **Tests:** unit: RequestContext immutability, Tenantless, RequireTenant, snapshot round trip, holder set only by the two writers, sandbox context throws unbound, guard verdict per state x mutation x AllowWhenReadOnly with fake initializers, assurance rules, ITellmaLinks cases (stack with/without Details via fake registry, unknown, null), ITenantConnectionProvider throws Tenantless; integration(SQL): scope factory against each state with/without allowNonActive, no activity started
- **Done when:** A tenant scope binds the tenant and runs initializers in Order; non-Active/ReadOnly states throw TenantUnavailableException except the System allowNonActive door; all tests green on both OSes.
- **Planner note:** The 0013 contract types (TenantSettings, ICalendarSystem) exist since 0013-a: use them, no placeholders. StepUpRequiredException is owned here (0011 §4.6, Tellma.Core.Abstractions.Tenancy).
- **Later seams (not gates):** PC-063 (0015-b): TellmaLinks resolves its target through an IStackRegistry seam; 0015-b registers the real registry. (Kickoff audit, 2026-11-08.)

### PC-026 — Dialogs and layout

- **Analysis id:** `0023-d` · **Spec:** 0023 · **Layer:** client · **Tier:** 1 · **Est. LOC:** 4,500
- **Sections:** §5.5, §5.6, §7.1, §7.2, §7.3, §9 (card, pageHeader, descriptionList, splitter), §10 (dialogs, error dialog, splitter, card keys), §12
- **Gated on (DEPS.md):** PC-023, PC-004, PC-020, PC-011, PC-008
- **Analyst dependency notes:** 0023-a; 0023-b; 0010 §D.2; 0010 §B.1
- **Deliverables:** Entry points `confirm` (TmConfirm.ask/confirm over an alertdialog TmModal), `error-dialog` (TmErrorDialog with item cap, reference row, Details disclosure, footer primary/secondary with async run and pending label at fixed width, in-place replacement resolving 'replaced'), `card` (collapsible disclosure, preserveContent, reserved invalid dot), `page-header` (slots, the band), `description-list` (dl directive plus the global stylesheet styles/tm-description-list.css) and `splitter` (clamp, separator semantics, pointer capture, RTL-aware delta).
- **Tests:** Unit: the splitter clamp, including the 1,200 px example. Harness: ask per dismissal path; error dialog item cap, in-place replacement, footer run (pending in the click's frame, false keeps the dialog, true closes it, a replaced dialog ignores a late result); card invalid dot and accessible name, collapsed and expanded; preserveContent keeps the body hidden and inert. E2E: header band wraps without clipping; hiding the first end-slot item moves nothing; the error-dialog pending button keeps its width; splitter drag on touch.
- **Done when:** Six entry points pass every gate. These are the dialog primitives 0024 §11 needs.

### PC-027 — Data contracts, batch assembly, contributor stages, schema guard and the shared SQL fixture

- **Analysis id:** `0012-c1` · **Spec:** 0012 · **Layer:** backend · **Tier:** 1 · **Est. LOC:** 6,000
- **Sections:** 0012 §5.1-§5.4, §6.1-§6.2, §13.2
- **Gated on (DEPS.md):** PC-021, PC-025, PC-016
- **Analyst dependency notes:** 0012-b; 0011-c
- **Deliverables:** Abstractions.Data contracts (IDataBatch and friends), DataBatch assembly, ordinals and @tb renaming, the contributor stages with their seams (prelude/guard/bump, IF @tm_Guard wrapper, marked-batch flag), the schema guard, and the test/shared/Tellma.Testing.Entities fixture project with migrations and stand-in tables. Original analysis entry 0012-c: Abstractions.Data: ITenantDatabase, ITenantDatabaseFactory/Handle, QueryexContextValues, BatchPurpose, TransactionMode, ConcurrencyMode, IDataBatch (full contract incl. Save/Update/Delete/Query/Rows specs and receipts), BatchResult, BatchOutcome, IDataBatchContributor, DataBatchStage, SqlOptions, SqlIdentifier, QueryArguments, TellmaSqlErrors, DataTelemetryNames, DataAccessScope. Tellma.Core.Data: DataBatch (assembly, ordinals, @tb renaming of FormattableString holes, result-set counting, line-offset map -> StatementOrdinal, internal channel), contributor stages, schema guard, persist frame steps 1–8 (incl. ForSystem and Maintenance variants), DataOptions with ValidateOnStart, command/timeouts/cancellation (a scoped cancellation seam for RequestAborted), spans and slow-round-trip log, provider error classification and the internal exception family, retry (reported/ambiguous/None/Explicit), commit probe with READCOMMITTEDLOCK, instruments. New test/shared/Tellma.Testing.Entities: TellmaFixtureDbContext with the fixture schema, stand-in core tables (Users, UserStamps, VersionTags, Jobs, Blobs) until 0013/0014/0017 contribute, migrations, change tracking on, FixtureTenant, role pre-creation for grants.
- **Tests:** unit: persist frame assembly order, retry classification for every §6.4 number (8134/8115 -> ArithmeticFailureException), commit-probe verdicts, parameter-count and result-set guards; integration(SQL, Tellma.Core.IntegrationTests/Data): schema guard 50501 on read/validate/persist and older-of-two served, deadlock victim retried, ambiguous all-idempotent re-run, probe Committed/NotCommitted/wait-then-Committed with connection kill, PK 2627 after NotCommitted -> Committed, THROW 50503 retried 3x, result sets before THROW read, None autocommit, Sql dedup with/without writes, factory handle runs Maintenance and refuses Read, persist guard blocks stale permissions tag (50412), out-of-band THROW refused, StatementOrdinal 3 and 4, change-tracking audit
- **Done when:** The fixture project builds and migrates; batches assemble deterministically with fake contributors.
- **Planner note:** Use the 0013-a tag contracts; do not pull them forward.

### PC-028 — Persist frame, error taxonomy, retry, commit probe, instruments and fault injection

- **Analysis id:** `0012-c2` · **Spec:** 0012 · **Layer:** backend · **Tier:** 1 · **Est. LOC:** 6,000
- **Sections:** 0012 §5.5, §6.3-§6.5, §12.1-§12.2
- **Gated on (DEPS.md):** PC-027
- **Analyst dependency notes:** 0012-c1
- **Deliverables:** Persist frame steps 1-8, the error taxonomy and classification, retry, the RCSI commit probe, the §12.1/§12.2 instruments and the connection-kill fault-injection suite, on top of 0012-c1.
- **Tests:** unit: persist frame assembly order, retry classification for every §6.4 number (8134/8115 -> ArithmeticFailureException), commit-probe verdicts, parameter-count and result-set guards; integration(SQL, Tellma.Core.IntegrationTests/Data): schema guard 50501 on read/validate/persist and older-of-two served, deadlock victim retried, ambiguous all-idempotent re-run, probe Committed/NotCommitted/wait-then-Committed with connection kill, PK 2627 after NotCommitted -> Committed, THROW 50503 retried 3x, result sets before THROW read, None autocommit, Sql dedup with/without writes, factory handle runs Maintenance and refuses Read, persist guard blocks stale permissions tag (50412), out-of-band THROW refused, StatementOrdinal 3 and 4, change-tracking audit
- **Done when:** Raw-SQL batches of every purpose execute in one round trip against the fixture database on LocalDB and Testcontainers. The full retry and probe matrix passes under connection-kill fault injection. Every §12.1 executor instrument is emitted.

### PC-029 — Access tables, entities, seeds and fixture migrations

- **Analysis id:** `0014-a` · **Spec:** 0014 · **Layer:** backend · **Tier:** 1 · **Est. LOC:** 4,000
- **Sections:** §1.1, §2 (vocabulary, enums, WellKnownIds, AccessActions, CoreResources), §3.1-§3.8 (table shapes, CHECKs, indexes, computed State, UserStamps, UserPreferences + UserPreferenceList, Roles, RoleMemberships window CHECK, Permissions CHECKs, HasData rows, Queryex names incl. read-only core.UserStamp), §1.2 model contribution
- **Gated on (DEPS.md):** PC-018, PC-027, PC-016, PC-019
- **Analyst dependency notes:** 0012 §2 entity bases/annotations/conventions checkpoint; 0012 §13.2 fixture project; 0013-a/0013-b ([ValueSet], CoreValueSets, DigitStyle); attribute types [Stack]/StackOperations (0015 §2.2), [ApiResource] (0016 §3), [BlobReference]/BlobPreset/BlobReadAccess + core.Blobs model (0017 §2.1) — declared early or stubbed in Abstractions
- **Deliverables:** Abstractions: UserKind, UserState, InviteStatus, Gender, WellKnownIds, AccessActions, CoreResources, User/Role/RoleMembership/Permission entity classes with all annotations; Tellma.Core: EF configurations (open generics over TUser/TRole) for the six tables incl. COLLATE Latin1_General_100_BIN2 on Subject, filtered unique indexes, covering UX_Users_Subject INCLUDE (Kind, IsActive, State), persisted computed State, CK_Users_* evidence checks, IX_* indexes, temporal history tables, UserStamps (non-temporal, cascade) and UserPreferences (composite PK, cascade), HasData rows of §3.8 (system user self-referencing, Administrator role, */* permission, system membership, UserStamps row 1 with fixed GUIDs); core.UserStamp exposed read-only to the Queryex schema; fixture migrations in test/shared/Tellma.Testing.Entities
- **Tests:** integration(SQL): schema applied by migrations with every constraint/index name present; HasData rows; State derived from evidence for every kind; each CHECK refuses its bad row (KindEmail, SystemIsRowOne, SystemHasNoSubject, IndividualEvidence, ServiceEvidence, WildcardHasNoFilter, FilterVersion, RoleMemberships_Window); history rows per save; unit: entity metadata (server-owned/derived/write-once flags) through 0012's metadata provider
- **Done when:** Fixture DB contains production-shaped access tables; every top-level fixture table's audit FKs resolve to core.Users; no production migrations required yet (reference-distribution migrations regenerate when 0011's distribution exists)
- **Planner note:** Lands after the 0012 fixture project (0012-c1, §13.2), which these tables join.

### PC-030 — Form controls

- **Analysis id:** `0023-e` · **Spec:** 0023 · **Layer:** client · **Tier:** 1 · **Est. LOC:** 6,500
- **Sections:** §2.1 (radio, segmented, switch, search-field, date-time rows), §6.1–§6.4, §9 (radio, segmented, switch, dateTime), §10 (search, date range, date-time, validation keys), §12
- **Gated on (DEPS.md):** PC-026, PC-004, PC-008, PC-013
- **Analyst dependency notes:** 0023-a; 0010 §B.1; 0010 §B.6; 0010 §D.4
- **Deliverables:** Entry points `radio` (TmRadioGroup/TmRadio: native radios with an own arrow handler), `segmented`, `switch` (native checkbox with role=switch), `search-field` (TmSearch input directive with leading/trailing edges, held-key, IME and Enter rules, undoable clear via execCommand; TmSearchField), `date-range` (two coupled pickers, tmDateRangeOrder validator) and `date-time` (TmTimeField with locale parse/format over tmFormatTime; TmDateTimeField in an explicit zone with DST gap/overlap rules, a zone label that is always shown, and no write when the instant is unchanged). All bind as Signal Forms controls except the search field.
- **Tests:** Unit: search timing with fake timers (leading and trailing edges, held key, Enter, IME, equal values); time parse/format in en-GB, en-US, ar-SA and ar-SA-u-nu-latn; Up/Down across midnight; Asia/Beirut DST gap and overlap. Harness: Signal Forms binding for radio, segmented, switch, date range (order error, coupling), time field (parse), date-time field (dateTimeIncomplete, zone label, no write on blur or popup commit over a bound '+00:00' instant with a fraction). E2E: clear and Escape undone by Mod+Z in Chromium, Firefox and WebKit; arrows in RTL; touch targets of at least 24 px; zone label bounding box across a DST change.
- **Done when:** Seven controls pass every gate. Cross-engine undo passes. The DST vectors are pinned.

### PC-031 — Pager, tree, drag-drop and reorder list

- **Analysis id:** `0023-f` · **Spec:** 0023 · **Layer:** client · **Tier:** 1 · **Est. LOC:** 6,000
- **Sections:** §2.1 (tree, reorder bullets), §8.1–§8.4, §9 (pager, tree, reorderList, dropTarget), §10 (pager, tree, reorder keys), §12
- **Gated on (DEPS.md):** PC-030, PC-004, PC-020, PC-011, PC-008
- **Analyst dependency notes:** 0023-a; 0023-b; 0010 §D.1; 0010 §B.6
- **Deliverables:** Entry points `pager` (range and item modes, width reservation in ch, lower-bound totals, maxPage, size select), `drag-drop` (TmDragSource, TmDropTarget, the TmDragDrop service, a text/plain payload plus a drag id), `tree` (@angular/aria/tree, lazy children with a reserved spinner slot, selection modes, typeahead, dragData, a dev warning above 500 open nodes) and `reorder-list` (handle grab model, Alt+Arrow, LiveAnnouncer strings, external drops).
- **Tests:** Unit: pager width reservation. Harness: tree lazy load, selection modes, activate, rejection collapses the node and announces; reorder grab, move, drop, cancel and `moved`. E2E @cross-engine: a native drag of a tree node into a textarea at the caret and into a reorder list, in Chromium, Firefox and WebKit; reorder announcements; pager clicked twice mid-load writes page twice; RTL arrows on the tree.
- **Done when:** Four entry points pass every gate. The cross-engine drag suite is green. Every drag has a tested non-drag path (the reorder handle).

## Phase 3 Queries, caching and access control

### PC-032 — Web host: pipeline, route groups, tenant middleware, audit, CSRF and tenantless surfaces

- **Analysis id:** `0011-e` · **Spec:** 0011 · **Layer:** backend · **Tier:** 1 · **Est. LOC:** 7,500
- **Sections:** §3.1 routing, §4.3 TenantMiddleware + TenantAccessFilter (not hub), §5.1, §5.2, §5.3, §5.4, §5.5 bearer scheme/policies/PrincipalKind, §5.6, §5.8, §5.9 (without support-request routes), §9 trace adoption/logger scope/enrichers
- **Gated on (DEPS.md):** PC-025, PC-015
- **Analyst dependency notes:** 0011-c
- **Deliverables:** New src/core/Tellma.Core.AspNetCore: AddTellma(WebApplicationBuilder, slug, compose, composeWeb) + AddTellmaAspNetCore, UseTellma fixed order (header stamp, forwarded headers guard, Serilog request logging, problem-details handler with a minimal tenancy mapping until 0016, HSTS/HTTPS, placeholders for 0016's compression/rate limiter/timeouts/output cache/contract middleware, routing, UseTellmaIdentity slot, authn/authz, CSRF, TenantMiddleware), MapTellma + TellmaEndpoints groups (Web, Api, Hub (empty), Blobs (empty), Tenantless), TellmaEndpointConventions, TellmaAuthentication/TellmaPolicies constants, TenantEndpointMetadata/TenantlessEndpointMetadata/AcceptsBinaryMetadata, TellmaEndpointAudit (host.endpoint-audit), TellmaCsrfMiddleware, Tellma.Bearer + policies, fallback policy, /api/distribution-info, /health/live and /health/ready, admin v1 (info, tenants, state, register with 501, members), PRM document, SPA static assets + navigation fallback per 0024 §6, Data Protection (host.data-protection), forwarded headers, OTel wiring incl. SqlClient enrich removing db.query.text, traceparent adoption ignoring sampled, HostTelemetryNames csrf/stepup counters. New test/core/Tellma.Core.AspNetCore.Tests (WebApplicationFactory + fake auth).
- **Tests:** unit (WebApplicationFactory): tenant verdict matrix on the Web group (unknown/retired/provisioning/suspended/read-only/frozen/active x read/write/allowed-when-read-only x anonymous/member/non-member), every audit violation named by route and method incl. route words, CSRF matrix (each rule, exemptions, AcceptsBinary, 415), scheme isolation, PrincipalKind derivation, step-up challenge shape, info document shape and headers, admin routes v1 matching adminVersions, 501 with no row, host.data-protection outside Development, health before/after first snapshot, PRM shape, SPA hosting rules, traceparent adoption
- **Done when:** A WebApplicationFactory host with a fake composition maps every platform route, the audit passes for platform endpoints and fails startup for each seeded violation, CSRF and verdict matrices pass; builds and tests green on both OSes.
- **Planner note:** SecurableEndpointMetadata/MemberEndpointMetadata are in the contract seed.

### PC-033 — Queryex host integration: schema provider, Rows/Count/constants, entity queries, materializer, display names

- **Analysis id:** `0012-d` · **Spec:** 0012 · **Layer:** backend · **Tier:** 1 · **Est. LOC:** 7,000
- **Sections:** §11.1–§11.5, §5.2 Query/Rows/Count/EvaluateConstant/Assert/Limits, §5.4 key capture and structural dedup, §3.3 reader, §2.5 IDisplayNames, §5.6 read budgets
- **Gated on (DEPS.md):** PC-028, PC-003, PC-012
- **Analyst dependency notes:** 0012-c; 0010 Part F
- **Deliverables:** IQueryexSchemaProvider/QueryexSchemaProvider (per MultilingualShape, navigations incl. sibling left join, TreeNode, 'uq' CalendarMap, exclusions, Fingerprint, descriptor->metadata map); Rows (slot binding: literals, context slots, declared/omitted parameters, measures), Count, EvaluateConstant, Assert; keys-only twin compile and @tb{b}_keys restriction chaining; QueryRowSet.Builder buffers per (Queryex type, field type) with Scale/Precision from column schema, HierarchyId as path string; IncludeAncestors; Query<TEntity> (DiscoverQuery, key paths, children/grandchildren by parent key, AllProperties with UndeclaredColumns); EntityMaterializer (root, related dedup with projection, child stitching, JSON columns by name, missing columns); DisplayNames renderer (shape-admitted columns, prefix separator, no bidi controls); display-name-golden.json created at its @tellma/core path if 0029 has not.
- **Tests:** unit: golden SQL for Rows/Count/key capture/ancestors/AllProperties (root and child JSON columns), omitted parameter folds out and typed null binds NULL, Limits admits a spec Default refuses, Builder buffers for every pair + InvalidOperationException otherwise, materializer routing, IDisplayNames vs the shared golden; integration(SQL): grid page with count and ancestors in one round trip, cap+1, Skip/Take one plan, KeySetRestriction over IdList/BigIdList/StringList/DateList/@tb table, children and grandchildren stitch, sibling left join and cascade delete, omitted vs null @Min, related dedupe with projection, nullable enums, three schemas differ only in Name2/Name3, FilterTree.Via, EvaluateConstant today() equals the query's, JSON columns via AllProperties only, divide-by-zero/overflow -> ArithmeticFailureException, computed decimal precision/scale, level(Node) == GetLevel(), grid and details budgets
- **Done when:** Grid and details reads of the fixture schema run in one round trip each, verified by RetrieveStatistics. Golden SQL is stable. All tests are green on both OSes.
- **Planner note:** No Measures pass-through in Rows: 0010 §F.14 measures are Tier 3 (0010-f6), which adds them.

### PC-034 — The migrator: commands, provisioning steps, grants and database creation

- **Analysis id:** `0011-d` · **Spec:** 0011 · **Layer:** backend · **Tier:** 1 · **Est. LOC:** 6,500
- **Sections:** §3.4 migrator mode, §6.1–§6.4, §6.5, §6.7 (live), §5.9 ITenantProvisioningTrigger + NotConfiguredProvisioningTrigger
- **Gated on (DEPS.md):** PC-032, PC-025, PC-021, PC-028
- **Analyst dependency notes:** 0011-c; 0012-b; 0012-c
- **Deliverables:** Tellma.Core.Migrator: TellmaMigrator.RunAsync (generic host, Tellma:Jobs:Enabled=false last, gate then one command; exit codes 0/1/2/3), migrate (create catalog + RCSI, catalog applock, tellma_app role + application principal, catalog migrations, idsvr via delegate, grants recompute over catalog/idsvr, registry force reload, seed tenants, per-tenant fan-out with bounded parallelism: tenant lock, Migrate(), fingerprint row per 0012 §4.3, grants, version-tag seed seam, steps, drift-scan seam; reports ahead/frozen/provisioning/suspended-steps-deferred), provision (lock, register/resume, create-or-check DB, fresh-DB RCSI + Query Store, principal, migrations, fingerprint, steps in Order, membership hint, invite seam, SetState Active), set-state, status, emit-client-assets (steps 1–4, non-infrastructure checks only, CoreFeature contributor writing distribution/features/routing per 0024 §3.2), TellmaDesignTimeDbContextFactory<TContext> with --connection/env/LocalDB fallback. Tellma.Core: ProvisioningStepContributionItem + realizer, ITenantProvisioningStep, TenantProvisioningContext, step runner (System snapshot, root activity per step, Provisioning-frame seam), dbo.__TellmaProvisioning writes, ITenantProvisioningTrigger + NotConfiguredProvisioningTrigger.
- **Tests:** unit: InProc migrate without migrateIdentityStore exits 1 naming Tellma.Identity.Migrations; command runs after the startup gate; emit-client-assets runs no RequiresInfrastructure check and exits 3 on a composition-only problem; grant statement generation; integration(SQL): migrate creates a missing catalog in Latin1_General_100_CI_AS with RCSI and refuses another collation; role and grants recomputed (every schema but dbo, every sequence, SELECT only on __TellmaSchema, no DML on catalog history) in catalog and a test tenant DB; role exists before TenantMembershipList's migration; ahead/suspended-steps-deferred/provisioning reports; fingerprint keeps two rows across three migrations (with 0012's guard test); __TellmaProvisioning versioned re-run with a fake step
- **Done when:** Against an empty server, migrate + provision bring a test tenant model to Active with the fingerprint row and tellma_app grants; status prints pending migrations and fingerprints; emit-client-assets writes deterministic core metadata with no database; all tests green on both OSes.

### PC-035 — Notification and realtime contracts, registries and null publisher

- **Analysis id:** `0021-a` · **Spec:** 0021 · **Layer:** backend · **Tier:** 1 · **Est. LOC:** 3,000
- **Sections:** §1.1, §1.3, §2.3, §2.4 (contracts: INotificationRenderer, LocalizedText + converter), §2.5 (contributor), §3.1, §3.2 (table-type classes), §4.1, §4.3 (NotificationPreference row), §4.4 (INotificationChannel, NotificationRecipient, TestNotificationResult), §6.3, §6.4 (IClientEventPublisher, IRealtimeStatus, NullClientEventPublisher), §9.1, §10.1 names, §11 (notification-types/-channels/client-events/-options checks)
- **Gated on (DEPS.md):** PC-001, PC-015, PC-028
- **Analyst dependency notes:** 0011 composition checkpoint; 0012 abstractions (IDataBatch, OnCommitted)
- **Deliverables:** Tellma.Core.Abstractions.Notifications/.Realtime contract types (NotificationSeverity, NotificationTypeDescriptor, NotificationChannelDescriptor, DuplicatePolicy, NotificationRequest, WithdrawRequest, INotifier, NotificationsOptions, ClientEvent, ClientEventEnvelope, IClientEventRegistry, telemetry names), LocalizedText + LocalizedTextJsonConverter + Names()/In(), registries + realizers for the three contribution items, NullClientEventPublisher (refusals, OnCommitted coalescing), null IRealtimeStatus (TryAdd), NotificationsClientAssetContributor, startup checks
- **Tests:** unit: grammar + duplicate rejection, Inbox=false rule, TargetResource with Guid-keyed entity refused, options bounds, LocalizedText JSON round trip and slot fallback, null publisher refusals (unregistered name, >4,096 bytes), OnCommitted coalescing with a fake batch, three client-metadata sections in registration order
- **Done when:** Contracts compile; 0013's cache.changed publisher and 0015's PersistContext.Notify/Withdraw can compile against them; unit tests green

### PC-036 — Jobs contracts

- **Analysis id:** `0020-a` · **Spec:** 0020 · **Layer:** backend · **Tier:** 1 · **Est. LOC:** 2,500
- **Sections:** §2.2, §2.3 (Job entity class), §2.4, §2.5 (table-type classes), §3.1–§3.3, §3.7 (key grammar, ClientJobHandler/ClientJobResult records), §4.1 (JobRequest, IJobQueue), §12.1 JobsOptions + bounds, §13 JobsTelemetryNames
- **Gated on (DEPS.md):** PC-035
- **Later seams (not gates):** PC-062 (0015-a). Ship the seam; the later checkpoint fills it.
- **Analyst dependency notes:** 0012 abstractions (IDataBatch, BatchResult, Entity<int>, attributes); 0015 §7.3 CodedError/ValidationException contracts; 0021-a
- **Deliverables:** Tellma.Core.Abstractions.Jobs: JobStatus, Job (Entity<int>, [RelatedSelect], [ServerOwned]), IJobEntity restated (0012), JobHandlerAttribute (Key ctor + settable named properties), IJobHandler, IEntityJobHandler<T>, JobBatch<T>, JobItem<T> (outcome marking, Arguments/State tolerant JSON, CancellationReason), IJobProgress, JobMessage, JobError : CodedError, JobFailedException, JobCancellationReason, JobRequest, IJobQueue, JobsOptions + validator, JobsTelemetryNames, ClientJobHandler/ClientJobResult; table-type classes JobRequestList/JobOutcomeList/JobProgressList/JobLeaseList/ScheduleNextList; key-grammar + attribute-range validation helpers; 4,000-char argument-size guards
- **Tests:** unit: key grammar, option ranges, JobsOptions bounds, JobItem last-call-wins marking, Arguments<T> tolerant JSON, argument-size ArgumentException paths
- **Done when:** Abstractions compile under warnings-as-errors with XML docs; 0015's PersistContext/SaveOptions.OnPersist can reference IJobQueue/IJobProgress; unit tests green on Windows+Linux

### PC-037 — Id allocator and the save emitter

- **Analysis id:** `0012-e` · **Spec:** 0012 · **Layer:** backend · **Tier:** 1 · **Est. LOC:** 7,000
- **Sections:** §7.1–§7.4, §8.1–§8.5, §2.3 null/[]/list semantics, §5.6 create/update/import budgets
- **Gated on (DEPS.md):** PC-033
- **Analyst dependency notes:** 0012-d
- **Deliverables:** IIdAllocator/IdAllocator (per (tenant, sequence) range buffer, exact deficit incl. children, reservation statement on the given batch, low-water refill on any batch writing the table, Assign with temporary-id rewriting in self FKs/parent keys/grandchild keys, Dispose returning ranges before persist only, TakeAsync, heal-at-reservation by consuming the gap, buffer drop on PK 2627, instruments, IdAllocator.Healed); SaveEmitter (two TVPs per table + parents IdList, @tb new/saved/touched/conflicts, U-lock concurrency guard with conflict result set and capped JSON message, inserts with server stamps and provisional tree node, child synchronise incl. nested @tb gone deletes deepest first, binary-collation EXCEPT skip-unchanged, stamped update of changed/touched roots, receipt, OPTION (RECOMPILE) above LargeBatchThreshold, text cache, capture rule @tb cap tables, blob capture tables and ColumnCapture handles); SaveHandle.
- **Tests:** unit: golden SQL per statement kind (root, child, grandchild, blob capture, conflict cap 90/50), SaveReceipt from canned result sets, SaveHandle lists one ColumnCapture per blob column, allocator deficit/buffer/hold-return arithmetic and temp-id rewriting; integration(SQL): skip-unchanged writes no history, case-only edit written and stamped, child-only change stamps parent, unsupplied parent untouched, [] deletes all, grandchild change stamps root, deleted-under-you NotFound, stale stamp Concurrency, Override never resurrects, two racing saves one conflict, write-once and server-owned overwrite, blob capture old/new on insert/update/child delete, RECOMPILE at 1,001 not 1,000; allocator warm buffer no extra round trip, cold one statement, validation failure returns ids, persist failure does not, heal once, PK 2627 not retried and empties buffer, TakeAsync round trips; create/update/import budgets
- **Done when:** Saves of aggregates with children and grandchildren round-trip correctly on the fixture schema within §5.6's budgets. Every emitter and allocator assertion of §13.3 passes on both OSes.

### PC-038 — Version tags: registry, prelude/guard/bump contributors, snapshots, migrator seed

- **Analysis id:** `0013-d` · **Spec:** 0013 · **Layer:** backend · **Tier:** 1 · **Est. LOC:** 6,000
- **Sections:** §2.1-§2.10, §1.3 (version-tag registry checks), §11 (bump/stale/missing instruments, VersionTagRowMissing, StaleVersionTagRetry)
- **Gated on (DEPS.md):** PC-022, PC-016, PC-027, PC-029, PC-034, PC-035
- **Analyst dependency notes:** 0013-a; 0012 §5.1-§5.5 executor checkpoint (contributor stages, marked-batch flag, persist frame step 4/7 seams); 0012 §13.2 fixture database; 0014-a (core.UserStamps/core.Users tables in the fixture model); 0011 §6.1 migrator (for the seed/bump hook; the statements can be tested against the fixture DB before the migrator exists)
- **Deliverables:** core.VersionTags EF model (Model<T>, no HasData); VersionTagRegistry built from EF model attributes ([BumpsVersionTag], [BumpsUserVersionTag], [Cacheable] -> entity:<Name> + entities) plus fixed rules for core.UserPreferences/core.NotificationPreferences; VersionTagSnapshots and UserVersionTagSnapshots stores (private bounded MemoryCache); the prelude contributor (Order 50, yields on marked batches), the bump epilogue contributor (@tm_tag, @tm_tagNames StringList, DECLARE/fill @tm_userIds_<Column>, @@ROWCOUNT -> THROW 50422 VersionTag.Missing, the affected-users select) and cache.changed publishing per §2.5's table after commit; the guard text + @tm_expectedTags VersionTagList binding handed to the 0012 executor (REPEATABLEREAD/UPDLOCK/FORCESEEK arms, UserStamps arm UPDLOCK when the batch writes the caller's stamp row); mismatch-policy application for Read/Validate batches (Refresh vs Rerun); post-batch snapshot overlay without read-back; StaleContextException; wire-tag builder for Tellma-Version-Tags (composite permissions form); migrator hook: seed INSERT every run, wholesale UPDATE only after a run that applied a migration or provisioning step
- **Tests:** unit: registry resolution (attributes -> names and rules, fixed preference rules, hand-written entity: names rejected, [BumpsUserVersionTag] property checks), guard/bump text goldens, policy table; integration(SQL): end-to-end bump (cacheable fixture save moves entity:fixture.Lookup + entities, not settings; raw statement with SqlOptions.UserIds bumps named users; unseeded name -> VersionTag.Missing 500), cache.changed rows via a recording publisher, prelude heads every batch after the schema guard, tag-then-rows ordering under a concurrent bump, round-trip counts via SqlConnection.RetrieveStatistics, migrator seed/wholesale-bump/no-bump-on-no-op, two tenant DBs isolation; the guard concurrency cases (REPEATABLEREAD wait, UPDLOCK serialisation, no deadlock) that do not need the runner
- **Done when:** Every write path of the fixture bumps exactly its declared tags; guard raises 50412 on a moved tag; Missing row degrades reads to Uncached and fails writes with a 500; the integration tests that need 0014's runner (re-run once, StaleContextException after two) are explicitly deferred to 0014-d

### PC-039 — Securables registry, startup audit and securables metadata section

- **Analysis id:** `0014-b` · **Spec:** 0014 · **Layer:** backend · **Tier:** 1 · **Est. LOC:** 3,500
- **Sections:** §4.1-§4.5, §10 (ClientSecurable, SecurablesClientAssetContributor), §1.2 (singleton ISecurableRegistry, Securables(...) realisation), §2 (resource/action grammar)
- **Gated on (DEPS.md):** PC-029, PC-001, PC-033, PC-032
- **Later seams (not gates):** PC-063 (0015-b), PC-094 (0024-f). Ship the seam; the later checkpoint fills it.
- **Analyst dependency notes:** 0011 §2 (FeatureContribution.Securables, realised gate) and §5.3 (TellmaEndpointAudit walk); 0012 §11 schema (FilterRoot resolution against the Queryex schema); 0015 §2.7 StackSecurableContributor (stack-derived registration; until then tests register by hand); 0024 §3.2 IClientAssetContributor (soft)
- **Deliverables:** SecurableRef, SecurableOwner, SecurableDescriptor, SecurableRegistryBuilder (Add idempotent per tuple, Alias, MarkSensitive/MarkNotSensitive), ISecurableContributor, ISecurableRegistry (All ordered, Resources, Find with aliases ordinal-ignore-case, ForResource, Fingerprint SHA-256 over sorted lines), registry freezing at realise; Core's default sensitive set; core.UserStamp × Read with Owner (core.User, User); SecurableEndpointMetadata, MemberEndpointMetadata, NoActivityStampMetadata; startup validation (§4.5) aggregated into 0011's realised gate alongside TellmaEndpointAudit; ClientSecurable + SecurablesClientAssetContributor
- **Tests:** unit: grammar; freezing; duplicates with different filter roots; aliases resolve on Find only; MarkNotSensitive removing a Core default; a pair marked both ways refused; Fingerprint stable across registration order; startup audit over an in-memory EndpointDataSource (unregistered securable, child FilterRoot without Owner, owner cycle); golden of the securables section (one ClientSecurable per securable, owners and label keys)
- **Done when:** Registry is the oracle for validators/evaluator; composition fails on every §4.5 finding
- **Planner note:** StackSecurableContributor (0015 §2.7) arrives with 0015-b. Until then tests register securables by hand.

### PC-040 — Analyzers TELLMA0001–0006 and their packaging

- **Analysis id:** `0012-g` · **Spec:** 0012 · **Layer:** backend · **Tier:** 1 · **Est. LOC:** 4,000
- **Sections:** §12.3, §1.1 analyzer row, §14 CI
- **Gated on (DEPS.md):** PC-037, PC-028
- **Analyst dependency notes:** 0012-c
- **Deliverables:** New src/core/Tellma.Core.Analyzers (netstandard2.0, ScriptDom bundled) packed as an analyzer asset of Tellma.Core.Abstractions (in-repo OutputItemType=Analyzer with its dependency DLL); TELLMA0001 SqlDataRecord/GetOrdinal outside [TableTypeBinder]; TELLMA0002 EF query/SaveChanges over TellmaDbContext outside Tellma.Core and *.Migrator; TELLMA0003 ScriptDom checks on IDataBatch.Sql text (Writes required for DML, MERGE, EXEC allow-list, isolation level, reserved prefixes, THROW bands, hole kinds, non-constant text); TELLMA0004–0006 matched by metadata name for 0015's [ApiAction]/[EntityAction], EntityService<,> write methods from pipeline participants, and writes to stack-owned tables by non-owners. New test/core/Tellma.Core.Analyzers.Tests. CI design-e2e package flow packs Queryex and verifies the analyzer is in the Abstractions nupkg.
- **Tests:** analyzer unit tests (Microsoft.CodeAnalysis.Testing): each condition and its negative
- **Done when:** Every platform project builds clean under the analyzers. Each diagnostic fires as an error in a test project and is silent on its negative. CI design-e2e package flow is green.

### PC-041 — Rich data-driven menu

- **Analysis id:** `0010-c1` · **Spec:** 0010-C · **Layer:** client · **Tier:** 1 · **Est. LOC:** 4,500
- **Sections:** §C.10, the menu-row glyphs of §C.13, the ./menu row of §C.11
- **Gated on (DEPS.md):** PC-008, PC-011, PC-004
- **Analyst dependency notes:** 0010-b1; 0010-d1; 0023 §3.1 (icon); 0023 §3.2 (kbd)
- **Deliverables:** The TmMenuEntry union (TmMenuItem gains description, detail, role, checked, keepOpen, lang and disabledReason; TmMenuLink; TmMenuSubmenu one level deep; TmMenuGroup). TmMenuIcon accepts a string, a TmIconSource or a template. Shortcuts become TmKeys rendered through tmFormatKeys. The button[tmMenuTriggerFor] trigger (APG menu button). ng-template[tmMenuHeader] wired to aria-describedby. Submenus anchored inline-end, mirroring with the live direction, each level pushing onto §D.1's Escape stack. Checkable items via role and aria-checked. Links rendered as <a ngMenuItem>, keeping their native context menu and modifiers. 44 px touch rows below 576 px. disabledReason entries focusable but inert. The menu-rich story, menu harnesses, strings plus Arabic, the golden and ./menu at 7.
- **Tests:** unit (each entry kind; lang on rows; disabledReason focusable, inert and described; submenu keys; typeahead on labels only); Playwright (menu-rich in LTR/RTL; 44 px rows at 390 px; submenus by keyboard and hover; link modifiers stay native); axe both themes and both directions
- **Done when:** Existing items arrays render identically, and itemSelected still emits TmMenuItem. The ./menu ceiling is 7 with a note.

### PC-042 — Update and delete statements and the tree statements

- **Analysis id:** `0012-f` · **Spec:** 0012 · **Layer:** backend · **Tier:** 1 · **Est. LOC:** 5,500
- **Sections:** §9.1, §9.2, §10.1–§10.4, §6.4 (530/547 mappings), §5.6 delete/activate budgets
- **Gated on (DEPS.md):** PC-040, PC-037
- **Analyst dependency notes:** 0012-e
- **Deliverables:** Update<TEntity> (ByIds with AllOrNothing 50404/50403, ByQuery cap/count verified, stamp/no-stamp, skip already-assigned rows, WriteOnce/BlobReference compose-time refusal, UpdateReceipt); Delete<TEntity> (ByIds with ReadFilter missing-ids set, Filter check, IdStampList stamps with conflict set; ByQuery cap/count; WithDescendants by node closure; old-node capture; children deepest first; blob capture; DeleteHandle); TreeStatements (provisional /0/<Id>/, affected set, top-down path CTE with MAXRECURSION = MaxDepth, cycle fence, old-node capture, re-path without stamping, scoped ancestor recount, post-update/delete recount, Verify with repairs + TreeVerify.Repaired), TreeCycleValidator<T>, tree instruments.
- **Tests:** unit: golden SQL for each update/delete shape and each tree statement; integration(SQL): ByIds missing 404, hidden 404, outside Filter 403, stamps 409, ByQuery 50413/50428, WithDescendants hidden root 404 and partly hidden closure 403 and one-statement delete, 547 -> Fk.InUse, repeated activate writes no history; trees: saved row under another saved row pathed once, id paths survive re-parenting, write-skew cycle -> Tree.Cycle, deeper than MaxDepth -> Tree.TooDeep, counts correct after move/activate/deactivate/delete-with-descendants, Verify repairs injected drift; delete/activate budgets
- **Done when:** Every update, delete and tree assertion of §13.3 passes on LocalDB and Testcontainers. TreeStatements.Verify is callable for spec 0020's job.

### PC-043 — Evaluation core: UserAccess.Decide, composition, owners, drift classification

- **Analysis id:** `0014-c` · **Spec:** 0014 · **Layer:** backend · **Tier:** 1 · **Est. LOC:** 5,500
- **Sections:** §5.1 (AccessOutcome, AccessProblemCode, AccessGrant family, AccessProblem, AccessDecision, AccessCriterion, IAccessCriteriaProvider, AccessFilters, UserAccess, AccessComposition), §5.2, §5.5, §6.1, §6.2 (Build: resolve rows, validate each distinct (text, stamp) under its stamp, interning, memoised trees until NextBoundary), §9 (drift table), §12.1-§12.2 (AccessOptions, AccessLimits/PipelineLimits)
- **Gated on (DEPS.md):** PC-039, PC-003
- **Analyst dependency notes:** 0014-b; 0010 Part F §F.6, §F.7, §F.18; 0008 QueryexEngine.Validate (implemented)
- **Deliverables:** Pure C# evaluation: UserAccess (immutable but ValidatedAt with thread-safe monotonic MarkValidated), Decide(securable, criteria, owner, now), NextBoundary, UserAccess.System, AccessComposition.Compose (steps 2-5: windows, wildcard/implied-read matching, FilterRoot-null pruning, absorption, sorted distinct versioned leaves, bespoke criteria, owner Via union), set builder from PermissionRow lists against registry + ITenantDatabase.Schema (drift -> AccessProblem with diagnostics), AccessFilters.CurrentMembership tree + CurrentMembershipSql, AccessOptions with ValidateOnStart, AccessTelemetryNames and set build instruments, PermissionDrift logging per (permission id, code) per tag cycle
- **Tests:** unit: Decide matrix — absorption, union, implied read with the same filter, wildcard x filter, (R,*) filter never granting a non-filterable action, inactive and public roles, system set, criterion-only access, drift exclusion, leaf dedup and ordering (byte-identical compiled SQL for equal grant shapes), leaf carries FilterQueryexVersion, empty -> Denied, no user -> Denied; windows with FakeTimeProvider (before start, inside, at exclusive end, after; two windows; NextBoundary; memo recomposed at boundary); owner composition (direct only, owner only, both, neither, either Unrestricted, owner that is itself a child); drift table row by row; AccessGrant kind discriminator through TellmaJson.Options (needs 0016 §3.1, else defer that one test)
- **Done when:** Decide is I/O-free and clock-free; the composition contract of §5.2 is pinned exhaustively

### PC-044 — VersionedCache and [Cacheable] entities

- **Analysis id:** `0013-e` · **Spec:** 0013 · **Layer:** backend · **Tier:** 1 · **Est. LOC:** 4,500
- **Sections:** §3, §4, §11 (cache instruments, CacheableEntityOverflow)
- **Gated on (DEPS.md):** PC-038, PC-033, PC-039
- **Analyst dependency notes:** 0013-d; 0012 §5 Query/BatchResult and FromCache seam; 0014-b (registry Find for the FilterRoot==null startup check; can be stubbed until then)
- **Deliverables:** VersionedCache<TKey,TValue> base (private MemoryCache per kind, SizeLimit in kind units, TrackStatistics, single-flight via ConcurrentDictionary<TKey, Lazy<Task>>, faulted-loader removal, Peek/Peek(isCurrent)/Set(aged)/Remove, Uncached when the loader's prelude returned None, observable gauges, evictions with reasons); CacheOptions binding Tellma:Cache with ValidateOnStart; CacheableEntityStore + scoped CacheableEntities façade (GetAsync/Peek/GetWireTagsAsync); the capped probe (Select all scalars, OrderBy Id, Take MaxRows+1) and IDataBatch.FromCache implementation with DependsOn Refresh/Rerun; overflow -> Oversized; [Cacheable] startup checks (§4.2 incl. 0014 registry FilterRoot==null check); fixture.Lookups entity ([Cacheable(MaxRows=50)]) added to test/shared/Tellma.Testing.Entities with migration
- **Tests:** unit: tag mismatch is a miss; one loader under 64 concurrent callers; faulted loader retried; bounds and eviction reasons; Uncached stores nothing; keys begin with tenant id; loader runs on the passed handle; Peek(isCurrent) semantics; overflow -> Oversized and one log per type; integration(SQL): cold FromCache costs no extra round trip, Validate batch FromCache declares Rerun
- **Done when:** Cache kinds settings/permissions/preferences/entities can be instantiated on the base; all tellma.cache.* instruments asserted with MetricCollector

### PC-045 — Grid key map through kbd and grid glyphs through tm-icon

- **Analysis id:** `0010-c2` · **Spec:** 0010-C · **Layer:** client · **Tier:** 1 · **Est. LOC:** 3,000
- **Sections:** §C.12, the grid parts of §C.13
- **Gated on (DEPS.md):** PC-041, PC-004
- **Analyst dependency notes:** 0010-c1; 0023 §3.2 (kbd)
- **Deliverables:** One keymap binding table, with TM_GRID_KEYS derived from it (tmUi.grid.keys.* labels). Letter, digit and symbol bindings (Mod+A/Z/Y/Shift+Z/D/F, Mod+Alt+Plus/Minus/=) match through tmKeysMatch. Pointer Mod reads tmIsApplePlatform, and the find bar's navigator.platform sniff and grid-core's IS_MAC go. The ESLint rule tm/no-platform-sniffing over projects/core/** and projects/module/** outside kbd/. The context menu passes TmKeys and TmIconSource (the 'Press {shortcut} to paste' hint filled with tmKeysText). ɵTmGridIcons removed. Find bar, status bar and tree-expander glyphs via tm-icon, with the expander swapping glyphs under live direction. Masks stay.
- **Tests:** unit (each binding matched by a synthetic event, including an Arabic key with a Latin code); lint:test vectors for tm/no-platform-sniffing; Playwright (Mod+Z/D/A/F on a synthetic Arabic layout; glyph geometry in LTR/RTL; the expander and status-bar chevrons after a live flip; the scroll probe on an expanded tree)
- **Done when:** No navigator.platform or userAgentData outside kbd, enforced by lint. TM_GRID_KEYS is exported and golden-recorded, and every existing grid keyboard e2e is green.

### PC-046 — String pack, label provider and resource audit

- **Analysis id:** `0013-g` · **Spec:** 0013 · **Layer:** backend · **Tier:** 1 · **Est. LOC:** 5,000
- **Sections:** §7.4-§7.6, §10.4, §5.4, §1.3 (string-pack key conflicts), §12 (labels, string pack, resource audit, [Multilingual] label suffixes)
- **Gated on (DEPS.md):** PC-044, PC-022, PC-001, PC-021
- **Later seams (not gates):** PC-094 (0024-f). Ship the seam; the later checkpoint fills it.
- **Analyst dependency notes:** 0013-c; 0011 §2 (composed feature assemblies); 0012 §3 (EntityMetadata.Name, properties, navigations, collections, [Multilingual]) and §11.1 (schema per MultilingualShape); 0024 §3.2 IClientAssetContributor (soft)
- **Deliverables:** StringPackComposer (one pack per offered language from every composed feature assembly's Strings resource set, flat JSON, resx keys verbatim, Version = DeploymentVersions.DistributionVersion, merged dictionaries kept in memory, conflict -> CompositionProblem, ServerStrings excluded); IStringPackProvider; LabelProvider with every chain of §7.6 and the key-resolving twins (multilingual twin -> primary key + ' (Symbol)' per tenant shape); string-pack emission through the client asset contributor; new test/shared/Tellma.Testing.Resources (BCL-only resource-audit helper: every neutral Strings/ServerStrings key derived by convention from the composed model or present as a string literal under src/, satellite keys subset of neutral) run over Core
- **Tests:** unit: every label chain incl. navigations, collections, actions and each twin; null when no key; entity declared in one assembly with keys in another composed assembly resolving in ar and en; one pack per offered language, key conflicts refused, ServerStrings absent; resource audit over Core (orphan fails, satellite-only key fails); [Multilingual] twin discovery and suffixes per shape, schema gating over 0012's fixture model
- **Done when:** Labels resolve against the merged dictionaries, never one assembly; the audit helper is consumable by later pack suites (0018 first); the audit locates src/ robustly on both OSes

### PC-047 — Sessions, BFF, back-channel logout, timers and in-proc identity mode

- **Analysis id:** `0011-f` · **Spec:** 0011 · **Layer:** backend · **Tier:** 1 · **Est. LOC:** 6,500
- **Sections:** §5.5 (cookie/OIDC schemes, session store, refresh CAS, back-channel receiver, BFF endpoints, landing, profile cookie, step-up, SessionSweepService), §3.8 MembershipReconcileService, §5.7, §7.4, §7.5 session statements, §9 session/backchannel instruments and log events
- **Gated on (DEPS.md):** PC-034, PC-032, PC-002
- **Analyst dependency notes:** 0011-e; 0010 §A.1; 0010 §A.11
- **Deliverables:** ICatalogSessionStore/CatalogSessionStore (key + handle, per-instance MemoryCache, TokensVersion-guarded renew, revoke by sid/subject with OUTPUT, sweep), TellmaSessionEvents (refresh skew, single flight per key, CAS adopt, invalid_grant reject, MaxAuthorityOutage, OnCheckSlidingExpiration for background/hub/telemetry), OIDC scheme options (PAR required, PKCE, claims kept, OnTokenValidated sub+sid), POST /bff/backchannel-logout validation, GET /bff/login, POST /bff/logout (LogoutResult, Clear-Site-Data, listener before delete), GET /bff/user, profile cookie v2, post-sign-in landing (OnTicketReceived) with BffLandingFailed, IAuthenticationPolicyProvider/LoginPolicy, StepUpChallenge.Write, SessionSweepService, MembershipReconcileService (applock leader), in-proc mode wiring (AddTellmaIdentity options, registry before engine, Distribution seed entry, host.identity check, engine endpoint conventions), launch-landing-vectors.json at client/projects/core/tellma-core/src/launch/ linked into Tellma.Core.Tests.
- **Tests:** unit (AspNetCore.Tests): back-channel token vectors (bad iss/aud/typ, missing events, nonce, expired, sub-only, replay 200 no-op), listener gets handles never keys, /bff/user shape and 401, login redirect and ui_locales, persistent cookie Expires == row ExpiresAt at sign-in and renewal, landing cases, logout per scope, profile cookie v2 re-issue and no renewal for background/hub/telemetry, in-proc connect/* reachable without cookie and engine form post not refused by CSRF, timers fire on interval (reconcile only on lock holder) with FakeTimeProvider; integration(SQL): session store CRUD with handle, older TokensVersion renewal writes nothing, two concurrent refreshers -> exactly one redeems, sweep deletes only rows expired for a day
- **Done when:** A host signs a user in through a fake or in-proc authority, persists the ticket in catalog.Sessions, refreshes once under concurrency, and ends the session on back-channel logout; all listed tests green on both OSes.

### PC-048 — List-page grid features: activation, driver, sort, select-all, row classes, gutter

- **Analysis id:** `0010-c3` · **Spec:** 0010-C · **Layer:** client · **Tier:** 1 · **Est. LOC:** 5,000
- **Sections:** §C.1, §C.2, §C.3, §C.5, §C.6, §C.7
- **Gated on (DEPS.md):** PC-045
- **Analyst dependency notes:** 0010-c2
- **Deliverables:** rowActivation='row' (readonly only) and rowActivate with TmGridRowActivation: the gesture table, the 400 ms double-activation guard, F2 actuating the first interactive child, printable keys left unhandled in readonly grids, links keeping the browser menu, the active row's tint and 2 px accent, and the aria-describedby hint. The activeRowId model, moveActiveRow, activateRow, controlsId, activeDescendantId and pageStep. TmGridDriverTarget and input[tmGridDriver] (ArrowUp/Down, PageUp/Down, Enter, aria-controls and aria-activedescendant). TmGridSort, orderBy and the sortable column: the asc/desc/null cycle, a fixed 14 px indicator slot, aria-sort, announcements. Select-all across pages with the checkedCountAcross announcement. rowClass with the --tm-grid-row-* properties and the muted and emphasis modifiers, plus rowHeaders=false. scrollbar-gutter stable with header and body bands. The first part of the grid-search-page story. The new token.
- **Tests:** unit (activation gestures and the guard; a link right-click and long-press left to the browser; printable keys unhandled; driver keys and ARIA; the sort cycle and aria-sort; select-all across pages; rowClass); Playwright (grid-search-page: driver input, activation, sort, cross-page selection over a paged source, no row headers, the native menu on a cell link; the gutter test launched with ignoreDefaultArgs ['--hide-scrollbars']; the active-row class in the press's frame; RTL variants); axe
- **Done when:** A readonly grid with rowActivation='none' behaves exactly as before, and all existing grid e2e suites pass. TmGridHarness gains activateRow, getActiveRowIndex, sortBy and getSort.

### PC-049 — Tenant settings: tables, keys, store, cold load, provisioning step, negotiation initializer

- **Analysis id:** `0013-f` · **Spec:** 0013 · **Layer:** backend · **Tier:** 1 · **Est. LOC:** 7,000
- **Sections:** §5.1-§5.7, §5.4 (SettingLabel fallback), §8.2 (core.CalendarMonths model + 2,196-row seed), §9.3, §1.2 (models, singletons, scoped façades, Order-200 initializer, settingKeys/settingCategories/languages/calendars metadata sections via SettingsClientAssetContributor), §1.3 setting-key checks, §11 settings events
- **Gated on (DEPS.md):** PC-046, PC-019, PC-044, PC-029, PC-025, PC-018, PC-037
- **Analyst dependency notes:** 0013-b; 0013-e; 0014-a (audit FKs to core.Users; core.Users HasData row 1); 0011 §4.1-§4.4 initializer chain and scope factory; 0011 §6.2 provisioning step runner (step class can be unit-tested before the runner exists); 0012 §2/§8 emitter (Save<Settings>, temporal history)
- **Deliverables:** Settings (sealed, single row, CK_Settings_*, HasData placeholder TenantId=0) and SettingEntry entities with temporal history, UDTTs and fixture migrations; core.CalendarMonths EF model + generated HasData migration (AH 1318-1500); SettingKeyRegistry (static-field discovery, grammar/uniqueness/type-set/default/group/References/Secret rules, categories in tab order, Describe()); Secret protection (IDataProtectionProvider protector purpose 'Tellma.Core.Settings.Secret' + tenant id; unprotect on first Get<T>; SettingsSecretUnreadable); TenantSettingsStore + TenantSettingsCache (settings kind; TenantSettings + pre-serialized TenantSettingsForClient bytes; GetForClientAsync); cold-load contributor at Order 60 (bare on loader batches; hands statements + @tm_LoadSettings to the 0014 prologue on marked batches); loader: tenant-id guard (TenantDatabaseMismatchException, placeholder accepted only in System scope), unresolvable value fallback (en/gc/UTC + Critical log), zone IANA->Windows conversion, entry parsing; SettingsProvisioningStep (core.settings, Order 15, Version 1, seeded Languages/Calendars); the Order-200 IRequestContextInitializer setting Language/Culture/CultureInfo/Calendar/CalendarSystem/TimeZone/TenantTimeZone/Today/ContentLanguageIndex/TenantSettings and CurrentCulture/CurrentUICulture; SettingsClientAssetContributor (four sections) 
- **Tests:** unit: setting-key rules as composition problems, stated vs derived group, category order, Get<T> defaults/invalid stored values/Validate codes, Secret protected/unprotected/other-purpose -> default and never in client view, client metadata sections for a composition with two categories, a grouped key, an enum key, DescriptionKey only when the resource exists, language entries with default region/regions/nativeDigits; integration(SQL): history rows for Settings/SettingEntries writes and none for tag bumps; TenantId guard fails closed; placeholder accepted only in System scope; provisioning completes the placeholder with seeded languages/calendars; Region/Digits round trip through cold load and both views; CalendarMonths seed equals UmAlQuraCalendar months, contiguous, non-overlapping
- **Done when:** A request scope (built through 0011's initializer chain with a fake connect initializer at 100) carries negotiated context and TenantSettings; warm path costs no settings round trip; prologue absorption and initializer re-run after SettingsStale are explicitly completed in 0014-d

### PC-050 — Connect prologue, caches, guarded runner and the Order-100 initializer

- **Analysis id:** `0014-d` · **Spec:** 0014 · **Layer:** backend · **Tier:** 1 · **Est. LOC:** 7,500
- **Sections:** §6.2-§6.3 (permissions VersionedCache kind, profile kind, connect cache, PermissionsMaxAge reload), §7.1-§7.6, §7.8, §2.10 of 0013 (round trips), 0013 §5.7 absorption and §9.3 re-run, §13 (connect instruments and events)
- **Gated on (DEPS.md):** PC-043, PC-029, PC-038, PC-044, PC-049, PC-025
- **Analyst dependency notes:** 0014-a; 0014-c; 0013-d; 0013-e; 0013-f; 0012 §5.3-§5.5 executor (prologue stage, IF @tm_Guard wrapper, BatchOutcome, result-set accounting); 0011 §4.3-§4.4 (tenant endpoint filter/initializer chain, scope factory, TenantState)
- **Deliverables:** UserProfile, ConnectedUser, ConnectPremises (ForSubject/ForUser/ForSystem, Cold, For), ConnectExpectations, PermissionRow, ConnectResult; the byte-identical prologue texts for the three variants with @tm_ parameters and the reader contract (sets 0-3, absorbed settings sets, body sets iff GuardPassed); IUserConnector (ConnectAsync single-flighted per (TenantId, Subject), ConnectAsUserAsync, ConnectAsSystemAsync guarded by Kind=System, Contribute marking the batch so 0013's Order 50/60 contributors yield, Apply replacing connect/permissions/profile entries, Set(aged) after PermissionsMaxAge); IGuardedBatchRunner (connect, compose, execute, MarkValidated on pass, apply + 0013 initializer re-run + one recompose on fail, 50412 -> cold re-connect + one recompose, 50401 -> TenantNotFoundException, second failure -> StaleContextException + GuardThrash); ConnectInitializer at Order 100 (Frozen premises, NoActivityStampMetadata, Tellma-Activity: background, throttle memo; job scopes ForUser; System ForSystem; Anonymous -> TenantNotFoundException); 0013 settings-load absorption and Order-200 re-run wiring; the persist re-check 50401 statement handed to the executor
- **Tests:** unit: prologue reader against scripted result sets (every presence combination of sets 2-3, settings sets, body); ForSystem text binding @tm_UserId=1 and failing only on strict stale settings; runner recompose/thrash logic with fakes; integration(SQL): prologue end to end — cold, warm, stale permissions (tenant tag), stale permissions (user tag), stale settings served on read and refused on validate with rows in the same round trip, stale profile, unknown subject, deactivated, ForUser and ForSystem; join flip exactly once under two concurrent first requests; service account resolved by client id; throttled stamp writes once under 1,000 requests and UsersHistory gains zero rows; background request stamps nothing; persist re-checks (role save committed between the two round trips forces exactly one recompose; deactivation between them -> 50401); System-scope persist with no permissions expectation and no caller re-check; Frozen (READ_ONLY DB) reads with no prologue write, ReadOnly stamps and flips; two tenant DBs sharing ids never share cache entries; the 0013 runner-dependent integration cases (guard 50412 re-run once, Rerun/Refresh per §2.6)
- **Done when:** Round-trip budgets of 0014 §7.5 rows Query/Details/Save/create hold on the fixture; every connect result counted by tellma.access.connect.results; prologue duration histogram tagged by variant

### PC-051 — Header navigation, header actions, row-end column

- **Analysis id:** `0010-c4` · **Spec:** 0010-C · **Layer:** client · **Tier:** 1 · **Est. LOC:** 4,000
- **Sections:** §C.4, §C.14
- **Gated on (DEPS.md):** PC-048, PC-011
- **Analyst dependency notes:** 0010-c3; 0010-d1
- **Deliverables:** ng-template[tmGridHeaderEnd] with TmGridHeaderEndContext, and button[tmGridHeaderAction] (24×24, active state). Header navigation turns on when a column is sortable, has a header action, or a row-end header exists: ArrowUp from the first row, Left/Right/Home/End, Enter/Space, Alt+ArrowDown opening the action, Tab leaving. A roving tabindex with the headerNavHint and headerActionHint strings, and focus inside an action counting as header focus. The tmGridRowEndHeader and tmGridRowEnd templates: a sticky inline-end column of --grid-row-end-width, painting row bands, with a hairline, kept outside selection, copy, find, resize and the state store, and kept when the loading or empty templates show. The grid-search-page story completed with header filter popovers and the column-editor button. Harness openHeaderAction and openRowEndAction.
- **Tests:** unit (header navigation, including Enter on an action-only header; the row-end column outside selection, copy, find and column select; an empty body cell activating its row); Playwright (header actions with popovers; the row-end column at the inline end in LTR and RTL while scrolling sideways; the header kept with the empty template; slot geometry stable when active toggles); axe with the popover open
- **Done when:** Grids without sortable columns or header actions keep today's header behaviour and tab order. The row-end column never joins a range, copy or find.

### PC-052 — IAccessEvaluator, deny re-check, witness, EvaluateFor and the holders resolver

- **Analysis id:** `0014-e` · **Spec:** 0014 · **Layer:** backend · **Tier:** 1 · **Est. LOC:** 4,500
- **Sections:** §5.1 (IAccessEvaluator, IAccessHoldersResolver, AccessHolders, AccessHolder), §4.6 layer 4 (witness recording), §6.4, §5.3 (semantics consumed by 0015), §10 (me AccessSummary computation via EvaluateAllAsync, access/check semantics, access/holders semantics, IAdministratorDirectory), §13 (decisions counter, witness counter, MissingWitness/FilterTooComplex events)
- **Gated on (DEPS.md):** PC-050
- **Later seams (not gates):** PC-062 (0015-a). Ship the seam; the later checkpoint fills it.
- **Analyst dependency notes:** 0014-d; 0015 §14.1 exception types (ForbiddenException, StepUpRequiredException) or their declaration early
- **Deliverables:** AccessEvaluator (scoped): EvaluateAsync/RequireAsync (ForbiddenException NoGrant, StepUpRequiredException for sensitive pairs below 0011's bar), batch EvaluateAsync, EvaluateForAsync (one memoised ForUser load per scope, deactivated target -> Denied), EvaluateAllAsync, GetAccessAsync, Invalidate; criteria providers read per evaluation; owner recursion; FastDenyWindow re-check (one prologue-only round trip, once per scope, never inside EvaluateAllAsync); per-request witness record; AccessHoldersResolver (one Read round trip: matching permissions of active roles, current members via CurrentMembershipSql, public-role audience count, Related projections; per-user Compose; MaxHolders cap); AdministratorDirectory (active Individual Invited/Joined with a current role-1 membership, ordered, capped at 20)
- **Tests:** unit: deny re-check inside/outside FastDenyWindow, at most once per scope, never inside EvaluateAllAsync, passing prologue advances ValidatedAt; RequireAsync on Denied raises ForbiddenException with the pair and NoGrant; holders conformance: for every securable of a seeded set, listed users equal those whose Decide holds a role grant; integration(SQL): IAdministratorDirectory and holders skip an ended and a future membership; EvaluateFor shares one load per scope and never feeds the response header
- **Done when:** All instruments of §13 for decisions emitted and asserted via MetricCollector; witness is recorded for every Evaluate/Require; the HTTP witness filter itself is wired with 0016
- **Planner note:** Also ship the IAccessGuards contract (0014 §8.3 names) with a no-op default registration. 0015-g calls it and 0014-f replaces the registration with the real guards.

### PC-053 — Tellma.Core.Imaging: SkiaSharp processor and PDF thumbnails

- **Analysis id:** `0017-b` · **Spec:** 0017 · **Layer:** backend · **Tier:** 1 · **Est. LOC:** 3,000
- **Sections:** §1.1 (Imaging row, native isolation, pins), §1.3 (AddTellmaSkia), §3.5 (pixel cap, decode memory, thumbnail timeout), §3.6, §10.1 image instruments, §10.2 BlobImageRender, §11 Imaging row
- **Gated on (DEPS.md):** PC-017
- **Analyst dependency notes:** 0017-a
- **Deliverables:** src/core/Tellma.Core.Imaging (SkiaImageProcessor: SKCodec identification, pre-decode pixel cap, scaled decode for JPEG/WebP, frame 0, EncodedOrigin transform, Contain/CoverSquare, re-encode WebP/Jpeg/Png without metadata, alpha preserved; ThumbnailAsync for images and PDF page 1 through PDFtoImage behind one process-wide lock, timeout → null; ImagingComposition.AddTellmaSkia), README, package pins; test/core/Tellma.Core.Imaging.Tests with committed or generated fixtures
- **Tests:** unit over fixtures: each accepted format; Contain/CoverSquare dimensions; thumbnail sizes; EXIF orientation applied and metadata absent; a 60-megapixel header refused before decode; JPEG/HTML polyglot re-encoded with no '<' in the first 8 KiB; animated GIF reduced to one frame; content type per ImageFormat; Signature preset keeps alpha; PDF page 1 at thumbnail size; encrypted PDF and OOXML yield null
- **Done when:** Imaging package references only Abstractions + Skia + PDFtoImage; the suite passes on the Windows and Linux runners (native assets resolve, no fontconfig dependency on Linux); Tellma.Core does not reference it.

### PC-054 — scrollToRow and remembered tree expansion

- **Analysis id:** `0010-c5` · **Spec:** 0010-C · **Layer:** client · **Tier:** 1 · **Est. LOC:** 3,000
- **Sections:** §C.8, §C.9
- **Gated on (DEPS.md):** PC-051, PC-048
- **Analyst dependency notes:** 0010-c3
- **Deliverables:** scrollToRow(rowId, {flash, activate, align, column}): ancestors expanded first, never moving focus, a flash keyed by row id that respects reduced motion and forced colors, with the --grid-flash-bg and --grid-flash-duration tokens. TmTreeGrid gains expansionSource ('store' | 'model') and the expandedIds model, kept unpruned with unknown ids retained, seeded through TmGridEngine.restoreExpansion. loadChildren becomes batch-shaped (rows => Promise), batched at content bind or restore or a consumer write, after each batch level by level, and for same-task user expands coalesced in a microtask. A failed batch removes its ids and announces once. Sibling order follows data order. The tree-grid examples and specs migrate to the new signature. The tree-grid expansion-restore story. Harness isRowFlashing and getExpandedIds.
- **Tests:** unit (scrollToRow with column and flash; expansionSource in both modes; the bound set kept unpruned across batches; a consumer write after bind not echoed; batching moments and sibling order); Playwright (tree expansion restore; flash under reduced motion)
- **Done when:** The breaking loadChildren change is recorded in the tree-grid golden and has no other consumer. A 500-id restore issues one batch per level.

### PC-055 — Identity-server client

- **Analysis id:** `0018-a` · **Spec:** 0018 · **Layer:** backend · **Tier:** 1 · **Est. LOC:** 2,500
- **Sections:** §2.1-§2.3, §8.2 (identity instruments, IdentityCall log, identity.invite activities)
- **Gated on (DEPS.md):** PC-047, PC-001
- **Later seams (not gates):** PC-062 (0015-a). Ship the seam; the later checkpoint fills it.
- **Analyst dependency notes:** 0011 §5.7; 0011 §2.1; 0014 (InviteStatus, Gender); 0015 §14.1
- **Deliverables:** Abstractions.Identity: IIdentityServerClient, IdentityInvitation, IdentityInvitationResult, IdentityDeliveryStatus, IdentityDeliveryState, IdentityServiceAccount; Tellma.Core.Identity: IdentityServerClient (named HttpClient 'Tellma.Identity', 30 s timeout, v1 routes, source-generated JSON, client_credentials token with scope tellma_identity cached until 60 s before expiry under single-flight, one 401 refresh+retry, 1,000-item chunking with input order and prefix-on-failure/cancel, failure classification DependencyUnavailable vs InvalidOperation), IdentityServerClientOptions binding from Tellma:Identity with ClientId '<slug>-svc' set after binding and https-outside-Development validation, CoreFeature registration, tellma.identity.* instruments
- **Tests:** unit against a fake HttpMessageHandler: chunking/order; token cache, 60 s margin, single flight (FakeTimeProvider); one 401 retry; second 401 and token-endpoint 4xx → InvalidOperationException; prefix on a failed chunk and on cancellation; DependencyUnavailable before any chunk on transport/5xx; ExistingOnly serialised; options validation; secrets never logged
- **Done when:** Client is resolvable from both the web and migrator compositions. All §2 behaviours are pinned by unit tests and the telemetry tags are asserted.

### PC-056 — Popup cell editors and final grid ceilings

- **Analysis id:** `0010-c6` · **Spec:** 0010-C · **Layer:** client · **Tier:** 1 · **Est. LOC:** 3,500
- **Sections:** §C.15, §C.11
- **Gated on (DEPS.md):** PC-054, PC-048, PC-011
- **Analyst dependency notes:** 0010-c3; 0010-d1
- **Deliverables:** TmGridColumn.editorPopup with TmGridEditorPopup (minWidth 320, maxHeight 360). The editor renders in a §D.1 anchored pane hosted by the gridcell (popoverHost parent), covering the cell and growing toward the block end. It flips upward at the viewport edge, aligns to inline start and is pushed into the viewport. It always opens in edit mode, with Tab/Shift+Tab at its edges, Enter (unless consumed), Mod+Enter, staged Escape, a read-only open via F2, the error popover suppressed while open, one undo step per commit, and hide and restore when its cell scrolls out. The grid-popup-editor story. Harness getEditorPopup. ./grid ceiling at 43 with budgetNotes.
- **Tests:** unit (edit mode under a typed seed; Tab inside and out of a two-control pane; Enter consumed and committing; Mod+Enter; Escape stages; the read-only open; one undo step); Playwright @cross-engine (the pane spilling past the grid's bottom; the upward flip; RTL inline-start alignment; focus inside the gridcell with axe; a press outside committing; hide and restore on scroll)
- **Done when:** A null editorPopup leaves today's in-cell editor unchanged. The ./grid ≤43 and ./menu ≤7 gates are green, with notes recorded.

### PC-057 — Two-line options, page context, grid cell options, picker glyphs

- **Analysis id:** `0010-e1` · **Spec:** 0010-E · **Layer:** client · **Tier:** 1 · **Est. LOC:** 2,500
- **Sections:** §E.1, §E.3, §E.4, §E.5
- **Gated on (DEPS.md):** PC-004, PC-011, PC-048
- **Analyst dependency notes:** 0023 §3.1 (icon); 0010-d1; 0010-c3
- **Deliverables:** itemDescription and itemLabelParts on TmEntityPicker. All result rows share the two-line height, footer rows stay one line, each line sits in <bdi>, parts are joined with ' - ', and the accessible name is 'label, description'. TmEntityPickerPage object form gains context, and TmEntityPickerPageData gains context, multiple, ids and maxItems (with Done disabled above the cap). TmGridColumn gains itemDescription, itemLabelParts and formatParts, and the cell editor passes them through. The three glyphs through tm-icon. The new token, golden updates and story rows.
- **Tests:** unit (the fixed row height with and without descriptions; the accessible name; each label part in its own <bdi>; context reaching TM_MODAL_DATA for advanced search, create and edit; the grid column passing functions to its picker); Playwright (two-line rows in a field and in a cell at fixed height; a Latin code beside an Arabic name staying code-first in LTR and RTL; glyph geometry); axe
- **Done when:** An unbound picker renders exactly as today, and the ./entity-picker and ./grid goldens are updated.

### PC-058 — tm-entity-multi-picker

- **Analysis id:** `0010-e2` · **Spec:** 0010-E · **Layer:** client · **Tier:** 1 · **Est. LOC:** 4,000
- **Sections:** §E.2
- **Gated on (DEPS.md):** PC-057, PC-020, PC-023
- **Analyst dependency notes:** 0010-e1; 0023 §4.1 (badge); 0023 §4.3 (chip)
- **Deliverables:** TmEntityMultiPicker: value in pick order with no duplicates; picked and removed outputs; maxItems; chip labels through the displayWith, memo and String(id) ladder. The inner picker in emit mode: value stays null, the settled answer is retired, the panel stays open after a list or Enter commit and closes after a blur, Tab or create commit. A list pick or Enter toggles a chosen row off, while auto-resolution and Tab on a chosen id keep its chip. Advanced search opens with {multiple, ids, maxItems}, and the returned TmEntityMultiPick replaces the value and emits the difference. Chips in tm-chip-set scroll mode with a fixed-width count badge, at a one-line fixed height. Keyboard paths (Backspace twice, chip arrows, exits), the hidden selectedSummary, polite announcements. The harness and the story with an advanced search that adds and removes. ./entity-picker ceiling 14 with a note.
- **Tests:** unit (the single picker's suite re-run in emit mode; toggle-remove by pointer and by Enter; auto-resolution and Tab on a chosen id; panel open and closed after each commit kind; label parts reaching inner rows; maxItems; advanced search with add, remove and empty sets); Playwright (chip keyboard paths in LTR and RTL; field height identical with 0, 1 and 30 chips; a chip shown in the same frame as the pick); axe with chips and an open panel
- **Done when:** The existing single-picker suite passes unchanged without ɵcommitMode, and an app importing only TmEntityPicker tree-shakes the multi-picker.
- **Planner note:** Promoted to Tier 1: 0029-d's navigation filters need tm-entity-multi-picker.

### PC-059 — Client-cache sweep, blob cancellation, basics glyphs

- **Analysis id:** `0010-d3` · **Spec:** 0010-D · **Layer:** client · **Tier:** 1 · **Est. LOC:** 3,000
- **Sections:** §D.5, §D.6, §D.11
- **Gated on (DEPS.md):** PC-013, PC-011, PC-004
- **Analyst dependency notes:** 0010-d1; 0023 §3.1 (icon)
- **Deliverables:** TmClientCache.clearAll({keep}) over CacheStorage tm-*, IndexedDB tm-* (via indexedDB.databases, the area skipped when absent; blocked deletes resolve), localStorage tm.* and this tab's sessionStorage tm.*, never throwing, never touching ngsw: caches, and with every tm-* database opener closing on versionchange. TmBlobFetchOptions.signal: tm-image aborts on destroy, src or etag change and leaving the defer viewport; ref-counted coalescing in the blob cache with an AbortController per URL; revalidation aborted with the last instance; no cache write or error placeholder after an abort; the default HttpClient fetcher unsubscribes and rejects with signal.reason. Glyphs move to tm-icon in alert, date-picker (popup chevrons with mirrorInRtl), file-preview, files, image (the edit pane check at stroke 2.25), modal and private. The button and input docs examples use <tm-icon [svg]>.
- **Tests:** unit in browser mode (each area deleted; keep per area; a blocked IndexedDB delete resolving; missing indexedDB.databases; ngsw: untouched; abort on destroy; two instances aborting only when both go; revalidation aborted; no cache write after abort); Playwright (aborted image requests counted on navigation; glyph geometry in LTR/RTL; popup chevrons after a live flip)
- **Done when:** clearAll keeps the old behaviour for its one caller (the image story). ./image stays ≤10, and ./alert and ./image are re-measured with notes.

### PC-060 — tm-multi-select, option groups, foundation glyphs through tm-icon

- **Analysis id:** `0010-b2` · **Spec:** 0010-B · **Layer:** client · **Tier:** 1 · **Est. LOC:** 4,000
- **Sections:** §B.4, §B.7
- **Gated on (DEPS.md):** PC-008, PC-004
- **Analyst dependency notes:** 0010-b1; 0023 §3.1 (icon)
- **Deliverables:** TmMultiSelect: ngListbox multi, aria-multiselectable, toggle-and-stay-open, a one-line Intl.ListFormat trigger, unmatched values kept. TmOptionGroup in tm-select and tm-multi-select: role=group, a heading row at option height, navigation skipping headings, one nesting level. TmMultiSelectHarness and TmSelectHarness.getGroups. checkbox, select and form-field glyphs (check, minus, chevron-down, circle-alert) through <tm-icon [svg]>, keeping box and stroke. Showcase stories, strings with Arabic twins, the ./select golden, and the ./select ceiling at 9.
- **Tests:** unit (value in option order; toggle keeps the panel open; unmatched values; navigation skips headings); Playwright plus axe (both selects with groups in LTR/RTL and light/dark; glyph geometry in LTR/RTL; tm-checkbox against a grid checkbox cell pixel for pixel, where the grid masks already use the same lucide paths at stroke 1.75); gates (goldens, size:check)
- **Done when:** Both selects render groups accessibly, and a tm-select without groups renders byte-identical to before. All three foundation entry points draw through tm-icon, with ./checkbox and ./form-field re-measured and their notes recorded.
- **Planner note:** Promoted to Tier 1: tm-multi-select and option groups are used by 0029-c/d, 0030-d, 0032-d/f.

### PC-061 — Tiles

- **Analysis id:** `0023-g` · **Spec:** 0023 · **Layer:** client · **Tier:** 1 · **Est. LOC:** 4,500
- **Sections:** §8.5, §2.1 (tiles rows), §9 (tiles), §10 (tiles keys), §12
- **Gated on (DEPS.md):** PC-031, PC-020, PC-041, PC-048, PC-054
- **Analyst dependency notes:** 0023-b; 0010 §C.1; 0010 §C.2; 0010 §C.9
- **Deliverables:** Entry point `tiles`: TmTiles implementing TmGridDriverTarget through `import type`, the lattice column formula, virtualization through tmComputeAxisWindow (70 px pitch, 3 lines overscan, active row always rendered), flat role=grid and tree-mode treegrid, selection with a reserved checkbox and announcements, activation with newTab and the 400 ms repeat guard, batched loadChildren, flash, skeleton loading, empty template; TmTilesHarness and TmTileHarness.
- **Tests:** Unit: the lattice (columns per width, line windows, moves on a short last line). Harness: selection, external activeRowId moves, tree-mode batched loadChildren. E2E: a tap activates and a checkbox tap selects (Pixel 7); RTL tree mode; scrolling 10,000 rows with no long task over 50 ms (perf, see slow tests).
- **Done when:** The entry point passes every gate within its 7 KB budget. `<input [tmGridDriver]="tiles">` moves the active tile in the showcase.
- **Planner note:** Promoted to Tier 1: tm-tiles is used by 0030-d and 0030-g.

## Phase 4 CRUD pipeline and web API

### PC-062 — Pipeline contracts: Crud, Validation, Errors namespaces and API golden

- **Analysis id:** `0015-a` · **Spec:** 0015 · **Layer:** backend · **Tier:** 1 · **Est. LOC:** 4,500
- **Sections:** §1.1, §2.2-§2.3 (records/annotations only), §3.1-§3.2 (EntityService base, IEntityPipeline, IEntityBehavior, IEntityPipelineFactory, components, extensions), §4.1-§4.2, §5.2 interfaces (IQueryTraversalRule, IChildQueryService), §5.3 DetailsPlan/RecordPermissions, §7.1-§7.3, §7.5 IConfirmations, §11.2 [EntityRef], §11.3 IApiActionInvoker/IApiActionInterceptor, §13.1 PersistContext/Outcome kinds, §13.3 enlistment contracts, §14.1, §16.1 CrudTelemetryNames, §17
- **Gated on (DEPS.md):** PC-015, PC-018, PC-033, PC-001, PC-016, PC-039, PC-028, PC-035, PC-043
- **Analyst dependency notes:** 0016-a; 0012 §2-§3 contracts; 0012 §5.2 IDataBatch contract; 0011 §2.1/§4.1 contracts; 0013 §2.2 snapshot types; 0014 §4.1/§5.1 contracts
- **Deliverables:** Tellma.Core.Abstractions.Crud (StackDescriptor, ActionDescriptor/EntityActionDescriptor/ApiActionDescriptor, ChildCollectionDescriptor, ChildQueryDescriptor, StackLimits, StackOperations, ApiServiceDescriptor, EnlistmentDescriptor, IStackRegistry, StackAttribute, EntityActionAttribute, ApiActionAttribute, DetailsExtrasAttribute, ApiRouteAttribute, EntityRefAttribute, EntityService<,>/EntityService<> forwarding bodies, EntityServiceExtensions, IEntityValidator/IPersistEffect/IDetailsContributor/INamedFilterContributor with default interface methods, DetailsRequest, SaveOptions, SaveSource, ActionOptions, DetailsPlan<,>, RecordPermissions<TKey>, PersistContext kinds, PersistOutcome kinds, IEnlists<T>, WriteHostKind, IWriteHost, IOpenWriteHost, EnlistSaveOptions/EnlistDeleteOptions/EnlistedSave/EnlistedDelete/EnlistmentExtensions signatures, EnlistmentOutcome, IApiActionInvoker, IApiActionInterceptor, IChildQueryService, IQueryTraversalRule, TraversalRefusal, CrudTelemetryNames); .Validation (SaveContext, DeleteContext(<,>), ActionContext(<,>), IContextLoader, ContextRef<T>, ValidationPath + segments, ValidationErrors(<T>) with expression-based Add, ValidationCodes, IConfirmations); .Errors (TellmaException, CodedError, ValidationError, the 12 sealed exceptions, ForbiddenReason, ConcurrencyConflict, ConfirmationRequest, LimitNames); the 0016 wire records that need 0012/0014 types if 0016-a could not hold them (EntitiesResult, SaveRequest, AccessCheckRequest); unit tests for ValidationPath/ValidationErrors<T>; a C# public-API golden test over the three namespaces
- **Tests:** unit (ValidationPath rendering, ValidationErrors<T> member-name inference, exception argument shapes, LimitNames), golden (public API of .Crud/.Validation/.Errors)
- **Done when:** Abstractions builds EF-free and ASP.NET-free (a test asserts no EF/SqlClient/AspNetCore type in the three namespaces); API golden file checked in and green; every public member XML-documented; Tellma.slnx builds on Windows and Linux with warnings-as-errors
- **Planner note:** Its contract-first part is already in the contract seed (PC-015); extend it, do not re-declare. Also create, contract-first and under the owners' exact names: Tellma.Core.Abstractions.Excel ExportSource (+ cases), ExcelQuery, ExcelPage and IExcelRowSource (0019 §2.1/§4.1), and Tellma.Core.Abstractions.Assignments BatchAssignments, IAssignments and AssignmentRequest (0028 §3.1). PersistContext/ActionContext.Assignments exists, and calling it throws NotSupportedException until 0028-a registers IAssignments. 0019-a and 0028-a list these as already seeded. Also create, contract-first under the owners' exact names and namespaces, 0017's BlobRejectedException (0017 §1.1) and 0019's ImportException, so 0016-d maps them by type; 0017-c1 and 0019-c/d extend them and list them as already seeded. (Kickoff audit, 2026-11-08.)

### PC-063 — Stack registry, descriptor derivation, realizers, startup checks, securable contributor

- **Analysis id:** `0015-b` · **Spec:** 0015 · **Layer:** backend · **Tier:** 1 · **Est. LOC:** 5,500
- **Sections:** §2.1-§2.7, §3.2 (component closing over the leaf), §3.3, §7.5 (Confirmation item realizer + check), §12 (capability vocabulary and projections into the descriptor), §18.1 descriptor/startup-check/component pins
- **Gated on (DEPS.md):** PC-062, PC-001, PC-021, PC-039
- **Analyst dependency notes:** 0015-a; 0011 §2.1-§2.3; 0012 §3.1-§3.2; 0013 §4.1 [Cacheable], §10.1; 0014 §4.1-§4.5
- **Deliverables:** Tellma.Core.Crud: StackDescriptorBuilder (Resource, ResourceSegment kebab-plural, Operations, Capabilities closed vocabulary, Actions in declared order incl. built-in activate/deactivate and companion [ApiAction]s, Properties/Children/MaxCount, Searchable/NaturalKeys, DefaultSelect/RelatedSelect/DetailsExpand/DefaultOrderBy, Extras incl. permissions, NamedFilters built from root provider, Limits, Mcp/ContractRevision/Description, Feature), StackRegistry (Find, FindChildQuery, OwnerOf(TableName), ChildQueries at any depth with Queryable opt-out and owner chain, Services, Enlistments graph), realizers for StackContributionItem / StackCompanionContributionItem / ApiServiceContributionItem / ValidatorContributionItem kinds / ConfirmationContributionItem (closing generic components and companions over the substituted leaf, open-definition registration), every IStartupCheck of §2.6 (incl. core.enlistments acyclicity with self-pair exemption, pipeline-substitution check, projection-text Validate against the tenant-independent schema), StackSecurableContributor (§2.7 filter roots, child owner rule, Feature stamping); unit tests with one failing fixture per startup check
- **Tests:** unit (descriptor derivation for every fixture entity incl. Long/Idempotent; child surfaces; Extras/NamedFilters with no tenant scope; each §2.6 check with a failing fixture; components generic over a base run closed over a substituted leaf; non-generic component on substituted entity fails; export action name on a stack without Export passes)
- **Done when:** A host composing the fixture entities builds an IStackRegistry whose descriptors match goldens; every §2.6 rule fails startup naming the entity/service/method; StackSecurableContributor output passes 0014's securables audit; child-surface Actions empty pending 0019 (seam documented)

### PC-064 — Row-set and related converters, key-typed entity envelope, route model and fingerprints

- **Analysis id:** `0016-b` · **Spec:** 0016 · **Layer:** backend · **Tier:** 1 · **Est. LOC:** 3,500
- **Sections:** §3.2 wire ids, §3.5, §3.6 (related/extras/ids shape), §3.10 (IRouteContracts, RouteContract, fingerprint, ContractsClientAssetContributor)
- **Gated on (DEPS.md):** PC-015, PC-063, PC-021
- **Later seams (not gates):** PC-094 (0024-f). Ship the seam; the later checkpoint fills it.
- **Analyst dependency notes:** 0016-a; 0015-b; 0012 §3.3; 0024 §3.2 IClientAssetContributor (contract)
- **Deliverables:** QueryRowSet/RowPage envelope converter (columns always present, type name without Qx, scale omitted when null, row-major via Utf8JsonWriter switching per column buffer, count/countCapped from capped Count, ancestors), RelatedEntities converter (entity name → array, one JsonTypeInfo per type), EntitiesResult<TEntity> converter writing ids/permissions ids in the stack's key type, problem-body id display rule helper; Tellma.Core.Api: IRouteContracts derived from IStackRegistry (stack standard ops by Operations/Capabilities, every Action, ApiRoute services, child surfaces) with IsMutation/AllowWhenReadOnly/Long/Idempotent and the 12-char base64url SHA-256 fingerprint over a canonical transitive JSON-contract description + ContractRevision; ContractsClientAssetContributor (fingerprints, long, idempotent sections)
- **Tests:** unit (row-set JSON goldens incl. integer column scale 0 and Sum scale; int id number vs long id string in ids, row cells and permissions extra; conflict id number below 2^53 and string above; fingerprint stability and a ContractRevision bump changing exactly the covered endpoints; Contracts chunk long/idempotent lists)
- **Done when:** IRouteContracts is the single route model; fingerprints deterministic across Windows/Linux; converters registered in TellmaApiJsonContext

### PC-065 — Repo-root workspace, package skeletons, lint presets and decimals

- **Analysis id:** `0024-a` · **Spec:** 0024 · **Layer:** both · **Tier:** 1 · **Est. LOC:** 4,000
- **Sections:** §1 (packages, entry-point table, import rules), §2.1 (eslint rules list), §10.4 (plain JSON, TmDecimals, lint), §12 (lint gate)
- **Gated on (DEPS.md):** PC-008
- **Analyst dependency notes:** none
- **Deliverables:** Repo-root pnpm-workspace.yaml with a catalog; client scripts adapted. New ng-packagr library client/projects/core/tellma-core (primary, `/private`, and empty slots only for entry points whose owners ship). New library tellma-core-testing (primary plus a Node `/e2e` entry with optional peer @playwright/test). New Node ESM package tellma-core-tools with a `tellma-client` bin stub and presets `/eslint`, `/tsconfig.json`, `/stylelint` (the latter re-exporting 0010 §B.1's breakpoint rule). ESLint: the @tellma/core import-boundary rule with cycle refusal, tm/no-raw-http, tm/no-web-storage, tm/lazy-layouts, tm/literal-declaration-keys, type-aware tm/no-decimal-operators, and restrict-plus-operands. TmJson/TmDecimal/TmId types and TmDecimals. Seam tokens for the later specs (TmTelemetry, TmRequestLog, page scope) with no-op defaults. API goldens for the new packages. Packages build on Ubuntu and Windows.
- **Tests:** Unit: TmDecimals (parse 1e3, -0.0, 30 digits; add/subtract/compare at 28 digits; toNumber; fromNumber); JSON round trip of '9007199254740993'. lint:test vectors for every new rule, including tm/no-decimal-operators refusing +, +=, <, <=, >, >= on TmDecimal and passing on numbers and plain strings.
- **Done when:** `pnpm install` at the repo root resolves one Angular. Every existing client build, test, e2e and gate passes from the new root. The three new packages build with goldens. The boundary rule refuses an eager→lazy import in a fixture.

### PC-066 — Validation engine: context loader, parking scheduler, confirmations, attribute walker

- **Analysis id:** `0015-c` · **Spec:** 0015 · **Layer:** backend · **Tier:** 1 · **Est. LOC:** 4,000
- **Sections:** §6.1 step 1 (shape, ceilings, metadata MaxLength/Precision checks, BCL ValidationAttribute walker incl. children), §7.2, §7.3 (re-homing basics), §7.5 (IConfirmations runtime, ordering rule, System kind), §18.1 scheduler/walker/confirmation pins
- **Gated on (DEPS.md):** PC-063, PC-062, PC-033
- **Analyst dependency notes:** 0015-a; 0012 §5.2 IDataBatch, §11.4 entity queries; 0008 CompileQuery (+0010 §F.1)
- **Deliverables:** ContextLoader (ByIds/ByKey/ByParentIds as model-emitted TVP-restricted Query<T>, Query/Visible/Rows/Exists/Count compiled through the engine, Ancestors, Sql with Writes=[], Prime; structural-key dedup, TVP key union, select union, not-found caching), ValidationScheduler (concurrent validator tasks, park at LoadAsync, one dispatch per round, late registration into a running round, MaxValidationRounds InvalidOperationException + metric), Confirmations (scope-seeded, Require with declared-key check, all keys confirmed under Kind=System), the cached-metadata attribute walker reporting all failures with structured paths; a scripted IDataBatch double (records appended statements, replays canned result sets) reused by later checkpoints
- **Tests:** unit (dedup and select union, one dispatch per round, round-limit bug, property test that validators awaiting foreign tasks terminate and never dispatch early, walker MaxLength/Precision/never-rounded/IValidatableObject ignored, confirmation ordering primitives)
- **Done when:** Scheduler property test green with a bounded iteration count (seconds); scripted batch double available to Crud unit tests; no SQL executed in this checkpoint's tests

### PC-067 — Declaration API, metadata contract and the config check

- **Analysis id:** `0024-b` · **Spec:** 0024 · **Layer:** both · **Tier:** 1 · **Est. LOC:** 4,000
- **Sections:** §2.2 (types, tmFeature, tmOverride, TM_APP_DECLARATIONS), §2.3, §2.4, §2.5, §3.1, §3.3 (loaders, TM_CLIENT_METADATA), §3.4 (types), §3.5, §3.6 (tmCheckAppConfig, tmAppConfigOf), Appendix A (config codes)
- **Gated on (DEPS.md):** PC-065
- **Analyst dependency notes:** 0024-a
- **Deliverables:** TmAppConfig, TmFeatureDefinition, tmFeature, tmOverride and tmPage (path grammar). The merge rule (order, keys, overrides member by member, coded-report patches, hide kinds, home/appBar rules) builds TM_APP_DECLARATIONS. TmClientMetadata/TmEntitiesChunk/TmContracts types and the `tmMetadataLoaders` runtime. tmCheckAppConfig with every Appendix A config code, using interim minimal types for 0027/0029/0032/0034/0035 declarations. tmTestMetadata() and test-metadata fixtures in core-testing.
- **Tests:** Unit: every Appendix A config code; merge order; arrays replaced; each hide kind; tmOverride; two features patching one coded report; an unpatched key present in TM_APP_DECLARATIONS.reports; tmPage path grammar.
- **Done when:** All config codes are pinned by unit tests. The config is a plain object whose lazy members are never invoked. API goldens are approved.

### PC-068 — Pipeline skeleton and the query path (stacks and child surfaces)

- **Analysis id:** `0015-d` · **Spec:** 0015 · **Layer:** backend · **Tier:** 1 · **Est. LOC:** 5,500
- **Sections:** §3.1 factory binding, §4.3, §5.1 (runner usage, DataAccessScope Operation, Read batches), §5.2 incl. §5.2.1 search, traversal rule and select-type rule, §9.1, §2.5 ceilings for query, §14.1 cancellation semantics for reads, §14.2 ArithmeticFailure row, §16.1 operations/duration/discover instruments
- **Gated on (DEPS.md):** PC-066, PC-063, PC-003, PC-043, PC-046, PC-050, PC-052
- **Analyst dependency notes:** 0015-b; 0015-c; 0010 §F.1, §F.4, §F.6, §F.7, §F.8, §F.21; 0014 §5, §7; 0013 §7.4
- **Deliverables:** EntityPipelineFactory + sealed EntityPipeline<,> skeleton (operation scope, telemetry, outcome classification, OperationCanceled → cancelled), QueryPlanner (filter composition And(user leaves, named filters, activatable conjunct, search, access) with per-leaf clause names filters[i]/namedFilters[i], PipelineLimits profile, IDataBatch.Limits, DefaultOrderBy/'Id desc', CountCap, IncludeAncestors via RowQueryOptions with AncestorsFilter), argument discovery cache keyed by (schema fingerprint, clause texts) with JSON→type conversion and explicit nulls, search lowering (bidi strip, Contains/Prefix, id/#id branch, display-name branches incl. ' - ' split and Arabic-Indic digits, SearchFilter override), QueryTraversalRule (RelatedSelect projection under filtered/absent grant, owner path from child root, Geography select refusal), ChildQueryService.QueryAsync; unit tests over the scripted batch; first integration tests for query
- **Tests:** unit (search per SearchKind and language shape, final composition with empty conjuncts dropped, named filters incl. unknown name and traversal exemption, default orderings, argument conversion errors, every query ceiling); integration (query with count/ancestors in one RT, arithmetic divide-by-zero → Query.ArithmeticFailed on query, child surface WidgetParts under a filtered Widget grant, restricted path refused)
- **Done when:** QueryAsync and IChildQueryService.QueryAsync answer RowPage in exactly 1 round trip on warm caches (asserted via DataAccessScope.RoundTrips); traversal refusals carry RestrictedPath and the path; IQueryTraversalRule public for 0029/0033

### PC-069 — tm-boot.js, boot page, session and launch mark

- **Analysis id:** `0024-c` · **Spec:** 0024 · **Layer:** both · **Tier:** 1 · **Est. LOC:** 4,000
- **Sections:** §4.1 (rules as data), §4.2, §8.1, §8.2, §8.3, §9.3 (TmLaunch, tmSingleLiveTenant, launch-landing-vectors.json)
- **Gated on (DEPS.md):** PC-067, PC-065
- **Analyst dependency notes:** 0024-a
- **Deliverables:** @tellma/core/boot/tm-boot.template.js with the pure tmBoot(env): the gate, cookie decode, record validation, sign-in mark loop guard, expiry delete over 0025 §2.2's frozen layout (1 s box, oldVersion=0 abort), returnUrl cases, hash restore, warm/cold language order, the warm '/' rewrite, the `auto` cases and window.__tmBoot. The DOM-built boot page with `.tm-boot-page` styles. tmParseProfileCookie and TmSession (profile, state, check, signIn including step-up params, mark consumption). tmSessionGuard. TmLaunch. tmSingleLiveTenant with src/launch/launch-landing-vectors.json. Store-layout vectors shared with 0025.
- **Tests:** Unit: tmBoot(env) vectors for every §4.2 step (gate, cookies off, loop guard, expiry delete with its 1 s box and oldVersion=0 abort, the three 3d returnUrl cases, the hash restored only when the landing equals returnUrl, the sign-in-failed retry keeping returnUrl and hash, the two auto cases, the cold language order, the warm '/' rewrite and its mark, document.title, /signed-out). Profile cookie parser (v1, v2, padding, bad base64url, empty sub). TmLaunch: each way the mark ends. tmSingleLiveTenant over the shared vector file. A gzip size assertion of at most 3 KB on the generated script.
- **Done when:** tmBoot is a pure, fully vector-tested function. The generated script fits in 3 KB gzip. The vector file exists at the path 0011's C# test links.

### PC-070 — Details plan, extras, record permissions, get-by-parent-ids, all cached

- **Analysis id:** `0015-e` · **Spec:** 0015 · **Layer:** backend · **Tier:** 1 · **Est. LOC:** 4,000
- **Sections:** §5.3, §5.4, §5.5, §12.1-§12.2 read-side, §18.2 details/get-by-parent-ids/cacheable pins
- **Gated on (DEPS.md):** PC-068, PC-016, PC-064, PC-015
- **Analyst dependency notes:** 0015-d; 0012 §11.4-§11.5 materializer; 0013 §4.3-§4.4; 0016-b QueryRowSet converter (or 0016-a if the converter lands there)
- **Deliverables:** DetailsPlanner (main-ids statement capturing @tb_keys under the access filter, AllProperties entity query with children and related along DetailsExpand ≤ MaxExpandDepth, AddExtra/AddRead/AddComputedExtra with name rules, extras serialised to JsonElement under TellmaJson.Options), the permissions extra (abilities from operations/actions, one keyed AddRead per Filtered decision, RefuseOnRecord after the batch), GetById (NotFound on empty IdsSource) / GetByIds (partial, request order), GetByParentIds (roots / children / IncludeRoots via DeclareIdTable, MaxTake+1 bound), GetAllCached via ICacheableEntities incl. oversized re-read
- **Tests:** unit (record-permission abilities, RefuseOnRecord reasons beside outside-filter, extra name rules); integration (computed extra from one AddRead in the details RT; permissions extra under a filtered Save grant; hidden-equals-missing on get/get-by-ids; get-by-parent-ids with IncludeRoots in 1 RT and a tree beyond MaxTake refused; cacheable Gadgets served in 0 RT after warm-up and refreshed after a write; details arithmetic failure passes through as 500)
- **Done when:** Details/parent-ids/all-cached meet §16.2 budget rows; same DetailsPlanner callable over @tb_saved for the save read-back (seam for 0015-g)

### PC-071 — TmApi HTTP layer

- **Analysis id:** `0024-d` · **Spec:** 0024 · **Layer:** both · **Tier:** 1 · **Est. LOC:** 5,000
- **Sections:** §3.4 (runtime), §5.6 (verdict from Tellma-Build; worker step via a TmUpdates seam), §10.1–§10.6
- **Gated on (DEPS.md):** PC-069, PC-067
- **Analyst dependency notes:** 0024-b; 0024-c
- **Deliverables:** TmApi: read/write/download/upload (XHR)/send (keepalive, 16 KB cap)/fingerprint, online, writesInFlight. Route mapping and the no-tenant rule. All §10.2 headers. The §10.3 step sequence, including the user check at both ends, the offline write, and the watchdog (60 s, or 6 min for long routes). Read retries by class, with abort ending waits. The online probe of /health/live. The §10.6 status→kind table. TmApiError/TmProblem. TM_BLOB_FETCHER. The SemVer verdict of §5.6, with a seam to TmUpdates.check(). Observer seams (TmRequestLog, TmCacheRefresher.observe one task after the body, TmLaunch.settle). TmFakeApi in core-testing.
- **Tests:** Unit with fake fetch, XHR and clock: headers (priority and confirmations included); the user check at both ends; §10.6 row by row; retries and the one 30 s server-outdated retry; an aborted wait; the watchdog, and 6 min for a long route without the option; read on a non-idempotent route (dev throw, prod without retry); a tenant route with no tenant; an offline write; send's cap; upload progress and abort; begin/end; settle on the first 2xx; observe one task after the body; §5.6's precedence cases.
- **Done when:** Every §13 'TmApi' bullet is green. No component outside TmApi/TmErrorPresenter imports HTTP status logic.

### PC-072 — Save pipeline, pre-persist: ownership, preprocessing, ids, child sync, RT1 and validation

- **Analysis id:** `0015-f` · **Spec:** 0015 · **Layer:** backend · **Tier:** 1 · **Est. LOC:** 6,000
- **Sections:** §6.1 steps 1-6, §6.2, §6.3, §6.4, §6.5, §6.6, §7.4 (uniqueness key loads, FK visibility pre-load), §9.2 save stage, §12.1 cycle validation, §12.2 IsActive server-owned
- **Gated on (DEPS.md):** PC-070, PC-037, PC-019, PC-042
- **Analyst dependency notes:** 0015-e; 0012 §7, §10.4; 0013 §7.7
- **Deliverables:** Save front half: non-empty/MaxSaveCount/MaxRowsPerSave/[MaxChildren] per parent at every depth, id classification and Entity.DuplicateId, ResetDerived/ResetServerOwned/WriteOnce/Defaulted/multilingual gating (Import.LanguageNotConfigured), trimming and [PreserveWhitespace], nesting parent keys, [ValueSet] canonical spelling, PreprocessAsync, IIdAllocator.Reserve immediate vs deferred and temp-id rewriting through self-typed FKs, RT1 composition (before images under Read, Save-grant count, child before images per present collection, FK targets via Loader.Visible, tree (Id,ParentId) set, validator loads, reservation), the §6.6 ordered checks (existence 404 / OutsideFilter 403, early concurrency incl. StampRequired, WriteOnce, DisplayName.PrefixSeparator, child identity Entity.NotFound, payload and key-load uniqueness, TreeCycleValidator Tree.Cycle/TooDeep, validators and further rounds, Validation.ValueNotInSet), error-then-warning raise order, reservation disposal on failure
- **Tests:** unit over the scripted batch (ids and temp-id rewriting incl. undefined temp id; immediate vs deferred reservation ordering; every ownership kind; child sync null/[]/list/foreign ids/grandchildren; uniqueness within payload and against loaded rows; one-RT create skips RT1 exactly when §6.1 says)
- **Done when:** A save that fails validation writes nothing and returns ids to the buffer; RT1 skipped exactly for the §6.1 one-round-trip create; all §6.6 errors collected in one ValidationException with structured paths

### PC-073 — tellma-client CLI: emit, serve, build and checks

- **Analysis id:** `0024-e` · **Spec:** 0024 · **Layer:** both · **Tier:** 1 · **Est. LOC:** 6,000
- **Sections:** §2.6, §3.6 (Node step via tsx with @angular/compiler first), §4.1 checks, §5.1, §5.3, §6 (hashed-name check), §7.6 (brand checks and copy), §12 (budget measuring, notes), Appendix A (source and output codes)
- **Gated on (DEPS.md):** PC-071, PC-067, PC-069
- **Analyst dependency notes:** 0024-b; 0024-c
- **Deliverables:** tellma-core-tools commands: `emit` (runs the Migrator's emit-client-assets), `serve` (dev cert export, .dev-ports.local ports, a proxy of metadata.routing words with websockets, no CSP, no worker) and `build`, with ordered steps: source checks (config check, index.html, manifest, brand SVGs); ng build --stats-json; brand copy; tm-boot.js generation with TM_BOOT from the string packs; manifest name_localized; font, fetch and modulepreload injection (moved from client/scripts); copying ngsw-worker.js and safety-worker.js; generating ngsw-config.json (shell and on-demand groups, navigationUrls) and running ngsw-config; moving source maps to dist/<slug>/sourcemaps; output checks (budgets from stats.json with Brotli report, hashed names, built index.html with no inline script or on* attribute). Exit codes 0/1/2/3, with each problem printed as one `code key feature` line.
- **Tests:** Unit and integration on a fixture app: each source and output check code on an injected problem, with exit 3; a source problem stops before ng build; a built index.html with an onload attribute fails index-html-invalid; the output holds ngsw-worker.js and safety-worker.js; ngsw.json's hashes match the files; navigationUrls derived from the routing words; budget-note-missing.
- **Done when:** `tellma-client build` produces a deployable output from a fixture with zero problems on Ubuntu and Windows. The worker config validates with ngsw-config.

### PC-074 — Save pipeline, persist and commit: assembly, post-check, read-back, re-entry, translation

- **Analysis id:** `0015-g` · **Spec:** 0015 · **Layer:** backend · **Tier:** 1 · **Est. LOC:** 6,500
- **Sections:** §5.1 (persist re-entry: in-memory validator re-run, recompose, recording view + replay; tenant-state verdict), §6.1 steps 7-10, §6.7, §6.8 (import-facing options), §6.9, §8, §9.3, §13.1-§13.2, §14.1 (sent persist completes), §14.2, §16.1 save/concurrency/effects/stale instruments, §18.2 save-related pins
- **Gated on (DEPS.md):** PC-072, PC-028
- **Analyst dependency notes:** 0015-f; 0012 §5.5 persist frame, §6.3-§6.5, §8; 0013 §2.5-§2.6; 0014 §7.4, §8.3
- **Deliverables:** Persist assembly (emitter Save, service ContributeAsync, IPersistEffect in order, OnPersist once, IAccessGuards ContributeLock/ContributeInvariants when declared writes meet core.Users/Roles/RoleMemberships/Permissions and Kind≠System, post-check over @tb_saved, read-back inside the transaction, DependsOn re-declaration of entity:* tags served in RT1), recording view of IDataBatch for participants and deterministic replay on runner re-entry (StaleContextException for unseen loads), tenant-state verdict (Frozen/ReadOnly/AllowWhenReadOnly/RunsWhenReadOnly hook/Provisioning), ConcurrencyConflictException → NotFound/ConcurrencyException with ModifiedByName in request language, full §14.2 translation table incl. 5060x guards, UX_ unique → Unique at path or Root, blob UX → Blob.NotAttachable, 547 → Fk.NotFound, ambiguous commit (probe → plain Read read-back; update-only → DependencyUnavailable), post-commit hooks/effects/OnCommitted with failure metering, ConfirmationAccepted log
- **Tests:** unit (persist assembly order, OnPersist once and never on validation failure, stack-owned write from OnPersist refused at append, every translation row, verdict matrix); integration (budget rows for save, stamp conflict/override/missing under both modes/StampRequired/ModifiedByName, write-once and server-owned ignored, IsActive ignored on save, unique race from two connections → one 422, Fk.NotFound under target Read filter, tree recompute and SQL cycle race rolled back, stale permissions tag re-run then StaleContextException with validators re-run in memory and contributions committed once, ambiguous commit probe on insert and DependencyUnavailable on update-only, distribution THROW 50650 → ValidationException, OnPersist statement committed/rolled back with the save, MaxRowsPerSave and [MaxChildren] depth two, client abort after persist still commits)
- **Done when:** Create = 1 RT and update = 2 RT on warm caches asserted; a recompose never calls a hook twice; every executor exception reaching the pipeline maps to a closed-set exception or passes through as specified

### PC-075 — Client store and tab bus

- **Analysis id:** `0025-a` · **Spec:** 0025 · **Layer:** both · **Tier:** 1 · **Est. LOC:** 4,500
- **Sections:** §1 (placement, storage writers), §2.1–§2.7, §6.1, §6.2, §15 (store telemetry)
- **Gated on (DEPS.md):** PC-065, PC-069, PC-059
- **Analyst dependency notes:** 0024-a; 0024-c; 0010 §D.5
- **Deliverables:** TmClientStore: the tm-app DB, `docs` store keyed ['p','n'] with stored names <name>@<format>, partitions and key ranges, readAll in one readonly transaction, doc() mirror signals, put with the savedAt rule, patch in one readwrite transaction, the write guard (other-user/cookie/session) with TmStoreRefusedError, sweeps (session-ended, sessionOtherThan, tenant) posting store.changed with a prefix, the boot step (backstop sweep plus one readAll), boot.read/patch/changes over localStorage tm.boot, TM_DOC_FORMATS, the idle rule, persist(), quota drop, memory mode, versionchange close. TmTabBus with the device channel tm:<slug> and user channel tm:<slug>:<sub>, envelope v1, and close/reopen around the back-forward cache. The store-layout golden vectors shared with tm-boot.js. provideTellmaStateTesting with an in-memory store and fake bus.
- **Tests:** Unit in browser mode on real IndexedDB: each guard row; each sweep leaves exactly the right keys; expiry clean-up keeps sub and lastTenantId while sign-out's clears them; the device `zone` survives every sweep and the boot script's expiry delete, is capped at 20 users and is mirrored after readAll; format and idle rules; two builds' settings formats side by side; quota drop; memory mode; versionchange close. Store-layout golden shared with tmBoot vectors. Bus: channel per type, unknown v and type ignored, close and reopen around the back-forward cache.
- **Done when:** The warm path's only store access is one readonly transaction (asserted). The layout golden is shared with 0024-c. Every §14 'Store' and 'Bus' bullet is green on Ubuntu and Windows.

### PC-076 — Deletes, activate/deactivate fast path and the general action path

- **Analysis id:** `0015-h` · **Spec:** 0015 · **Layer:** backend · **Tier:** 1 · **Est. LOC:** 5,500
- **Sections:** §3.1 ExecuteActionAsync overloads, §9.2 delete/action stages, §10.1-§10.3, §11.1, §11.2 (incl. Persisted, PartialFailureException, [EntityRef] metadata), §12.1-§12.2 action side, §18.2 delete/action pins
- **Gated on (DEPS.md):** PC-074, PC-042
- **Analyst dependency notes:** 0015-g; 0012 §9.1-§9.2, §10.3
- **Deliverables:** DeleteByIds (fast path: one Persist RT through DeleteSpec.ByIds with read filter/grant/expected stamps; hooked path with RT1 two-stage + ValidateDeleteAsync rounds; override detection for service hook and default interface methods), DeleteByQuery (blank filter refusal, capped Count + DeleteSpec.ByQuery with Cap/ExpectedCount, 50413/50428 translation), DeleteWithDescendants, Fk.InUse mapping, activate/deactivate SQL-only fast path via UpdateSpec.ByIds AllOrNothing + recount, general path (descriptor resolution, SingleTarget checks, JsonElement → TArguments under TellmaJson.Options, argument attribute walk at Arguments.<Prop>, RT1 load + grant count, ActionContext Save/Update/Batch/Notify/Withdraw/Assignments/Host, RT2 with Override emitter, optional post-check, read-back or method TResult, Persisted completion/fault, PartialFailure executes RT2 then rethrows)
- **Tests:** unit (both overloads and mismatch refusal; SingleTarget with ≠1 id refused; Persisted: RT2 sent while method parked on it; argument failures at Arguments.<Property>); integration (hidden-equals-missing on delete/delete-with-descendants/activate/deactivate; two-stage pre-check in RT1 for hooked delete and WidgetService's [EntityAction]; NoGrant/OutsideFilter; delete-by-query blank filter/cap/expected-count mismatch inside the transaction; partially visible subtree refused; fast path leaves already-active rows unstamped; deactivate answers alike on fast and general paths; Fk.InUse naming entity and property; delete with stamps; actions stamp)
- **Done when:** Delete/activate = 1 RT, hooked = 2 RT, custom action = 2 RT asserted; 404-before-403 on every id-addressed write

### PC-077 — IApiActionInvoker, write frames and enlisted writes

- **Analysis id:** `0015-i` · **Spec:** 0015 · **Layer:** backend · **Tier:** 1 · **Est. LOC:** 6,500
- **Sections:** §11.3, §13.3 (frames, EnlistSaveAsync/EnlistDelete, group ordering incl. nesting, MapPath composition with enlistedResource, authority, concurrency, bypass resistance incl. CallerAuthority append check and front-door refusal while composing), §14.2 group attribution by StatementOrdinal, §2.6 core.enlistments, §16.1 enlistments counter, §18.2 enlistment/ApiAction pins
- **Gated on (DEPS.md):** PC-076, PC-033
- **Analyst dependency notes:** 0015-h; 0012 §5.2 internal channel and append-authority seam, §6.4 StatementOrdinal; 0011 §6.2
- **Deliverables:** ApiActionInvoker (DI resolution of HandlerType per request, RequireAsync or member check, body attribute walk, interceptor chain, ApiAction open frame registered as IOpenWriteHost), pipeline frame (IWriteHost) and Provisioning open frame hook for 0011's step runner, job-frame seam for 0020, enlistment engine (pair/participant/phase checks, target pre-persist stages as scheduler tasks, unfiltered before images, ServerOwned re-application, groups placed BeforeHost/AfterHost with composite nesting, duplicate root id refusal, self-pair only from an open frame), PersistAsync (pending rounds on Validate batches, one Persist with every group, access guards over the union of writes, post-commit of targets), error re-homing view, StatementOrdinal attribution, run-time bypass refusals
- **Tests:** unit (pair gate refusals, front-door call inside a composing frame refused, group order B,C,A, MapPath through nesting with enlistedResource, double enlistment refused, ordinal attribution, PersistAsync twice refused); integration (WidgetService → Shipment in one transaction with FKs in each direction, target validators/effect run, errors re-rooted, Concurrency=Check on loaded row refusing a default stamp, caller-channel write to Shipment refused at append, [ApiAction] via invoker refused for caller without its action)
- **Done when:** users/me/save-style self-pair from an [ApiAction] frame works with 3 RT; an enlistment adds 0 RT when made before the writer's last dispatch; no front-door or stack-owned write path bypasses the owner's rules at run time

### PC-078 — Write side: guards, validators, companion insert, preference bag, bootstrap, drift scanner

- **Analysis id:** `0014-f` · **Spec:** 0014 · **Layer:** backend · **Tier:** 1 · **Est. LOC:** 7,000
- **Sections:** §3.4 (bag statement + caps), §3.7 (role-save validation and FilterQueryexVersion stamping), §3.9, §8.1-§8.4, §9 (IPermissionDriftScanner), §11, Appendix A, §13 (guards counter, GuardTriggered/EscalationRejected events)
- **Gated on (DEPS.md):** PC-052, PC-062, PC-028, PC-034, PC-063, PC-074, PC-076
- **Analyst dependency notes:** 0014-e; 0015 §7 validator contract and context loads, §13.1 IPersistEffect, §3.2 Validator<,> closing; 0012 §5.5 step 3 applock seam; 0011 §6.1-§6.2 (migrator scan hook, provisioning step 10)
- **Deliverables:** IAccessGuards (ContributeLock marker -> sp_getapplock 'tellma.access' with @LockTimeout = SecurityLockTimeout, 50503 codes; ContributeInvariants L1-L3 as THROW 50422) invoked by 0015's pipeline on Persist batches touching the four security tables outside System scope; UserAccessRules<TUser> and RoleAccessRules<TRole> (every Appendix A code incl. escalation tests, own-administrator-membership rule, overlap/EndsBeforeStart/EndsInPast, stamping matrix, filter validation under AccessLimits, MaxPermissionsPerRole, wildcard-on-public-role) with ValidateDeleteAsync/ValidateActionAsync for activate/deactivate; IPersistEffect<User> companion UserStamps insert over @tb{b}_new; the preference-bag statement shapes (replace-all, set, delete halves; TooManyPreferenceKeys assertion; PreferenceValueTooLarge in C#) over UserPreferenceList; ITenantBootstrapper (System scope, idempotent on email, Development subject statement, refusal outside Development) and the core.bootstrap-administrator step 10 class if 0011 does not own it; IPermissionDriftScanner invoked by the migrator after migrate
- **Tests:** unit: both escalation rules with (R,*) and (*,A) rows against callers holding (R,*), (*,A) and */*, and (R,Read) against a caller holding (R,Save); every validator code; CannotRemoveOwnAdministratorMembership for delete, added EndsAt, moved EndsAt, StartsAt moved past now, and not for another user's row or a cleared EndsAt; RoleAccessRules stamping matrix (new, changed, unchanged, null stamp, below Minimum), engine diagnostics -> FilterInvalid, wildcard root resolution; integration(SQL): tag bumps for every declared write path (role save -> permissions; membership -> that user's PermissionsTag only; user save -> PreferencesTag; bag statement -> PreferencesTag; none on permissions for Users-only writes); companion insert; role re-save keeps an untouched filter's stamp; L1-L3 under two concurrent administrator removals (exactly one succeeds), rollback of the whole transaction, system bypass; L1 refuses removing the last permanent administrator while another's membership is time-bounded; RoleMemberships overlap/window codes; two self-writes of one user serialise on the stamp row; bootstrap idempotent on email, Development subject path, refusal outside Development; drift scanner against a filter over a renamed column and against a tenant lacking a language a stored filter names
- **Done when:** No path can produce a tenant without an active invited-or-joined permanent administrator; every guard counted; the 'role save racing a membership edit and a me/locale write' test is deferred to 0018 where me/locale exists
- **Planner note:** Also ship UserService<TUser> and RoleService<TRole> shells (EntityService subclasses with no hooks, in their 0018 namespaces), and CoreFeature's Entity<User, UserService<User>>, Entity<Role, RoleService<Role>>, Validator<User, UserAccessRules<User>> and Validator<Role, RoleAccessRules<Role>> lines (0014 §1.2). 0018-c and 0018-e fill the shells and add no Entity line.

### PC-079 — SettingsService: client, entity-tags, details, save, refresh-caches

- **Analysis id:** `0013-h` · **Spec:** 0013 · **Layer:** backend · **Tier:** 1 · **Est. LOC:** 5,500
- **Sections:** §6.1-§6.5, §5.5 (securables), §2.6 (settings Rerun on Validate/Persist exercised end to end), §12 Settings integration bullet
- **Gated on (DEPS.md):** PC-049, PC-050, PC-052, PC-068, PC-077
- **Analyst dependency notes:** 0013-f; 0014-d (IGuardedBatchRunner, SettingsStale guard failure + initializer re-run); 0014-e (IAccessEvaluator.EvaluateAsync/RequireAsync with step-up); 0015 §9.1 ApiService/IApiActionInvoker and §14.1 exceptions; 0011 §3.7 ITenantCatalog.RenameAsync (stub ok)
- **Deliverables:** SettingsService registered via ApiService<SettingsService>() with [ApiRoute('settings')]: ClientAsync, EntityTagsAsync, DetailsAsync (one Read batch: typed row when General Read held, entries with ModifiedAt, Related via KeySetRestriction per referenced type with RelatedSelect projection), SaveAsync (field-mask TenantSettingsPatch, per-row stamps, Concurrency.StampRequired, ConcurrencyException incl. UX_SettingEntries_Key on insert, Secret protection, shape nulling of Name2/Name3, BumpVersionTag('permissions') on language change, closing reads, warm-the-writer Set, OnCommitted RenameAsync best-effort), RefreshCachesAsync (bump every registry name + raw UserStamps UPDATE + tenant-group preferences event); securables core.Settings.General (Save sensitive) and core.Settings.<Category> registration; TenantSettingsDetails/TenantSettingsPatch/SettingsSaveResult records; validation codes of §6.5
- **Tests:** unit: patch merge semantics, every §6.5 code; integration(SQL): stale typed-row/entry stamp -> 409; entries-only save neither reads nor checks the typed row; concurrent first write of one key -> 409 not Unique; Override skips stamps; SettingsTag equals the header's settings tag; language change bumps permissions and switches schema shape; Name change mirrors after commit and a failing mirror is logged; refresh-caches moves every row and both user columns; details/save by a member without grants -> 403; details holds only held categories with Related rows and a deleted target bare; save Details carries stamps of unlisted rows; cached views after save equal a cold load; a settings bump from a second instance fails a validate batch's guard with fresh rows in that round trip while a read batch is served
- **Done when:** All six actions behave per §6 through the runner; round-trip budget 'update 2' holds for save

### PC-080 — Endpoint projection, binding, endpoint metadata, contract middleware, audit rules

- **Analysis id:** `0016-c` · **Spec:** 0016 · **Layer:** backend · **Tier:** 1 · **Est. LOC:** 6,500
- **Sections:** §1.2 (MapTellma projection part), §2.1-§2.4, §3.7-§3.8 web-side rules, §4.2-§4.6, §5.1 (filter order), §5.2, §5.4, §6.1 Tellma-Confirm seeding, §8.1 MaxEntitiesPerSave lowering
- **Gated on (DEPS.md):** PC-064, PC-025, PC-039, PC-076, PC-077, PC-032
- **Analyst dependency notes:** 0016-b; 0011 §5.1-§5.3; 0014 §4.5 metadata records; 0015-h; 0015-i
- **Deliverables:** Tellma.Core.AspNetCore: StandardEndpoints<TEntity,TKey> static methods closed per stack (or per-stack lambda fallback), action delegates per EntityActionDescriptor (EntityActionRequest(<T>), IdsRequest(<T>), IdRequest(<T>) by ResultType/SingleTarget) and ApiActionDescriptor (RequestType → IApiActionInvoker), child query routes, [ApiRoute] routes, SaveRequest → SaveOptions{Source=Web} with MaxEntitiesPerSave 413, WireIds narrowing, Tellma-Confirm parsing (≤16 keys, ≤100 ASCII) seeding the scope's IConfirmations, metadata per §4.3 (TenantEndpointMetadata, Securable/Member, ContractMetadata, NoActivityStampMetadata list, RequireAssuranceMetadata for IsSensitive, API-endpoint marker, ProducesProblem, request size, activity tags, tellma-long), TellmaContractMiddleware (412 client-outdated before body read, absent header passes), the four projection audit rules + JsonTypeInfo resolution + [JsonIgnore]-not-ServerOwned gate, ApiChildSurfacesProjected log, 202 for JobAccepted results, Cache-Control: no-store
- **Tests:** unit under WebApplicationFactory with fake authentication and fake services (projection table for both descriptor kinds, IdsRequest/IdsRequest<T>/IdRequest/IdRequest<T> bindings, audit failures for missing policy/securable/unregistered pair/AllowAnonymous/tenantless without metadata, contract 412 with nothing executed, Tellma-Confirm 17 keys → 400, entities-per-save lowering)
- **Done when:** A composed host maps exactly IRouteContracts.Routes; every tenant endpoint passes 0011's and 0016's audit; no endpoint filter evaluates a securable

### PC-081 — Problem details, exception mapping, headers and the telemetry filter

- **Analysis id:** `0016-d` · **Spec:** 0016 · **Layer:** backend · **Tier:** 1 · **Est. LOC:** 5,000
- **Sections:** §5.5 (wire mapping of verdicts/step-up), §6.1-§6.4, §7.1-§7.6, §10.1 (meter, filter, log events)
- **Gated on (DEPS.md):** PC-080, PC-019, PC-025
- **Analyst dependency notes:** 0016-c; 0013 §9.2-§9.3, §10.1; 0011 §4.3, §9
- **Deliverables:** IExceptionHandler mapping every closed-set exception by type to status/code/body (§7.1 table incl. TenantUnavailable codes, ImportException, BlobRejected status by code, PartialFailure 502, StaleContext/DependencyUnavailable Retry-After), RFC 9457 body (type = ProblemTypeBase+code, localized title/detail via Problem_ resources, instance = trace id, errors[], errorDetails, arguments), ValidationPath rendering (entities/ids/id/arguments/body rooting, camelCase), conflict and confirmation bodies, AddProblemDetails customizer for framework problems (malformed JSON, unauthenticated, unsupported-credential, route-not-found, 405, 413 body, 415, 429, 504) and the 500 backstop (Development adds exception type/message), header parsing (Tellma-Time-Zone, Tellma-Calendar, Tellma-Client, Tellma-Activity, Tellma-Priority, traceparent; unparseable headers ignored and counted) into RequestContextInputs, response headers (Content-Language, Tellma-Calendar, Tellma-Version-Tags from BatchOutcome, Server-Timing), telemetry filter (log scope Client/Resource/Operation, activity tags, tellma.api.operation.duration, tellma.api.problems), bearer-on-web 401 unsupported-credential
- **Tests:** unit (every 4xx row of §7.1 produced by a test throwing that type; body shape; path rendering grammar; header precedence and negotiation incl. ar-SA-u-nu-latn → ar-SA; Tellma-Version-Tags omits preferences/permissions after a batch without prologue; forbidden bodies never name roles/filters/users)
- **Done when:** Every exception reaching the edge produces the documented status/code; MCP can reuse the mapping (§7.6); headers present per §6.2
- **Planner note:** Map BlobRejectedException and ImportException by type: both are seeded contract-first by 0015-a. Do not re-declare them. (Kickoff audit, 2026-11-08.)

### PC-082 — Limits, rate and concurrency policies, timeouts, compression, caching, OpenAPI

- **Analysis id:** `0016-e` · **Spec:** 0016 · **Layer:** backend · **Tier:** 1 · **Est. LOC:** 3,500
- **Sections:** §1.3 ApiOptions, §8.1-§8.5, §12.1, §12.2 (Api group + Tellma.Api policy only)
- **Gated on (DEPS.md):** PC-081, PC-080
- **Analyst dependency notes:** 0016-c; 0016-d
- **Deliverables:** ApiOptions binding (Tellma:Api), chained PartitionedRateLimiter per Web endpoint (route policy → tellma-user → tellma-tenant), policies tellma-user/tenant/anonymous/export/import/reports (two lanes by Tellma-Priority)/telemetry/blobs/mcp with lane tagging and Retry-After, request-timeout policies tellma-web/tellma-long/tellma-mcp with 504-when-nothing-written, cancellation token plumbing (reads cancel, sent persist completes), Brotli/Gzip Fastest for JSON only, output caching for distribution-info/PRM/openapi only, AddOpenApi("web") in Development with document transformers (x-tellma-securable, readOnly/x-tellma-write-once/x-tellma-derived, x-tellma-related-select, tags, decimal/long string-or-number schemas), Api group seam with Tellma.Api policy and nothing mapped
- **Tests:** unit (partition keys and closed tag sets, chained limiter releases leases on later refusal, lane selection, compression MIME set, OpenAPI schema for decimal/long); integration with lowered limits via configuration (429 with Retry-After, 504 timeout)
- **Done when:** No limiter shares state across instances; policies attachable to routes owned by later specs (reports, blobs, telemetry) via metadata without code change here
- **Planner note:** Promoted to Tier 1: 0016-f's integration suite asserts these policies, and a launchable host needs limits and timeouts.

### PC-083 — Blob entity, kind registry, options, startup check and staging

- **Analysis id:** `0017-c1` · **Spec:** 0017 · **Layer:** backend · **Tier:** 1 · **Est. LOC:** 5,500
- **Sections:** 0017 §2.1-§2.4, §3 (staging), §9
- **Gated on (DEPS.md):** PC-053, PC-017, PC-001, PC-033, PC-037, PC-027, PC-062, PC-074, PC-068, PC-007, PC-076
- **Analyst dependency notes:** 0017-a; 0017-b; 0011 §2.1-§2.3; 0011 §6.2; 0012 §5.2; 0012 §8.5; 0012 §13.2; 0014 (IGuardedBatchRunner, IAccessEvaluator, §5.5); 0015 §3.2; 0015 §6.7; 0015 §13.1; 0015 §14.2; 0010 §F.7
- **Deliverables:** core.Blobs entity and model, the kind registry, BlobOptions/BlobsBuilder, the `blobs` startup check, StageAsync and the stage statement. Original analysis entry 0017-c: Abstractions: Blob entity, BlobState, IBlobService, BlobStageRequest, BlobDescriptor, BlobDownload, IBlobKindRegistry, BlobKindDescriptor, BlobRejectedException. Tellma.Core.Blobs: BlobConfiguration (sequence core.sq_Blobs, StorageKey/Sha256 as shadow columns outside the Queryex schema, IX_Blobs_Expiry/UX_Blobs_StorageKey/IX_Blobs_StagedBy), Queryex entity core.Blob + RelatedSelect + per-owner navigations, BlobKindRegistry (root/child/grandchild OwnerResource/OwnerEntity/OwnerPath, feature then host transforms), BlobOptions, BlobsBuilder, `blobs` IStartupCheck (all §9 rules except Azure-specific), BlobService.StageAsync (sanitise, length checks, memory vs ScratchPath spill + SHA-256, sniff, process/thumbnail, key32, the stage statement with quota and System-scope exemption, row-before-bytes) and ResolveAsync (two-statement Read batch with FilterTree.Via, combination rules, ETag), BlobReferenceValidator<T>, BlobReferenceEffect<T> (one Sql per column), the realizer adding the pair to every stack with a [BlobReference] at any depth, BlobContainerStep; fixture.BlobOwners/BlobOwnerFiles in test/shared/Tellma.Testing.Entities
- **Tests:** unit: registry construction for root/child/grandchild kinds and every §9 problem over synthetic models; StageAsync over a fake store and an in-memory batch (each rejection code, quota mapping, row-before-bytes on a failing store, null thumbnail without a thumb variant); ResolveAsync combination over canned result sets; §10.1 instruments asserted. integration (LocalDB/Testcontainers): stage statement (sequence id, quota 50422, System scope past the quota); capture+effect over fixture.BlobOwners (attach on insert, replace, null release, delete by ids/query/with descendants, override save releases the actual old value, child insert/replace/remove, root delete releases children, UX_ refuses a second owner, expired and foreign-uploader NotAttachable, FileId+PhotoId on one row); reconcile statement; the RCSI concurrency test (confirm holds the lock while the claim blocks then skips); ResolveAsync through filtered Read grants incl. the child kind via the parent's grant
- **Done when:** A save that attaches, replaces or removes a blob adds zero round trips; every §4 path is pinned by the SQL suite; the realised gate fails startup on each §9 problem; the confirm/claim race test passes deterministically.

### PC-084 — Blob references: validator, effect, attach/release, resolve, reconcile and the FK exemption

- **Analysis id:** `0017-c2` · **Spec:** 0017 · **Layer:** backend · **Tier:** 1 · **Est. LOC:** 5,500
- **Sections:** 0017 §3 (attach/release), §4, §5.1-§5.2, §10
- **Gated on (DEPS.md):** PC-083
- **Analyst dependency notes:** 0017-c1
- **Deliverables:** BlobReferenceValidator/Effect and the realizer, attach and release over fixture.BlobOwners, ResolveAsync, the reconcile statement, the RCSI race test, and the exemption of [BlobReference] columns from 0015 §7.4's FK Read validation (core.Blob has no securable).
- **Tests:** unit: registry construction for root/child/grandchild kinds and every §9 problem over synthetic models; StageAsync over a fake store and an in-memory batch (each rejection code, quota mapping, row-before-bytes on a failing store, null thumbnail without a thumb variant); ResolveAsync combination over canned result sets; §10.1 instruments asserted. integration (LocalDB/Testcontainers): stage statement (sequence id, quota 50422, System scope past the quota); capture+effect over fixture.BlobOwners (attach on insert, replace, null release, delete by ids/query/with descendants, override save releases the actual old value, child insert/replace/remove, root delete releases children, UX_ refuses a second owner, expired and foreign-uploader NotAttachable, FileId+PhotoId on one row); reconcile statement; the RCSI concurrency test (confirm holds the lock while the claim blocks then skips); ResolveAsync through filtered Read grants incl. the child kind via the parent's grant
- **Done when:** A save that attaches, replaces or removes a blob adds zero round trips; every §4 path is pinned by the SQL suite; the realised gate fails startup on each §9 problem; the confirm/claim race test passes deterministically.

## Phase 5 Distribution, reference stacks and jobs

### PC-085 — Reference distribution Acme and the Azure defaults bundles

- **Analysis id:** `0011-g` · **Spec:** 0011 · **Layer:** backend · **Tier:** 1 · **Est. LOC:** 5,500
- **Sections:** §1.2 (without the Client esproj build), §1.3, §5.8 Azure key ring, §6.3 Development trigger, §6.6, §8 (all options validated), §10 Acme suites (achievable subset), §11
- **Gated on (DEPS.md):** PC-047, PC-034, PC-002
- **Analyst dependency notes:** 0011-d; 0011-f; 0010 §A.11
- **Deliverables:** src/defaults/Tellma.Defaults.Azure (UseAzureDefaults: email now; blobs/imaging added by 0017) and src/defaults/Tellma.Defaults.Azure.AspNetCore (UseAzureWebDefaults: webhooks, DP key ring in Blob + Key Vault; SignalR added by 0021). distributions/acme/src: Tellma.Distro.Acme (AcmeComposition.Slug/Compose), Tellma.Distro.Acme.Web (three-call Program.cs, appsettings + Development defaults with seed tenants 1 and 2, tracked launchSettings, .dev-ports.local reading in AddTellma Development), Tellma.Distro.Acme.Migrator (one-line Program.cs, AcmeDesignTimeFactory, generated tenant migrations), Development process provisioning trigger. distributions/acme/test: Web.Tests, IntegrationTests, E2E (empty Playwright scaffold). Tellma.Client esproj deferred to spec 0024's checkpoint (or added outside Tellma.slnx). OidcFlowClient moved to a shared test-support project. CI: Windows LocalDB integration job; alert queries + generalized cross-check.
- **Tests:** unit (Acme.Web.Tests): composition parity (byte-identical models web vs migrator), emit-client-assets exits 0 with no DB (in-process call), audit passes, one-way dependency (no src -> distributions), Web reaches no Tellma.Identity.Migrations, tracked-config secret scan, Program.cs has exactly three calls, collation pin over Acme tenant migrations; integration (Acme.IntegrationTests, Category=Integration): migrate then provision from an empty server, seed tenants Active, provision allocates 1000 and refuses provision --tenant 1000 --name, collations of catalog and tenants, existing DB in another collation fails without migrating, __TellmaProvisioning versions and bumped step re-run, full in-proc sign-in, idsvr migrate idempotent, missing idsvr migration fails web startup naming the command, suspension on next request and on a second host within one refresh, back-channel logout end to end, refresh rejection ends the session, readiness before/after catalog availability
- **Done when:** From a fresh clone: dotnet run Migrator -- migrate creates Tellma.dev.acme.catalog with idsvr and tenants 1 and 2; dotnet run Web serves /api/distribution-info, health and the BFF; in-proc sign-in works; all Acme suites green on both OSes (assertions needing 0013–0020 deferred to 0011-h).

### PC-086 — Server emit: client entity model, enums, labels, action fields, .d.ts

- **Analysis id:** `0029-a` · **Spec:** 0029 · **Layer:** both · **Tier:** 1 · **Est. LOC:** 5,500
- **Sections:** §2 (all), §15, §16 (policy emission), §5.1 (rule emission)
- **Gated on (DEPS.md):** PC-067, PC-021, PC-046, PC-062, PC-017, PC-063, PC-068, PC-070, PC-084, PC-073
- **Analyst dependency notes:** 0024 §3.1, §3.2; 0012 §2.5, §3.1, §11.1; 0013 §7.6, §7.7, §10.1; 0015 §2.2, §11.2; 0017 §2.3
- **Deliverables:** Tellma.Core.Abstractions.Client: IClientEntityModel, ClientEntityMetadata and part records (properties, navigations, children, actions, action fields, enums, display-name rule, limits, blob policies) with source-generated camelCase JSON; Tellma.Core.Client: ClientEntityModel (built once from the realised composition: All-shape roots incl. children and read-only roots, ownership, multilingual slots, foreign keys, operations/capabilities, extras, limits incl. maxFilterLength), label-key resolution chains incl. newLabelKey and the action-field chain with fallbacks and label-missing warnings, named enums once (C# enums and value sets with keyed/literal labels and aliases), JsonSchemaExporter for action arguments plus TmActionField list, EntityClientAssetContributor writing Core-chunk summaries, Entities-chunk entities/enums and generated/tellma-entities.d.ts (interfaces, TmEntityTypes/TmEntityShapes augmentation); deterministic ordering
- **Tests:** unit (C#): §2 over a fixture composition (Acme-like), determinism (same bytes), every label chain incl. newLabelKey and action fields, fallbacks, twins, blob policies, json names equal the wire's, each enum once, value-set labels and core.TimeZone aliases, defaulted/allowWhenReadOnly/maxFilterLength; TS type test that the emitted fixture types as TmEntitiesChunk; typed-codegen test that the emitted .d.ts compiles
- **Done when:** Emit suites green; `tellma-client build` writes the three sections and the .d.ts for the test composition; fixture JSON checked in for the client checkpoints.

### PC-087 — GL module: Center, CenterService, GlFeature and sample centers

- **Analysis id:** `0018-b` · **Spec:** 0018 · **Layer:** backend · **Tier:** 1 · **Est. LOC:** 4,000
- **Sections:** §5.1-§5.7, §8.1 centers rows, §9 Gl.Tests + centers integration
- **Gated on (DEPS.md):** PC-055, PC-001, PC-042, PC-022, PC-062, PC-066, PC-076, PC-082
- **Analyst dependency notes:** 0011 §2.1; 0011 §6.2; 0012 §2.2; 0012 §10; 0013 §10.1; 0014 (securables from descriptor); 0015 §3.1; 0015 §7.2
- **Deliverables:** src/module/gl/Tellma.Module.Gl.Abstractions (Center : ActivatableTreeEntity with annotations only, CenterType, GlModule, GlResources, GlValidationCodes, IGlSampleCenters, SampleCenter, SampleCenterName) and src/module/gl/Tellma.Module.Gl (CenterService<TCenter> ValidateAsync/ValidateDeleteAsync rules, GlFeature with exactly three contributions, GlSampleCentersStep<TCenter> Development-only, resolve by Code then System save with temporary negative parent ids, DefaultGlSampleCenters, Resources/Strings.resx + Strings.ar.resx keys), READMEs with the Apache-2.0 header, taxonomy.json 'Gl' plus the module-folder consistency test in Tellma.Core.Tests, test/module/gl/Tellma.Module.Gl.Tests
- **Tests:** unit: rules with a fake loader (parent in payload, parent loaded, type change with children, self-parent, delete with children); sample tree acyclic, unique codes, Arabic twins; step with fake IGlSampleCenters (replacement, empty list, unknown ParentCode before any write, name slot mapping); reference closure excludes Tellma.Core and every EF package; resource audit. integration: save with in-payload parents in two round trips, parent-type rule, HasChildren, delete-with-descendants, activate/deactivate recounts, ParentMustBeGrouping on type change, §8.1 budgets
- **Done when:** gl.Centers is produced from annotations alone and Tellma.Module.Gl references no Tellma.Core. Projected routes appear under /centers/. Round-trip budgets are asserted through DataAccessScope.

### PC-088 — Users stack: hooks, invite, issue-credentials, invitation status, side effects

- **Analysis id:** `0018-c` · **Spec:** 0018 · **Layer:** backend · **Tier:** 1 · **Est. LOC:** 6,500
- **Sections:** §1.2 (CoreFeature lines except the email channel), §3.1 (service shell, Invite, IssueCredentials, GetInvitationStatus, RefuseOnRecord, UserAccessCriteria, result records, UserInvitationOutcome, UsersTelemetryNames), §3.2, §3.3 phases 1-4 WITHOUT the core.user.added notice and the welcome email, §3.4, §3.6, §3.8, §8.1 invite/credentials/status/admin save/activate/delete rows, §8.2
- **Gated on (DEPS.md):** PC-087, PC-055, PC-062, PC-068, PC-024, PC-025, PC-002, PC-006, PC-005, PC-077, PC-078, PC-081, PC-085
- **Analyst dependency notes:** 0018-a; 0014 (User entity, UserAccessRules, §5.1, §7.1); 0015 §11.2; 0015 §5.1; 0016 (IdsRequest, IdRequest, PartialFailure 502); 0011 §3.8; 0011 §4.5; 0011 §5.10; 0013 (ILanguageCatalog, value sets, TenantSettings); 0010 §A.1; 0010 §A.8; 0010 §G.1
- **Deliverables:** UserService<TUser> (IEnlists<TUser>) with the preprocess/validate rules and action validators; the invite action (identity call between RT1 and RT2, write-back statement over UserInvitationOutcomeList + IdList with UserIds epilogue tag bump, SubjectMismatch second result set, sandbox ExistingOnly mapping to Users.SandboxRequiresExistingIdentity, PartialFailureException with prefix, OnCommitted membership hint, recompose replay without a second remote call); issue-credentials (create, context.Update Subject/JoinedAt, OnCommitted RecordAsync + delete previous client, delete new client on faulted persist); invitation-status; deactivate/activate post-commit effects incl. DeleteServiceAccountAsync for Service rows; logs UsersInvited/UsersInvitePartial/ServiceAccountDeleteFailed/UserAccessRevoked; Users.* validation codes and resx strings
- **Tests:** unit with a fake client and fake pipeline: Locale/DisplayName/ReturnUrl/ExistingOnly mapping, InviteResult order and SubjectMismatch, PartialFailure prefix + not-attempted ids, credentials mapping and secret never logged, §3.2 rules. integration: write-back statement (transitions, InvitedAt once, LastInviteError cleared, mismatch untouched, 2627 → Unique at Subject, PreferencesTag bumped for invited users only, ModifiedAt stamped); credentials (Subject, JoinedAt once, rotation deletes old client after commit, deactivation deletes client, Kind write-once, Service refused by invite, Individual refused by issue-credentials); recomposed RT2 with one identity call; against the in-proc identity server: Invited/Reinvited/Active/per-user error, delivery status after a real send through the email log sink, service account + client_credentials token + prologue resolving ServiceAccount through a test-mapped bearer endpoint; sandbox ExistingOnly test skipped until §A.7 lands
- **Done when:** Admins can invite users and issue service credentials end to end against the in-proc identity server. No transaction is held across a remote call (asserted). The users/invite budget is 2 DB round trips.

### PC-089 — Blob web surface, Azure adapter and the Azure defaults

- **Analysis id:** `0017-d` · **Spec:** 0017 · **Layer:** backend · **Tier:** 1 · **Est. LOC:** 5,000
- **Sections:** §1.1 (Azure row, pins), §1.3 (AddAzureBlobStore), §5.3, §6.3, §8.1, §8.2 (HTTP mapping), §9 (Azure option checks), §11 (Azurite row, distributions/acme end-to-end row), §12 Docs
- **Gated on (DEPS.md):** PC-084, PC-053, PC-081, PC-085, PC-032, PC-088
- **Analyst dependency notes:** 0017-c; 0017-b; 0016 §7; 0016 §8.2; 0011 §1.3; 0011 §5.2-§5.4; 0018-c
- **Deliverables:** Tellma.Core.AspNetCore MapTellmaBlobs: POST raw-body upload (AllowMember, WithMutation(true), AcceptsBinary(MaxUploadSize), Tellma-Client required, 411/413/415/404 via BlobRejectedException mapping, 201 + Location) and GET (strong ETag before store, 304 without storage, Results.Stream with range processing → 206/416/If-Range, CSP frame-ancestors 'self', nosniff, private immutable vs no-store, inline vs attachment, tellma-blobs rate policy, Tellma-Client exemption). src/connector/azure-blobs/Tellma.Connector.AzureBlobs.Adapter (AzureBlobStore with per-tenant containers, IfNoneMatch=ETag.All create-only, 409→BlobAlreadyExists, 404 ContainerNotFound forget/ensure/retry, ranged OpenRead with ReadBufferSize, bounded-parallel deletes, AzureBlobStoreOptions, AddAzureBlobStore) + Adapter.IntegrationTests on Azurite. Tellma.Defaults.Azure gains the Imaging and Adapter references and the AddAzureBlobStore(Tellma:Blobs:Azure)/AddFileSystemBlobStore(Development)/AddTellmaSkia calls. ARCHITECTURE.md updates listed in §12.
- **Tests:** unit: ETag/header computation (inline per served type, attachment otherwise and on download=true, frame-ancestors). integration: the shared conformance suite against Azurite (Linux runner; skipped without Docker on Windows), container naming and EnsureTenant, create-only conflict; acme e2e: upload → save User.ImageId → GET 200 immutable → If-None-Match 304 → Range 206 and unsatisfiable 416 → save null → GET 404; non-member 404 tenant-not-found; missing Tellma-Client 403 csrf-rejected; oversize 413 before the body is read; ReadOnly and Frozen tenants: upload 403 with each code, download 200
- **Done when:** A developer on Windows without Docker can upload and see an avatar in the acme host on the file-system store. The Azurite suite is green on Linux CI. UseAzureDefaults outside Development registers the Azure store, and the `blobs` check fails when the Azure section is missing.

### PC-090 — Self-service: me, me/save, me/locale, preference bags and the activity extra

- **Analysis id:** `0018-d` · **Spec:** 0018 · **Layer:** backend · **Tier:** 1 · **Est. LOC:** 4,500
- **Sections:** §3.1 (Me/SaveMe/SetMyLocale/SetMyPreferences/DeleteMyPreferences/GetPreferences/SetPreferences/DeletePreferences, UserActivityContributor), §3.2 locale rules (LocaleEmpty, LocaleResetConflict, path mapping), §3.5 except test-notification and notification preferences, §8.1 me/* and preferences rows
- **Gated on (DEPS.md):** PC-088, PC-062, PC-084
- **Analyst dependency notes:** 0018-c; 0014 (UserPreferences statement, MaxPreferenceKeys, MaxPreferenceValueBytes, UserStamps); 0015 §13.3; 0015 §5.3; 0016 §3.4; 0013 §2.5; 0017-c
- **Deliverables:** MeAsync (one RT: profile from ConnectedUser, EvaluateAllAsync, tags, bag); SaveMeAsync (load+stamp check, enlist with the invoker's frame via IOpenWriteHost self-pair, MapPath dropping the row index, Override); SetMyLocaleAsync (in-memory validation, ContributeLock + Update<TUser> with Reset → NULL + invariants + read-back in one RT, AllowWhenReadOnly); me/preferences set/delete (key grammar, size, duplicates, in-statement MaxPreferenceKeys THROW); admin preferences get/set/delete under core.User × Preferences with the target's tag bump and UserPreferencesChangedByAdministrator log; activity extra over core.UserStamps
- **Tests:** unit: SaveMe mapping, RoleMemberships null, 409 with no enlistment, Override; key grammar incl. hyphenated/numeric segments. integration: me/save by a member without a Save grant changes only its members and bumps PreferencesTag; the admin save of one's own row is 403 no-grant; stale ModifiedAt 409; own-row read via UserAccessCriteria; SignatureId through me/save and the user save; every me/locale case (each member, all five, Reset, LocaleEmpty, LocaleResetConflict, regioned language, alias zone Asia/Calcutta, unchanged value writes no history, ReadOnly stored, Frozen 403, tag in Tellma-Version-Tags); regional formats; preference statements and the admin routes; §8.1 budgets
- **Done when:** The client shell's boot calls (users/me, me/locale, me/preferences) work on the acme host within the round-trip budgets.

### PC-091 — Roles stack extras and AccessService

- **Analysis id:** `0018-e` · **Spec:** 0018 · **Layer:** backend · **Tier:** 1 · **Est. LOC:** 2,500
- **Sections:** §4, §4.1-§4.3, §3.7, §8.1 roles and access rows
- **Gated on (DEPS.md):** PC-090, PC-078, PC-043, PC-039, PC-062, PC-007, PC-070, PC-077
- **Analyst dependency notes:** 0014 §3.7; 0014 §5.1; 0014 §9; 0014 §10; 0015 §5.3; 0010 §F.7
- **Deliverables:** RoleService<TRole> with [DetailsExtras("members","problems")]: members AddExtra over core.RoleMembership restricted to IdsSource, filtered by the caller's core.User × Read rebased with FilterTree.Via, ended memberships omitted, ordered by User.Name, max 500 rows, absent when Denied; problems as AddComputedExtra using QueryexEngine.Validate under AccessLimits and the row stamp; PermissionProblem record. AccessService [ApiRoute("access")] with check (self 0 RT; other: RequireAsync core.Role × Read, visibility RT, EvaluateForAsync, 200 cap, unknown securable → Denied with problem code) and holders
- **Tests:** unit: problems per drift condition against a scripted registry/schema; AccessService self vs other, core.Role Read requirement, the 200 cap, holders over a fake resolver. integration: check/holders without core.Role × Read → 403; a hidden holder counted but not listed under a filtered grant; public role answers Everyone; an ended membership lists no holder; members extra under a filtered grant, at the 500 cap, absent when Denied, carrying windows; a role with a drifted permission answering problems
- **Done when:** The role page's single details read returns both extras in one round trip. The access/check and access/holders budgets are asserted.

### PC-092 — AcmeFeature: entities, actions, settings and development data

- **Analysis id:** `0018-g` · **Spec:** 0018 · **Layer:** backend · **Tier:** 1 · **Est. LOC:** 5,000
- **Sections:** §1.2 (AcmeComposition), §1.4 (except AcmeReports, ReportSeed, [Assignable]/assign/post/acme.invoice.assigned), §6.1 Development bootstrap, §6.2, §6.3 (except assignments and report seeds)
- **Gated on (DEPS.md):** PC-091, PC-001, PC-087, PC-088, PC-084, PC-016, PC-062, PC-085, PC-049, PC-089
- **Analyst dependency notes:** 0011 §1.2; 0018-b; 0018-c; 0017-c; 0013 §5.3; 0015 §11.2
- **Deliverables:** distributions/acme/src/Tellma.Distro.Acme: AcmeFeature [Requires(gl)]; Currency [Cacheable]; Customer (CustomerType enum, decimal CreditLimit, ImageId customer-image Avatar, Contacts child) + CustomerService set-credit-limit; SalesInvoice ([DisplayNameSource], Lines child with CenterId → gl.Center, Attachments child with invoice-attachment) + SalesInvoiceService close; MyCenter.ManagerId; RelatedDocumentsContributor (related-documents extra); AcmeSettings (five keys incl. Secret); Strings.resx/Strings.ar.resx for every entity, member, action and Acme_Settings_* key; AcmeSampleCenters (40-node, four-level tree); AcmeSampleDataStep (Order 200, Development only: currencies, ~60 customers with images on some, role Sales, e2e principals, 20 invoices, four centers deactivated); Development configuration (InProc identity, Support enabled, no Tellma:Blobs:Azure)
- **Tests:** integration (Tellma.Distro.Acme.IntegrationTests): Development data counts (40 centers with four inactive, two currencies, about 60 customers, Sales role, 20 invoices); none of it outside Development; set-credit-limit and close actions; related-documents extra; resource audit over the distribution assemblies
- **Done when:** `migrate` on a fresh clone provisions tenants 1 and 2 with the sample data, the administrator signs in, and the client specs' acme.Customer/acme.SalesInvoice/gl.Center surfaces exist. The e2e-principal invite issue (see contradictions) is resolved explicitly.

### PC-093 — InitialTenant migration, deployed bootstrap and distribution parity

- **Analysis id:** `0018-h` · **Spec:** 0018 · **Layer:** backend · **Tier:** 1 · **Est. LOC:** 3,500
- **Sections:** §6.1 (deployed provision --admin-email, sandbox flow), §6.2 (composition parity, no tracked secret), §7, §9 distribution suite, §10 Docs + CI smoke
- **Gated on (DEPS.md):** PC-092, PC-088, PC-034
- **Analyst dependency notes:** 0018-g; 0018-c; 0011 §6.1; 0011 §6.2
- **Deliverables:** Migrations/Tenant/<ts>_InitialTenant in Tellma.Distro.Acme.Migrator generated with dotnet-ef and hand-reviewed (schemas, core tables of every landed spec, gl.Centers, acme tables, __Tellma* tables, UDTTs, HasData rows; no IDENTITY/rowversion/trigger/MERGE); the migrator provision path resolving UserService<TUser> and running ExecuteActionAsync('invite') in the system scope after the steps; IdentityServerClientOptions bound in the migrator host; model-parity test (GetPendingModelChanges empty) on Windows and Linux; ARCHITECTURE.md updates listed in §10
- **Tests:** integration: migrate then provision from an empty server creates gl.Centers and acme tables and records gl.sample-centers and acme.sample-data at Version 1; admin@localhost Invited then Joined on first sign-in; a deployed-style provision --admin-email invites through the in-proc identity server (and enqueues the welcome email once 0022 lands); a bumped step version re-runs idempotently; model parity; no tracked secret
- **Done when:** Parity is green on both OSes and the provisioning suite passes. est_loc excludes the generated migration and snapshot (likely 8-12k generated lines). The migration must be regenerated whenever a later spec adds a table until first release.

### PC-094 — Acme client app and C# metadata contributors

- **Analysis id:** `0024-f` · **Spec:** 0024 · **Layer:** both · **Tier:** 1 · **Est. LOC:** 3,000
- **Sections:** §2.1 (scaffold, esproj, angular.json), §2.2 (Acme app.config), §3.1 (distribution/features/routing sections, Version), §3.2 (CoreFeature contributor, deterministic output, absent sections as []), §5.1 (Acme manifest and icons), §7.6 (default brand files), §14 ('What Acme adds', minimal)
- **Gated on (DEPS.md):** PC-073, PC-001, PC-025, PC-034, PC-046, PC-085
- **Analyst dependency notes:** 0024-e; 0011 §1.2; 0011 §5.1; 0011 §6.1; 0013 §10.4
- **Deliverables:** distributions/acme/src/Tellma.Distro.Acme.Client per §2.1: esproj with BuildCommand `pnpm run build:esproj`, package.json scripts, angular.json (serviceWorker:false, outputHashing:all, the optimization object of Decision 33), tsconfig, eslint config, index.html, manifest and icons, styles.css with the tm.theme delta, main.ts, app.config.ts, and a minimal app/acme.feature.ts. Default brand SVGs in @tellma/core/brand. The C# CoreFeature IClientAssetContributor, with ClientAssetContext and TellmaRoutes if 0011 has not shipped them. distributions/acme/Directory.Build.props Version. CI: `tellma-client build` on Acme with zero problems; `dotnet publish` of the Web project builds the SPA into wwwroot.
- **Tests:** C# unit: deterministic bytes and names with no database; Tellma-Build equals distribution.version; absent sections emitted as []. Integration: Acme `tellma-client build` exits 0 in CI on both OSes. Hosting matrix tests (headers per response class, metadata chunks anonymous and public, fallback matrix) only if 0011 did not already add them.
- **Done when:** Acme builds end to end through MSBuild (esproj → Migrator emit → ng build → checks) with zero problems. The served output has hashed names and a worker.
- **Planner note:** Consumes 0011-d's CoreFeature IClientAssetContributor; do not create a second one.

### PC-095 — Web integration suite over the acme reference distribution and snapshot gates

- **Analysis id:** `0016-f` · **Spec:** 0016 · **Layer:** backend · **Tier:** 1 · **Est. LOC:** 4,500
- **Sections:** §13.1-§13.3 (AspNetCore.IntegrationTests), §13.2 fixtures, §3.10 contracts.json snapshot, §12.1 OpenAPI snapshot, §9.4-§9.5, §10.2 alert queries, §14 docs
- **Gated on (DEPS.md):** PC-082, PC-001, PC-085, PC-087, PC-088, PC-091, PC-093
- **Analyst dependency notes:** 0016-e; 0018 Core and GL stacks; 0011 §1.2 reference distribution, §6 migrator
- **Deliverables:** test/core/Tellma.Core.AspNetCore.IntegrationTests (in-process test server over tenants provisioned by 0011's migrator: admin, restricted filtered-Read member, Save-without-Delete member, deactivated member, non-member; Sandbox tenant; a tenant driven through every state), session fixture via 0011's session store with Tellma-Client: test/1, end-to-end Center/User/Role operations, verdict matrix, step-up 401, traversal 403 restricted-path, version tags, abort semantics, round-trip budget per operation, child surface end to end, Tellma-Activity: background; contracts.json and OpenAPI snapshot tests in distributions/acme/test/Tellma.Distro.Acme.Web.Tests with the Contracts chunk equality test; alert queries under infra/monitoring/; ARCHITECTURE.md rows
- **Tests:** integration (Category=Integration, Windows LocalDB + Linux Testcontainers), snapshot/golden (contracts.json, openapi web.json)
- **Done when:** Suite green on both OS; snapshot gates fail on drift; tellma.data.roundtrips per operation at or under 0015's budget

### PC-096 — client/sync service, TmCacheRefresher and TmLaunchCache

- **Analysis id:** `0025-b` · **Spec:** 0025 · **Layer:** both · **Tier:** 1 · **Est. LOC:** 5,500
- **Sections:** §3.1–§3.3, §4.1–§4.4, §5.1–§5.3, §15 (sync telemetry)
- **Gated on (DEPS.md):** PC-075, PC-071, PC-016, PC-078, PC-090, PC-035
- **Analyst dependency notes:** 0025-a; 0024-d; 0013 §2.5-§2.8; 0013 §5.6; 0014 §3.4; 0014 §7.3; 0016 §3.4; 0018 §3.5; 0021 §6.4 (IRealtimeStatus interface)
- **Deliverables:** C#: ClientSyncService.SyncAsync ([ApiAction("sync", MemberOnly, Idempotent, Mutation=false)], NoActivityStamp) running one Read batch (connect prologue plus the preference-rows SELECT), the tag comparison, conditional Settings/Me, StaleEntities, and TenantFacts with Realtime from IRealtimeStatus; records in Abstractions.Api; registration in CoreFeature. Client: TmCacheRefresher (observe with wanted tags; refresh with coalescing, the tm:<slug>:<sub>:<t>:sync lock, re-check, 2 s throttle, background except cold, one-transaction apply, error routing; adopt for settings/me/preferences/locale; the membership-window timer with jitter and the 30 s→10 min backoff; lastSyncAt). TmLaunchCache (docs signals, warm, cold refresh and its failure paths, entitySet lazy loading via {segment}/all, tenants/distribution stale-while-revalidate, revalidateUser). The TmApi header observe seam wired. cache.changed handling as a function the leader calls later (§4.4).
- **Tests:** C# integration on real SQL: each Held combination and the members it returns; one round trip when warm; the composite tag; a cold call; unknown names ignored; Realtime from IRealtimeStatus. Client unit with a fake clock and API: an own write's header never syncs; lock serialization across two instances; re-check skips; the 2 s throttle; foreground errors presented; each adoption member's patch; the validUntil timer and its jitter; a hidden tab refreshing on becoming visible; two tabs' timers giving one sync; an access refresh's held; the device-clock-ahead backoff at 30 s, 60 s and up to 10 min.
- **Done when:** The cold entry renders after one sync. A warm header with held tags triggers no sync. ClientSyncService passes its integration suite under Category=Integration.

### PC-097 — Job schema, handler registry and enqueue

- **Analysis id:** `0020-b` · **Spec:** 0020 · **Layer:** backend · **Tier:** 1 · **Est. LOC:** 5,000
- **Sections:** §1.1, §1.3, §1.4, §2.1, §2.3 (model config: shadow StateJson/LeaseToken/ConfirmedKeys, UDTT exclusion), §2.4, §2.5 registration, §3.7 realizers, §4.2–§4.4, §6.10 (JobWorkerState table + HasData), §7.1–§7.2 tables + Schedule entity, §7.9 HasData realisation, §15 composition gate + jobs.handlers/jobs.schedules checks
- **Gated on (DEPS.md):** PC-036, PC-019, PC-029
- **Analyst dependency notes:** 0020-a; 0011 composition checkpoint; 0012 data-access + fixture tier; 0013 value sets (core.TimeZone); 0014 users/WellKnownIds; 0015 contracts (IActivatable, [Stack])
- **Deliverables:** TellmaDbContext model configuration for core.Jobs (8 filtered indexes, CHECKs, LOCK_ESCALATION=DISABLE, sq_Jobs), core.Schedules (temporal, UDTT, sq_Schedules), core.ScheduleStates, core.JobWorkerState; Schedule entity + enums; JobHandler/BuiltInSchedule/RetiredJobKey realizers with every composition problem; built-in HasData rows (Schedules + ScheduleStates, NextDueAt NULL); JobStatements.Enqueue (sp_sequence_get_range, owner JobId UPDATE from EntityMetadata, golden); JobQueue (Enqueue/EnqueueAsync, ConfirmedKeys copy, traceparent copy, RunAsUserId defaulting, programmer-error validation); an internal tenant-poller wake seam (used later by INotifier, run-now, tick step 2); executor whitelist of EXEC sys.sp_sequence_get_range
- **Tests:** unit: realizer problems (grammar, ranges, core. reservation, duplicate key, key both registered and retired, built-in naming retired key, built-in ids/bands/uniqueness, built-in zone outside set), IStartupCheck jobs.handlers/jobs.schedules invoked directly, enqueue golden SQL; integration(SQL): schema migrates on LocalDB/Testcontainers with RCSI on/off, CHECK constraints refuse invalid lease/pending-confirmation combos, enqueue riding a persist batch with Entity (3,000 owners in one UPDATE, ids in request order, post-commit nudge observed, two entity types refused), LabelKey/LabelArgumentsJson stored
- **Done when:** All four tables create cleanly (including the ScheduleStates/Jobs/Schedules cascade graph — see gaps), enqueue integration tests green, no existing test regresses

### PC-098 — Lease statements, outcomes and the completion transaction

- **Analysis id:** `0020-c` · **Spec:** 0020 · **Layer:** backend · **Tier:** 1 · **Est. LOC:** 8,000
- **Sections:** §3.4–§3.6, §3.8, §5.1–§5.6, §8, §9 (process/settle spans, log events JobClaimed/JobCompleted/JobHandlerFailed/JobCompletionFailed), §10 (completion-batch notices, job.changed on batch)
- **Gated on (DEPS.md):** PC-097, PC-003, PC-035, PC-050, PC-066, PC-077
- **Analyst dependency notes:** 0020-b; 0010 §F.1; 0015 job frame §13.3 + IConfirmations §7.5; 0014 connect prologue; 0021-a
- **Deliverables:** JobStatements claim (with token re-collection), renew, append, complete (goldens); entity join load appended to the claim; run-as partitioning; outcome engine (§3.4 table, CancelRequested/LeaseLost/HostStopping, Released vs Retry, MaxAttempts→Failed, full-jitter backoff, PendingConfirmation with {count, items} 64 KB/100-id trimming and the 16-key/2,000-char cap); completions without a handler (CancelRequestedAt, AttemptsExhausted, EntityMissing, UserInactive) in system scope; job scope building (RequestContextSnapshot, connect, negotiation, IConfirmations seeded from ConfirmedKeys), job frame registered as IOpenWriteHost bound to the completion batch; completion transaction ordering and every §5.6 failure path (LeaseLost, transient, ambiguous, guard failure → fresh Retry batch, 50401 → inactive path, constraint/invariant → ValidationException translation, other → Jobs.Internal); NotifyOnSuccess + core.job.failed/core.job.pending-confirmation via INotifier; IJobProgress Report/FlushAsync/Append (OnCommitted state promotion, job.changed on Append); a test-only 'poll once for tenant' driver
- **Tests:** unit: §3.4 outcome mapping for every reason/exception shape incl. a multi-item batch with one cancelled, Jobs.Internal + ErrorTraceId equal to JobHandlerFailed's trace id, backoff bounds, partition order; golden SQL; integration(SQL): claim contention from two connections (disjoint, DueAt/Id order), plan test (filtered-index seek under TOP(@room)), renew/append/complete fencing (Job.LeaseLost rolls back handler statements and enlisted groups), enlisted group committing with outcome, constraint violation failing all items, guard failure retrying with statements discarded, Released un-counting, resumed item reading its checkpoint, the four handler-less completions, Append publishing job.changed only on commit, confirmations end-to-end (stored keys, PendingConfirmation, 17 keys, 120 KB warnings), CallerAuthority not refusing internal-channel writes
- **Done when:** Every §16 integration pin for §3–§5 and §8 green under RCSI on and off; a fixture handler runs enqueue→claim→complete through the driver

### PC-099 — Notifier, notifications schema and rendering

- **Analysis id:** `0021-b` · **Spec:** 0021 · **Layer:** backend · **Tier:** 1 · **Est. LOC:** 7,000
- **Sections:** §1.4, §2.1, §2.2 (entity + model), §2.4 (NotificationRenderer), §3.3, §3.4, §3.5, §4.2 (table), §10.1 (created/suppressed/replaced/withdrawn/delivery_enqueued), §10.2
- **Gated on (DEPS.md):** PC-035, PC-097, PC-022, PC-029
- **Analyst dependency notes:** 0021-a; 0020-b; 0013 localizer/LocalizationScope; 0014 users
- **Deliverables:** core.Notifications (indexes, LOCK_ESCALATION, sq_Notifications), core.NotificationPreferences table, NotificationRowList/NotificationSliceList/NotificationPreferenceList registration, NotificationStatements (replace, insert with Ongoing variant, withdraw x2, delivery enqueue with the channel condition built at startup from channels paired with a sender), Notifier (C# validation → InvalidOperationException, recipient dedup/Ordinal, slicing, parameter accounting, OUTPUT reading, one OnCommitted callback publishing inbox.changed + poller nudge), NotificationRenderer (ICU named args by JSON kind, LocalizedText slot, detail line, missing resource → key, malformed JSON → NotificationArgumentsInvalid)
- **Tests:** unit: §3.1 validation paths, slicing, channel-only composition, delivery condition omitted for InboxOnly/no sender, renderer cases; integration(SQL): ids contiguous, inactive/unknown recipient skipped, muted mutable skipped, non-mutable ignores disabled inbox row, severity override, Suppress/Ongoing/Replace semantics (both result sets), withdrawal all/named + inbox.changed to exactly those users, delivery enqueue with a fake channel (no job, one job per qualifying slice, DefaultEnabled=true channel, no sq_Jobs range for InboxOnly), nudge after commit, RCSI on/off
- **Done when:** INotifier usable from 0015 contexts and 0020 completion batches; all notifier pins green

### PC-100 — JobWorker hosting, polling, drain and the gap hold

- **Analysis id:** `0020-d` · **Spec:** 0020 · **Layer:** backend · **Tier:** 1 · **Est. LOC:** 7,500
- **Sections:** §6.1–§6.10, §5.2 (renewal loop, progress buffer, 5-point/5-s jump), §9 (root span with links, receive span), §10 (gap notices, job.changed after round trips), §12.1–§12.3, §13, §17 docs
- **Gated on (DEPS.md):** PC-098, PC-099
- **Analyst dependency notes:** 0020-c; 0011 registry/listeners/hosting
- **Deliverables:** JobWorker (BackgroundService + IHostedLifecycleService + ITenantStateListener), TenantPoller (bounded channel wake, PeriodicTimer on TimeProvider, adaptive interval), poll round trip assembly (gap check/hold, claims for keys with free slots, tick-step-1 hook, per-minute backlog sample, hourly orphan sample + retired-key cancel, heartbeat stamp last), ReadOnly key filtering, MaxParallelBatches semaphore + per-(tenant,key) MaxConcurrency, renewal scheduler, cancellation token tree, StoppingAsync drain with final renewal bound to ShutdownTimeout and abandonment, gap notifications in system Persist batches, retired-key pending-confirmation withdrawals, every instrument of §13 (orphaned gauge), log events, infra/monitoring jobs KQL + generalised name test, ARCHITECTURE.md updates
- **Tests:** unit (fake TimeProvider): interval doubling/reset, immediate re-poll after full claim or full tick, nudge coalescing (ten nudges one wake), MaxParallelBatches/MaxConcurrency limits, drain rules (Released for untouched, running left leased, final renewal binding ShutdownTimeout, abandonment warning), jump renewal at most once per 5 s, log events, JobsTelemetryNames vs infra/monitoring; integration(SQL): gap hold with per-requester counts, heartbeat throttle, stamp last, retired-key cancel (running row only after lapse), poll re-run after injected deadlock and killed connection returning every leased row with Attempts=1, ReadOnly tenant claiming only RunsWhenReadOnly keys, Frozen never polled, e2e fixture handler with fake INotifier/IClientEventPublisher incl. InboxOnlyFailureNotices
- **Done when:** Worker runs unattended in a composed host; all §6 pins green; instruments asserted; ARCHITECTURE.md Hosting/Library/Observability sections updated

### PC-101 — Printer-based entry points and discovery additions

- **Analysis id:** `0010-f4` · **Spec:** 0010-F · **Layer:** backend · **Tier:** 1 · **Est. LOC:** 4,000
- **Sections:** §F.10, §F.13, §F.20, §F.21
- **Gated on (DEPS.md):** PC-012, PC-010
- **Analyst dependency notes:** 0010-f3
- **Deliverables:** BindParameters with ParameterBindingOptions: canonical printing with the literal table (numeric, string, bool, date, datetime, datetimeoffset, guid, typed null), folding of omitted names, and caller errors. MeasureCondition: the distinct aggregation conditions, OR-joined, null when any aggregation is unconditioned. ParameterUse.ComparedWith, set from the bound tree for comparisons, in elements and descendantOf/ancestorOf keys (root-relative inside Via, silent inside Exists). QueryDiscoverySpec.Conjuncts with FilterConjunct and FilterConjunctOperator for top-level AND atoms comparing against literals, is null, and descendantOf. The inspection tool shows conjuncts and measure conditions.
- **Tests:** unit: a vector per literal type and per folding row; the printer round trip parse(print(t)) = t; the property test that bind-then-compile equals compile-with-arguments; MeasureCondition table rows, deduplication and a false fold; ComparedWith corpus rows per operator and operand order, conflicting paths, Via prefixes, Exists silence; conjunct rows per operator, operand order and literal form, with the skipped atoms and sites across two leaves. integration: the property test differential on the fixture; a measure's aggregation over the rows its condition admits.
- **Done when:** Every vector is green, the round trip holds, and discovery answers as before with Conjuncts false.
- **Planner note:** Promoted to Tier 1: the discover endpoint (0029-f) needs §F.13 and §F.21.

### PC-102 — queryex/discover server endpoint

- **Analysis id:** `0029-f` · **Spec:** 0029 · **Layer:** both · **Tier:** 1 · **Est. LOC:** 3,000
- **Sections:** §9 (all)
- **Gated on (DEPS.md):** PC-086, PC-068, PC-078, PC-101, PC-033, PC-077
- **Analyst dependency notes:** 0015 §5.1, §5.2, §7.3; 0016 §2.2, §2.4, §3.4; 0014 §3.7; 0010 §F.13, §F.14, §F.21; 0012 §11.1
- **Deliverables:** Tellma.Core.Abstractions.Api: QueryexDiscoverRequest, QueryexParameterInput, QueryexProfile, QueryexDiscoverResult, QueryexConjunct, QueryexParameterInfo, QueryexPathInfo, QueryexSite; Tellma.Core.Api QueryexService [ApiRoute('queryex')] with DiscoverAsync (member-only, 404 for an unknown root, 400 for Measures without Aggregate, guarded runner with no statement, DiscoverQuery over all clauses incl. Measures and conjuncts, CompileQuery under PipelineLimits with SQL discarded, Permission profile matching role-save validation, QX3007 dropped, traversal refusals as Query.PathNotReachable diagnostics, 200 always); registration via contribution.ApiService<QueryexService>()
- **Tests:** integration on SQL Server: each §9 step, the same diagnostics as query, PathNotReachable under a filtered grant, an unconstrained parameter, Permission profile matching role save, measures with QX4010/QX4011 at Measures[<i>] and 400 without aggregate, conjuncts across two filters with wire-encoded literals, no round trip when warm
- **Done when:** §17 'queryex/discover (C#)' suite green on Windows and Linux; route in the Contracts chunk's idempotent list.
- **Planner note:** Measures (request member, step-1 400, step-2 measure diagnostics) are deferred to 0010-f6 (Tier 3): the request record omits them, or a non-null value answers 400 not-supported, stated as a deviation. 0010-f6 adds them with this endpoint's measure tests.

## Phase 6 Client foundation, shell and entity model

### PC-103 — Bootstrap, derived routes, URLs, query codec and route host

- **Analysis id:** `0024-g` · **Spec:** 0024 · **Layer:** both · **Tier:** 1 · **Est. LOC:** 5,500
- **Sections:** §2.2 (provideTellma, bootstrapTellma), §4.3, §4.4, §9.1, §9.2, §9.3 (TmLaunchRedirect), §9.4, §9.5
- **Gated on (DEPS.md):** PC-094, PC-067, PC-069, PC-071, PC-075, PC-096, PC-020, PC-026
- **Analyst dependency notes:** 0024-b; 0024-c; 0024-d; 0025-a; 0025-b; 0023-b; 0023-d
- **Deliverables:** bootstrapTellma (zoneless; gated on __tmBoot.state) and provideTellma (derived router, initializers, tokens). TmApp. TmRouteHost: skeleton kinds in the click's frame on page-header geometry; the cold-entry await on 0025's refresh; the tmPage securable gate rendering the forbidden view; chunk failure → TmUpdates.check; page scope via the seam. TmTitleStrategy and focus to [tmPageTitle]. The §9.2 route table with canMatch on TmEntities.bySegment (implemented over Core-chunk summaries until 0029). TmLaunchRedirect. lastTenantId writes. TmUrls (forTenant, describe, preload, list/parseList, picker). The TmSearchQuery codec with the #d= fragment. Placeholder /shell route components until 0027.
- **Tests:** Unit: §9.2's table; /17/currencies/5 matching no details route and target(...) returning null; §9.3's conditions; forTenant, the routed default and the no-tenant rule; /017, /17/ and /17/Centers; §9.5's tables; search(parseSearch(x)) is canonical x; list over parseList likewise, a malformed d parsing to a null payload; a #d= payload with Arabic text and & survives navigateByUrl and parses from the raw hash; describe telling two lists apart by d; q stripped of bidi controls with ZWNJ kept; preload loads chunks once and runs no guard. Harness: route host skeleton in the next frame; cold entry waits for the launch documents.
- **Done when:** A fake-API app boots, routes and renders skeletons with no network before a warm render. Every §13 'Route assembly…' bullet is green.
- **Planner note:** Ship placeholder components for the home, search and details routes (replaced by 0035-a1, 0030-b, 0031-b), and make the page-route canDeactivate skip same-route, same-component parameter changes (the settings tabs); report the ruling.
- **Later seams (not gates):** PC-105 (0029-b): the route host's tmPage securable gate reads a TmPermissions seam token (can(resource, action)) whose default allows everything (the server stays the authority); 0029-b provides the real service and adds the forbidden-view harness case. (Kickoff audit, 2026-11-08.)

### PC-104 — Localization, strings, server text and theme

- **Analysis id:** `0024-h` · **Spec:** 0024 · **Layer:** both · **Tier:** 1 · **Est. LOC:** 5,000
- **Sections:** §7.1–§7.7, §7.9, §2.2 (platform icons file, styles.css), §10.2 (Accept-Language and Tellma-Calendar sources)
- **Gated on (DEPS.md):** PC-103, PC-096, PC-004
- **Analyst dependency notes:** 0024-g; 0025-b; 0023-a
- **Deliverables:** TmLocalization (language/region/digits/locale/dir/calendar/contentSlot/timeZone/displayName/languages/calendars/localeSave; setLanguage/setCalendar/setLocale) with switches in one change-detection pass. The coalesced `users/me/locale` write: 600 ms, one in flight, offline retry, pagehide send, adopt. String loading and merging (string pack at the root, then tmUi, then tm), with English fallback and the string-key-collision check. The Transloco resolver that strips bidi controls. TmServerText. Lazy calendar entry points. Theme glue (boot.patch theme, boot.changes, the theme-color meta effect). @tellma/core/styles.css. src/icons/platform-icons.ts. tmEnglish(); tmArabic() in locale-ar. The locale browser check with fallback telemetry.
- **Tests:** Unit: §7.1's table; §7.7's resolution and locale strings, and the browser-check fallback that leaves Accept-Language unchanged; switch atomicity; locale-write coalescing (two changes within 600 ms send one write, a reset member goes in reset), adoption and retry; TmServerText with a LocalizedText argument. @cross-engine unit: every offered (language, region) pair resolves in each engine, apart from the known gap list.
- **Done when:** Language, calendar, region and theme switch in place with no request (beyond the locale write). Every §13 'TmLocalization' bullet is green.

### PC-105 — Primary entity registry: tmEntity, TmEntities, TmDisplayNames, TmPermissions

- **Analysis id:** `0029-b` · **Spec:** 0029 · **Layer:** both · **Tier:** 1 · **Est. LOC:** 6,000
- **Sections:** §1 (primary), §3, §4 (all), §5 (all), §6.2 (tmRowsToObjects), §7
- **Gated on (DEPS.md):** PC-102, PC-086, PC-067, PC-096, PC-104
- **Analyst dependency notes:** 0029-a (chunk shapes; fixture JSON); 0024 §3.1, §3.6, §4.4; 0025 §3.1, §3.3; 0024 §7.9
- **Deliverables:** primary src/entity/: tmEntity and declaration types (incl. the 0030 tile/custom-filter/page-action types it exports, TmColumnInput, TmDetailsDeclaration, TmEntityActionDeclaration, TmPickerDeclaration, TmPath<N> typed against TmEntityShapes), tmCheckEntityDeclaration (unknown-path, unknown-action, operation-missing, entity-not-routable), TmEntities (summary, bySegment, declaration, label, load once, get with shape gating and twin labels, contentDir, enum, resolve with value/text/navigation/image/file ends and slash labels), TmDisplayNames (of, parts, select, label, labelParts, remember, rememberRelated, resolve batching with background query, 5,000-row LRU per entity, tag-move rules, cacheable entitySet path, id coercion by key type), tm-display-name component, display-name-golden.json (TS side, clientOnly rows), tmRowsToObjects, TmPermissions (can, filtered, allows incl. details-page rule and tenant-state gating, sensitive, reachable with child-root and core.Blob rules, label, disabledReason); core-testing tmTestEntities()
- **Tests:** unit: shape gating, twin labels for one to three languages, contentDir, resolve for every end kind and unknown paths, allows per operation with details false for an entity lacking a details member, isSystem, reachable vectors incl. child roots and core.Blob, label for entity/settings category/other/unlisted, disabledReason per operation, allows/disabledReason under ReadOnly and Frozen incl. an allowWhenReadOnly action, TmDisplayNames.label with TmDecimal id for an int key and number for a long key, cache cleared or kept by settings/permissions moves; display-name golden rows (TS); tmCheckEntityDeclaration codes
- **Done when:** Unit suites and the TS half of display-name-golden.json green; primary share ≤ 6 KB gzip; 0024's route host and route matching (bySegment, details canMatch) compile against these members.

### PC-106 — Shortcut registry, tmSearchNormalize and shell seams (primary)

- **Analysis id:** `0027-a` · **Spec:** 0027 · **Layer:** client · **Tier:** 1 · **Est. LOC:** 3,500
- **Sections:** §4 (all), Appendix A, §9.4 (tmSearchNormalize), §5.1 (TmShell, TM_SHELL_LAYOUT, frame CSS variables), §7.5 (TmShellMain)
- **Gated on (DEPS.md):** PC-065, PC-004, PC-045, PC-069, PC-104
- **Analyst dependency notes:** 0024 §1/§2.2 scaffold; 0023 §3.2, §5.4; 0010 §C.12; 0024 §8.1 TmSession.state; 0024 §7.9 tmStripBidiControls
- **Deliverables:** primary nav/: TmShortcuts (one bubble-phase document listener, skip rules incl. composition/repeat/busy overlay/inactive session, tmKeysMatch, editable-target and Option-character rules, modal rule, run/preventDefault/stopPropagation, tm.shortcut.used), family checks and refusals (reserved ids, TM_GRID_KEYS with modifiers, TM_BROWSER_KEYS, live duplicates) with TmShortcutConflictError in dev and tm.shortcut.conflict in prod, TM_RESERVED_SHORTCUTS, TM_BROWSER_KEYS, active signal; tmSearchNormalize (bidi strip, NFKD, tashkeel, Arabic letter folding, digit folding, punctuation); TmShell (immersive with DestroyRef), TmShellMain (override/replace/restore, restore on path change), TM_SHELL_LAYOUT; frame.css root variables
- **Tests:** unit: §4.2 steps (bubble phase, defaultPrevented, composition, repeat, editable targets, Option-typed characters, modals), Mod+K and Mod+/ in a field on an Arabic layout, every §4.3 refusal, labels; tmSearchNormalize vectors (bidi controls removed, ZWNJ kept, Latin diacritics, Arabic letters, tashkeel, tatweel, digits)
- **Done when:** Unit suites green; API golden for the primary members; 0024's error presenter can call TmShellMain and 0026-a can register page shortcuts.

### PC-107 — Preferences, page state, drafts, tree expansion and recents

- **Analysis id:** `0025-e` · **Spec:** 0025 · **Layer:** both · **Tier:** 1 · **Est. LOC:** 4,500
- **Sections:** §9.1–§9.5, §10, §11, §12, §13, Appendix A, §15 (prefs and drafts telemetry, dump section)
- **Gated on (DEPS.md):** PC-096, PC-075, PC-071, PC-103
- **Analyst dependency notes:** 0025-a; 0025-b; 0024-d; 0024-g (TmUrls.describe)
- **Deliverables:** TmPreferences and tmPreferenceKey (dev checks on key grammar and platform first segments; fallback-equal deletes; the 32 KB cap), JSON encoding with per-string parse caching, the overlay/dirty model, prefs.changed broadcast, and flush policy: curated at once; write-behind at 2 s and once per 10 s, at once when hidden or over 8 KB, nav.recent every 5 min; pagehide send capped at 16 KB; transient/read-only/Frozen/key-cap/rejected handling; adoption. TmPageState (LRU of 20, markStale by entity via TmUrls.describe, permission-move stale marking, language-change forget). TmDrafts (session storage tm.draft:<sub>:..., 300 ms debounce, pagehide/hidden flush, quota fallback, dirtyCount, clearAll, sub refusal). TmTreeExpansion (session partition, 250 ms debounce, 500-id cap, replace). TmRecents (2 s dwell, kinds, de-duplication, cap of 10, title/icon via the TmNavigation seam, access filter on read). The dump-section builder (names and tags only).
- **Tests:** Unit: preference read order; a same-frame set; flush timings; pagehide bodies and cap; adoption; a received value drops the dirty one; the overlay's end; key-cap drop and restore; tenant-frozen dropped silently as read-only; no flush while Frozen, then flushed on Active; nav.recent's 5 min spacing. Page state: LRU, markStale on search and details entries, permission move, languages change. Drafts: debounce, pagehide flush, quota, sub refusal. Tree: cap and replace. Recents: kinds, dwell, de-duplication, cap, title source, and a no-access page skipped then back, with nav.recent unchanged.
- **Done when:** set() changes get() in the same frame and reaches another tab via the bus. A draft survives a reload in a harness, and session storage holds only drafts and tm.login. Every §14 'Preferences' and 'Page state, drafts, tree, recents' bullet is green.
- **Planner note:** TmPageState.remember(key, snapshot, { keepStale: true }) (or a patch method) for 0030 §15.1 and 0031 §12.2, so a remember does not drop a markStale from a save; report the ruling.

### PC-108 — Leader election, leader documents and degraded polling

- **Analysis id:** `0025-c` · **Spec:** 0025 · **Layer:** both · **Tier:** 1 · **Est. LOC:** 3,000
- **Sections:** §7.1–§7.4, §8.4 (relay and act, without a connection), §8.6, §8.7, §15 (leader telemetry)
- **Gated on (DEPS.md):** PC-107, PC-075, PC-096
- **Analyst dependency notes:** 0025-a; 0025-b
- **Deliverables:** TmLeader (isLeader, hubStatus mirror, events()): the hub lock tm:<slug>:<sub>:<t>:hub with an AbortSignal; grant and release reasons (tenant, pagehide, freeze, session, steal); election on resume and pageshow; leader.probe and hub.status messages; the hung-leader and hidden-disconnected steal rules with jitter and a holder re-check via navigator.locks.query; follower probes every 60 s while visible and degraded. The TM_LEADER_DOCUMENTS registry with leader-document refresh (250 ms coalescing, one in flight plus one queued, savedAt = send time, pollWhile). Degraded polling (disabled realtime, or 60 s without a connection), with refresh(t,['tenant'],'leader'|'poll'). The hub.event relay and dispatch, including the cache.changed actions and jitter of §4.4, with the TmHub connection left as a seam.
- **Tests:** Unit with two instances and real Web Locks in browser mode: grant and every release reason; the steal rules (hung, hidden and disconnected, never a visible leader, never `disabled`); the holder re-check; the degraded follower's 60 s probe; a queued request aborted on freeze and re-requested on resume; leader-document coalescing and savedAt ordering.
- **Done when:** One leader per tenant per browser across instances. With realtime off, polling runs at most once per 60 s, only while a tab is visible. Every §14 'Leader' bullet is green.
- **Planner note:** Promoted to Tier 1: 0028 §9 banners and 0026's TmLeader consumers need §7 and §8.6.

### PC-109 — Primary attention seams: TmBanners, TmInputIdle, platform banners, attention documents

- **Analysis id:** `0028-c` · **Spec:** 0028 · **Layer:** both · **Tier:** 1 · **Est. LOC:** 2,500
- **Sections:** §9.1–§9.3, §9.5, §9.6, §5.1, §8.1 (TM_TOAST_* constants), §1 primary row
- **Gated on (DEPS.md):** PC-067, PC-108, PC-103
- **Analyst dependency notes:** 0024 §2.2, §5.5, §10.5; 0025 §8.6
- **Deliverables:** primary src/attention/: TmBanners (levels, precedence, head, pager, dismiss per tab and per update version, update/close, current/visible after the idle gate, empty-row rule, dev throw for distribution levels 1–3), TmBannerOptions/Ref/View types, TmInputIdle (passive capture listeners, idle signal, whenIdle), offline (TmApi.online false for 5 s) and update (TmUpdates.available) banner sources started by provideTellma, TM_TOAST_FEEDBACK_MS/TM_TOAST_ARRIVAL_MS, mirror types (TmInboxSummary, TmInboxRunningJob, TmInboxVerbResult, TmAssignmentsSummary, TmNotificationTypeMetadata, TmNotificationSeverity), TM_ATTENTION_DOCUMENTS provided into TM_LEADER_DOCUMENTS with pollWhile; core-testing tmFakeInboxSummary/tmFakeAssignmentsSummary
- **Tests:** unit on a fake clock: precedence, pager, dismiss per tab and per version; gate entry/exit waits, swaps/hidden page/user dismiss apply at once, empty row, whenIdle only after 2 s without input, queue-time announcement hook
- **Done when:** Unit suites green; 0024 §7.8's time-zone prompt can await TmInputIdle and §5.5 raises tm.update through TmBanners; primary share ≤ 2.5 KB.
- **Planner note:** Also ship the /attention entry-point scaffold with tm-banner-host (tm-banner-bar from TmBanners.current, the one hidden live region), sections 0028 §9.4. The update banner source moves to 0024-i.

### PC-110 — Service worker updates, error presentation and session paths

- **Analysis id:** `0024-i` · **Spec:** 0024 · **Layer:** both · **Tier:** 1 · **Est. LOC:** 5,500
- **Sections:** §5.4, §5.5, §5.6 (worker leg, rollback tie-break), §8.4–§8.8, §11
- **Gated on (DEPS.md):** PC-104, PC-103, PC-020, PC-023, PC-026, PC-075, PC-107, PC-105, PC-106, PC-109
- **Analyst dependency notes:** 0024-g; 0024-h; 0023-b; 0023-c; 0023-d; 0025-a; 0025-e
- **Deliverables:** tmRegisterWorker through a TrustedScriptURL, and the lazy `tellma` Trusted Types policy. TmUpdates (check outcome detection, update, reload, visibility and 6-hour checks, VERSION_READY/unrecoverable/install-failed handling). TmErrorPresenter (describe and present, the error table, forbidden labels with the raw pair, read-only names, the Reload busy/fail/'Reload anyway' flow on TmErrorDialog primary/secondary, the 400 dialog, one dialog at a time, tenant-not-found and tenant-unavailable handling, the overlay page-context seam). tm-error-state. The step-up dialog. Sign-out across tabs (busy overlay immediate, the drafts.query wait loop up to 60 s, the confirm, telemetry flush, session.signing-out/signed-out, /bff/logout, clean-up, failure toast, the other tabs' 15 s lift). Signed out elsewhere. The expiry path with the session-ended dialog. The changed-user path. The re-check triggers.
- **Tests:** Unit: TmUpdates check() per result and update() with a fake SwUpdate; the rollback tie-break under the 3 s cap; TmErrorPresenter (each forbidden reason with labels and raw pair, tenant-read-only and tenant-frozen with the tenant's name, Reload's busy state, the download failure and 'Reload anyway' as primary/secondary, tenant-not-found on automatic and chosen entries). Harness: the sign-out state machine with fake bus and drafts (no question when nothing is dirty or busy; the confirm variants; a wait capped at 60 s on a fake clock).
- **Done when:** All dialogs are alertdialogs with correct focus start, return and Escape. Sign-out reaches every tab in a multi-instance unit harness. Every §13 'TmErrorPresenter' and 'TmUpdates' bullet is green.
- **Planner note:** Owns the update (TmUpdates.available) banner source (moved from 0028-c).

### PC-111 — Page-context registry, page actions and dispatcher (primary)

- **Analysis id:** `0026-a` · **Spec:** 0026 · **Layer:** both · **Tier:** 1 · **Est. LOC:** 3,500
- **Sections:** §9.1–§9.6, §9.8 (TM_HELP_ARTICLES token only), §2 (escape hatches 2 and 3)
- **Gated on (DEPS.md):** PC-065, PC-106, PC-026, PC-105
- **Analyst dependency notes:** 0024 §1/§2.2 package scaffold; 0027-a; 0023 §5.5 TmConfirm; 0029-b (TmEntities.label for masking; can use tmTestEntities)
- **Deliverables:** @tellma/core primary folder context/: TmPageContext/TmVisibleData/TmContextError/TmFormContext/TmFormField types, TmPageKind, TmPageContexts (current/overlays/top, register, replacement carrying actions), provideTmPageScope, TmPageActions (register with scope resolution, invoke dispatcher steps 1–6, running signal, shortcut registration while top, dev throws), TmPageAction/TmActionResult/TmActionOrigin types, tmSerializePageContext and TmPageContextSnapshot with masking, TM_HELP_ARTICLES InjectionToken; @tellma/core-testing TmPageContextHarness; API golden
- **Tests:** unit + harness (vitest browser mode): scope resolution page/overlay/root, replacement with carried actions, merged order, dispatcher steps 1–6 per origin, refused action carrying disabledReason, shortcuts live only while top, snapshots with and without page data and masks
- **Done when:** Registry, dispatcher and serializer pass §13's 'Registry and dispatcher' suite; API golden approved; primary share counted against the 6 KB budget; 0024's route host can call provideTmPageScope and register a custom context.

### PC-112 — TmTelemetry pipeline, TmErrorHandler and redaction

- **Analysis id:** `0026-b` · **Spec:** 0026 · **Layer:** both · **Tier:** 1 · **Est. LOC:** 4,000
- **Sections:** §3 (all), §4 (all), §1 lint row, Appendix A.1 (tm.error.captured, tm.telemetry.dropped), Appendix A.2
- **Gated on (DEPS.md):** PC-111, PC-071, PC-110, PC-075, PC-096
- **Analyst dependency notes:** 0024 §10.1 TmApi.send; 0024 §8.4/§8.7 TmSession states; 0025 §6.1 TmTabBus; 0025 §3.1 TmLaunchCache.distribution; 0026-a (action-id grammar shared by the lint rule)
- **Deliverables:** primary observe/: TmTelemetry (event/timing/error/flush/recent ring of 100), envelope (version, tabId, tenantId per item, sentAt, route/pageKind reserved props), name/property guard with TmTelemetryError in dev and drop counting in prod, queue cap 200 with overflow order, flush triggers (50 items, 30 s, 1 s after error, visibilitychange/pagehide, tenant switch), 16 KB/100-item per-tenant batches paced 1/s, pagehide single batch, offline/session/disabled/cold-launch holds, per-tab sampling; TmErrorHandler (sources render/global/api, filters, ChunkLoadError, fingerprint, per-fingerprint 10 min and 30/10 min throttles, TmErrorRecord ring of 50), five-mask message redaction and stack reduction; dev console.debug; TmFakeTelemetry in core-testing; @tellma/core-tools/eslint rule tm/telemetry-names
- **Tests:** unit on fake clock: envelope, guard (dev throw, prod drop and count), flush triggers, batch splitting, one batch on pagehide, cap and overflow order, offline/session holds, sampling at rates 0 and 1; redaction table plus e-mail/URL/Arabic/digit vectors, stack reduction, each source and filter, fingerprint stability, throttles; ESLint rule tests (vitest under tools)
- **Done when:** §13 'TmTelemetry' and 'Redaction, TmErrorHandler' suites green; provideTellma can provide TmErrorHandler; nothing awaits or throws in production; lint rule wired into the workspace lint.

### PC-113 — TmRequestLog, TmAppVersion, Copy diagnostics text and platform measurements

- **Analysis id:** `0026-c` · **Spec:** 0026 · **Layer:** both · **Tier:** 1 · **Est. LOC:** 3,500
- **Sections:** §5 (all), §7, §8.1–§8.3, Appendix A.1 (tm.page.visit, tm.vitals.load, tm.api.*, tm.action.invoked, tm.support.diagnostics-copied)
- **Gated on (DEPS.md):** PC-112, PC-111, PC-108, PC-106, PC-023
- **Analyst dependency notes:** 0026-b; 0026-a; 0024 §10.3 (TmApi calls begin/end); 0025 §7.1 TmLeader; 0027-a (shortcut registry); 0023 §5.1 TmToasts
- **Deliverables:** primary requests/: TmRequestLog (begin/end handles, ring of 100, 3 s slow timer while pending, Tellma-Build to TmAppVersion.server), diagnosticsText (English, Latin digits, local times with offset header, no bidi controls) and synchronous copy(); TmAppVersion (ui/display/label/commit/platform/features/server/serverNewer, SemVer 2.0 compare ignoring build metadata, null for same version different commit); page-visit timing (ms, chromeMs, visitS, entry, outcome) driven by NavigationStart and the registry's status; load-metrics collector as an idle dynamic chunk (TTFB, FCP, LCP, navigation type, controlled; buffered observers); per-flush-window tm.api.requests (top 20 routes), tm.api.slow (20/10 min, pending at pagehide); tm.action.invoked emission from the dispatcher; Mod+Alt+Shift+D registration (tm.support.copy-diagnostics) with toasts
- **Tests:** unit in browser mode: visit boundaries on navigation and pagehide, chromeMs/ms stamps, absent metrics absent, summaries per window, slow at 3 s while pending, ring of 100, diagnosticsText goldens in en, ar and tenantless, compare vectors and serverNewer, Mod+Alt+Shift+D on an Arabic layout; perf unit diagnosticsText() < 2 ms for 100 records
- **Done when:** §13 'Measurements, TmRequestLog, TmAppVersion' suite green; 0024's TmApi (begin/end) and §5.6 412 judgement compile against these members; goldens checked in.

### PC-114 — Acme e2e host and the foundation's Playwright flows

- **Analysis id:** `0024-k` · **Spec:** 0024 · **Layer:** both · **Tier:** 1 · **Est. LOC:** 5,000
- **Sections:** §12 (gates, nightly), §13 (Acme e2e host, Playwright flows, responsive/RTL), §14 (DoD pinning specs)
- **Gated on (DEPS.md):** PC-110, PC-094, PC-103, PC-104, PC-075, PC-096, PC-092, PC-085, PC-086, PC-093
- **Analyst dependency notes:** 0024-f; 0024-g; 0024-h; 0024-i; 0025-a; 0025-b; 0011; 0018 §6.2; 0018 §6.3
- **Deliverables:** The shared Playwright fixture. webServer runs migrate and tellma-client build, then `dotnet run --no-launch-profile` of the Acme Web host with --webroot set to the built output and Tellma__PublicOrigin, in-proc identity, LocalDB on Windows and Testcontainers SQL Server on Linux. @tellma/core-testing/e2e helpers: warmUp, holdNetwork, goOffline, openTabs, signInAs (needs an email-code capture mechanism, see gaps), fakeProblem, expectNoAxeViolations. Flows (launch, automatic entries, session, updates/network, language/errors, time zones) and the DoD specs warm-launch, route-feedback, language-switch, layout-shift, cold-deep-link, rtl-boot, bidi-free and keyboard-errors. Nightly jobs: installability via CDP, Lighthouse on Slow 4G, warm chrome ≤ 300 ms with 4× CPU throttling.
- **Tests:** E2E (Playwright, Chromium on PR, @cross-engine subsets on Firefox and WebKit), axe, nightly perf and installability.
- **Done when:** The fixture runs on Ubuntu and Windows. The flows that don't need later specs are green; flows needing 0027 shells, 0028 banners, 0031 saves/drafts or 0032 Language & region are listed as pending and land with those specs.
- **Planner note:** Also: a Development-only file-drop IEmailSender in the Acme host (Tellma:Email:DevelopmentDropDirectory, refused outside Development by a startup check) writing one JSON file per message; signInAs polls it and reuses storage state per principal (10 codes per IP per hour). Done-when adds: every e2e page runs under the production CSP with no securitypolicyviolation event. The e2e webServer never uses default database names: on Windows it uses Testcontainers SQL Server (as on Linux), or LocalDB with the per-worktree DB_SUFFIX from .dev-ports.local, and the Acme host port comes from ACME_WEB there.

### PC-115 — Navigation registry, core entries and the pins model

- **Analysis id:** `0027-b` · **Spec:** 0027 · **Layer:** client · **Tier:** 1 · **Est. LOC:** 3,500
- **Sections:** §3 (all), §8.2–§8.3 (model, validation, resolve, label and current-pin rules), §9.6 (seam types), §2
- **Gated on (DEPS.md):** PC-106, PC-105, PC-067, PC-107
- **Analyst dependency notes:** 0027-a; 0029-b; 0024 §2.2–§2.4, §9.4; 0025 §9, §13
- **Deliverables:** primary nav/: TmNavSection/TmNavEntry/TmNavContext/TmNavItem/TmNavSectionView, TmNavigation (sources from TM_APP_DECLARATIONS after hide, entity/page/report/entry kinds, permission rule via can/allows, visibleWhen with absent distribution, order bands, localized-label tie order, recomputation triggers, byHref, resolve with ok/no-access/unavailable), synonyms split on ',' and '،', core sections analysis/administration and the six core entries (core.settings, core.me and four sections) with resx labels and synonyms; TmPinsDoc/TmPinItem/TmPin/TmPinFolder, tmPinsKey and tmPinnedBarVisibleKey with validate (shape, ids, lengths, caps 30/10/512, one level) and fallback; TM_LAUNCHER_PROVIDERS, TmLauncherProvider, TmLauncherItem, TmLauncherSearchContext
- **Tests:** unit: §3.2 table, section vs search-only, any-of requires, visibleWhen with the distribution document absent and changing, order bands, hide and tmOverride, language re-sort, byHref, resolve's states incl. a URL hidden by visibleWhen; TmPins model: validation and caps, capture minus page and with #d=, a drill-through list pinned and resolved, a URL past 512 refused, current-pin rule, fallback
- **Done when:** Unit suites green; 0025's recents can filter through TmNavigation.resolve; primary share within 5 KB.

### PC-116 — /shell entry point: frame, tenantless shell, tenant picker, signed-out page

- **Analysis id:** `0027-c` · **Spec:** 0027 · **Layer:** client · **Tier:** 1 · **Est. LOC:** 4,500
- **Sections:** §1 (placement, in-app links), §5.2–§5.4, §6 (all), §14 page contexts for /tenants and /signed-out
- **Gated on (DEPS.md):** PC-115, PC-106, PC-111, PC-073, PC-075, PC-020, PC-109, PC-110
- **Analyst dependency notes:** 0027-a; 0027-b; 0026-a; 0024 §2.6, §7.2, §7.6, §8.3, §9.2, §9.3, §11; 0025 §2.1, §3.1; 0023 §4.1, §4.4, §5.3, §6.2
- **Deliverables:** @tellma/core/shell: shared frame (CSS grid, only main scrolls, skip link, header/main/aside landmarks, print and no-animation rules), TmTenantlessShell with ink app bar (wordmark/mark, language menu, account menu with My account and Sign out), TmTenantPicker (cached-first list, revalidateUser, per-language names with lang spans, badges, Connect links with forTenant and preload, disabled reasons, live-then-sandbox ordering, search from seven memberships with ?q= and Enter, Mod+K focusing search, skeleton/error/empty/no-match states, tenant-not-found notice), TmSignedOutPage (no API, profile-cookie variant); custom page contexts; TmTenantPickerHarness; budgets wiring for /shell
- **Tests:** harness: picker order, names, badges, search threshold, empty, error, Connect's forTenant href, notice after a chosen entry and none after an automatic one; unit for name-slot selection
- **Done when:** Harness green; 0024's route table loads both shells; /tenants and /signed-out render offline from the cache with no request (signed-out).
- **Planner note:** Render <tm-banner-host/> between the app bar and the pinned-bar slot (0027 §5.2).

### PC-117 — /entity entry point: select plans, formats, typed values and blob checks

- **Analysis id:** `0029-c` · **Spec:** 0029 · **Layer:** both · **Tier:** 1 · **Est. LOC:** 5,000
- **Sections:** §6.1, §6.2 (tmSelectPlan), §6.4, §12 (all), §16
- **Gated on (DEPS.md):** PC-105, PC-071, PC-060
- **Analyst dependency notes:** 0029-b; 0005 controls + 0010 §D.4, §D.15; 0004 grid config; 0024 §10.1 upload
- **Deliverables:** @tellma/core/entity (lazy) scaffold; tmSelectPlan (column kinds value/text/navigation/image/file/expression, Id first, dedup, order terms per active slot, extra paths, dropped keys with reasons and tm.entity.column-dropped telemetry), TmColumnPlan/TmPlanRow/TmQueryRowSet, TmFormats (text/cell/zoneSuffix with zone label modes, exact decimal/long formatting via Intl.NumberFormat string input, calendars), tm-value (links, badges, avatars with colorKey, thumbnails, file links), tm-value-editor (control per type, maxlength/textarea, tmNumber scale, tm-date-picker, tm-select/tm-multi-select, tm-entity-field/multi-field, tm-image and file picker uploading via TmApi.upload, ownership read-only rules, instants read-only), TmUploads, tmGridColumnFor, tmCheckBlobFile and cover-square client crop
- **Tests:** unit: §6.1 per kind, Id first, order terms per slot, dropped keys, TmDecimal text at 28 digits and long values formatted exactly, zone labels (text, zoneSuffix, 'column' cells across Asia/Beirut DST), tmRowsToObjects nesting and skipping expression columns, extra paths in select never in columns; harness: tm-value-editor per type, pending uploads
- **Done when:** Unit and harness green; /entity budget tracked (28 KB total target); 0030/0031 can bind grids and forms through plans and value editors.
- **Planner note:** tm-value-editor's target (navigation) branch is a seam (read-only text) until 0029-e.

### PC-118 — Tenant shell, app bar, markers, session menu, About and Shortcuts dialogs

- **Analysis id:** `0027-d` · **Spec:** 0027 · **Layer:** client · **Tier:** 1 · **Est. LOC:** 6,500
- **Sections:** §5.1, §5.3, §7 (all), §10 (all), §11 (all), §12
- **Gated on (DEPS.md):** PC-116, PC-113, PC-096, PC-110, PC-014, PC-020, PC-109, PC-114
- **Later seams (not gates):** PC-154 (0026-f). Ship the seam; the later checkpoint fills it. PC-122 (0027-e): ship a TmLauncher seam (an injectable with show('menu' | 'field' | 'icon') and close(), a no-op default) that the Menu+search control and the Ctrl K keycap call; 0027-e replaces it with the real launcher. (Kickoff audit, 2026-11-08.)
- **Analyst dependency notes:** 0027-c; 0026-c; 0026-f (soft: Diagnostics tab); 0025 §3.1, §5; 0024 §5.5, §7.2, §7.5, §8.4, §11; 0010 §A.9, §B.1, §B.2, §C.10, §D.2, §D.3; 0023 §3.3, §4.1, §4.2, §7.1
- **Deliverables:** TmTenantShell (rows and --tm-* variables, immersive, TmShellMain override rendering, warm/cold/failed/client-outdated/tenant-unavailable states, overlay close and text clear on tenant switch), TmAppBar (wordmark/mark, grouped Menu+search by breakpoint with Ctrl K keycap, installed-mode back/forward via Navigation API with depth-counter fallback, distribution slots with badges and lazy panels as popover or end drawer, zero-width help/assistant reservations, reserved fixed boxes for bell/tray, markers with popovers, session button with avatar colorKey and truncation), TmSessionMenu (header, tenant card, My settings, Language, Calendar, Theme radios keepOpen, Keyboard shortcuts, About, Report a problem, slots below sm, Sign out), deferred About dialog (wordmark, Distribution_About, versions dl with copy, server-newer alert with Reload, copyright and links, Diagnostics tab lazily importing /support) and Shortcuts dialog (filter, General/This page/Lists and grids/Launcher tables); TmTenantShellHarness, TmSessionMenuHarness, tmTestShellSlot()
- **Tests:** harness: TmTenantShellHarness for each §7.5 state; TmSessionMenuHarness rows per condition and the avatar colorKey; dialog harnesses (About with/without Distribution_About, server newer/older; Shortcuts sections from TmShortcuts.active)
- **Done when:** Harnesses green; a warm tenant shell renders with no request (unit-level probe); bell/tray and banner host slots reserved with fixed boxes for 0028 to fill; API golden and /shell eager ≤ 16 KB, dialogs ≤ 6 KB.
- **Planner note:** Render <tm-banner-host/> (0027 §5.2). The About dialog renders only its About tab until 0026-f adds Diagnostics. PR-tier Acme e2e: warm tenant shell at /1 and the session menu.

### PC-119 — Generic entity picker binding, tm-entity-field and tm-entity-multi-field

- **Analysis id:** `0029-e` · **Spec:** 0029 · **Layer:** both · **Tier:** 1 · **Est. LOC:** 4,000
- **Sections:** §14 (all)
- **Gated on (DEPS.md):** PC-117, PC-096, PC-058
- **Later seams (not gates):** PC-160 (0030-h). Ship the seam; the later checkpoint fills it.
- **Analyst dependency notes:** 0029-c; 0006 + 0010 §E.1–§E.4; 0025 §3.3; 0030 §17 / 0031 §15 (soft: modal pages behind TmLazyPickerPage)
- **Deliverables:** TmEntityPickers.binding (server search via {segment}/query with take 21, bidi-stripped capped search text, separate filter leaves, order by display name; cacheable in-memory search over entitySet with findable rules; descriptions; unsearchable read-only; itemLabel/itemLabelParts/displayWith/createLabel; TmLazyPickerPage seam for Advanced search/Create/Edit with context { entity, filter, kind } and outcome handling incl. multiple), tm-entity-field (FormValueControl, TM_FORM_FIELD_CONTROL delegate), tm-entity-multi-field (maxItems), grid navigation-column spread (filter, format, formatParts, resolvePastedLabels with one query and the findable equality rule), tm.entity.picker-* telemetry; harness
- **Tests:** harness: search caps, cacheable search and its server fallback, search text stripped of bidi controls, the unsearchable field, form-field forwarding, itemLabelParts/formatParts isolating parts in <bdi>; unit: findable rows from the display-name golden (pasted '1101 - Cash', a name holding ' - ', '42' and '٤٢', '#7')
- **Done when:** Harness green; every navigation field and cell in 0030/0031 can use the binding; modal pages resolve once 0030/0031 land (seam returns 'not available' meanwhile).
- **Planner note:** TmLazyPickerPage is a token whose default resolves to 'not available'; 0030-h (Advanced search) and 0031-g (Create/Edit) provide it. Also ship tm-value-editor's target branch over tm-entity-field and tm-entity-multi-field (0029 §12.2).

### PC-120 — Client Queryex (lexer, builder, printer) and column filter controls

- **Analysis id:** `0029-d` · **Spec:** 0029 · **Layer:** both · **Tier:** 1 · **Est. LOC:** 5,500
- **Sections:** §8 (all), §13 (all)
- **Gated on (DEPS.md):** PC-119, PC-117, PC-058, PC-030, PC-060
- **Analyst dependency notes:** 0029-c; 0029-e (navigation filter's tm-entity-multi-field; or order 0029-e first); 0008 lexer (implemented); 0010 §E.2; 0023 §6.1, §6.3
- **Deliverables:** tmQxLex, tmQxParameters, TmQx AST, tmQx builder (literal per data type), tmQxPrint (precedence parentheses, bracketed identifiers, literal escaping, 15-digit number guard, raw), tmQxInsert; a C# tool that generates the engine lexer corpus (tokens, parameters, QX1xxx codes per text) and queryex-print-golden.json with its C# Discover check; tmParseFilter/tmFormatFilter/tmFilterLeaf (multilingual or/and, instant days in display zone with DST-safe day start, datetime whole days, enums, bool, navigation FK paths), tmDescribeFilter with whole-sentence ICU messages and placeholder-split parts, tm-filter-description, tm-column-filter (operator select, value inputs, between pair, enum checkbox list/tm-multi-select/tm-entity-multi-picker over enum values at TM_ENUM_PICKER_THRESHOLD 50, Apply/Clear, Arabic-Indic digit parsing), strings tm.filter.*
- **Tests:** unit: spec 0008 §4 vectors, the engine-generated corpus, queryex-print-golden.json (+ C# Discover check), tmQxInsert boundaries, raw, num refusing a 16-digit number and non-decimal text; §13.2 row by row incl. multilingual ne and datetime between, a guid column with no filter, instant days in UTC/Asia/Riyadh/Asia/Beirut (DST), invalid values, descriptions per operator in en, ar with an LTR value, am with the verb last; harness: enum filter of 49 values as tm-multi-select and 50 as tm-entity-multi-picker searching ar labels
- **Done when:** Suites green in TS and C#; corpus and print golden checked in with a regeneration command that runs on Windows and Linux.

### PC-121 — Expression editor and path tree picker

- **Analysis id:** `0029-g` · **Spec:** 0029 · **Layer:** both · **Tier:** 1 · **Est. LOC:** 5,500
- **Sections:** §10 (all), §11 (all)
- **Gated on (DEPS.md):** PC-120, PC-102, PC-012, PC-106, PC-031, PC-056
- **Analyst dependency notes:** 0029-d; 0029-f (wire contract; harness uses TmFakeDiscover); 0023 §8.2, §8.3; 0004 TmCellEditor + 0010 §C.15; 0010 §F.17, §F.19; 0027-a (tmSearchNormalize)
- **Deliverables:** TmQueryexDraft and injectTmQueryexDraft (per-clause text signals, 250 ms debounced discover with abort, stale-answer rule, offline/failed status with retry, lexical diagnostics, parameters with merged types), tm-expression-editor (textarea over highlighted pre mirror, fixed rows, status line with problems popover, parameter chip row, F8/Shift+F8, Mod+Enter submit, insertAtCaret as one undo step, native drop re-spacing, TmCellEditor implementation for popup cell editors, ARIA), tm-path-picker (columns/fields modes, node rules incl. group slots, blob branches, date parts with 'uq'/'et' calendars or Gregorian-labelled fallback, hidden unreachable paths, ordering, icons, normalized search to 3 levels/200 matches, tm.path drag payloads, activation and multiple selection), TmFakeDiscover and harnesses
- **Tests:** harness: stale answers, F8, insert, the editor as a cell editor (Enter inserts a line, Mod+Enter commits), date parts per calendar ('uq' and 'et' passed, Gregorian fallback labelled), path-picker search caps; geometry unit for fixed rows/status line/chip row
- **Done when:** Harnesses green; editor and picker within the /entity 28 KB budget; 0030's advanced filter and column chooser and 0032's permission-filter cell can host them.
- **Planner note:** Promoted to Tier 1: the roles permission editor and the column filters need the expression editor and path picker.

### PC-122 — Launcher and global search

- **Analysis id:** `0027-e` · **Spec:** 0027 · **Layer:** client · **Tier:** 1 · **Est. LOC:** 5,500
- **Sections:** §9 (all), §5.3 (launcher focus rules)
- **Gated on (DEPS.md):** PC-118, PC-115, PC-107, PC-011
- **Later seams (not gates):** PC-167 (0035-a). Ship the seam; the later checkpoint fills it.
- **Analyst dependency notes:** 0027-b; 0027-d; 0025 §13; 0023 §6.2; 0010 §D.1, §D.2; 0035 §9.2 (soft: kind-to-icon map for recents)
- **Deliverables:** TmLauncher service (state, kept query, show/close/setQuery, transitions table, tenant-switch clear), surfaces (≥lg overlay panel popover=manual under --tm-shell-top with page-only scrim, md panel with own field, <md TmModal lg full screen), browse state (Pinned band when bar hidden or <md, Recently visited chips, section columns, pinned glyphs, current entry tint, footer hints), typed state (index built on first open from TmNavItems/pins/recents in active language and English via translate(key,{},'en'), ال variant, prefix and contains classes with ≤5 tokens, ranking, 50-row cap, create rows ≤3, <mark> via DOM nodes, no-result text), APG combobox keyboard (arrows across bands and sections, PageUp/Down, Home/End, Enter/Mod+Enter/Shift+Enter, Escape, Tab), polite announcements, provider execution on the search output with abort and ordered buckets, deferred chunk prefetched at idle with skeleton, option preload; TmLauncherHarness; telemetry tm.launcher.*
- **Tests:** unit: matching vectors, the order rule, create rows, the 50-row cap, the 300-entry benchmark (index ≤ 5 ms, query ≤ 2 ms); harness: §9.2's table, §9.5's keys, bucket placement, a recent page hidden by visibleWhen and one hidden by a lost permission
- **Done when:** Matching and harness suites green; launcher chunk ≤ 6 KB gzip; first open renders in the click's frame from the prefetched chunk.
- **Planner note:** Ship the recents kind-to-icon map of 0035 §9.2 (list, chart-column, layout-dashboard, settings, file) in the primary nav folder; 0035-a1 imports it. PR-tier Acme e2e: Mod+K, type 'cent', Enter opens /1/centers.

### PC-123 — Home page scaffold: /dashboards entry point, grid and tile, recents widget, TmHomeHost

- **Analysis id:** `0035-a1` · **Spec:** 0035 · **Layer:** both · **Tier:** 1 · **Est. LOC:** 4,000
- **Sections:** 0035 §1-§2 (registry, core.recents only), §9.2, §15
- **Gated on (DEPS.md):** PC-001, PC-013, PC-026, PC-094, PC-103, PC-107, PC-111, PC-122
- **Analyst dependency notes:** 0011-a; 0010-d2; 0023-d; 0024-f; 0024-g; 0025-e; 0026-a; 0027-e
- **Deliverables:** Server widget registry and the widgetTypes emit with core.recents only; client primary tmWidget and TmDashboardWidgetModel; the /dashboards entry point; TmDashboardGrid, TmDashboardTile and tmPackDashboard; the core.recents widget; TmHomeHost, TmHomePage, TmHomeGreeting and the home skeleton; TM_HOME_WIDGETS rendering only registered types. Replaces 0024-g's placeholder home route.
- **Tests:** unit: tmPackDashboard vectors for the recents-only layout; harness: home skeleton → recents; Acme e2e (Chromium): sign in lands on /1 with the greeting and recents
- **Done when:** Every entry to /{t} lands on a working home page with recents.

## Phase 7 Search, details and stock pages

### PC-124 — Search query model, URL canonicalization and request builder (pure logic)

- **Analysis id:** `0030-a` · **Spec:** 0030 · **Layer:** client · **Tier:** 1 · **Est. LOC:** 3,500
- **Sections:** §1 (scaffold, types-only exports in primary), §2.1, §2.4 (key grammar, filter sources), §4.1–§4.4, §5.1, §5.3 diagnostic-to-leaf mapping, §14 page-size arithmetic
- **Gated on (DEPS.md):** PC-103, PC-105, PC-120
- **Analyst dependency notes:** 0024 §9.4–§9.5 (TmUrls, codec); 0029 §6 (select plans), §13.1 (tmFilterLeaf); 0025 Appendix A (preference key shapes)
- **Deliverables:** `client/projects/core/tellma-core/search/` entry point scaffold (ng-package, README, eslint boundary); primary types `TmTileDeclaration`, `TmCustomFilterDeclaration`, `TmSearchPageActionDeclaration`, `TmColumnPreference`, `TmSearchSnapshot`; internal pure modules: query-state resolver with drops and `tm.entity.column-dropped` emission, §4.2 precedence resolver (superseded snapshot, URL, page state, preferences, defaults), canonical-URL writer (`cols` and `view` rules, explicit `order` kept, list path), history-change classifier (push vs replace, page reset rules), request-body builder (select = plan ∪ tile paths ∪ tree keys, ordered leaves, `namedFilters`, active-slot `orderBy`, paging, flags), diagnostic→leaf mapper, page-size change arithmetic.
- **Tests:** unit (vitest): §18's vectors for §4.2 incl. supersedes and #d=-only URL, list-mode URLs keyed apart, canonical URL (cols with expression columns, view by width, drops, explicit order on the default's column), history per change, leaf order and diagnostic mapping, page-size arithmetic
- **Done when:** Entry point builds and passes the boundary lint. API golden approved. Every §4/§5.1 vector green. No UI yet.
- **Planner note:** TmTileDeclaration, TmCustomFilterDeclaration and TmSearchPageActionDeclaration are exported by 0029-b (PC-105): extend them, do not re-declare them. (Kickoff audit, 2026-11-08.)

### PC-125 — Shared entity-action runner, generated action dialog and the confirmation loop (@tellma/core/entity)

- **Analysis id:** `0031-a` · **Spec:** 0031 · **Layer:** client · **Tier:** 1 · **Est. LOC:** 4,000
- **Sections:** §9.3, §10.4 (TmEntityActions, outcomes, bodies, generated dialog, injectTmActionDialog with keepOpen/ids/rethrow, default destructive confirm), §8.5 steps 1–2 (path splitter and FieldTree walk, shared by action-argument errors)
- **Gated on (DEPS.md):** PC-071, PC-023, PC-119
- **Analyst dependency notes:** 0029 §2.2 §3 §12 §14 (/entity entry point exists); 0024 §10.1 §10.6 §11; 0026 §9.4–§9.5; 0023 §5.4
- **Deliverables:** In `client/projects/core/tellma-core/entity/`: `TmConfirmations` root service plus its lazily loaded `alertdialog` (lists warnings via `TmServerText`, up to 5 rows by `TmDisplayNames.label`, 'Continue without these {n}' via `skip`, the fifth-409 failure); `TmEntityActions.run/dialog` with §10.4's body table (entities/singleTarget/other), the default `include`/`returnEntities`, and the outcome union incl. `job` and `declined`; the generated `TmModal md` action dialog (one `tm-value-editor` per field, picker for `value.target`, danger button when destructive); `injectTmActionDialog`/`TmActionDialogContext` (`run` under the busy overlay with `pending`, 422 `arguments.*` mapped onto `form`, other errors in a `tm-alert`, `rethrow`, `keepOpen`, `ids` subset); the shared error-path splitter and FieldTree walker; the 'which actions appear' filter with its development warning; primary types-only `TmDetailsSnapshot`/`TmDetailsDraft`.
- **Tests:** unit: path splitter and walk (children, unknown segments, the root, `arguments`), §10.4 body selection; harness: one 409 with two warnings confirmed in one dialog and resent with both keys, 'Continue without these' on a bulk run, the fifth round failing, a declined outcome, a generated dialog with argument errors, `keepOpen`, `rethrow` (partial-failure rejects while a 500 lists in the alert), a 202 job outcome
- **Done when:** Acme's `set-credit-limit` opens a generated dialog and posts the documented body against the fake API. The bulk caller in 0030-f1 can consume the runner. The dialog chunks stay ≤4 KB each.
- **Planner note:** Ruling to report: TmEntityActions.run wraps TmConfirmations only; the busy overlay and the destructive confirm stay at the call sites.

### PC-126 — Search host, grid view, read lifecycle, states, paging, Create and Refresh

- **Analysis id:** `0030-b` · **Spec:** 0030 · **Layer:** client · **Tier:** 1 · **Est. LOC:** 6,500
- **Sections:** §1 parts table (host), §2.2, §3.1 (minus tabs and the selection bar), §3.2 (except tabs), §3.3, §5.2 (resource, aborts, announcements; not the anchor), §5.3, §5.4, §7.1–§7.3, §13.1, §13.4, §14, §19 page context and the host's own page actions (search.create, search.refresh, search.query, search.open)
- **Gated on (DEPS.md):** PC-124, PC-020, PC-041, PC-103, PC-114, PC-111, PC-051, PC-031
- **Analyst dependency notes:** 0030-a; 0023 §4.4 §5.3 §7.2 §8.1; 0010 §C.1 §C.3 §C.4 §C.6 §C.7 §C.14 §D.12; 0024 §4.4 (route host, search skeleton); 0026 §9.3–§9.5; 0029 §5.3 §7 §12.1
- **Deliverables:** `TmSearchHost` (`tm-search-host`) wired as the `':segment'` route component; `injectTmSearch`/`TmSearchContext` (`highlight`, `select`, `update`, `refresh`, `open`, `url`); the `TmSearchCell` base and lazy display-cell loading in parallel with data; rows 1–2 on `tm-page-header` with the leading and trailing groups and responsive forms; the `tm-grid` binding (gridId, contentKey, rowActivation, orderBy, links in display-name and navigation cells, zone-suffixed headers, row-end Columns placeholder); one `resource()` keyed by canonical query + refresh generation; first-load skeleton, reloading, no-data, no-matches, invalid-filter, calculation-error and offline states inside the grid's templates; the §5.4 re-read rule on language and permission moves; the `tm-pager` range mode with capped counts and the maxSkipWindow 413 fallback; Create link or disabled-with-reason button; Refresh; telemetry `tm.search.query`; `TmSearchPageHarness` with the fake TmApi and fake clock.
- **Tests:** unit + harness: superseded reads aborted, a retry keeps the loading state, each §5.3 state incl. ArithmeticFailed, offline on a changed query empties the region while an offline Refresh keeps the rows, reconnect re-reads, the §5.4 re-read vectors (scalar-display navigation order sends nothing, text order on en+ar sends one read, single-language tenant none, dropped filter or re-granted column one), headers with and without the zone suffix, the pager's capped count and maxPage
- **Done when:** `/17/customers` renders from descriptors with skeleton rows and then data via the fake API. Sort cycles asc/desc/none. Paging pushes history and other changes replace it. Every §5.3 state is reachable in the harness. The Columns slot stays reachable in every state.
- **Planner note:** PR-tier Acme e2e: /1/customers lists real rows, sorts and pages.

### PC-127 — Details host, sources, context, form, modes, loading and the header

- **Analysis id:** `0031-b` · **Spec:** 0031 · **Layer:** client · **Tier:** 1 · **Est. LOC:** 6,000
- **Sections:** §1 (scaffold, parts), §2, §3.1–§3.6, §4.1–§4.2, §5.1–§5.3, §10.1 (edit/create/save/cancel/refresh registration only)
- **Gated on (DEPS.md):** PC-125, PC-103, PC-107, PC-105, PC-020, PC-114, PC-111
- **Analyst dependency notes:** 0031-a; 0024 §4.4 §9.2; 0025 §10; 0026 §9.3; 0029 §3 §4 §5.3 §7 §12; 0023 §4.4 §7.2
- **Deliverables:** `client/projects/core/tellma-core/details/` entry point. `TmDetailsHost` routed on `':segment/:id'` and `':segment/new'` (incl. `?clone=`), keyed per record. `TmDetailsSource` and `tmEntitySource` (get/save via `TmApi`, `can.*`, draftKey, saveSecurable). `TmDetailsPage` (`tm-details-page`) for source pages. `injectTmDetails`/`TmDetailsContext` (mode, status, entity, model, form, dirty by reference, related, extras, refused, `edit/cancel/refresh/update`, registration APIs). The Signal Forms form per record. One `resource()` read keyed by record + generation with `include` of every extra, held-record vs skeleton loading, the progress bar and `aria-busy`, quiet refresh. Read-failure states with the '{Plural}' way out. Create defaults and the clone strip (structuredClone, ownership removal, blobs nulled, child negative ids, `declaration.clone`). Header rows: crumb, title with display-name cache or skeleton, badges in fixed boxes, the responsive toolbar (Edit, New, Save, Cancel, Refresh, Actions-menu placeholder, pager slot). The band and the status strip container. The Discard-changes confirmation on Cancel. `TmDetailsPageHarness` and `tmFakeDetails()`.
- **Tests:** unit: create defaults, the clone strip, dirty by reference; harness: load; Edit sends nothing and focuses the first field; Cancel with and without changes; Refresh, a step-like re-key and a stale return keep the held record under the bar with Edit disabled; nothing instantiated while loading with nothing held; a quiet refresh with no bar and no aria-busy; permission-disabled Edit and New with reasons
- **Done when:** `/17/customers/5` renders a markup-only layout from descriptors. View→edit→cancel round-trips with zero requests. `/17/customers/new?clone=5` produces the stripped copy.
- **Planner note:** PR-tier Acme e2e: /1/customers/{id} renders.

### PC-128 — Search field, type-to-search, highlight driver, pending Enter and the undo anchor

- **Analysis id:** `0030-c` · **Spec:** 0030 · **Layer:** client · **Tier:** 1 · **Est. LOC:** 3,000
- **Sections:** §6.1–§6.4, §5.2 (anchor), §7.2 (preloading, one tab stop, F2, return highlight), §6.4 row context menu items
- **Gated on (DEPS.md):** PC-126, PC-048
- **Analyst dependency notes:** 0030-b; 0010 §C.2; 0023 §6.2; 0027 §4.2 (TmShortcuts, overlays)
- **Deliverables:** Search field (`input[tmInput]` + `tmSearch` + `tmGridDriver` in `tm-form-field`, clear and Escape as undoable edits, the bidi-strip and maxlength rules); the document-level (page/list) and modal-pane (picker) keydown and paste capture with the five conditions, the `execCommand('insertText')` path and the `setRangeText` fallback; the host-owned `TmGridDriverTarget` forwarding to grid or tiles and wrapping `activateRow` with the stale rule; highlight seeding; pending-open arm/disarm; the in-memory anchor (take, end of run, restore with no request, drop rules); preloading of targets and `TmUrls.create`; the 'Open in new tab'/'Copy link' row menu items; the 500 ms polite announcement debounce; `tm.search.type-to-search` and `open` telemetry.
- **Tests:** unit: the type-to-search predicate over key, modifier and target vectors per mode with a stacked overlay; the stale-result and bar-`active` rules; harness with fake clock: five keys 30 ms apart → two reads (first aborted), the bar on the first key holding ≥250 ms, Enter before arrival opens the first row, a typed key replaces text, a paste on the grid lands in the field, accidental key + undo sends no request and restores page, scroll and highlight
- **Done when:** The keyboard-only path type → arrows → Enter → open works in the harness. The anchor restores with zero requests. The predicate vectors are green.

### PC-129 — Layout kit: cards, fields, tabs, aside, audit, extras and validation schemas

- **Analysis id:** `0031-c1` · **Spec:** 0031 · **Layer:** client · **Tier:** 1 · **Est. LOC:** 4,500
- **Sections:** §6 intro (TmDetailsExtra), §6.1, §6.2, §6.6, §6.7, §6.8, §7.1–§7.2, §5.1 title-extra slot
- **Gated on (DEPS.md):** PC-127, PC-026
- **Analyst dependency notes:** 0031-b; 0005 (tm-tab-group); 0023 §7.1; 0029 §4.3 §12.1–§12.2
- **Deliverables:** `TmDetailsKit` bundle: `tm-details-card` (grid columns, half width, collapsible with remembered state hooks, scrollable, flush, `tmDetailsCardSection`, `preserveContent`, error dot); `tm-field` for value/text slots/navigation/dotted paths in both modes with equal geometry, read-only lock, hints, unadmitted slots hidden, `tmFieldEdit`/`tmFieldView` templates, the two-state skeleton rule, registration; `TmFieldGroup` host directive forwarding registration; `tm-details-tabs`/`tm-details-tab`/`tmDetailsTabContent` on `tm-tab-group` with `preserveContent`, `collection` counters and error dots, the `selected` model; `tm-details-aside` (sticky column from lg); `tm-details-audit`; `*tmDetailsExtra`; `tmDetailsTitleExtra`; `tmDetailsPlatformSchema` (required/maxLength/precision/readonly/upload-pending/server-error tree validator) plus the declaration's `validate`; `TmFieldHarness`.
- **Tests:** harness: field rendering per end kind in both modes, geometry equality across modes, a collapsed card's error dot and `focus()` expanding it, the development errors (tabs inside a field grid, duplicate tabs id, second aside, undeclared extra), a card section holding tabs; unit: platform schema rules
- **Done when:** Acme's 17-line customer layout renders in view and edit modes with validation messages shown on touch.

### PC-130 — Images, files, child grids and child files

- **Analysis id:** `0031-c2` · **Spec:** 0031 · **Layer:** client · **Tier:** 1 · **Est. LOC:** 4,000
- **Sections:** §6.3, §6.4, §6.5
- **Gated on (DEPS.md):** PC-129, PC-086, PC-054
- **Analyst dependency notes:** 0031-c1; 0029 §16 (TmUploads, policies); 0017 §4.1 §5.1 (wire); 0010 §C.8; 0004 editable binding (implemented)
- **Deliverables:** Image and file fields (pick-time type and size errors, cover-square crop, immediate upload with progress ring through `TmUploads`, pending validator, staged preview through `TmUrls.blob`, `Blob.NotAttachable` and `blob-rejected` mapping, 5xx retry, null clears); `tm-child-grid` (one editable/readonly `tm-grid` bound to `form.<collection>`, `details:<entity>:<collection>` gridId with the record id as contentKey, placeholder new rows `{ id: −n, ...defaults() }`, heights fixed per load and mode, exact `TmDecimals` totals footer, `@defer (on viewport; prefetch on idle)` with a same-geometry placeholder, registration once rows render with `reveal()`, view-mode links); `tm-child-files` (chips, dropzone in edit mode, per-row staging, captions); `TmChildGridHarness`; the editable-grid budget line.
- **Tests:** unit: exact totals with TmDecimals; harness: adding and deleting rows, upload pending blocks Save, an upload error on the field, the deferred grid registering and `reveal(rowId, { column })`, heights stable while typing
- **Done when:** Sales invoice lines and attachments edit and stage files in the harness. The grid chunk has its own budget line.

### PC-131 — Tiles view, view toggle, tabs, column filters and the Filters popup

- **Analysis id:** `0030-d` · **Spec:** 0030 · **Layer:** client · **Tier:** 1 · **Est. LOC:** 5,500
- **Sections:** §2.3, §2.4, §3.2 (tabs forms, Filters label), §3.4, §8, §9, §10
- **Gated on (DEPS.md):** PC-128, PC-020, PC-121, PC-011, PC-060, PC-061
- **Analyst dependency notes:** 0030-c; 0023 §4.1 §4.3 §6.1 §8.5; 0029 §10 §13; 0010 §D.2 §D.3 §C.10
- **Deliverables:** Tile layout mapping (image/title/subtitle/topEnd/badge defaults, `{ component }` body) on `tm-tiles`; the grid/tiles `tm-segmented` toggle writing `view` and the per-width-class preference halves; tabs as segmented, menu button or popup radio group per width; custom filter switches and radio groups (`x.{key}`), with `named` filters sent in `namedFilters`; per-column funnel buttons in `tmGridHeaderEnd` with the `tm-popover sm` `tm-column-filter` and the ICU name; the Filters popover (md) with the staged tab (xs), expression editor, Active-only switch, custom filters, applied chips via `tmDescribeFilter` (incl. the hidden-order chip), 'Add column filter' menu, Clear all/Apply, the counter and the error tone; Edit filter focusing the editor; `tm.search.filter-applied` and `view-changed` telemetry.
- **Tests:** unit: view halves (a toggle to the class default deletes its half; a desktop peek leaves the narrow half); harness: tabs with a leaf and with a `named` filter, a switch with one, Apply as one write and one read on page 1, dismiss discards, Apply disabled only on a completed failing check, f.IsActive dropped on activatable entities
- **Done when:** Customers show tiles below md with no re-read on toggle. Sales invoices tabs render in their three forms. Every filter source produces its leaf or named filter in §5.1 order.
- **Planner note:** Promoted to Tier 1: column filters and the Filters popup are needed by 0030-g and by any useful search page.

### PC-132 — Page state return and trees (browse, expansion memory, search tree, fallback)

- **Analysis id:** `0030-g` · **Spec:** 0030 · **Layer:** client · **Tier:** 1 · **Est. LOC:** 4,000
- **Sections:** §15.1–§15.3, §16.1–§16.5, §8 (tree of tiles), §4.4 (tmFrom/tmPage router state on open)
- **Gated on (DEPS.md):** PC-131, PC-107, PC-054, PC-062, PC-061
- **Analyst dependency notes:** 0030-d; 0025 §10 §12; 0010 §C.9; 0015 §5.2 §5.4 §12.1 (wire)
- **Deliverables:** Snapshot remember on destroy and before open (forget on forbidden/not-found), restore with no request when canonical matches, a stale re-read, selection and expansion restore; router state `{ tmFrom, tmPage }` on open; the Tree/List toggle and `list` preference flag; the browse tree's single unpaged `get-by-parent-ids` with remembered `parentIds` (newest maxIds), pruning, `hasChildren` from (Active)SubtreeCount, batched expand through `loadChildren` with failure collapse, `expansionSource="model"` memory (toggle/replace with loaded descendants removed), the 413 fallback to List with notice; the search tree (`includeAncestors`, static, muted context rows with hidden text and legend, first-match highlight); sibling ordering; tree of tiles with `tm-tiles__item--context`; `tm.search.tree-load`/`tree-fallback`/`restored` telemetry.
- **Tests:** unit: tree pruning, collapse through `replace`; harness: a page-state restore sends nothing, a stale one re-reads, a 403 on that read forgets the entry, expand batching, the 413 fallback notice
- **Done when:** Centers opens as a tree in one call, restores expansion on a later visit, shows a search tree with muted ancestors, and returning from a record restores the page with zero requests.
- **Planner note:** Rule (and report) whether a search tree's snapshot ids hold matches only; 0031-g consumes it.

### PC-133 — Save flow, error summary, server-error mapping, conflict/deletion dialogs and the leave guard

- **Analysis id:** `0031-d` · **Spec:** 0031 · **Layer:** client · **Tier:** 1 · **Est. LOC:** 5,500
- **Sections:** §8.1–§8.7, §9.1–§9.2 (and §9.3's integration into Save), §11.3, §11.4, §3.4 (Cancel interplay)
- **Gated on (DEPS.md):** PC-130, PC-125, PC-023, PC-069, PC-080
- **Analyst dependency notes:** 0031-c2; 0031-a; 0023 §5.4; 0024 §8.3 §9.2 §11; 0016 §6.1 §7.3 §7.4 (wire)
- **Deliverables:** Save (button, Mod+S, `details.save`): upload wait ('Finishing uploads…'), clear errors, `source.validate`, `submit` with `onInvalid`, `beforeSave`, body building (working copy as is, the stamp, full child arrays, `include`, Check/Override, keys via `Tellma-Confirm`), `TmBusyOverlay.run` with the 'Saving…' label and the inert page, the `beforeunload` armed only during a write. 200 adoption (view mode, draft clear, the create-mode `replaceUrl` to the id or `createdUrl` with the snapshot remembered under the target key, `markStale`, 'Saved' announcement). The error summary in the strip (collapsed/expanded, at most 10 items + 'and {n} more', items for field, child-row and unrendered errors, 'Fixed' items, `TmTabGroup.instantiate()` then afterNextRender touch for unvisited tabs). The one tree validator holding server errors keyed by value at save. §8.6's outcome table routing. `TmDetailsDialogs` (conflict with other/self/no-author copy, deletion, leave). `tmLeaveGuard` registered in the page scope (busy refuses; host navigations exempt). `tm.details.save/conflict/leave-guard` telemetry.
- **Tests:** harness: Save 200 where create replaces the URL; a create landing on createdUrl with its snapshot and no request; a clean Save in edit mode sends nothing; 422 with field, child-row, unregistered and root errors and 'Fixed' items; a 422 and a client error on an unvisited tab instantiating it; 409 with each choice and the self and no-author cases; 404 Discard; §8.6's presenter calls; a conflict, Overwrite, then a warning resending Override and the key; the overlay start (Save busy in the click's frame, scrim after 200 ms; immediate after the conflict dialog); leave guard Discard and Stay
- **Done when:** Every row of §8.6 has a harness case. A dirty in-app navigation asks, and a busy one refuses with history intact.
- **Planner note:** PR-tier Acme e2e: edit, save, then a two-context conflict and Overwrite.

### PC-134 — Page actions, the Actions menu, delete, activate and entity actions on the page

- **Analysis id:** `0031-e` · **Spec:** 0031 · **Layer:** client · **Tier:** 1 · **Est. LOC:** 3,500
- **Sections:** §10.1–§10.4 (page integration), §5.2 (menu and responsive moves), §4.1 `refused`/`can` composition
- **Gated on (DEPS.md):** PC-133, PC-041
- **Analyst dependency notes:** 0031-d; 0026 §9.4–§9.5; 0027 §4.3; 0010 §C.10 §D.12
- **Deliverables:** The full §10.1 action table registered as page actions (clone, activate/deactivate by `isActive`, delete and delete-with-descendants on trees, entity actions as `<schema>.<entity-kebab>.<action>` with `button: true` promotion, `visible`). Disabled reasons in priority order (tenant state, missing securable, `permissions` extra refusal `outside-filter` → `tm.details.outsideFilter`, other rule keys via `TmServerText`). Menu groups and the below-lg move of New, promoted and primary actions. The 'routed record shown' gating during steps. Delete (dispatcher confirm, overlay, `TmConfirmations`, `markStale`, forget, URL replace, polite '{Entity} deleted.'). Activate and deactivate with `returnEntities` and read-back adoption. Entity actions with the post-success adoption or re-read and the job toast. Mod+S/F2 shortcuts. `tm.details.action/confirm` telemetry.
- **Tests:** harness: permission-disabled menu items with reasons; ReadOnly and Frozen reasons winning over a missing grant; row-level refusals by outside-filter and by a rule key; delete; activate; entity actions with the generated and a declared dialog; a metadata-destructive action without a declaration confirming with the default text; the immediate overlay after a menu item and a confirmation
- **Done when:** Customers and sales invoices expose every metadata action with correct enablement. Delete and activate round-trip and mark lists stale.

### PC-135 — Drafts and reconciliation, state restoration and the child-row deep link

- **Analysis id:** `0031-f` · **Spec:** 0031 · **Layer:** client · **Tier:** 1 · **Est. LOC:** 4,500
- **Sections:** §11.1–§11.2, §13, §14.1–§14.3
- **Gated on (DEPS.md):** PC-134, PC-107, PC-071, PC-054
- **Analyst dependency notes:** 0031-e; 0025 §10 §11; 0024 §10.1 (fingerprint); 0010 §C.8
- **Deliverables:** `TmDetailsDraft` writes (debounced, pagehide flush, contract fingerprint awaited at edit/create start, `savingAt`, uploads, tabs). Clear rules. §11.2's open table incl. the clone cases. The contract-change reconciler (walk by descriptor kind/nullability/enum, children by id, fill from the fresh read or defaults, keep `baseModifiedAt`). The strip notices in priority order. `TmDetailsSnapshot` remember/restore (tabs and cards before first render, band/aside/panel scroll after, stale re-read, refused forget, draft wins). `?child=Collection:id` parsing into `childTarget`, tab selection or a bare grid, `reveal(id, { flash: true })`, the polite announcement, `child` removal via replaceUrl, and the three failure notices. Hatch support for `childTarget`. `tm.details.draft-restored/child-link/load` telemetry.
- **Tests:** unit: §11.2's table, draft reconcile vectors (renamed member, kind change, removed enum value, renamed child collection, same-shape contract change) and the notice order, snapshot rules; harness: the step-up draft returning to edit mode, restore with no request, a stale restore re-reading
- **Done when:** Reloading during edit restores the draft with its notice. Returning to a record restores tabs, cards and scroll with zero requests. `?child=Lines:42` flashes the row once.
- **Planner note:** Promoted to Tier 1: the settings pages (0032-f) use drafts and reconciliation. PR-tier Acme e2e: reload during edit restores the draft.

### PC-136 — /admin scaffold, stock declarations, the GL pack with Centers, and the five locale fields

- **Analysis id:** `0032-a` · **Spec:** 0032 · **Layer:** both · **Tier:** 1 · **Est. LOC:** 4,500
- **Sections:** §1, §2, §3, §4.2, §6, §12.3 (TmJobHandlerMetadata type), §12.4 (TmSecurableMetadata, TmSettingKeyMetadata, TmSettingCategoryMetadata type mirrors)
- **Gated on (DEPS.md):** PC-132, PC-134, PC-067, PC-114, PC-122
- **Analyst dependency notes:** 0030-g; 0031-e; 0024 §2.2–§2.4 §7.7; 0029 §2.2 §7 §12
- **Deliverables:** `client/projects/core/tellma-core/admin/` lazy entry point with budget line (31 KB). Primary `src/admin/admin.entities.ts`: `tmUsers`, `tmRoles`, `tmSchedules`, `tmOutgoingEmails`, `tmExports`, `tmImports`, `tmAccessPage`, `tmJsonTextSchema`, and the metadata mirror types, registered in the implicit `core` feature. New package `@tellma/module-gl` (primary: `tmGlCenters`, `tmGlFeature()` with the `financials` section; `/centers`: `TmGlCenterFields` and `TmGlCenterDetails`), with README and goldens. `tm-language-field`, `tm-calendar-field`, `tm-time-zone-field` (grouped by first segment with `longOffset`, ICU-unknown ids without an offset), `tm-region-field` (Intl.Collator sort, samples) and `tm-digits-field` (native system rules): each nullable with 'Tenant default (…)', standalone `value` mode, and LTR isolates. Lucide icons registered.
- **Tests:** unit: every `visible` predicate; harness: locale fields (views equal to the value sets' labels, zone options from core.TimeZone grouped, an id without an ICU offset, region options per scope with samples, digits hidden without native digits and its native option under each scope, each field standalone); Centers tree page and details layout over the fake API; budgets for the primary declarations (≤2 KB) and module-gl primary (<1 KB)
- **Done when:** The launcher's Administration section lists the stock entries at their orders. `/17/centers` opens as a tree with Delete with descendants. The Acme app config's `tmGlFeature()` resolves from the new package.
- **Planner note:** PR-tier Acme e2e: /1/centers tree.

### PC-137 — Users: fields, memberships with window editor, Sign-in tab, invite and credentials dialogs, signature

- **Analysis id:** `0032-c` · **Spec:** 0032 · **Layer:** both · **Tier:** 1 · **Est. LOC:** 6,500
- **Sections:** §4.1, §4.3, §4.4, §4.5, §4.7
- **Gated on (DEPS.md):** PC-136, PC-125, PC-130, PC-056, PC-030, PC-088, PC-114
- **Analyst dependency notes:** 0032-a; 0031-a (keepOpen/ids/rethrow); 0031-c2 (editable grid binding pattern); 0010 §C.15; 0023 §6.4 §3.3; 0018 §3.1–§3.8 (wire)
- **Deliverables:** `TmUserDetails`, `TmUserFields` (TmFieldGroup, kind-dependent fields, the Email lock and hint, `tm-user-locale-fields` with `offered` scopes, `tm-signature-field` on a fixed paper box, `TmUserStateBadge` via `tmDetailsTitleExtra`), `TmUserTabs` (Roles, Sign-in, and the Access tab gated by `core.Role × Read`, rendered as a placeholder until 0032-e). `tm-user-memberships`: an editable grid over `form.roleMemberships` registered as a collection, a Role picker filtered to active non-public roles, the Period text with next-boundary re-render, the `[editorPopup]` window sub-form (Permanent/Time-limited, Now or a date-time, durations or Custom, one `applyTransaction` of two `TmCellEdit`s), Notes, and the three membership error placements. `tm-user-sign-in` (states table, Check delivery via `users/invitation-status` with abort, the delivery lines, Retry). `TmInviteDialog` (sandbox and sensitive copy, `run({}, { keepOpen, rethrow: ['partial-failure'] })`, results list, 422 codes, the retry line with `failed.map(Number)`). `TmCredentialsDialog` (rotation warning, mono LTR fields with copy buttons, 'Copy both', the close guard, secret never published or stored). The new paper token. `TmMembershipsHarness`; `tm.admin.users.invite/credentials/delivery` telemetry.
- **Tests:** unit: the window text for each state and its next boundary; the window editor's writes (Permanent, Now, each duration, Custom); harness: the tabs inside the card; the State badge; memberships with a Period popup, its commit undone in one step restoring both bounds, and its three errors landing; invite outcomes (all sent, mixed, 422, a 502 read through rethrow with retry, a 500 in the alert); credentials shown once and the close guard
- **Done when:** An administrator can create a user, add a time-limited membership, invite (single and bulk) and issue credentials in the harness. The secret never appears in page context or drafts (asserted).
- **Planner note:** PR-tier Acme e2e: create a user and add a membership.

### PC-138 — Settings: settings source, metadata-driven tabs, tm-setting-field, General tab, save diff and cache effects

- **Analysis id:** `0032-f` · **Spec:** 0032 · **Layer:** both · **Tier:** 1 · **Est. LOC:** 6,500
- **Sections:** §7.1–§7.7, §12.1
- **Gated on (DEPS.md):** PC-137, PC-136, PC-133, PC-135, PC-096, PC-022, PC-103, PC-060, PC-114, PC-079
- **Analyst dependency notes:** 0032-a; 0031-d (source pages, conflict dialog); 0031-f (drafts); 0025 §5.3 §4.4 §10; 0013 §1.2 §5.3 §6.1–§6.5 (wire); 0024 §9.2 (route matcher)
- **Deliverables:** `TmSettingsPage` on the `settings[/:category]` route, with tabs from `settingCategories` filtered by `can(resource, 'Read')`, `replaceUrl` tab navigation through `tm-details-tabs` `selected` (with the canDeactivate same-route exemption, see gaps), the unknown-category state, and one page-wide edit mode with read-only locked categories. `tmSettingsSource` (read mapping `settings/details` to `{ ...general, entries }` with `setKeys`/`entryStamps` extras; the dynamic `saveSecurable`; `errorSegments` for dotted keys; `changes` = the stored settings document minus its own adoption). `injectTmSettings`/`TmSettingsContext`. `tm-setting-field` (descriptor from key type, Int64/Decimal as TmDecimal, references and arrays, Enum arrays with Intl.ListFormat, scalar-array and JSON textareas with the `$invalid` marker, Secret Set/Not set with Replace/Clear, the Default chip, Reset to default, reserved boxes). `tm-settings-category` (groups declared or derived, `SettingGroup_` titles, humanized fallbacks). `tm-settings-general` (Organization names, Languages with the change and removal confirmations, Calendars, Time zone, Formats). The `settingsTabs` hatch via `NgComponentOutlet` and `hide: ['settingsTab:…']`. §7.6's diff (fields, entries, default→null, secrets, stamps, client checks) and `settings/save`. 409 no-author Overwrite. Adoption via `TmCacheRefresher.adopt` with `settingsTag`. The `settings.refresh-caches` menu action. `TmSettingsPageHarness`, `tmFakeSettingsDetails()`.
- **Tests:** unit: the settings diff (typed members, entries, default → null, secrets, stamps, client checks), errorSegments, groups declared and derived and tab order from fixture metadata; harness: tabs from access; read-only categories; a first tab click from /{t}/settings in edit mode keeping the host and the working copy; §7.6's body; 409 → Overwrite → Override; language confirmation; the Formats card's defaults; adoption under the result's settingsTag with no sync from the header; a view-mode re-read on a moved document
- **Done when:** Settings renders tabs, labels and controls with every request held. Two edits to different keys produce §7.6's documented body. Region changes re-format the app in place.
- **Planner note:** PR-tier Acme e2e: save one setting.

### PC-139 — Roles: securables on the client, permissions editor with live filter checks, members, drift

- **Analysis id:** `0032-d` · **Spec:** 0032 · **Layer:** both · **Tier:** 1 · **Est. LOC:** 5,500
- **Sections:** §5.1–§5.5, §12.4 (label chains; server side if not already in 0014's contributor)
- **Gated on (DEPS.md):** PC-138, PC-137, PC-121, PC-051, PC-078, PC-091, PC-060, PC-114
- **Analyst dependency notes:** 0032-c; 0029 §10 (cell editor, permission profile); 0010 §C.14 §C.15 §B.4; 0014 §3.7 §9 §10 (server); 0018 §4.3 (members/problems extras)
- **Deliverables:** `TmRoleDetails` (the Administrator notice and locks), `TmRoleFields`, `TmRoleTabs`. A securables client service over the metadata's `securables` section: feature-grouped resource options, registry-ordered actions plus '*', the filter root for `*` (shared root), and sensitive glyphs. `tm-role-permissions`: an editable grid over `form.permissions` registered as a collection, with a status column (drift and local-error markers with text), Resource (grouped select), Action (cleared when invalid), Filter (mono LTR cell, `[editorPopup]="{ minWidth: 480 }"` holding `tm-expression-editor` with the 250 ms permission-profile check, the hint, a read-only 'All records' when no root, F2 read-only in view mode), Notes, and a row-end 'Who has this' link. Public-role wildcard disabled. Save-error placement incl. summary items that open the cell popup. `tm-role-members` over the `members` extra (avatar colorKey, Period, the 500 cap line, client search over 20 rows, the no-access text). The drift banner and row markers from the `problems` extra. If missing: the server's label chain in `SecurablesClientAssetContributor` with golden. `TmPermissionsEditorHarness`.
- **Tests:** unit: filter roots for `*`; harness: cell ↔ popup incl. F2 read-only in view mode and the summary opening a cell's popup; live diagnostics; drift markers; members states, windows and the cap line; server (xUnit, only if amended): the securables golden with label keys and fallbacks per §12.4's chains
- **Done when:** A filtered permission is typed, checked live, saved, and its server errors land on its Filter cell. A drifted permission shows the banner and marker.
- **Planner note:** PR-tier Acme e2e: save a filtered permission.

### PC-140 — MVP gate: human-testing readiness

- **Analysis id:** `MVP` · **Spec:** plan · **Layer:** both · **Tier:** 1 · **Est. LOC:** 1,500
- **Sections:** PLAN.md §7.2
- **Gated on (DEPS.md):** PC-000, PC-001, PC-002, PC-003, PC-004, PC-005, PC-006, PC-007, PC-008, PC-009, PC-010, PC-011, PC-012, PC-013, PC-014, PC-015, PC-016, PC-017, PC-018, PC-019, PC-020, PC-021, PC-022, PC-023, PC-024, PC-025, PC-026, PC-027, PC-028, PC-029, PC-030, PC-031, PC-032, PC-033, PC-034, PC-035, PC-036, PC-037, PC-038, PC-039, PC-040, PC-041, PC-042, PC-043, PC-044, PC-045, PC-046, PC-047, PC-048, PC-049, PC-050, PC-051, PC-052, PC-053, PC-054, PC-055, PC-056, PC-057, PC-058, PC-059, PC-060, PC-061, PC-062, PC-063, PC-064, PC-065, PC-066, PC-067, PC-068, PC-069, PC-070, PC-071, PC-072, PC-073, PC-074, PC-075, PC-076, PC-077, PC-078, PC-079, PC-080, PC-081, PC-082, PC-083, PC-084, PC-085, PC-086, PC-087, PC-088, PC-089, PC-090, PC-091, PC-092, PC-093, PC-094, PC-095, PC-096, PC-097, PC-098, PC-099, PC-100, PC-101, PC-102, PC-103, PC-104, PC-105, PC-106, PC-107, PC-108, PC-109, PC-110, PC-111, PC-112, PC-113, PC-114, PC-115, PC-116, PC-117, PC-118, PC-119, PC-120, PC-121, PC-122, PC-123, PC-124, PC-125, PC-126, PC-127, PC-128, PC-129, PC-130, PC-131, PC-132, PC-133, PC-134, PC-135, PC-136, PC-137, PC-138, PC-139
- **Deliverables:** plans/platform-core/HUMAN-TESTING.md: how to run the Acme distribution locally from a clean clone (Windows and Linux), seed and dev data, signing in through the in-proc identity server, the test tenants, what works, what is known to be missing (Tier 2/3 list), and where to file findings. A full PR-tier run plus one Nightly-tier run on GitHub, with failures triaged. A scripted smoke (migrate → provision → start → sign in → open search, details and settings) that a human runs with one command.
- **Tests:** the one-command smoke on Windows locally and on Linux CI
- **Done when:** Ahmad can follow HUMAN-TESTING.md on a clean clone and reach a signed-in tenant shell with working search, details and stock admin pages.

## Phase 8 Completion (Tier 2)

### PC-141 — Acme tenant-data verdict assertions after the CRUD stack lands

- **Analysis id:** `0011-h` · **Spec:** 0011 · **Layer:** backend · **Tier:** 2 · **Est. LOC:** 2,000
- **Sections:** §3.3 ReadOnly/Frozen verdicts end to end, §4.3 UserId binding, §10 remaining Acme integration rows
- **Gated on (DEPS.md):** PC-085
- **Analyst dependency notes:** 0011-g; 0014 connect prologue; 0016 projection; 0018 reference stacks; 0020 worker
- **Deliverables:** Acme IntegrationTests rows needing sibling specs: first tenant request binds UserId; Frozen tenant: read succeeds and mutation is 403 tenant-frozen against a READ_ONLY database; ReadOnly tenant: read stamps activity, mutation 403 tenant-read-only, an AllowWhenReadOnly action and a RunsWhenReadOnly job succeed; migrate with a changed step on a ReadOnly tenant applies it. Hub front door (§4.3) and its negotiate verdicts land with spec 0021, not here.
- **Tests:** integration (Acme.IntegrationTests)
- **Done when:** Every §10 Acme integration row green on both OSes.

### PC-142 — StackConformanceTests base, instrument pins, docs

- **Analysis id:** `0015-k` · **Spec:** 0015 · **Layer:** backend · **Tier:** 2 · **Est. LOC:** 3,500
- **Sections:** §16.1 (MetricCollector pins over every instrument), §16.2 budget assertion, §18.3, §18.4, §19 docs
- **Gated on (DEPS.md):** PC-077, PC-076
- **Analyst dependency notes:** 0015-i; 0015-h
- **Deliverables:** StackConformanceTests<TEntity,TKey> in Tellma.Core.Testing (operations reachable/refused, Enlist acceptance, securables with filter roots and owners, budget via DataAccessScope.RoundTrips, hidden-equals-missing, conflict/override/missing, ownership, null vs [] and foreign child ids per collection, Unique per [Unique]/[NaturalKey], Fk.InUse per incoming reference, recipe checks, instruments), closed over the fixture stacks in the integration suite; MetricCollector assertions for every tellma.crud.* instrument and tag set; ARCHITECTURE.md row updates; READMEs
- **Tests:** integration (the conformance base closed over Widget, Node, Shipment, Gadget; instruments)
- **Done when:** Conformance base runs green over fixture stacks on LocalDB and Testcontainers; every instrument of §16.1 observed with its closed tags; ARCHITECTURE.md updated

### PC-143 — Notification surfaces of the users stack

- **Analysis id:** `0018-f` · **Spec:** 0018 · **Layer:** backend · **Tier:** 2 · **Est. LOC:** 2,500
- **Sections:** §3.3 (core.user.added notice), §3.5 (SendTestNotification, GetNotificationPreferences, SaveNotificationPreferences), §1.2 (Scoped<INotificationChannel, EmailNotificationChannel>), §8.1 test-notification and notification-preferences rows
- **Gated on (DEPS.md):** PC-093, PC-088, PC-090, PC-035
- **Analyst dependency notes:** 0018-c; 0018-d; 0021 §2.4; 0021 §4.1; 0021 §4.3; 0021 §4.4
- **Deliverables:** context.Notify of core.user.added in invite (LocalizedText tenantName/actorName from cached settings and ConnectedUser.Profile, DedupKey); me/test-notification (inbox → Users.ChannelNotTestable, unknown key → Notifications.UnknownChannel, NotConfigured, SendTestAsync); EmailNotificationChannel with SendTestAsync through IEmailSender (Internal audience, ServerStrings Email_TestNotification_*); notification-preferences get/save for another user under core.User × Preferences with the target's tag bump and log; tellma.users.test_notifications
- **Tests:** unit: test-notification branches. integration: core.user.added inserted for every outcome with a status and deduplicated with all three name slots and no load; notification-preferences get/save for another user (unmutable inbox row and service account refused, admin log)
- **Done when:** The invite raises the notice, and My settings' test-send works for the email channel. The welcome email (IEmailOutbox) and EmailNotificationChannel.DeliverAsync are explicitly left to 0022's checkpoint.

### PC-144 — Scheduler and the Schedule stack

- **Analysis id:** `0020-e` · **Spec:** 0020 · **Layer:** backend · **Tier:** 2 · **Est. LOC:** 6,500
- **Sections:** §7.3–§7.9, §11.2 (core.Schedule securables), §15 (jobs.schedules realised check)
- **Gated on (DEPS.md):** PC-100
- **Analyst dependency notes:** 0020-d; 0015 activatable recipe/details extras/actions; 0013 time zones/settings
- **Deliverables:** Cronos 0.13.0 integration (CronFormat.Standard, zone resolution via core.TimeZone/TenantSettings.TimeZone), tick step 1 (both fixed texts incl. ReadOnly variant) and step 2 (fence, sp_sequence_get_range, overlap predicate, ScheduleStates advance) goldens, occurrence engine (Coalesce/ReplayAll window/Skip window, gap degradation via GetPreviousOccurrence, exhaustion), pause statement + core.schedule.paused notices, ScheduleService (PreprocessAsync, validator with every Schedules.* code, ContributeAsync ScheduleStates upsert + PausedReason clear + Withdraw on activate, state details extra, RefuseOnRecord for built-ins, take-over, run-now with RunInProgress, delete withdraw after commit), built-in initialisation on first tick
- **Tests:** unit: Cronos policies with fixed zones and DST transitions, ReplayAll catch-up window, Skip window, exhaustion, gap degradation without enumeration, ReplayAllRequiresAllow; integration(SQL): tick-1 leasing (uninitialised built-in initialised without firing, deactivated never leased), step-2 overlap predicate with Held/PendingConfirmation, LastSkippedAt on overlap only, pause single write, tick-lease fence, two schedules + ReplayAll three occurrences in one tick, plan test for the reserved-band range seek and IX_ScheduleStates_Due seek, validator codes (CronNeverOccurs, OwnerInactive, Asia/Calcutta refused, core.export not schedulable), take-over→activate withdrawing notice, run-now rows, state extra, permissions extra on built-ins, ReadOnly tenant skipping non-RunsWhenReadOnly schedules and firing them when Active
- **Done when:** Built-in schedules fire on a composed host; every scheduler pin of §16 green

### PC-145 — Job admin surface, labels and platform maintenance handlers

- **Analysis id:** `0020-f` · **Spec:** 0020 · **Layer:** backend · **Tier:** 2 · **Est. LOC:** 5,000
- **Sections:** §2.3 (labels, entity argument), §3.7 (jobHandlers client section), §11.1–§11.3, §14.4 (core.job-retention + retention stop), §14.5 (core.tree-verify), §10 (job.changed from actions, held/gap/pending-confirmation withdrawals)
- **Gated on (DEPS.md):** PC-144, PC-094
- **Analyst dependency notes:** 0020-e; 0015 general action path; 0016 MapTellma projection; 0012 TreeStatements.Verify; 0024 §3.2 (contributor interface)
- **Deliverables:** JobService (Query|Details stack, retry/cancel/resume/confirm with RT1 loads incl. held-requester list, fixed-text statements, withdrawals, job.changed publishes, Destructive/AllowWhenReadOnly/Mcp=Hidden flags), core.Job securables + self-scope criteria provider, route segments, JobHandlersClientAssetContributor + label render order, core.job-retention (three loops, 1,000-row pages, retention stop + withdrawals), core.tree-verify, CoreFeature contributions of §15
- **Tests:** unit: label render order and entity argument, jobHandlers golden (LabelFallback only when no Strings key, CommitsInSteps carried); integration(SQL): action preconditions and repeats, cancel kinds F/Q/C incl. checkpointed Pending/Held re-queue, withdrawals with fakes, four actions publishing job.changed, ReadOnly tenant serving cancel and refusing retry, Job.Status traversal from an owning row under the self-scope, retention paging (>1,000 rows) and SET NULL, retention stop of old PendingConfirmation, tree-verify writing a tree table under CallerAuthority
- **Done when:** jobs/* endpoints projected and green; retention and tree-verify built-ins run; §16 action pins green

### PC-146 — Inbox, preferences, delivery and retention handlers

- **Analysis id:** `0021-c` · **Spec:** 0021 · **Layer:** backend · **Tier:** 2 · **Est. LOC:** 7,000
- **Sections:** §2.2 (stack + self-scope criteria), §4.3 (service), §4.5, §5.1–§5.4, §7, §8, §10.1 (inbox/preferences/retained counters)
- **Gated on (DEPS.md):** PC-099, PC-100
- **Analyst dependency notes:** 0021-b; 0020-d; 0014 UserStamps + IGuardedBatchRunner; 0015 ApiService + criteria; 0016 endpoint projection
- **Deliverables:** Notification stack registration (Query|Details) + NotificationAccessCriteria; InboxService (summary statement with capped counts/MaxUnseenSeverity/Latest/Running + actors Query<User> into Related; seen/read/read-all/clear/clear-read with InboxVersion tail and inbox.changed); NotificationPreferencesService (get/save, validation codes, defaults dropped, ForCaller PreferencesTag bump); NotificationDeliveryHandler (one Read round trip, resolution per channel in registration order, per-recipient negotiated LocalizationContext, DeliverAsync on the job frame, single PersistAsync); NotificationRetentionHandler + BuiltInSchedule(2); InboxSummary/InboxVerbResult/InboxRunningJob contracts
- **Tests:** unit: delivery handler resolution with fake channels (preference over DefaultEnabled, registration order, nothing persisted on throw); integration(SQL): summary caps (>100 rows), Latest ordering, MaxUnseenSeverity, Related actors only, Running filters (ScheduledFor null, live statuses, label columns), verbs' counts/versions/other-user ids ignored, ReadOnly served / Frozen refused, summary during open verb transaction (RCSI on), preferences save codes and PreferencesTag bump, self-scope 404 on another user's id, retention bands/paging, delivery job e2e through the worker incl. core.job.failed inbox-only, change-tracking write audit
- **Done when:** notifications/*, inbox/*, notification-preferences/* endpoints green; delivery and retention handlers run under the worker

### PC-147 — TellmaHub, SignalR publisher and hosting modes

- **Analysis id:** `0021-d` · **Spec:** 0021 · **Layer:** backend · **Tier:** 2 · **Est. LOC:** 6,500
- **Sections:** §6.1–§6.6, §9.2, §10.1 (realtime instruments), §10.2 (hub log events), §11 (realtime-hosting check), §12 realtime suites, §13 docs
- **Gated on (DEPS.md):** PC-146, PC-035
- **Analyst dependency notes:** 0021-a; 0011 tenant middleware + session store + listeners; 0016 MapTellma §9.2; 0014 connect/guard
- **Deliverables:** Tellma.Core.AspNetCore.Realtime: TellmaHub (no invocable methods, groups from claims), TellmaHubClaims (Create/Select), TellmaUserIdProvider, HubConnectionTracker (ISessionTerminationListener + ITenantStateListener; session.ended then Abort; cache.changed {tag:'tenant'}), SignalRClientEventPublisher, RealtimeOptions, RealtimeApplicationName, AddTellmaRealtime (factories honouring Enabled), MapTellma hub mapping with CloseOnAuthenticationExpiration and hub options; the tenant-middleware hub-claims step (0011 §4.3 step 3); realtime-hosting startup check; UseAzureWebDefaults realtime line with the Microsoft.Azure.SignalR pin; ARCHITECTURE.md updates; realtime KQL
- **Tests:** unit (TestServer, in-process mode): member connects, non-member and deactivated (warm cache) refused 404 at negotiate, claims on principal and Select output only, OnConnectedAsync with Tenantless holder, ticket-expiry close, no LastActiveAt/cookie renewal, per-user/tenant isolation, payload cap, unregistered name, tracker aborts by handle/(tenant,subject)/tenant group with session.ended first, ReadOnly/Frozen connect and Suspended refused, invocations refused, Enabled=false (negotiate 404, null publisher), ReadOnly state sending cache.changed, realtime-hosting failing with Jobs.Enabled=false; live (Category=Integration, Live=true): Azure SignalR connect/groups/session.ended/aborts/application-name isolation
- **Done when:** Hub serves events in-process; unit realtime suite green on PR; live suite wired nightly behind the secret

### PC-148 — Outbox row, enqueue and the email notification channel

- **Analysis id:** `0022-a` · **Spec:** 0022 · **Layer:** backend · **Tier:** 2 · **Est. LOC:** 6,000
- **Sections:** §1–§4, §2.2 (table types), §9.1, §9.3 (entity, BlobKind, IEmailOutbox registration, Email_* resources)
- **Gated on (DEPS.md):** PC-097, PC-146
- **Analyst dependency notes:** 0020-b; 0021-c; 0017 staging/attach/confirm; 0015 enlist frame §13.3
- **Deliverables:** Contracts OutgoingEmailState, EmailDeliveryStatus, OutgoingEmail, OutgoingEmailAttachment, EmailBlobAttachment, EmailEnqueueRequest, IEmailOutbox, OutboxOptions + bounds; model config (indexes incl. filtered UX_OutgoingEmails_JobId, sq_ sequences); OutgoingEmailOutcomeList/EmailDeliveryEventList registration; OutgoingEmailService (Query|Details|Delete|Enlist, ContributeAsync enqueueing core.outgoing-email per new row, ValidateDeleteAsync); EmailOutbox (resolve, validate Email.InvalidMessage/TooManyRecipients/TooLarge, stage bytes and copy committed blobs, RFC 5322 address-list formatting/parsing, EnlistSaveAsync); BlobKind email-attachment without thumbnail; EmailNotificationChannel.DeliverAsync (group by LocalizationContext, render via INotificationRenderer + Email_Notification_* ServerStrings, ITellmaLinks.Target link, one request per addressed recipient, Source=notification)
- **Tests:** unit: validation codes, address formatting/parsing, options bounds, §4 with a fake IEmailOutbox (one request per recipient, ContactEmail ?? Email, language/calendar/zone, link, no-address skipped); integration(SQL): enqueue with a byte attachment, a user-staged blob and a copied committed blob — commit confirms all three, rollback leaves only staged rows, no thumbnail; job enqueued with RunAsUserId=system and DueAt=SendAfter; write audit sees declared tables
- **Done when:** 0018's invite and the notification delivery job can enlist OutgoingEmail rows that commit with their jobs; pins green

### PC-149 — Dispatch handler

- **Analysis id:** `0022-b` · **Spec:** 0022 · **Layer:** backend · **Tier:** 2 · **Est. LOC:** 4,500
- **Sections:** §5, §7.2, §9.3 (core.email.failed type + resources, outbox.sender startup check), §9.2 (enqueued/settled/latency)
- **Gated on (DEPS.md):** PC-148, PC-100, PC-099
- **Analyst dependency notes:** 0022-a; 0020-d; 0021-b
- **Deliverables:** OutgoingEmailHandler (triage settled/Sending/Pending, attachment Read load joined to core.Blobs + IBlobStore reads, pre-mark Maintenance round trip returning end state, one IEmailSender.SendAsync with outbox correlation and no cancellation token, settle over OutgoingEmailOutcomeList matched on State='Sending', outcome→job mapping incl. attempts-exhausted, NotifyOnSuccess core.email.failed InboxOnly/Replace to administrators); OutboxStatements pre-mark/settle (authored — not given in spec); NotificationType core.email.failed; outbox.sender check; OutboxSendInterrupted log
- **Tests:** integration(SQL): CapturingEmailSender.OnSending scripting every §5 step-5 outcome incl. attempts exhausted (drive Job.Attempts directly, not via real backoff), sender exception before any send, missing attachment object → Email.AttachmentMissing; pre-mark re-run returns same rows; Sending row at claim → Email.Interrupted with no send; cancel then claim sends nothing; Rejected raises one inbox notice and no delivery job; sandbox tenant → Sandboxed with ExpectsDeliveryEvents=0
- **Done when:** Welcome and notification emails actually leave through the configured sender; dispatch pins green

### PC-150 — Server relay POST /api/client-telemetry

- **Analysis id:** `0026-d` · **Spec:** 0026 · **Layer:** both · **Tier:** 2 · **Est. LOC:** 3,000
- **Sections:** §6.1–§6.4, §6.3, the relay half of §1, Appendix A metric names
- **Gated on (DEPS.md):** PC-113, PC-025, PC-080
- **Analyst dependency notes:** 0011 §5.1, §5.4, §5.5, §5.9, §8, §9; 0016 §2.1, §8.2, §10.1
- **Deliverables:** Tellma.Core.Abstractions.Api: ClientTelemetryBatch, ClientTelemetryItem, ClientTelemetryOptions (Tellma:ClientTelemetry), ClientTelemetryNames; Tellma.Core.AspNetCore: the endpoint (RequireAuthorization Web, AsTenantlessEndpoint, tellma-telemetry rate policy, 32 KB limit, untraced, no renewal), row-by-row validation with per-item/per-property drops and one Information drop log, backstop URL/e-mail masks, skew-corrected ClientAt, client/server dimensions in logger scope, LogInformation custom events named by microsoft.custom_event.name, Warning error traces with extras, unexported Activity parented to the item's trace id, the two counters with closed tag sets; infra/monitoring KQL alerts for client errors and slow requests
- **Tests:** integration (WebApplicationFactory/TestHost): §6.2 row by row, 204 with dropped items, 429 from the rate policy, skew correction, no session renewal, an in-memory OpenTelemetry exporter sees custom events, Warning traces, the item's trace id on the record and no exported span, counter tags only from closed sets
- **Done when:** §13 'C# relay' suite green on Windows and Linux; DistributionInfo.telemetry reflects the options; the SPA's batches answer 204.

### PC-151 — Cross-cutting accessibility and stability gates

- **Analysis id:** `0023-h` · **Spec:** 0023 · **Layer:** client · **Tier:** 2 · **Est. LOC:** 2,500
- **Sections:** §2.2, §12 (axe, forced colors, reduced motion, touch, live direction switch), §13 (DoD)
- **Gated on (DEPS.md):** PC-061, PC-004, PC-020, PC-023, PC-026, PC-030, PC-031
- **Analyst dependency notes:** 0023-a; 0023-b; 0023-c; 0023-d; 0023-e; 0023-f; 0023-g
- **Deliverables:** Shared Playwright suites over every 0023 story. axe in light/dark × LTR/RTL with toasts and dialogs open, trees collapsed and expanded, and avatars in every tone. The forced-colors battery: selected tile, active segment, switch on, counter tone, drop target, tinted avatar. A reduced-motion battery. Resolved sizes at 390 px. A live English→Arabic switch spec for radio, segmented, splitter, tree and tiles. A keydown-to-paint probe per story. The remaining §13 bounding-box specs. Final llms.txt/MCP entry verification.
- **Tests:** E2E (Playwright on the showcase: Chromium full, @cross-engine subset, Pixel 7 project), axe.
- **Done when:** Every §13 bullet has a named pinning spec, and all of them are green on Ubuntu and Windows.

### PC-152 — Device time zone detection and prompt

- **Analysis id:** `0024-j` · **Spec:** 0024 · **Layer:** both · **Tier:** 2 · **Est. LOC:** 2,500
- **Sections:** §7.8
- **Gated on (DEPS.md):** PC-114, PC-104, PC-075, PC-109
- **Analyst dependency notes:** 0024-h; 0025-a; 0029 §2.2 (core.TimeZone aliases in Entities chunk); 0028 §9.3 (TmInputIdle)
- **Deliverables:** TmDeviceZone: the zone document in the device partition (cap of 20 users); the check steps 1–7 with the Web Lock tm:<slug>:<sub>:zone-prompt; the TmInputIdle and no-open-modal wait; the deferred dialog chunk (TmModal sm) with the auto checkbox; the automatic update with a toast; alias mapping through core.TimeZone; telemetry with no zone ids. Re-reading deviceTimeZone on visibilitychange and pageshow.
- **Tests:** Unit with a fake clock, locks and store: each step, including a Frozen and a ReadOnly tenant; two tabs and one dialog; Keep with the box ticked; each failure. Harness: the dialog in en and ar, both themes, at 390 and 1440 px, with focus, Escape and axe. E2E (later, in 0024-k): device-zone.spec via CDP Emulation.setTimezoneOverride, including Asia/Calcutta→Asia/Kolkata.
- **Done when:** The prompt appears only on a detected change, at most once per browser, and the automatic mode writes in the background. My settings' tm-auto-time-zone (0032 §8) can bind TmDeviceZone.auto/setAuto.

### PC-153 — TmHub realtime connection

- **Analysis id:** `0025-d` · **Spec:** 0025 · **Layer:** both · **Tier:** 2 · **Est. LOC:** 3,000
- **Sections:** §8.1–§8.5, §15 (realtime telemetry)
- **Gated on (DEPS.md):** PC-108, PC-110, PC-146, PC-147, PC-035
- **Analyst dependency notes:** 0025-c; 0024-i; 0021 §5.4; 0021 §6.1; 0021 §6.3
- **Deliverables:** The lazy entry point @tellma/core/realtime: tmCreateHub over @microsoft/signalr (pinned to 10.x) with the JSON protocol, no withAutomaticReconnect, TmHubHttpClient extending DefaultHttpClient with TmSession.check() before every send, timeouts of 30 s and 15 s, logging through TmTelemetry; the reconnect loop (0, 2, 10, 30 s, then doubling to 5 min, ±20 % jitter, reset after 60 s connected, nudge, offline wait); terminal verdicts (401/403/404 → refused; session.ended → ended); the TmHubSink wiring into TmLeader; terminal refreshes, with followers also syncing on access-revoked and tenant-unavailable; session-terminated → session.signed-out and 0024 §8.5. TmFakeHub. The entry point's API golden is marked internal; budget 18 KB gzip with the library.
- **Tests:** Unit with a fake connection: §8.3's schedule and jitter bounds; nudges; terminal verdicts stop the loop; a refusal followed by a successful sync keeps the attempt counter; relay and drop rules; the TmHubHttpClient check.
- **Done when:** @microsoft/signalr loads only in the leader tab (asserted by chunk graph). A hung negotiate never delays navigation. Every §14 'TmHub' bullet is green.

### PC-154 — /support entry point: TmSupport facade, state dump, download and Diagnostics panel

- **Analysis id:** `0026-f` · **Spec:** 0026 · **Layer:** both · **Tier:** 2 · **Est. LOC:** 3,500
- **Sections:** §10.1–§10.4, §12.1 (TmSupport, canSend, instant open placeholder), §8.4
- **Gated on (DEPS.md):** PC-150, PC-111, PC-112, PC-113, PC-073, PC-075, PC-004
- **Analyst dependency notes:** 0026-a; 0026-b; 0026-c; 0024 §5.3, §5.5; 0025 §15; 0004 grid + 0010 §C.1; 0023 §3.2, §3.3
- **Deliverables:** primary support/: TmSupport (canSend, open with final-geometry placeholder in the click's frame and synchronous context snapshot, copyDiagnostics, downloadDump, registerDumpSection with name grammar, uniqueness, throwing-reader and 64 KB handling) plus the offline 'Diagnostics aren't available offline' text; @tellma/core/support entry point (worker group on-demand): tmBuildSupportDump (contents, never-list, 2 MiB cap with the four truncation steps, storage.estimate time-boxed 200 ms), file download naming, TmDiagnosticsPanel tm-diagnostics (header, read-only tm-grid with slow/failed switch, row activation copies, pending duration ticking, narrow layouts, Copy/Download/Report buttons)
- **Tests:** unit: §10.1 contents, a section over its cap and a throwing reader, the four truncation steps in order, nothing of the never-list with seeded storage and cookies, page.url per §10.2; harness: diagnostics panel grid, offline text
- **Done when:** §13 'Dump' suite green; /support within its 25 KB gzip budget so far; 0024's 5xx dialog and 0027's About dialog can call TmSupport and host tm-diagnostics.
- **Planner note:** Adds the Diagnostics tab to the About dialog (0027 §11).

### PC-155 — /attention model: TmAttention, TmArrivals, TmAttentionText, banner host

- **Analysis id:** `0028-d` · **Spec:** 0028 · **Layer:** both · **Tier:** 2 · **Est. LOC:** 4,500
- **Sections:** §5.2–§5.4, §8.1–§8.3, §9.4, §11 (all)
- **Gated on (DEPS.md):** PC-109, PC-146, PC-036, PC-105, PC-023
- **Analyst dependency notes:** 0028-c; 0021 §5.1–§5.4; 0020 §3.7; 0024 §7.5, §9.4; 0025 §2.5, §7.1, §8.6; 0029-b; 0023 §5.1, §5.2
- **Deliverables:** @tellma/core/attention entry point (eager, modulepreloaded): TmAttention (inbox/assignments with overlays, bell/tray badges and accessible names, open, live from hub status, writable from Frozen/online, toggle, refresh, read/readAll/clear/clearRead overlays released by inbox version via TmDecimals.compare, stopJob overlay, seen overlays released by version/seenAt), TmArrivals (baseline, arrival rules for notifications/assignments/tenant state, visible-and-focused tab rule, grouping, keys n:/a:/a:many, notifications.toasts switch, overflow pill action wiring into provideTellmaToasts), outcome toast helpers, TmAttentionText (notification title/detail, job label order with entity plural argument, jobMessage, caption title/detail/amount with placeholder split for <bdi>), tm-banner-host (renders tm-banner-bar, owns the assertive/polite live region announcing at queue time)
- **Tests:** unit: badge table; overlays and their release by version and seenAt, incl. a copy read before the verb committed but saved after its response keeping the overlay; baseline, both arrival rules, replaced row, focused-tab rule, toasts off, grouping and keys; title/detail keys, label order, entity argument, captions with and without each member, LocalizedText detail per content slot and fallback, the title's isolated code, language switch; harness: TmBannerHostHarness
- **Done when:** Unit and banner-host harness green; /attention eager ≤ 6 KB gzip; 0027's shells render the banner host in the reserved row.
- **Planner note:** §9.4 (tm-banner-host) already shipped with 0028-c.

### PC-156 — Bell, tray, panels and tm-assignment-list

- **Analysis id:** `0028-e` · **Spec:** 0028 · **Layer:** both · **Tier:** 2 · **Est. LOC:** 5,500
- **Sections:** §6 (all), §7.1–§7.4, §13 page-context and dump rows
- **Gated on (DEPS.md):** PC-155, PC-118, PC-111, PC-154, PC-011, PC-020, PC-105
- **Analyst dependency notes:** 0028-d; 0027-d; 0026-a; 0026-f; 0010 §D.2, §D.3; 0023 §4.1, §4.4, §4.5, §5.3, §5.5; 0029-b (tm-display-name)
- **Deliverables:** tm-bell-button and tm-tray-button (30×30, overlay counter, job ring, ARIA, Alt+Shift+U/Q via TmShortcuts), @defer panels TmBellPanel (header with Mark all as read and More options incl. confirmed Clear read, In progress jobs with per-status rows, Stop with handler-dependent confirmation, notification rows as real links to the open route with severity icons, Download action for core.export.ready, withdrawn/replaced handling, '{n} new' pinned button, status line, empty) and TmTrayPanel (header counter, tm-assignment-list tray variant, footer), tm-assignment-list (tray/home variants, party, New marks against seenAt, loading skeletons), roving lists and keys incl. Delete, popover ≥lg / end drawer <lg, RTL bdi; overlay page contexts panel:bell/panel:tray with attention.* actions; the 'attention' dump section; placement into 0027's app bar; TmBellPanelHarness, TmTrayPanelHarness
- **Tests:** harness: bell states (job rows of §6.2, stop confirmation for a commitsInSteps handler and none for an export, empty, offline, ReadOnly with every verb, Frozen without them, status line), tray; unit: overlays apply synchronously, closing aborts the read
- **Done when:** Harnesses green; panels ≤ 9 KB gzip; axe clean on both panels light/dark LTR/RTL; badges render from stored documents with no request.

### PC-157 — Notifications section of My settings

- **Analysis id:** `0028-f` · **Spec:** 0028 · **Layer:** both · **Tier:** 2 · **Est. LOC:** 3,000
- **Sections:** §10.3
- **Gated on (DEPS.md):** PC-156, PC-155, PC-035, PC-090, PC-107, PC-105, PC-111
- **Later seams (not gates):** PC-166 (0032-g), PC-165 (0032-e). Ship the seam; the later checkpoint fills it.
- **Analyst dependency notes:** 0028-d; 0021 §4.3, §4.4; 0018 §3.5; 0025 §9; 0029-b (disabledReason); 0026-a; 0032 §8 (hub, soft); 0032 §4.8 (Preferences dialog, soft)
- **Deliverables:** @tellma/core/attention-pages entry point (lazy): TmNotificationSettingsPage and TmNotificationSettings (user 'me' or id; reads notification-preferences/get or users/notification-preferences/get + users/preferences/get; pop-up switch bound to notifications.toasts; category cards × channel switches with defaults, Always on for non-mutable inbox, '—' for channel-only; 600 ms debounced whole-set save, one in flight, Saving/All changes saved/Couldn't save + Try again, 422 reload; test delivery rows with Sent/NoContactAddress/NotConfigured inline results; Frozen/offline/ReadOnly disabling with reasons; settings page context and notifications.settings.* actions); TmNotificationSettingsHarness
- **Tests:** harness: locked, channel-only, ReadOnly saving with test disabled, Frozen, save statuses, inline test results; another user's id (its routes, each change saved, no test delivery)
- **Done when:** Harness green; /attention-pages budget tracked; component mountable by 0032's hub and Preferences dialog.

### PC-158 — Column editor, expression columns and the columns preference

- **Analysis id:** `0030-e` · **Spec:** 0030 · **Layer:** client · **Tier:** 2 · **Est. LOC:** 3,000
- **Sections:** §11.1–§11.4, §4.3 (cols with expression columns), §7.1 (row-end Columns button)
- **Gated on (DEPS.md):** PC-132, PC-131, PC-121, PC-031, PC-107
- **Analyst dependency notes:** 0030-d; 0029 §11 (tm-path-picker) and §10; 0023 §8.4; 0025 §9
- **Deliverables:** The `@defer (on interaction)` column editor (popover lg or end drawer with Available/Shown segmented tabs): `tm-path-picker mode="columns"`, `tm-reorder-list` accepting `tm.path` drops, checkbox and Enter toggles, the 1–50 limits with polite refusal, live apply (URL replace + write-behind preference; additions re-plan and re-read, removals and moves re-render), 'Add expression' inline form checked by `queryex/discover`, expression headers (ƒ glyph, LTR mono isolate, tooltip), declared-expression matching, equal-to-default deletes the key, Reset to default; `TmColumnEditorHarness`; `tm.search.columns-changed`.
- **Tests:** harness: the editor writes the preference and the URL, a removal sends nothing, its button stays over the error and empty states, a declared expression column keeps its label across an edit, a list equal to the default deletes the key; the deferred chunk's budget line
- **Done when:** Columns persist across reloads through preferences. URLs carry only path columns. The deferred chunk is ≤6 KB gzip.

### PC-159 — Selection, the selection bar, bulk actions and declared page actions

- **Analysis id:** `0030-f1` · **Spec:** 0030 · **Layer:** client · **Tier:** 2 · **Est. LOC:** 2,500
- **Sections:** §3.1 (pageActions placement, More menu), §12.1–§12.3, §19 page actions over the selection
- **Gated on (DEPS.md):** PC-158, PC-126, PC-125, PC-048
- **Analyst dependency notes:** 0030-b; 0031-a; 0026 §9.4–§9.5; 0010 §C.5 §C.10
- **Deliverables:** One `selectedIds` set across grid, tiles and tree that survives queries, capped at `limits.maxIds`; the selection bar swapping the leading group in place; the Actions menu groups (export items as disabled stubs until 0030-f2, Activate/Deactivate, derived entity actions excluding singleTarget and `bulk:false`, `selection:true` page actions, Delete and Delete with descendants) with disabled reasons; the run protocol (page action invoke, destructive confirms, busy item, polite count announcement, selection clear, `markStale`, re-read, focus to results; 404 and `Fk.InUse` through `TmErrorDialog` with display names; `TmConfirmations.run` with 'Continue without these'); the `pageActions` primary/menu/selection rendering; `tm.search.bulk`.
- **Tests:** harness: selection across pages; bulk success (announced, no toast), 404 and 422 Fk.InUse; a confirmation with a subset; a declined confirmation keeps the selection; the maxIds cap disabling actions
- **Done when:** Bulk deactivate and delete work end to end against the fake API, through the shared confirmation loop.

### PC-160 — Picker mode, the drill-through list mode and the `search` hatch

- **Analysis id:** `0030-h` · **Spec:** 0030 · **Layer:** client · **Tier:** 2 · **Est. LOC:** 2,500
- **Sections:** §17, §2.5, §1 (`TmSearchPickerPage`)
- **Gated on (DEPS.md):** PC-159, PC-132, PC-058, PC-103
- **Analyst dependency notes:** 0030-g; 0029 §14.4; 0010 §E.2 §E.3; 0024 §9.5 (list path)
- **Deliverables:** `TmSearchPickerPage` in a `TmModal lg` (full screen below sm): no URL or page state, own `picker[.N].search.<entity>` gridId, initial text selected, fixed leaves, reduced rows 1–2, no links, activation closing with `{ id, label, item }` (`tmRowsToObjects`), multiple mode with seeded checked ids, 'Done ({n})' with `TmEntityMultiPick` and the `maxItems` gate, an overlay page context, type-to-search on the modal pane; list mode on `/{t}/list/{segment}` (`#d=` payload of fixed filter and cols, `includeInactive: true`, no expression editor or Active only, cols editor rewriting the payload, crumb slot, invalid-payload state, `list.<entity>` gridId, never a recent); the `search` hatch replacing row 3 with the same context.
- **Tests:** harness: a picker over a live same-entity page grid registers its own gridId, closes with picks, takes a typed key with focus in the grid, and in `multiple` closes with the whole set after a removal and an addition, keeping an off-page chosen id; list mode's fixed leaf from #d=, sent with includeInactive true and no Active-only switch; a malformed payload; a child surface's export
- **Done when:** An entity field's advanced search opens this page and returns a pick. `/17/list/customers#d=…` renders with its fixed filter.
- **Planner note:** Provides TmLazyPickerPage for Advanced search (0029 §14.4).

### PC-161 — Paging through search results, picker create/edit pages and page/form context

- **Analysis id:** `0031-g` · **Spec:** 0031 · **Layer:** client · **Tier:** 2 · **Est. LOC:** 4,000
- **Sections:** §12.1–§12.2, §15, §16, §18 telemetry remainder
- **Gated on (DEPS.md):** PC-135, PC-124
- **Analyst dependency notes:** 0031-f; 0030-a (TmSearchSnapshot type); 0029 §14.4; 0026 §9.2–§9.3
- **Deliverables:** Recall of the search snapshot via `history.state.tmFrom`. The item pager (position, capped totals) with Alt+PageUp/PageDown. Within-page steps pushing one entry per record and remembering the snapshot's highlight. Edge reads with `snapshot.request` (skip/take/includeCount) writing a new snapshot with `supersedes`. Back across an edge re-reading the entry's page. The `request: null` bounds. Gaps showing not-found with the pager live. `TmDetailsPickerPage` in a `TmModal lg` (create with the typed text in the active display-name slot, or edit; footer Save and Cancel; `close({ id, label, item })`; `canDismiss` through `leave()`; no drafts, page state or paging; `picker.details:` grid ids with the depth rule; an overlay `details` context). The `details`/`settings` page-context publication (visible grid, errors with trace ids, actions). `TmFormContext.fields/propose/assisted` with the sparkles glyph. `tm.details.page/edit` telemetry.
- **Tests:** unit: paging arithmetic and edge reads; harness: paging within and across pages pushing per record; Back across an edge re-reading the page; `supersedes` on a jump to the list's entry; within ids with request null; picker create and edit over a same-entity page with its child grids live under their own ids; `paging-aborts` (three fast nexts, two aborted reads, one applied) under the fake clock
- **Done when:** Opening record 52 of 293 from the list and pressing next 49 times crosses to page 3 and Back returns to the list highlighted. An entity field's Create… opens the picker page and returns the new id.
- **Planner note:** Provides TmLazyPickerPage for Create and Edit.

### PC-162 — The page hatch, compact header and TmPageActionBar

- **Analysis id:** `0031-h` · **Spec:** 0031 · **Layer:** client · **Tier:** 2 · **Est. LOC:** 2,500
- **Sections:** §3.7, §5.4, §14.3
- **Gated on (DEPS.md):** PC-161, PC-134, PC-026
- **Analyst dependency notes:** 0031-e; 0023 §7.2
- **Deliverables:** The `details: { page, create?, edit?: 'page', refresh?: 'page', header?, createdUrl? }` handling: the hatch body region with its own scroll; no `details.edit` with `edit: 'page'` except inside a `tm-details-page` on another route; no `details.refresh` with `refresh: 'page'`; a hatch's own page-context kind with the host's actions kept after it; route query input binding. The compact header: one title row, badges ('Inactive' only), `tm-page-action-bar` in `[tmPageHeaderEnd]` (primary actions by order, a '⋯' menu in group order, the below-sm first-two rule), the `tmDetailsHeaderBand` in `[tmPageHeaderBand]`, no toolbar row and no pager. `TmPageActionBar` exported for pages outside the host.
- **Tests:** harness: the compact header (no toolbar row or pager, the band, the action bar's menu and its below-sm move); a hatch with edit: 'page', and a tm-details-page over the same entity on another route still entering edit mode; a hatch with refresh: 'page' (no details.refresh, its own action calling refresh())
- **Done when:** A test hatch with `header: 'compact'` renders the documented geometry. Spec 0034/0035 can build on it.

### PC-163 — Pinned bar, TmPins and the Manage pins dialog

- **Analysis id:** `0027-f` · **Spec:** 0027 · **Layer:** client · **Tier:** 2 · **Est. LOC:** 6,000
- **Sections:** §8 (all except the model in 0027-b), §5.3 (pin removal focus)
- **Gated on (DEPS.md):** PC-122, PC-115, PC-118, PC-107, PC-023, PC-105
- **Analyst dependency notes:** 0027-b; 0027-d; 0025 §9; 0023 §4.3, §5.1, §8.3, §8.4; 0029-b (tm.entity.tenantFrozen reason)
- **Deliverables:** TmPins service (items/current/canPinCurrent/visible, pinCurrent/update/move/remove/setVisible/addFolder/removeFolder/replace via TmPreferences.set, preload of ok pins), TmPinnedBar (chips, current-chip styling, folder chips with menus, More menu with locked rows and current marking, pin control with Alt+Shift+P and Frozen disabled reason, Manage pins button, empty text, <md pills with current first), measured overflow (hidden aria-hidden measuring row, cached widths re-read on label/state/language/direction/font changes, ResizeObserver + afterRenderEffect read phase, chips hidden until first fit after document.fonts), pin popover (name, folder select with New folder…, Remove/Done), toast 'Pinned {label}.' when the chip is not visible, roving tabindex and announcements, drag reorder (tmDragSource tm.pin, insertion line, 500 ms folder hover open), revoked/unavailable chips with menus, Manage pins dialog (tm-reorder-list with nested folder lists, rename, Move to, Remove, New folder, Show the pinned bar switch, Save/Cancel on a copy, Frozen-disabled Save); TmPinnedBarHarness; tm.shell.pins-changed and pinned-bar-toggled telemetry
- **Tests:** harness: roving focus; fit with long Arabic labels, RTL, below md, across a resize, a pin's state change and a font swap, current pin in More, unchanged by hover/focus and (≥md) navigation; a late-loading font with no chip painted before the fit; folders; revoked pins; Manage pins button in every bar state; unit: each mutation's document
- **Done when:** Harness green; no layout shift across pin into More and hover along the bar (unit/harness probe); dialog in the deferred dialogs chunk within budget.

### PC-164 — Notifications, Assignments and Jobs pages, open route and job details

- **Analysis id:** `0028-g` · **Spec:** 0028 · **Layer:** both · **Tier:** 2 · **Est. LOC:** 5,500
- **Sections:** §10.1, §10.2, §10.4, §10.5, §1 (core feature declarations)
- **Gated on (DEPS.md):** PC-157, PC-155, PC-156, PC-036, PC-105, PC-117
- **Later seams (not gates):** PC-176 (0019-c), PC-180 (0032-b2). Ship the seam; the later checkpoint fills it.
- **Analyst dependency notes:** 0030; 0031; 0028-d; 0028-e; 0020 §2.3, §3.7, §3.8, §11.1, §11.2; 0019 §12; 0032 §11 (Import page link, soft); 0029-b, 0029-c (cells)
- **Deliverables:** core-feature declarations in the primary: tmNotifications (columns with TmNotificationTextCell, tabs, rowLink to the open route, read-all/clear-read/read/clear page actions), tmNotificationOpen tmPage and TmNotificationOpenPage (store-first lookup, read when unread and writable, replaceUrl to target or job owning record or Jobs mine tab, not-found view), tmAssignments (TmAssignmentTargetCell, three tabs, rowLink via TmUrls.target), tmJobs (section administration order 60, TmJobLabelCell, TmJobProgressCell, tabs, cancel/resume/confirm actions, Stop labels), TmJobDetails (fixed progress block, details, PendingConfirmation items with Confirm for the initiator, job.changed quiet refresh with one read in flight, 5 s/30 s visible polling, terminal stop, status announcements, owning-record link via {segment}/query top 1)
- **Tests:** harness: job details (job.changed quiet refresh, poll for a non-requester and a down hub, coalesced reads, announcements, result link, 30 s poll while connected), PendingConfirmation job (bell Review, progress cell link, notice open route resolving the Import row, Resume hidden, warnings with 'and {n} more', Confirm for initiator only and disabled on ReadOnly, stale list re-read), core.job.failed of an import vs an export, core.job.held opening Jobs mine
- **Done when:** Harnesses green; launcher shows Jobs under Administration and Notifications/Assignments as search-only entries; /attention-pages ≤ 10 KB gzip excluding /search and /details.
- **Planner note:** The export and import job kinds and the Import page link (0019 §12, 0032 §11) land with Tier 3 (9a); ship the pages without them.

### PC-165 — Access tab, the /access page and another user's Preferences dialog

- **Analysis id:** `0032-e` · **Spec:** 0032 · **Layer:** both · **Tier:** 2 · **Est. LOC:** 4,500
- **Sections:** §4.6, §4.8, §4.9
- **Gated on (DEPS.md):** PC-139, PC-157, PC-090
- **Analyst dependency notes:** 0032-d; 0028 §10.3 (tm-notification-settings); 0018 §3.5 §3.7 (wire); 0026 §9.3 (access kind)
- **Deliverables:** `tm-user-access [user]`: every non-wildcard securable in registry order, in chunks of 200 with two in flight under the host's signal; rows grouped by resource label with `TmPermissions.label` names and the raw pair; Enter-expanded reasons (role grants with links, filters, until-times, bespoke `Access_Reason_*`, System user, drifted 'Not in effect', a 'Who else has this' link); search matching labels and raw names; the All/Allowed/Not allowed filter; Refresh. `TmAccessPage` on `/{t}/access` with URL state (`resource`/`action` or `tab=user&user=`): the searchable securable select, `access/holders` with abort, the Everyone banner, the holders grid (links, endsAt, roles, filters), capped and hidden counts, the Not-in-effect disclosure, a By user tab over `tm-entity-field core.User` + `tm-user-access`, and an `access` page context. `TmUserPreferencesDialog` (pinned-bar switch via `users/preferences/get|set`, a `@defer (on immediate)` `tm-notification-settings [user]`, self-saving with 'Couldn't save your changes'/Try again, Close-only footer, the Frozen-tenant disable). Cross-links from 0032-c/d. `TmUserAccessHarness`, `TmAccessPageHarness`.
- **Tests:** unit: the access page's URL state; harness: access chunks of 200; access labels equal to TmPermissions.label, and a search by label or raw pair; leaving the Access tab aborts its chunks; holders for a pair, the Everyone banner, the capped and hidden counts, problems; By user over tm-user-access; the cross-links; the preferences dialog saving each change with the target id (two grid changes within 600 ms sending one write), a failed write's status and Try again, Close only, hidden for a service account and without the grant, no test delivery, disabled on Frozen
- **Done when:** 'Who holds acme.SalesInvoice · Save' and 'what can user 9 do' both answer from the harness. Each Preferences change writes once with the target id.

### PC-166 — My settings hub: Profile source page, Language & region, Display, Notifications frame

- **Analysis id:** `0032-g` · **Spec:** 0032 · **Layer:** both · **Tier:** 2 · **Est. LOC:** 4,000
- **Sections:** §8.1–§8.5, §12.2 (self-service split read side)
- **Gated on (DEPS.md):** PC-165, PC-136, PC-133, PC-107, PC-115, PC-157
- **Analyst dependency notes:** 0032-a; 0031-d (source page, rebase); 0024 §7.2 §7.6–§7.8; 0025 §9 §5.3; 0027 §8.2 §10.1; 0028 §10.3
- **Deliverables:** `TmMeHub` on the `me` route with child routes (nav column from md, a menu button below md, `aria-current`, the Account external link, per-section headings and `document.title`, the multi-tenant scope line, ReadOnly/Frozen tenant behaviour, page contexts). `tmMeSource` + `TmMeProfileLayout` (read from `TmLaunchCache.docs.me().user` with no request, `users/me/save` with the eight members, `MeResult` adoption, `rebase` when the profile members are unchanged, the separate draft key, no step-up). Language & region (standalone locale fields with `scope="tenant"/"language"`, `TmLocalization.setLocale`, the `localeSave` status line with Try again, 422 on the field, `tm-auto-time-zone` over `TmDeviceZone`). Display (theme `tm-segmented` over `TmTheme`; the pinned-bar switch through `TmPreferences` curated flush). The Notifications section frame around 0028's body.
- **Tests:** harness: the nav's aria-current and the below-md menu; /{t}/me showing Profile; a Profile Save sending eight members; a locale change while Profile is in edit mode rebasing, so Save sends the new stamp and meets no 409; two Language & region changes within 600 ms sending one write, and 'Tenant default' sending reset; a failed write's status and Try again; tm-auto-time-zone applying at once; a ReadOnly tenant disabling Profile only, and a Frozen one disabling every tenant write but not the theme
- **Done when:** `/17/me` and `/17/me/region` render values with every request held. A session-menu locale switch during Profile edit leads to a clean Save.

### PC-167 — Home page completion: assignment widgets, full home geometry and home e2e

- **Analysis id:** `0035-a` · **Spec:** 0035 · **Layer:** both · **Tier:** 2 · **Est. LOC:** 7,500
- **Sections:** §1.1 (primary + main module), §2.3, §4.1 (registry + widgetTypes emit), §4.2 built-in rows, §4.3, §8.1–§8.6 (view-mode grid and tile; denied='hide' path via client checks), §9.2, §9.3, §15, §16 home context + tm.home.* telemetry
- **Gated on (DEPS.md):** PC-123, PC-001, PC-107, PC-111, PC-155, PC-026, PC-013
- **Analyst dependency notes:** 0011 §2.1; 0024; 0025 §13; 0026 §9; 0028 §5.2, §7.3–§7.4; 0023 §7.1; 0010 §D.4, §D.8
- **Deliverables:** Server: IWidgetTypeRegistry/WidgetTypeDescriptor/WidgetSubject, WidgetTypeRegistry realising WidgetTypeContributionItem with key-grammar and contributor-key startup checks, WidgetTypeClientAssetContributor (widgetTypes section), CoreFeature registering core.assignments-to-me, core.assignments-by-me, core.recents. Client primary: tmWidget, TmWidgetDefinition/Size/TypeDeclaration, TmDashboardWidgetModel, TmTextSlots, core feature `widgets` for the three built-ins. @tellma/core/dashboards main module: TmDashboardGrid (CSS grid, container queries, width cap token, denied input, tiles signal), TmDashboardTile (section/card, title rules, status line, menu skeleton, §8.6 states), tmPackDashboard (compaction + reading-order bands + narrow rows), injectTmWidget/TmWidgetContext, built-in list widgets (live via TmAttention and nav.recent, offline/empty/loading states), TmHomeHost (route kind home, hatch via NgComponentOutlet, title, context, tm.home.rendered), TmHomePage, TmHomeGreeting (time-of-day in display zone, full name in bdi, weekday date line, boundary/visibility timer), TM_HOME_WIDGETS; harnesses TmDashboardGridHarness and TmHomePageHarness, tmFakeDashboard; widget-missing/widget-not-registered drift checks wired
- **Tests:** unit(vitest): tmPackDashboard vectors (compaction with hidden tiles, bands, narrow rows, §8.2 example), greeting across zones/calendars with fake TM_NOW; harness: every state of §8.6 (client-derived), §9.2, §15.4, denied hidden, a home hatch through TmHomeHost, a fixture widget type; C# unit: registry startup refusals, widgetTypes emit; e2e: home warm/cold, home-geometry.spec (assignment arrives over hub, no other tile moves), warm-launch on /17
- **Done when:** /{t} renders greeting, date line and the three lists from stored documents with no request on a warm launch; main module ≤ 9 KB gz and the home route's transfer excludes /details, /entity, /reports, /sharing and core-ui/chart; drift build codes fire on mismatched keys
- **Planner note:** Tier-2 remainder of 0035-a after 0035-a1 (the home scaffold).

### PC-168 — Entity-model e2e, geometry and RTL specs on Acme

- **Analysis id:** `0029-h` · **Spec:** 0029 · **Layer:** both · **Tier:** 2 · **Est. LOC:** 2,500
- **Sections:** §17 (e2e row), §18
- **Gated on (DEPS.md):** PC-121, PC-119, PC-092
- **Analyst dependency notes:** 0029-e; 0029-g; 0030; 0031; 0018 §1.4; 0024 Acme client build
- **Deliverables:** Playwright on Acme: drag a field into a filter at the caret then undo (Chromium, Firefox, WebKit), keyboard-only insert, no unreachable path for a restricted role, a URL with an unreachable column loads without 403, picker advanced search and create round trips, display name copied from a cell/title/picker input pastes into a search and finds its row with no bidi control on the clipboard, a language switch with no request; entities-load.spec, expression-editor.spec, path-picker.spec, entity-language-switch.spec, discover-abort.spec, expression-editor-geometry.spec, column-filter-geometry.spec, path-picker-rtl.spec, display-name-bidi.spec, keyboard-insert.spec, axe battery; Acme customers declaration and the restricted role wiring
- **Tests:** e2e (Playwright, three engines for drag/undo), axe
- **Done when:** §17's nine suites green on Ubuntu and Windows, both goldens in TS and C#; §18 pinning specs pass.

### PC-169 — Shell e2e, responsive and axe battery on Acme

- **Analysis id:** `0027-g` · **Spec:** 0027 · **Layer:** client · **Tier:** 2 · **Est. LOC:** 3,000
- **Sections:** §13 (Playwright, Responsive, axe, Manual), §14
- **Gated on (DEPS.md):** PC-163, PC-116, PC-118, PC-122, PC-156
- **Analyst dependency notes:** 0027-c; 0027-d; 0027-e; 0027-f; 0028-e (badges in shell-layout); 0030 (filtered search pages to pin); 0018 Acme reference stacks and sandbox tenant 2; 0024 Acme client build
- **Deliverables:** Playwright specs on the Acme host in en and ar: cold launch to picker with tenants 1 and 2 then Connect, warm shell with every request held (shell-warm.spec), signed-out page; launcher Mod+K from a grid cell and a text field, arrows across sections in LTR and RTL, 'clients' in Arabic finds Customers, Escape keeps text; pins: filtered search pinned into a folder then reload, a revoked pin after a role change, pinning from the launcher band with the bar hidden, keyboard reorder in Manage pins; language switch with one me/locale write and no client/sync; About offline, with newer server (Reload, blocked update, Reload anyway) and older; installed mode back/forward under Chromium --app; responsive matrix at 390/576/768/992/1200/1440; axe light/dark × LTR/RTL with each overlay open; shell-feedback.spec, launcher-perf.spec, launcher-hang.spec, shell-layout.spec (CLS 0), keyboard-shell.spec; Acme sales section, synonyms and credit-review page wiring
- **Tests:** e2e (Playwright), axe, manual checklist per release
- **Done when:** Every §13 suite green on Ubuntu and Windows; §14's pinning specs pass; /shell and primary budgets within spec 0024 §12.

### PC-170 — Acme search declarations, e2e, axe and budget suites

- **Analysis id:** `0030-i` · **Spec:** 0030 · **Layer:** client · **Tier:** 2 · **Est. LOC:** 3,000
- **Sections:** §18 (e2e, axe, budgets), §19 (UI rules tests, reference distribution)
- **Gated on (DEPS.md):** PC-160, PC-114, PC-092, PC-136
- **Later seams (not gates):** PC-179 (0030-f2). Ship the seam; the later checkpoint fills it.
- **Analyst dependency notes:** 0030-h; 0030-f2; 0024 §13 (Acme e2e host); 0018 §6.3 (Acme dev data); 0032-a (gl.Center tree for the Centers e2e)
- **Deliverables:** Acme declarations: `customers` (tiles, the `credit` options filter, the `mine` switch, `acme-credit-cell` display hatch), `salesInvoices` (tabs, export/import). Playwright specs: search-geometry, search-feedback, view-toggle, keyboard-only, tree-rtl, pager-rtl, typing-aborts, warm-launch entries for /17/customers and /17/centers. The axe battery over the §5.3 states, popups, selection, search tree and tiles in light/dark and LTR/RTL. Budget lines (route ≤16 KB, each deferred chunk ≤6 KB, route ≤110 KB). en/ar strings for `tm.search`.
- **Tests:** e2e (Playwright; Chromium on PR; @cross-engine Firefox/WebKit), axe, budgets
- **Done when:** All §18 suites green on the Acme host. The budgets pass `size:check`.
- **Planner note:** The export/import e2e flows belong to 0030-f2 (Tier 3).

### PC-171 — Acme details layouts, e2e, axe, geometry and budget suites

- **Analysis id:** `0031-i` · **Spec:** 0031 · **Layer:** client · **Tier:** 2 · **Est. LOC:** 3,500
- **Sections:** §17 (e2e, axe, budgets), §18 (UI rules tests, what Acme adds)
- **Gated on (DEPS.md):** PC-162, PC-161, PC-170, PC-114, PC-092
- **Analyst dependency notes:** 0031-g; 0031-h; 0030-i (search page on Acme, for paging e2e); 0024 §13; 0018 §1.4
- **Deliverables:** Acme `customer-details` (with Image, §7.2's credit rule, the generated `set-credit-limit` dialog) and `sales-invoice-details` (header card, Lines/Attachments tabs, `related-documents` in the aside, audit card, promoted `close`). Playwright specs: details-feedback (rAF probe), paging-aborts, details-geometry, details-rtl, details-keyboard, warm-launch on `/17/customers/5` and `/17/sales-invoices/1284`, reload during edit/save (`savingAt` then self conflict), a changed save contract, Back during a save refused, a two-context conflict with Overwrite, step-up return, `?child=Lines:42`, paging across a page then Back to the list, image upload/replace, attachments; the axe battery; budget lines (/details ≤23 KB, dialogs ≤4 KB, route ≤80 KB). en/ar strings for `tm.details`/`tm.entity`.
- **Tests:** e2e (Chromium on PR; @cross-engine Firefox/WebKit), axe, budgets
- **Done when:** All §17 suites green on the Acme host and the budgets pass.

### PC-172 — Acme extensions, fixtures, e2e, axe and budget suites for the stock pages

- **Analysis id:** `0032-h` · **Spec:** 0032 · **Layer:** both · **Tier:** 2 · **Est. LOC:** 3,500
- **Sections:** §13 (e2e, axe, budgets), §14 (UI rules tests, page actions, telemetry, what Acme adds)
- **Gated on (DEPS.md):** PC-166, PC-165, PC-138, PC-170, PC-171, PC-114
- **Later seams (not gates):** PC-215 (0032-b1), PC-180 (0032-b2). Ship the seam; the later checkpoint fills it.
- **Analyst dependency notes:** 0032-b1; 0032-b2; 0032-e; 0032-f; 0032-g; 0030-i; 0031-i; 0024 §13 (Acme e2e host)
- **Deliverables:** Acme: the centers override with `Manager` and `my-center-details`, the `invoicing` settings tab with its preview, e2e fixtures creating New/Invited/Joined/service users, roles, scheduled and expired memberships, and the drift role (a `Name3` filter saved before removing the tertiary language). Playwright: settings (two administrators on different keys and on the same key, add a language, secret replace/clear, the invoicing preview), users (create/invite/Check delivery, credentials and Copy, restricted role, a one-hour Auditor membership expiring, Preferences), roles (popup spill, drift), access page, centers, My settings (warm sections, Profile save, region re-format, session-menu switch during edit, theme and pinned bar, Account link), schedules (run-now, built-in locks), emails (HTML, cid image, blocked remote image), imports (following, stopped ranges, pending confirmation Confirm). `admin-feedback`, `admin-keyboard`, `settings-geometry`, `role-editor-geometry` and `email-body-rtl` specs. The axe battery. The /admin chunk, dialog, primary and module-gl budgets. Remaining `tm.admin.*` telemetry and en/ar strings.
- **Tests:** e2e (Chromium on PR; @cross-engine Firefox/WebKit), axe, budgets
- **Done when:** All §13 suites green on the Acme host. The budgets pass.
- **Planner note:** The Schedules/Outgoing emails (0032-b1) and Exports/Imports (0032-b2) pages are Tier 3; their e2e lands with them.

## Phase 9a Excel import and export (Tier 3)

### PC-173 — Excel contracts, cell codec, number formats and the workbook/table writer

- **Analysis id:** `0019-a` · **Spec:** 0019 · **Layer:** backend · **Tier:** 3 · **Est. LOC:** 6,500
- **Sections:** §1.1, §1.3, §2.1 contract records, §3.1-§3.3, §4.1 records (ExcelQuery/ExcelPage/IExcelRowSource unless 0015 created them, ExcelContext, ExcelColumnSpec, ExcelWorkbookPlan/SheetPlan, IExcelExporter), §4.2 spooling, §4.3, §4.4, §5.3 manifest writer, §14.1-§14.3 constants, Appendix A
- **Gated on (DEPS.md):** PC-021, PC-062, PC-064
- **Analyst dependency notes:** 0012 §3.1; 0013 (ILabelProvider, ICalendarSystem, ILanguageCatalog); 0015 §7.3; 0015 §14.1; 0016 §3.1
- **Deliverables:** Tellma.Core.Abstractions.Excel: ImportMode, ExportKind, ExportSourceRequest/ExportSource cases, ExportRequest/ExportColumn, ExportForImportRequest/ExportCollection, ExportOutcome, ImportColumnMapping/ImportSheetMapping, InspectImportRequest, ImportRequest/StartImportRequest, ImportPlan*/status enums, ImportError, RowRange, ImportOutcome, ImportException, ExcelOptions + validation, ExcelErrorCodes, ExcelTelemetryNames. Tellma.Core.Excel: WorkbookWriter (OpenXmlWriter, inline strings, cellXfs/numFmt dedupe, bold frozen filterable header, widths, RTL, validation lists, hidden protected manifest sheet, 1900 date system), CellCodec encode+decode by type (long/wide decimal/Guid text fallbacks, serial rules incl. <61 and 1904 shift, cell kinds s/inlineStr/str/b/n/d/e), NumberFormatBuilder (token translation, [$-CCLLLL], custom-culture LCID omission, et → text), sheet-name scrubbing/uniqueness, WriteTableAsync with ExcelTableSpec/ExcelTableRow/ExcelRowStyle; pin DocumentFormat.OpenXml
- **Tests:** pure: encoder/decoder round trips for every §3.1 row incl. the 15-digit boundaries, ISO 8601 offsets; 1900 and 1904 workbooks, serial 60 refused; every cell kind; number formats for en-US, ar-SA gc+uq, am-ET (et → text with manifest pattern), a custom culture with no LCID; table writer (multiple header rows, each ExcelRowStyle, indent, frozen pane after FrozenColumns, RTL, FileNameStem, no manifest); RowRange JSON [{"from":2,"to":101}] through ImportOutcome/ImportException/ImportCheckpointState; option validation
- **Done when:** The writer produces files that pass Open XML SDK validation in tests. Memory stays flat (inline strings, forward-only). The table writer is ready for 0033.
- **Planner note:** ExportSource, ExcelQuery, ExcelPage and IExcelRowSource are already seeded by 0015-a: extend them.

### PC-174 — Excel row source

- **Analysis id:** `0015-j` · **Spec:** 0015 · **Layer:** backend · **Tier:** 3 · **Est. LOC:** 2,500
- **Sections:** §15, §12 Excel row (row source part), §16.2 Excel export row, §18.2 row-source pins
- **Gated on (DEPS.md):** PC-142, PC-070, PC-173
- **Analyst dependency notes:** 0015-e; 0019 §1.1 Excel contracts (IExcelRowSource, ExcelQuery, ExcelPage, ExportSource) or their contract-first stubs
- **Deliverables:** ExcelRowSource<TEntity,TKey> (ByIds/ByQuery/All composition incl. named filters, search and activatable conjunct, Read check on first call, per-page Read batches reconnecting through the runner, CountCap on first page, dependents restricted by @tb_keys at any depth, follow-up reads for dependents beyond MaxTake, ReadAsync multi-query in one RT, lookup targets under the caller's Read decision), IChildQueryService.ExcelRowSource for Exportable surfaces
- **Tests:** integration (WidgetParts/WidgetPartNotes riding each page's RT; capped count on first page; empty ByQuery in Id order; inactive excluded unless IncludeInactive; dependent beyond MaxTake completed before yield; ReadAsync several lookups in one RT; unreadable lookup target yields nothing; revocation mid-stream; child surface by-query export yields the same rows)
- **Done when:** Row source passes its pins with fixture stacks whose MaxTake is lowered via [Stack]; no connection opened outside the runner

### PC-175 — Exports: planners, manifest and the synchronous export operations

- **Analysis id:** `0019-b` · **Spec:** 0019 · **Layer:** backend · **Tier:** 3 · **Est. LOC:** 6,000
- **Sections:** §1.2 (EntityCompanion<TEntity, ExcelOperations<TEntity>>, ChildExportOperations, options binding, singletons, startup warnings), §1.4 export rows, §2.1 source rules + export/export-for-import methods + ChildExportOperations.ExportAsync, §2.2, §2.3 export rows, §4.1 (column expansion through IDisplayNames, multilingual content slot, folding, derived headers), §5.1-§5.4, §6, §13 export bullets, §14.3 export instruments/logs
- **Gated on (DEPS.md):** PC-173, PC-174, PC-062, PC-021
- **Analyst dependency notes:** 0019-a; 0015 §15; 0015 §11.3; 0015 §2.2; 0016 §3.9; 0012 §3.1 (IDisplayNames); 0008 (QueryexEngine.Validate)
- **Deliverables:** ExportPlanner (PlanDisplay, PlanEditable with Columns/Children/ReferenceKeys subset rules at every depth, child-sheet naming with leading-segment dropping, surrogate fallback with KeyKind), manifest header block and column table, same-source gate helper, ExcelContext builder from RequestContext, ExcelOperations<TEntity>.ExportAsync/ExportForImportAsync (ByIds/ByQuery mapping, BadRequest faults, CountCap first-page cap before any spool, empty-ids template, Ids ≤ MaxIds), ChildExportOperations<TChild>.ExportAsync, CoreFeature registration of the companion on every stack with Export or Import and on Exportable child surfaces
- **Tests:** pure: planner over the fixture entities (column sets, header labels per culture, sheet names at every depth, Columns/Children subsets, reference-key defaults and overrides, surrogate fallback); display columns (navigation → display name in the Arabic slot with slot-1 fallback, group anchor, Id only when listed, select terms once, derived headers, two Filters + NamedFilters → one ByQuery, value-set labels); manifest write; request validation (Ids beside clauses, IncludeInactive, empty/101 columns, header > 255, ExportCollection faults). integration: synchronous display and editable exports through the row source incl. a child-surface export under a filtered owner grant; MaxSynchronousExportRows+1 rejected at the first page with one round trip, no temp file, message naming export/start; §1.4 export rows through DataAccessScope
- **Done when:** Every eligible stack exposes export and export-for-import, and child surfaces expose export. The API goldens of 0015/0016 and any route-list tests (for example 0018 §5.5 centers) are updated for the new routes.

### PC-176 — Import intake, reader and mapping; Export/Import entities; inspect-import

- **Analysis id:** `0019-c` · **Spec:** 0019 · **Layer:** backend · **Tier:** 3 · **Est. LOC:** 6,500
- **Sections:** §2.1 inspect-import, §1.4 inspect row, §5.3 manifest read/validation, §5.4, §7.1-§7.4, §8.1-§8.3, §12.1 (entities, tables, services, access criteria, ValidateDeleteAsync), §13 manifest/package bullets
- **Gated on (DEPS.md):** PC-175, PC-173, PC-084, PC-089, PC-036, PC-043
- **Analyst dependency notes:** 0019-a; 0019-b; 0017-c; 0017-d; 0020 §2.3; 0020 §2.4; 0014 §5.1
- **Deliverables:** Export and Import entities (core.Exports/core.Imports with sequences, FileId [BlobReference] export-file/import-file, JobId FK ON DELETE SET NULL, indexes, DefaultSelect, ownership attributes) with ExportService/ImportService on Query|Details|Delete|Enlist stacks, ExportAccessCriteria/ImportAccessCriteria (CreatedById = me()), Imports.JobActive delete guard; WorkbookReader (package limits from the central directory, counting streams, structure checks, DtdProcessing.Prohibit, dimension or forward count, used-range header detection, veryHidden ignored, .xlsm), SharedStringStore with byte charge, spill to ScratchPath and entry cap, manifest reader (untrusted), HeaderIndex (all candidate cultures, symbols, normalisation, collision removal, cached per entity/settings tag/culture), ImportMapper (sheets, columns, overrides, Damerau-Levenshtein suggestions, ownership, identity-column warnings, language-aware slots), IExcelImporter.InspectAsync, ExcelOperations.InspectImportAsync with staged-blob intake (ResolveAsync + OpenReadAsync → delete-on-close temp copy); test/core/Tellma.Core.Tests/Excel/Corpus
- **Tests:** pure: manifest round trip, same-source gate incl. another deployment of the same app, SchemaFingerprintMatches, hostile manifest; mapping across tenant-language permutations, header-index candidate kinds and collisions, overrides by letter, suggestions, plan columns echoed as overrides being a fixed point across en,ar → ar,en, IdentityColumnIgnored on foreign-source and manifest-less files; invalid mappings refused before the blob is read; UnknownSheet/UnknownColumn/UnknownPath/Duplicate sheets; crafted zips (oversized part, external link, two workbook parts); shared-string entry charge and TooManySharedStrings; the corpus. integration: inspect-import over a staged upload in one round trip; another user's FileId → NotFound; Imports.JobActive
- **Done when:** An uploaded workbook from any supported producer yields a correct ImportPlan. Parser limits bound memory and disk.

### PC-177 — Import semantics and the synchronous import

- **Analysis id:** `0019-d` · **Spec:** 0019 · **Layer:** backend · **Tier:** 3 · **Est. LOC:** 7,500
- **Sections:** §6.2-§6.3, §9.1-§9.6, §10.1-§10.4, §11.1-§11.3, §2.3 import rows, §1.4 import rows, §13 import bullets
- **Gated on (DEPS.md):** PC-176, PC-175, PC-062, PC-072, PC-074, PC-068, PC-080, PC-081
- **Analyst dependency notes:** 0019-c; 0019-b; 0015 §4.1; 0015 §6.4; 0015 §6.8; 0015 §7.5; 0015 §8; 0015 §9; 0016 §6.1; 0016 §7.1
- **Deliverables:** ExcelImportSession<TEntity> (Parse: decode every mapped cell by mapped type, in-file uniqueness, links by Id at every depth, OrphanChildRow/OwnerSheetAbsent, per-owner TooManyChildren/TooManyDescendants, Kahn topological order and ParentCycle, OrderHash; LookupsFor with KeySetRestriction TVPs and KeyTooLong; HydrationIds; Resolve with overlay, WriteOnceChanged, child matching by Id/natural key/replacement with IncompleteChildSheet, temporary ids, self-reference binding order, committedNewIds; LaterReferencedIds; ReferencedFrom; CoordinateMap); ExcelOperations.ImportAsync (sync thresholds → TooLargeForSynchronous, lookups in one RT via ExcelRowSource(ExportSource.All), GetByIdsAsync hydration, SaveAsync Source=Import under Check, hydration-conflict re-hydrate/re-resolve retries, error translation through the coordinate map, ConfirmationRequired → confirmable ImportErrors, error capping)
- **Tests:** pure: in-file uniqueness, in-chunk resolution, stable order and OrderHash (changing on a tenant-language remap), ReferencedFrom pruning, cycles beyond the tree parent, links at depth 2 (negative/duplicate/blank Ids), IncompleteChildSheet (missing column, missing descendant sheet), TooManyChildren at depth 2 and TooManyDescendants, the coordinate map for every path shape, error capping and TotalErrors. integration: export → import round trips in Insert/Update/Upsert with the tree and Widgets → WidgetParts → WidgetPartNotes, same-source and cross-tenant (IsActive absent and true after Insert, WriteOnceChanged, part replacement deleting notes, renaming Code by Id, stale-Id DuplicateRowKey); hidden reference → ReferenceNotFound; readable-but-unwritable row → 403; concurrency (one retry with tellma.excel.import.retries = 1, a sheet Stamp conflicting at once, IgnoreSheetStamps saving); TooLargeForSynchronous naming import/start; a warning failing a synchronous import as confirmable and passing with Tellma-Confirm; §1.4 import rows
- **Done when:** A synchronous import is all-or-nothing, with coordinates on every error. The export-fix-import loop round-trips at every depth.

### PC-178 — Background exports and chunked, resumable background imports

- **Analysis id:** `0019-e` · **Spec:** 0019 · **Layer:** backend · **Tier:** 3 · **Est. LOC:** 7,500
- **Sections:** §2.1 /start methods, §2.3 /start rows, §1.4 background rows, §12.1 job argument records, §12.2-§12.6, §14.3 background instruments/logs
- **Gated on (DEPS.md):** PC-177, PC-036, PC-098, PC-144, PC-145, PC-084
- **Analyst dependency notes:** 0019-d; 0020 §3.1; 0020 §3.3; 0020 §3.8; 0020 §4.1; 0020 §5.6; 0020 §6.6; 0020 §11; 0020 §14.4; 0021 (NotificationType, NotifyOnSuccess); 0017-c
- **Deliverables:** ExcelOperations.StartExportAsync/StartExportForImportAsync (EnqueueAsync with Label, JobAccepted(JobId, null)); StartImportAsync (IEnlists<Import> enlisted insert via the invoker's IOpenWriteHost + ImportService.ContributeAsync enqueueing new rows with null JobId; JobAccepted(JobId, ImportId)); ChildExportOperations.StartExportAsync; ExcelJobCulture, ExportJobArguments (JSON-polymorphic on kind; Display and ForImport cases), ImportJobArguments; ExportJobHandler (Read re-check, plan + stream with CountCap = MaxExportRows, progress without 100, StageAsync with BlobRejected → non-retried Fail, enlisted Export insert with JobId ServerOwned on the completion batch, NotifyOnSuccess core.export.ready); ImportJobHandler (pass 1 parse + Atomic ceilings + ResumeMismatch; pass 2 chunks bounded by ImportChunkRows/parent ceiling/MaxRowsPerSave, checkpoint through SaveOptions.OnPersist → IJobProgress.Append with ImportCheckpointState and pruned CommittedNewIds, PendingConfirmation rethrow with sheet/row, cancel at chunk boundary, Stopped outcome, lost lease/host stop → OperationCanceledException, completion enlisted update + core.import.completed or Excel.Import.Failed); NotificationType registrations; Excel.Export.Label/Excel.Import.Label resources
- **Tests:** pure: ExportJobArguments JSON round trip for Display and ForImport (Report case added with 0033). integration through the 0020 worker harness: export/start and export-for-import/start rows inserted in the completion transaction with the blob confirmed, no handler-reported 100, notification with fileId, retention deleting the row and releasing the blob, cancel of a pending export, Labels, child-surface export/start, a ReadOnly tenant running exports while import/start is refused, oversize export-file failing once with Blob.TooLarge; chunked import with ImportChunkRows = 100 (parent in chunk 1 and child in chunk 2, by natural key and by negative Id across a simulated crash), chunk-2 failure reporting chunk-1 ranges, post-check rollback of chunk + checkpoint then retry, lease loss in chunk 3 resuming without re-applying, host stop → Pending then resume, three interrupted attempts → AttemptsExhausted, PendingConfirmation in chunk 2 then confirm, Atomic waiting, cancel/retention stop recording Stopped, retry-then-cancel, resume under 100 → 37 chunk size, ResumeMismatch, cancel during chunk 3, Atomic one transaction and TooLargeForAtomic, deleting one's own running Import refused
- **Done when:** A large import commits in chunks and survives deploys and lease loss without re-applying or skipping a row. Exports land as Export rows owning their blob.

### PC-179 — Export, the import dialog and TmImportResultView

- **Analysis id:** `0030-f2` · **Spec:** 0030 · **Layer:** client · **Tier:** 3 · **Est. LOC:** 4,000
- **Sections:** §13.2, §13.3
- **Gated on (DEPS.md):** PC-170, PC-159, PC-173, PC-109, PC-067
- **Analyst dependency notes:** 0030-f1; 0019 §2.1 §3 §4.1 §11.3 §12 (wire); 0028 §8.1 (toasts); 0005 §8.1 (tm-dropzone); 0024 §3.4 (long routes, 6 min)
- **Deliverables:** The Export menu (grid, for import, template) with the displayed-column `ExportColumn` model (headers as shown without the zone suffix, image columns dropped), source by query or ids, the sync download via `TmApi.download` + object URL vs `/start` for capped counts, the 413 `Excel.Export.RowLimitExceeded` retry on `/start`, the sticky progress toast with Cancel, the 202 toast with Track, child-surface export; `TmImportDialog` (lazy `import()`): dropzone upload with progress and abort, `inspect-import` plan view with unmapped-column suggestions and the mode/row-key choice, foreground `import` (non-dismissable) vs `import/start` with `atomic`, the result step, 'Import anyway' resending confirmed keys, the step-up note; the exported `TmImportResultView` (`tm-import-result-view`: counts, errors table of the first 100 + 'and {n} more', Copy errors, `stopped` committed ranges); `TmImportDialogHarness`; `tm.search.export`/`import` telemetry.
- **Tests:** harness: export bodies (columns, filters, ids), a capped count straight to /start, the 413 fallback on a known count, import steps with a plan, a 422, a confirmable 422 resent with its keys, a 202 and a stopped outcome; budget lines for the dialog chunk
- **Done when:** Export grid downloads a file named by the server. An import round-trips in the harness. `TmImportResultView` is exported for 0032-b2.

### PC-180 — Exports and Imports pages with job following and pending confirmations

- **Analysis id:** `0032-b2` · **Spec:** 0032 · **Layer:** both · **Tier:** 3 · **Est. LOC:** 3,000
- **Sections:** §11
- **Gated on (DEPS.md):** PC-172, PC-136, PC-179, PC-164, PC-134
- **Analyst dependency notes:** 0032-a; 0030-f2 (TmImportResultView); 0028 §10.5 §6.2; 0031-e (quiet refresh, delete failure)
- **Deliverables:** `TmEntityNameCell`, `TmDownloadCell`, the `Job.Status` column. `TmExportDetails` (Download primary link, collapsed Request card). `TmImportDetails` (Result card over `TmImportResultView`, the stopped-import alert with committed ranges, the job-ended-without-result alerts with View job, the pending-confirmation alert reading `jobs/get` `PendingConfirmationsJson` for the initiator only, Confirm via `jobs/confirm` with distinct keys, Stop via `jobs/cancel` after 0028 §6.2's confirmation, the `Imports.JobActive` delete refusal, 'Importing… {percent}%'). Job following via quiet refresh (0028 §10.5's rule: `job.changed`, 5 s/30 s polls, coalescing, terminal stop, polite status announcements). Empty-state copy.
- **Tests:** harness: quiet re-reads on job.changed, the 5 s poll for a non-requester and with the hub down, coalescing, the stop at a terminal status, the announcements; a job cancelled before its claim and one failed with Jobs.AttemptsExhausted, both with null ResultJson, showing their alerts; a pending confirmation's alert with sheet, row and 'and {n} more', Confirm sending the distinct keys for the initiator only, a stale list re-reading, Confirm disabled on ReadOnly, Stop confirming, the state alone for an administrator
- **Done when:** An import's page follows its job to completion in the harness with only quiet refreshes.

## Phase 9b Reports (Tier 3)

### PC-181 — Measures, partial columns and the C# measure evaluator

- **Analysis id:** `0010-f6` · **Spec:** 0010-F · **Layer:** backend · **Tier:** 3 · **Est. LOC:** 7,500
- **Sections:** §F.14, §F.15 (QX4010, QX4011, QX6001, QX6002)
- **Gated on (DEPS.md):** PC-101, PC-010, PC-033, PC-102
- **Analyst dependency notes:** 0010-f3
- **Deliverables:** QuerySpec.Measures and QueryDiscoverySpec.Measures (requires Aggregate). Binding at Measures[i], with QX4011 replacing QX4007 and QX4010 for constructs outside the subset. Partial decomposition (avg → sum and count; structural deduplication; reuse of a matching select item; appended columns), with CompiledQuery.Measures cached with the plan. The CompiledMeasures, CompiledMeasure, MeasureContext, QueryexNumericType, MeasureEvaluator, PartialTable, MeasureValue and QueryexEvaluationException types. Prepare() fixes SQL types per node by the types table: whole types typed from the plan, scale collapse at 38 digits, SqlDecimal rounding rules, the sign-defect workaround. Accumulation in a typed columnar store: Int128 sums at the partial's scale, long counts, min and max by type (text via InvariantCultureIgnoreCase with the ordinally smaller value kept on ties; hierarchy paths by integer levels). Lazy if and coalesce, short-circuit and/or, QX6001 and QX6002. The inspection tool shows partial columns.
- **Tests:** unit: the corpus (QX4011 vs QX4007, QX4010 per refused construct with span and construct, identity casts, discovery sites, the caller error); goldens (the avg split, deduplication, select-item reuse, conditional partials, folding inside a partial condition); evaluator vectors for every types-table row, the rounding rules, the sign defect and its controls, reader-shaped decimals, laziness, accumulation per kind and type, text ties, path order, the 38-digit boundary, shuffled-row determinism, two decimal types for one plan. integration: the differential on SQL Server over hand-written measures plus 500 generated ones (seeded, depth ≤4), holding the leaf, rollup and grand-total identities exactly in value and scale, with 8134 equivalent to QX6001.
- **Done when:** With Measures empty the engine emits today's bytes and CompiledQuery.Measures is null. All three identities hold for every generated measure. No data-library reference is added.
- **Planner note:** Also wire Measures into 0012 §11.3 Rows/QueryRowSet partial columns and into the 0029 §9 discover endpoint (steps 1-2), with the measure tests 0029-f deferred.

### PC-182 — Report stack and the generic sharing model

- **Analysis id:** `0033-a` · **Spec:** 0033 · **Layer:** backend · **Tier:** 3 · **Est. LOC:** 7,500
- **Sections:** §1, §2.1–§2.2 (CRUD/activate/share/publish/transfer routes), §5.1–§5.7 (except audience), §7, §13 reserved-code rule, §14 SharingOptions/ReportsOptions skeleton
- **Gated on (DEPS.md):** PC-003, PC-043, PC-093, PC-035
- **Analyst dependency notes:** 0010 Part F §F.7, §F.12, §F.18; 0012; 0014 §5; 0015; 0016; 0018 §7 (Acme migration project); 0021 §2.3–§2.4
- **Deliverables:** Tellma.Core.Abstractions.Reports: Report, ReportShare, ReportKind, ReportSeed record, IDashboardReadThrough. Tellma.Core.Abstractions.Sharing: IShareable, IShareRow, ShareAccess, ShareArguments/PublishArguments/UserGrant/RoleGrant/TransferArguments, IShareCommands<>, ShareableService<,>, Shareable<,>() extension + ShareableContributionItem, ShareList/ShareGrantView, SharingOptions. Tellma.Core/Sharing: ShareableAccessCriteria<,> (constant filter trees with Exists/Via/CurrentMembership, reason 'share'), ShareCommands<> (DELETE + INSERT/UPDATE over TVPs — see contradictions re MERGE), shares computed extra, shared-with-me named filter, RefuseOnRecord for standard rows, delete-shares persist participant, Sharing.* validation codes, core.report.shared raise/withdraw, startup checks. Tellma.Core/Reports: ReportService (Defaulted title derivation per slot incl. re-derive on Root/Kind change, QueryexVersion stamping hook, Report.CodeReserved), null DashboardReadThrough, Report.InUse pre-persist check; CoreFeature composition; Acme tenant migration (Reports, ReportShares with all indexes/checks/history). DefinitionJson parsed strictly into the records but full validation deferred to 0033-c.
- **Tests:** unit (criteria trees, default title vectors, standard-row refusals); integration(SQL): every criterion and reason kind, Edit saves/View doesn't, Everyone row uniqueness & View-only check, standard rows share but refuse save/transfer, share never stamps the row, delete removes shares, one SQL text for two users, expired/future membership, shared-with-me, permissions + shares extras, notification raise/withdraw dedup
- **Done when:** reports/query|get|save|delete|activate|share|publish|transfer work end-to-end on Acme's migrated DB; §15 'Sharing' bullets except audience/CannotRead green on LocalDB and Testcontainers; startup check refuses a malformed Shareable<,>() registration; no MERGE in any IDataBatch.Sql (TELLMA0003 analyzer clean)

### PC-183 — Audience resolver

- **Analysis id:** `0033-b` · **Spec:** 0033 · **Layer:** backend · **Tier:** 3 · **Est. LOC:** 3,500
- **Sections:** §6.1–§6.2, §2.2 audience route, §14 tellma.sharing.audience.members
- **Gated on (DEPS.md):** PC-182, PC-043
- **Analyst dependency notes:** 0033-a; 0014 §5.1–§5.5
- **Deliverables:** IAudienceResolver, AudienceQuery/ShareAudience/AudienceMember/AudienceReason/AudienceGrant/enums; AudienceResolver (one Read round trip for grants + permission holders + per-filter counts under each filter's QueryexVersion + data-reader marks incl. owner-chain and bespoke criteria; one more round trip for ThroughIds; merge; MaxAudienceMembers cap; Members null without core.Role×Read); ReportService.AudienceAsync (Long, Share securable) passing (Root, Read) and DashboardsUsing([id]) as Through
- **Tests:** integration(SQL): CannotRead per grant and per member, bespoke Read criterion counting for all, service account via shared role/public role/permission role, deactivated grantee Members:0, hidden Members, cap with exact Total, a role permission with >500 holders counted exactly, filtered permission counted only when row satisfies it
- **Done when:** reports/audience answers §6.2 example shape; all audience bullets of §15 'Sharing and audience' green; metric emitted

### PC-184 — Definition document, structural validator, default display and parameters vocabulary

- **Analysis id:** `0033-c` · **Spec:** 0033 · **Layer:** backend · **Tier:** 3 · **Est. LOC:** 6,000
- **Sections:** §3.1–§3.4 (all rules except 5 and the QX parts of 6/10), §4.1, §4.5–§4.6, §3.2 default display/decimals, §3.3
- **Gated on (DEPS.md):** PC-183, PC-182, PC-019, PC-064, PC-012
- **Analyst dependency notes:** 0033-a; 0013 §7.7; 0016 §3.1; 0010 §F.17, §F.19
- **Deliverables:** All document records/enums with JsonUnmappedMemberHandling.Disallow and JsonStringEnumMemberName spellings; binding-failure mapping to Report.UnknownMember/ChartTypeUnsupported/DisplayIncompatible with RFC 6901 pointers; ReportValidator rules 1–4, 7–9, 11–12 (counts, kind block, path resolution, blob-only-in-list, tree, having/top ranges, chart dimensions, display/format/enum/choices, drillColumns, size, formatVersion), parameter key/count/display rules (§4.5, §4.6), expr→path rewrite, default display resolver (path/outermost-call table, 'gc' inference, decimals from Scale), JsonSchemaExporter output checked in for client conformance
- **Tests:** unit: every §3.4/§4.5/§4.6 rule incl. EnumSourceInvalid, ChoiceInvalid (index/pointer), running refusal; every enum value round-trip under TellmaJson.Options; unknown member refused; default-display vectors per §3.2 row; corpus of checked-in documents with expected (pointer, code)
- **Done when:** reports/save refuses each structural violation with the right code and pointer; document JSON Schema emitted to a checked-in file; §15 'Document, validator, corpus' and 'Default display' bullets (non-compile parts) green

### PC-185 — Report compiler: planning for list, summary and chart; compile check and parameter inference

- **Analysis id:** `0033-d` · **Spec:** 0033 · **Layer:** backend · **Tier:** 3 · **Est. LOC:** 6,500
- **Sections:** §3.4 rules 5, 6, 10 (QX parts), §4.2, §4.3 grammar check, §8.3 step 4–5, §8.4 (select items, identity columns, ordering, top/paging, totals partials and take+1 probe, chart), §8.7, §8.10 plan cache
- **Gated on (DEPS.md):** PC-184, PC-010, PC-021, PC-062
- **Analyst dependency notes:** 0033-c; 0010 §F.8, §F.10, §F.13, §F.14, §F.18, §F.20; 0012 §3.1, §11.1; 0015 §5.2
- **Deliverables:** ReportCompiler producing QuerySpec per kind (navigation expansion via IDisplayNames.Columns with max() labels, text slots, identity columns for child/grandchild roots, orderBy per active slot, Measures for totals), discovery-based normalization (add/remove parameters, type & entity inference via ComparedWith, ParameterTypeRequired/Incompatible/Entity*), default-expression compile as constant items, QX diagnostics → Report.InvalidExpression with pointer remap, QueryexVersion selection, ReportLimits profile, LRU plan cache keyed (tenant, Id+ModifiedAt | Root+Kind+SHA-256 canonical hash, MultilingualShape, ContentLanguageIndex)
- **Tests:** unit + SQL goldens: §8.4 per term kind and shape, summary partial columns and take+1 probe, identity columns, ordering per slot, one golden per kind and per Part F item used; QX4002/4005/4007/4010/4011 vectors incl. totals on/off; normalization and stated-vs-inferred entity; plan-cache key unit tests
- **Done when:** save runs the full validator (rule 5 compile check with every parameter declared) and stores normalized documents with inferred types; goldens checked in; compile path never touches the DB

### PC-186 — Run engine for list, summary and chart; results, defaults, drill and measure errors

- **Analysis id:** `0033-e` · **Spec:** 0033 · **Layer:** backend · **Tier:** 3 · **Est. LOC:** 7,000
- **Sections:** §2.2 run route, §4.3–§4.4, §8.1–§8.3, §8.4 totals evaluation, §8.8–§8.11, §9.1, §9.2, §9.4, §14 run telemetry/logs
- **Gated on (DEPS.md):** PC-185, PC-010, PC-033, PC-050, PC-064
- **Analyst dependency notes:** 0033-d; 0010 §F.9, §F.15; 0012 §5.2, §6.4, §11.3; 0014 §7.1; 0016 §3.2, §8.2
- **Deliverables:** IReportEngine.RunAsync, ReportSource, ReportRunRequest/ReportInline, ReportService.RunAsync; argument sorting incl. URL-text conversion and 422s; EvaluateConstant defaults round trip; authorization (Id, Id+DashboardId via IDashboardReadThrough Or-filter, Inline) + root RequireAsync + traversal check per run; execution through IGuardedBatchRunner with Limits/CountCap; ReportResult/ReportColumn/ReportTable/ReportRowIdentity shaping, partial columns dropped, C# summary totals via MeasureEvaluator, chart MaxChartPoints probe, LimitExceededException mapping, ArithmeticFailureException + QueryexEvaluationException → 422/500 by document owner, ReportDrill with BindParameters + MeasureCondition + LeavesOnly, cancellation counter, ReportEvents logs, activity tags
- **Tests:** integration(SQL): §4.4 table incl. URL text per type; defaults share the query's today(); 'uq'/'et' grouping; omitted @From returns all rows; totals rule at take/take+1/top boundaries and Skip>0; summary total equals rows via ColumnOrdinals; 422 MeasureFailed from SQL 8134; 500 for standard/named-inline/Code; traversal 403 restricted-path; round-trip counts; abort reaches SQL; plan cache isolation across tenants/viewers and hits on whitespace-reordered inline; read-through load vectors (with null implementation + fake); drill payload vectors (shared drill-golden.json)
- **Done when:** reports/run serves list/summary/chart for stored and inline sources with §9 envelopes byte-stable; §15 'Runs' bullets except pivot ones green

### PC-187 — Pivot: leaf statement, member statements and the cube builder

- **Analysis id:** `0033-f` · **Spec:** 0033 · **Layer:** backend · **Tier:** 3 · **Est. LOC:** 7,000
- **Sections:** §8.5, §8.6, §8.8 pivot caps, §8.11 cell captions, §9.3, §9.4 pivot leaf rules
- **Gated on (DEPS.md):** PC-186, PC-181
- **Analyst dependency notes:** 0033-e; 0010 §F.14, §F.15; 0012 tree convention (hierarchyid)
- **Deliverables:** Pivot planning (one key per dimension, tree keys at node grain, Measures, HAVING, MaxPivotRows+1 probe), member statements over tree entities (structural Id/ParentId + related projection, ancestor-only pruning), CubeBuilder (chains, phantom children, (None), PartialTable folds rows then columns, evaluate every cell, visual totals over having survivors, caps checked incrementally, cancellation every 4,096, deterministic member ordering/numbering and byte-identical emission), ReportCube/CubeLevel/CubeMember/CubeCell (compact wire [row, column, v…]), first-failing-cell captions for 422/500
- **Tests:** unit: chains, phantom on/off, (None), two trees on one axis, each combine rule, §8.6 composite measures and having example, text ties, null partials, every cap incl. MaxPivotCellPartials (with lowered options), member order, byte-identical output for shuffled rows; integration(SQL): every cell of a two-tree pivot equals a brute-force descendantOf query per measure; QX6001 pivot 422 with pointer/row/column incl. outer subtotal and value-member caption; tree pivot under filtered tree Read
- **Done when:** §3.5 pivot example runs on Acme data with §8.6's numbers; §16 'Numbers' rule holds for pivots; all cube unit vectors green

### PC-188 — Coded reports: declaration, registry, interceptor, IReportResults, metadata, Acme credit exposure

- **Analysis id:** `0033-g` · **Spec:** 0033 · **Layer:** backend · **Tier:** 3 · **Est. LOC:** 5,000
- **Sections:** §11.1–§11.4, §14 coded telemetry
- **Gated on (DEPS.md):** PC-187, PC-186, PC-062, PC-067, PC-092
- **Analyst dependency notes:** 0033-e; 0015 §11.3; 0014 §4.2; 0024 §3.1–§3.2 (server metadata contributor seam); 0018 §1.4
- **Deliverables:** CodedReportAttribute, ReportParameterAttribute, ReportGrouping, CodedReport<TArguments>() extension, ICodedReportRegistry/CodedReportDescriptor/CodedReportParameter/CodedReportExport, core.coded-reports startup check (every §11.2 refusal), CodedReportInterceptor (422 ArgumentRequired, DefaultUndeclared warning, title fill, Kind mismatch 500), IReportResults.Table/RunAsync (ReportSource.Code, MaxTake probe), CodedReportClientAssetContributor (codedReports section), Acme AcmeReports.credit-exposure action + acme.CreditExposureReport securable
- **Tests:** integration: every core.coded-reports refusal; both declaration forms in registry and codedReports emit; interceptor 422 and warning; Kind-mismatch 500; Table refusing Pivot/Chart (500) and MaxTake+1 (413); code document MaxTake+1 413 and §3.4 error 500; Acme credit exposure runs under its securable
- **Done when:** acme-reports/credit-exposure runs with defaults echoed; metadata chunk carries codedReports entries within budget; startup fails fast on each malformed declaration

### PC-189 — MCP server: host, authentication, request filter, read tools

- **Analysis id:** `0016-g` · **Spec:** 0016 · **Layer:** backend · **Tier:** 3 · **Est. LOC:** 5,000
- **Sections:** §1.3 McpOptions, §11.1-§11.5 (whoami, describe, query, aggregate, get), §11.7, §11.8, §11.9 (distribution tools), §10.1 MCP meter
- **Gated on (DEPS.md):** PC-095, PC-081, PC-082, PC-032
- **Analyst dependency notes:** 0016-d; 0016-e; 0011 §5.5-§5.6
- **Deliverables:** src/core/Tellma.Core.Mcp: McpFeature (AddMcp), ModelContextProtocol.AspNetCore stateless registration, MapMcp on the tenant Mcp group (405 on GET/DELETE), Tellma.Bearer audience validator for {PublicOrigin}/{tenantId}/mcp, SDK challenge with resource_metadata, insufficient_scope 403, Tellma.Mcp policy, PRM document per tenant from PublicOrigin and tenant registry (never Host), Origin allow-list, request filter (tenant facts, EnsureAccessAsync IsMutation=false, RequestContextInputs with MessageLanguage/Calendar), static tool list (ttlMs, cacheScope public, additionalProperties false, openWorldHint false), tellma_whoami (structuredContent + outputSchema), tellma_describe (catalogue + search, entity description gated by tenant shape, cached per (TenantId, Entity) under settings tag), tellma_query/tellma_aggregate (table/objects rendering, row-by-row truncation, includeInactive, child entities via IChildQueryService), tellma_get (header first, child windows, collection paging), tellma://queryex/syntax resource, McpToolNames incl. reserved names and tellma_ prefix rule for distribution ToolTypes, problem mapping on isError results
- **Tests:** unit (Tellma.Core.Mcp.Tests: argument validation and entity matching, describe catalogue/search, renderings and truncation, child windows, tool-list determinism, prefix rule, Hidden gating)
- **Done when:** An SDK client with a test-issuer token lists 8 tools and runs the read tools against a tenant; a wrong-audience or cross-tenant token is refused

### PC-190 — MCP write tools, write safety and MCP integration suite

- **Analysis id:** `0016-h` · **Spec:** 0016 · **Layer:** backend · **Tier:** 3 · **Est. LOC:** 5,000
- **Sections:** §11.5 (save, delete, action), §11.6, §11.7 isError mapping, §13.1-§13.4 (Mcp.IntegrationTests, Live nightly, result.chars recording)
- **Gated on (DEPS.md):** PC-189, PC-095
- **Analyst dependency notes:** 0016-g; 0016-f
- **Deliverables:** tellma_save (Concurrency=Check, Source=Agent, no override, conflict/warning isError guidance), tellma_delete (preview + IDataProtector token binding sub/tenant/entity/sha256(ids)/withDescendants/action/expiry, single-use per process, form-mode elicitation path), tellma_action (activate/deactivate/EntityAction overloads by ResultType, SingleTarget id vs ids, ApiActionDescriptor via invoker, Destructive confirmation), confirmed-warning tokens bound to canonical input and keys, ReadOnly-stack and Hidden refusals, sensitive-operation refusal before any service call, test/core/Tellma.Core.Mcp.IntegrationTests (in-process JWKS issuer, FakeTimeProvider expiry, ephemeral and shared data-protection rings for cross-instance replay, Live=true JWKS/issuer cases skipped when unconfigured), tellma.mcp.result.chars recorded on PR
- **Tests:** unit (token binding/expiry/single use, sensitive refusal, ReadOnly gating); integration (eight tools end to end, delete preview/foreign token/right token/replay, warning token flow, deactivated user denied with valid token, ReadOnly tenant save isError, describe omits sensitive ops); Live=true nightly
- **Done when:** MCP suite green on PR with Live excluded; nightly Live job defined; write safety pins all pass

### PC-191 — Export: layout, Excel and Markdown writers, sync and background routes

- **Analysis id:** `0033-h` · **Spec:** 0033 · **Layer:** backend · **Tier:** 3 · **Est. LOC:** 6,500
- **Sections:** §12.1–§12.4, §8.1 PagesAsync, §11.3 ExportAsync/custom exports, §2.2 export routes, §14 export telemetry
- **Gated on (DEPS.md):** PC-188, PC-187, PC-173, PC-189
- **Analyst dependency notes:** 0033-f; 0033-g; 0019 §4.4, §12.2–§12.3; 0020; 0016 §11.5 (Tellma.Core.Mcp exists); 0013 §8.1
- **Deliverables:** IReportEngine.PagesAsync (MaxTake pages, count cap, last-page totals), ReportExportRequest, export/export-start actions for Id/Inline/Key (Platform/Custom/None), IReportLayout (Compact & Flat incl. pivot level columns and '(Total)' rows, 16,384/1,048,576 ExportTooWide), ReportExcelWriter (number formats, [$-CC] month serials, quarter/year/monthName/enum as text, row styles + indent, frozen panes, RTL sheet view), ReportMarkdownWriter in Tellma.Core.Mcp, 0019 core.export job report case (ReportExportJobArguments, Core_Job_ExportReport label), IReportResults.ExportAsync
- **Tests:** integration: layout sheets per kind and shape (image columns left out, file names, Flat pivot levels and (Total)); workbook headers/styles/indent/panes/RTL/formats incl. uq month under Gregorian request, et month as text; Markdown encoding per display; 413 RowLimitExceeded and ExportTooWide; background job + core.export.ready; export Total equals screen total; multi-page totals (with lowered MaxTake); Platform coded export invokes action once; Custom export title from TitleKey; refused export/start for Custom; None key 400
- **Done when:** every kind exports synchronously and in background on Acme; §15 'Export' bullets green

### PC-192 — Seeded standard reports, child-root coverage, Acme wiring and nightly performance suite

- **Analysis id:** `0033-i` · **Spec:** 0033 · **Layer:** backend · **Tier:** 3 · **Est. LOC:** 3,500
- **Sections:** §13, §10 (tests), §16 Acme server parts, §15 Performance
- **Gated on (DEPS.md):** PC-191, PC-187, PC-182, PC-092
- **Analyst dependency notes:** 0033-f; 0033-a; 0011 §6.2; 0018 §1.4, §6
- **Deliverables:** ReportSeedProvisioningStep per seed (core.report-seeds/<Code>, Order 900, Version re-run, upsert by Code as enlisted save with OwnerId null, titles per tenant language, grants on insert only via declared-write batch, SeedRoleMissing/SeedCodeTaken), composition refusal of non-prefixed seed codes, Acme ReportSeed(acme.sales-by-center) + embedded JSON + Sales grant; child-root report integration tests (Via rebasing, owner path, grandchildren, child export); [Trait] nightly perf tests (1M-line pivot, heap at caps) with data generator
- **Tests:** integration: seed insert, rewrite, grants once, missing role, removed seed keeps row, raised Version re-runs only its step, CodeReserved, owned row keeping a prefixed code still saves, SeedCodeTaken; child routes; perf (nightly trait)
- **Done when:** Acme provisioning creates acme.sales-by-center published to Sales and it runs as §3.5's pivot; perf suite runnable via filter and excluded from PR runs

### PC-193 — tm-pivot core-ui entry point

- **Analysis id:** `0034-a` · **Spec:** 0034 · **Layer:** client · **Tier:** 3 · **Est. LOC:** 7,500
- **Sections:** §1 (pivot row), §6.1–§6.4, §7.7 pivot token group, §14 tm-pivot unit + visual
- **Gated on (DEPS.md):** PC-041, PC-004
- **Analyst dependency notes:** 0010 Part C; 0023 §3.1–§3.2, §9
- **Deliverables:** client/projects/core/tellma-core-ui/pivot: TmPivot (tm-pivot) with TmPivotModel/Member/Cell/Widths, compact layout (row subtotal on parent row, column subtotal after children, Total last), lazy text(), widest()-based default widths via hidden probe, ResizeObserver, pointer resizing with per-measure application and double-click fit, sticky prefix rule (40% of W, <300 px nothing), row/column windowing (tmComputeAxisWindow; columns past 40), five styles + intersections, treegrid ARIA with roving tabindex, §6.4 keymap via kbd, Mod+C copy, activatable refusal tooltip/announcement, toggle announcements; pivot tokens; TmPivotHarness; showcase page; API golden
- **Tests:** unit (vitest browser mode): layout, spans, subtotal/total columns, styles & intersections, flattening, both windows, keys, ARIA, phantom suffix, text only for rendered cells, widths from widest/widen-only/never over user width, resize per measure, double-click fit, ellipsis full text in tooltip/accessible name/clipboard, activatable refusal; visual (light/dark/RTL)
- **Done when:** tm-pivot renders a 500,000-cell model with only visible text() calls; harness published; budget ≤ 15 KB gz for the entry point; showcase demo

### PC-194 — tm-chart core-ui entry point (ECharts facade)

- **Analysis id:** `0034-b` · **Spec:** 0034 · **Layer:** client · **Tier:** 3 · **Est. LOC:** 7,000
- **Sections:** §1 (chart row), §7.1–§7.8, §14 tm-chart unit, tokens, chart-bidi
- **Gated on (DEPS.md):** PC-193, PC-008, PC-004
- **Later seams (not gates):** PC-195 (0034-c). Ship the seam; the later checkpoint fills it.
- **Analyst dependency notes:** 0010 §B.8, §C.1, §D.14; 0023 §9; 0024 §12 (on-demand group/budget wiring can follow in 0034-c if 0024 tooling lands later)
- **Deliverables:** client/projects/core/tellma-core-ui/chart: TmChart (tm-chart), TmChartSpec types, TM_CHART_SPEC_SCHEMA, tmValidateChartSpec (schema, counts, bidi-control), one internal adapter that alone imports echarts/core (Bar/Line/Pie, Grid/Tooltip/Legend/MarkLine/Aria, CanvasRenderer) behind one dynamic import, canvasText(runs, base) LRM/RLM wrapping, rich-text tooltip, legend sides, decals, palettes, normalized stacks, thresholds, compact Arabic-Indic ticks, card type without engine, theme bridge via getComputedStyle, figure/role=img naming, asTable table view on tm-grid with row drill, pointActivate by dataIndex; chart token group; TmChartHarness; echarts npm dependency
- **Tests:** unit: spec validation incl. bidi-control; adapter option per type/option engine-free; palette slots, decals, normalized shares; canvasText marks (LRM/RLM only) per run and parts; the four guards; Arabic-Indic ticks; table view gridId and row activation; no HTML tooltip/legend; token checks (chroma/ΔE/contrast) both schemes; e2e chart-bidi.spec on Chromium/Firefox/WebKit (pixel-equal runs); visual per type
- **Done when:** tm-chart entry ≤ 9 KB gz, engine chunk within 0024's 170 KB line and loaded only on first non-card spec; production CSP holds without Trusted Types policy; harness published

### PC-195 — @tellma/core reports: declarations, wire mirrors, host core, list and summary renderers

- **Analysis id:** `0034-c` · **Spec:** 0034 · **Layer:** client · **Tier:** 3 · **Est. LOC:** 8,000
- **Sections:** §1 (primary and /reports rows), §2.1–§2.3 declarations (tmInlineReport, tmCodedReport + build codes, tmReports/tmReportStudioPage), §3.1, §3.3–§3.5, §5.1–§5.4
- **Gated on (DEPS.md):** PC-194, PC-184
- **Analyst dependency notes:** 0024; 0026; 0029; 0023; 0033-c (JSON Schema artifact for conformance)
- **Deliverables:** Primary: TmReport* wire mirrors (types only), TmReportRef/TmInlineReport/tmInlineReport, tmCodedReport + patch merge into TM_APP_DECLARATIONS.reports with coded-report-not-registered/redeclared/source checks, tmReports entity declaration and tmReportStudioPage, tmShareDialog/tmTransferDialog lazy stubs. /reports: TmReportHost (one resource() keyed by report/bound/args/generation, transport for id/inline/key, paging members, priority low in widget mode, abort via DestroyRef, get-once rule), output states table incl. calculation-error text and notice strip, refresh modes spinner/silent/clear/stale, tmReportFormat, tm-report-cell, List and Summary renderers (gridId rules, widths, image/file cells, instant headers, pager/footer, totals row, totalsOmitted footer), injectTmReportHost, TmReportHostHarness, tmReportResultFixture
- **Tests:** host harness (fake TmApi, fake clock): runs per commit with superseded aborts, take 200/1,000, no long option, priority low in widget mode and not on export, background flag, reloading bar timing (≥250 ms, held across supersede), §3.4 table, held/stale/forbidden clear, calculation error variants (pivot cell, outer subtotal, summary totals, SQL row), named-inline 422, create-mode 'Available after saving', card/widget write no URL, two hosts with stateScope; renderers: coded list without paging, summary totals/totalsOmitted, instant suffix, expression header, exact decimals >15 digits, grouping:false, choices per slot, month/weekday names per calendar, image cells; conformance: wire mirrors vs 0033 JSON Schema; build-code tests
- **Done when:** tm-report-host renders list and summary results from fixtures in every mode; build codes fire; /reports ≤ 22 KB gz budget tracked

### PC-196 — Parameter field, bar, URL codec, precedence and page state

- **Analysis id:** `0034-d` · **Spec:** 0034 · **Layer:** client · **Tier:** 3 · **Est. LOC:** 5,500
- **Sections:** §3.2, §3.6, §3.7, §4.1–§4.5
- **Gated on (DEPS.md):** PC-195, PC-107, PC-119
- **Analyst dependency notes:** 0034-c; 0025 §10, Appendix A; 0029 §14
- **Deliverables:** TmReportParameterField (controls per type/display: entity field, tmNumber, tm-select choices/enum/months/weekdays, text, bool, date pickers for Date/DateTime/DateTimeOffset, Guid), bar layout with Reset and the below-sm drawer summary, commit-and-run rules incl. waiting state, tmReportArgsFromUrl/tmReportArgsToUrl (raw-text mode for stored reports, typed codec with TmDecimal past 15 digits), URL rewrite from echo with replaceUrl, arg-dropped telemetry, precedence URL → page state → report.<id|key>.params → defaults, remembered-values filter, page-state save/restore {args,result,scroll,expansion,widths,asTable,modifiedAt} with 5-min/isStale re-run, mode table (footer text, 'Updated {time}' rule, block sizes)
- **Tests:** codec vectors per type (typed and raw-text), precedence and remembered-value filter incl. coded reports, get+run together with/without p.*, 422 ArgumentInvalid marks the field, URL rewritten from echo, coded/inline waiting with no request, page-state restore incl. widths, modifiedAt discard, run after 5 minutes or isStale
- **Done when:** a page-mode host round-trips parameters through URL, page state and preferences per §4.5; harness suites green

### PC-197 — Pivot and Chart renderers, drill-through and Excel export

- **Analysis id:** `0034-e` · **Spec:** 0034 · **Layer:** client · **Tier:** 3 · **Est. LOC:** 7,000
- **Sections:** §6.5–§6.6, §7.9, §8.1–§8.4, §9
- **Gated on (DEPS.md):** PC-196, PC-193, PC-194, PC-160, PC-109, PC-101
- **Analyst dependency notes:** 0034-a; 0034-b; 0034-d; 0030 §17; 0028 §8.1; 0010 §F.21 (discover conjuncts)
- **Deliverables:** Pivot renderer (cube → TmPivotModel, Map-keyed cells, widest scan on exact strings, leaf-only activatable, initial expansion from Expand, re-sort on language switch keeping ids), Chart renderer (spec from result, TmDecimals.toNumber, table gridId, card drill, unknown type as table), drill builder (coordinate → tmQx conditions incl. measure conditions, leavesOnly, maxFilterLength refusal, base64url #d= payload via TmUrls.list), TmReportDrillEvent with preventDefault and tmDrill navigation state (from/fromUrl/fromTitle/chips), drill-list additions in @tellma/core/search (crumb from history state, chips from state or from queryex/discover conjuncts with bidi strip/80-char cut, 'Open in {plural}', child-root select/opening), export flow (sync download with progress toast and Cancel, capped count → export/start, 413 fallback, Custom/None handling, error presenter), drill-golden.json
- **Tests:** drill filters vs drill-golden.json (one vector per row of 0033 §9.4 incl. measure conditions and leaf cells, shared with C#); #d= surviving navigateByUrl; maxFilterLength refusal; chips against fake discover (FK display name and #id, descendantOf, date, null, operator, bidi literal, >80 chars, leftovers); export: capped count straight to export/start, known count and pivot synchronous, 413 fallback, no Export in too-much-data state, Custom 413 presented, None hides Export
- **Done when:** all four kinds render, drill and export from fixtures; drill list shows crumb and chips from both sources

### PC-198 — Report page, coded report page, reports page and sharing dialogs

- **Analysis id:** `0034-f` · **Spec:** 0034 · **Layer:** client · **Tier:** 3 · **Est. LOC:** 7,500
- **Sections:** §2.2 (coded route/launcher derivation), §10.1–§10.4, §11, §13 report/search contexts
- **Gated on (DEPS.md):** PC-197, PC-124, PC-182
- **Analyst dependency notes:** 0034-e; 0031; 0030 §2.4–§2.5; 0027 §3.2, §9; 0033-a/b (wire shapes of shares extra and audience; fakes in harness)
- **Deliverables:** TmReportPage (details page hatch: compact header row 1, 'About this report' popover incl. Built-in/Standard and 'Who has this' link, band with the bar, action bar report.share/refresh/export/toggle-table/edit/expand-all/collapse-all/copy-link replacing details.refresh), coded report page under /{t}/reports/{key} with its own header, TmReportsPage results region (tabs all/mine/shared, fixed columns with kind icons and Standard badge, launcher line, empty states, bulk actions), @tellma/core/sharing: TmShareDialog (shares extra + audience in parallel, People/Roles/Everyone switch, cannotRead warnings with Show who, 'Who can see this' tab with reasons, capped list, staged save as ≤1 share + ≤1 publish, discard prompt) and TmTransferDialog; Report.InUse 400 dialog; page contexts report and drill-list search
- **Tests:** share harness (shares with permissions, staged ≤1 share/≤1 publish, Publish-only caller, active individuals only, Everyone switch, failing second call, cannotRead warnings/Show who/badge/chip, Inactive and Service account, capped audience, hidden members); reports page (tabs' filters, no coded reports, no Share/Publish in bulk, launcher line); report page Refresh (report.refresh registered, details.refresh not; re-read + new generation); delete/save/share announced not toasted
- **Done when:** /{t}/reports, /{t}/reports/{id} and /{t}/reports/{key} work against fakes with the action bar and dialogs; /sharing ≤ 5 KB gz and loadable without /reports

### PC-199 — Report studio: hosting, layout, sections, drops and kind switching

- **Analysis id:** `0034-g` · **Spec:** 0034 · **Layer:** client · **Tier:** 3 · **Est. LOC:** 8,000
- **Sections:** §12.1–§12.3, §12.5–§12.6, §12.9 (Save/Cancel/Share wiring)
- **Gated on (DEPS.md):** PC-198, PC-162, PC-121, PC-026
- **Analyst dependency notes:** 0034-f; 0031 §3.7, §8, §11, §13; 0029 §10–§11; 0023 §7.3, §8.4
- **Deliverables:** @tellma/core/report-studio: TmReportStudio as create hatch and TmReportStudioPage (edit route with studio draft key, auto edit() entry, createdUrl landing + TmReportStudioHandoff with held preview), dark panel scope with tm-splitter and collapsible rails, Root select (stacks + child surfaces), tm-path-picker fields with date parts, per-kind sections, term lists on tm-reorder-list, item editor (expression, labels per slot, display/format with placeholders from preview column, Labels table with 'Fill from preview' ≤100), drop targets and 'Add to…' menu, kind switching keeping per-kind terms, read-only notice for standard reports, Save/Cancel through details host, Share on edit page
- **Tests:** studio harness: §12.5 targets and Add to… incl. date parts; default displays and default title as placeholders; disabled totals switch; Fill from preview cap; kept blocks on kind switch; create and edit through the host; create landing on edit studio with no get and no run, splitter/rails/terms kept; edit mode re-entered after Save and Cancel; restore under the studio draft key
- **Done when:** an author can build, preview (manual Refresh) and save every kind by pointer and by keyboard against fakes; /report-studio in on-demand group ≤ 28 KB gz

### PC-200 — Report studio: live parameter inference, validation feedback and overlay preview

- **Analysis id:** `0034-h` · **Spec:** 0034 · **Layer:** client · **Tier:** 3 · **Est. LOC:** 6,000
- **Sections:** §12.4, §12.7, §12.8, §12.9 error mapping, §13 studio context
- **Gated on (DEPS.md):** PC-199, PC-101
- **Analyst dependency notes:** 0034-g; 0029 §8.1, §10.1–§10.5; 0010 §F.21
- **Deliverables:** Parameter cards from tmQxParameters per keystroke with types/conflicts/compared entity merged from the draft check, pre-filled displays, confirmed types into drafts; structural rules mirrored client-side; shared TmQueryexDraft (aggregate, measures clause for pivot/totalled summary) with span → term mapping; 600 ms auto-preview of unnamed inline runs with abort per edit; 422 pointer mapping (term, span, parameter card, General); problems badge/list and F8; TmReportOverlay presentation edits re-rendering held results with no request; 'Preview is out of date' strip; registerField + DefinitionJson 422 mapping on save; studio page context and studio.preview/studio.share actions
- **Tests:** studio harness: §12.7 span and pointer mapping with pivot measures and totalled summary measures as measures, QX4010 mapped to its term; parameter displays pre-filled; overlay edits send nothing; save 422 mapped by pointer; studio-overlay.spec (no request for label/option edits)
- **Done when:** preview runs within 600 ms after a valid edit and maps every server diagnostic to its source control

### PC-201 — Acme end-to-end, geometry, keyboard, visual, axe and budgets

- **Analysis id:** `0034-i` · **Spec:** 0034 · **Layer:** client · **Tier:** 3 · **Est. LOC:** 4,500
- **Sections:** §14 e2e/visual/axe/budgets, §15 UI rules table and Acme journeys, telemetry assertions
- **Gated on (DEPS.md):** PC-200, PC-192, PC-191
- **Analyst dependency notes:** 0034-h; 0033-i; 0033-h; 0018 (Acme client app); 0021 (bell for share journey)
- **Deliverables:** Acme client: creditExposureReport launcher patch and Customer details card over inline customerSales; Playwright specs: Acme journeys (launcher open, parameters, pivot leaf drill sums, build/save summary over lines and open line via ?child=, export each kind, share with a user, build pivot by drag and keyboard), warm-launch on /17/reports/42, /17/reports/acme-credit-exposure, /17/reports, report-feedback, run-supersede, report-geometry, pivot-geometry, pivot-sticky (5 widths × 0/1/2 attributes × 2 directions), pivot-rtl, chart-rtl, report-keyboard, studio-keyboard, CSP violation listener; visual baselines; axe battery; budget checks
- **Tests:** e2e(Playwright) Chromium on PR, Firefox/WebKit @cross-engine; visual; axe; budgets
- **Done when:** §15 UI-rules table pinned by the named specs; Acme journeys green against the real server

## Phase 9c Dashboards (Tier 3)

### PC-202 — Dashboard server: tables, service, validator, view extra, read-through and audience

- **Analysis id:** `0035-b` · **Spec:** 0035 · **Layer:** both · **Tier:** 3 · **Est. LOC:** 7,500
- **Sections:** §3.1–§3.3, §4.1 settings validation + core.report Subject, §4.2 ReportWidgetSettings, §5.1–§5.3, §6.1–§6.3, §7.1–§7.3, Appendix A
- **Gated on (DEPS.md):** PC-167, PC-182, PC-183, PC-184, PC-186, PC-188
- **Analyst dependency notes:** 0033-a; 0033-b; 0033-c; 0033-e; 0033-g; 0035-a
- **Deliverables:** Dashboard/DashboardWidget/DashboardShare entities with all constraints/indexes/sequences/history, Acme migration update (FK_DashboardWidgets_ReportId NO ACTION), DashboardService : ShareableService with share/publish/transfer/audience actions, DashboardOptions (MinRefreshMinutes), default title, DashboardValidator (every Appendix A code incl. overlap, kept-reference rule, bindings table, coded keys via ICodedReportRegistry, strict settings deserialization + data annotations), core.report widget type with ReportWidgetSettings/WidgetBinding (JSON-level 'exactly one member' check), DashboardViewContributor (eager view extra, two statements, MissingAccess incl. child roots, Readable), DashboardReadThrough (ReadThroughAsync memoized, DashboardsUsing ≤20 rows/report) replacing 0033's null implementation, per-tile audience data securables, core.dashboard.shared
- **Tests:** unit: validator — every Appendix A code at its path, §5.3 table row by row for stored and coded (hasDefault) reports, bounds and overlap, strict settings, kept-reference rule, display vectors per rule; integration(SQL): CRUD with widget sync, shares not part of save and never stamping, default title per slot, Report.InUse naming a readable dashboard and counting one not readable, ReadThroughAsync filter vectors + differential vs two-step reference, DashboardsUsing, view extra (summaries without share, Readable, MissingAccess for denied root and child root, no extra round trip), sharing suite parameterized for core.Dashboard, audience per-tile CannotRead for stored and coded tiles; web: routes/retry classes, dashboards/audience in Contracts long+idempotent lists, reports/run with DashboardId end to end, Tellma-Priority low on batch lane, background run stamps no activity, notification raised/withdrawn
- **Done when:** dashboards/* routes live on Acme; a viewer of a shared dashboard can run its stored-report tiles without a report share; deleting a report shown on a dashboard returns 422 Report.InUse

### PC-203 — Dashboard page (view mode), report widget, parameters, scheduler and present mode

- **Analysis id:** `0035-c` · **Spec:** 0035 · **Layer:** both · **Tier:** 3 · **Est. LOC:** 8,000
- **Sections:** §1.1 page and report-widget chunks, §6.2 server-driven denied tiles, §8.4 menu (view), §9.1, §10.1–§10.3, §11.1–§11.4, §12.1–§12.4, §14, §16 dashboard context + tm.dashboard.* telemetry
- **Gated on (DEPS.md):** PC-202, PC-167, PC-195, PC-196, PC-197, PC-198
- **Analyst dependency notes:** 0035-a; 0035-b; 0034-c; 0034-d; 0034-e; 0034-f; 0031; 0027 §4.3, §5.1
- **Deliverables:** tmDashboards entity declaration (tabs, clone, share/publish/transfer actions via /sharing), tmDashboardPage() + TmDashboardPage hatch under compact header, report widget chunk (tm-report-host mode=widget, refresh=silent, bound args, declarations from view or codedReports, stateScope, drillable off when presenting, Open report with p.*, Export), tmBindWidgetArguments, tmDashboardModel, dashboard parameter bar in band with unset/Default display, URL/page-state/preference precedence, scheduler (completion-based due times, visibility/online/edit gating, catch-up in reading order, one timer on TM_NOW), forced refresh and tile refresh with announcements, page-state return {args, held, scroll} with expansion/asTable, isStale behaviour, present mode (?present, setImmersive, requestFullscreen + wake lock, pointer hide, Escape/Back/fullscreenchange exit, foreground runs), dashboards search page, dashboard page context, TmDashboardPageHarness
- **Tests:** unit(vitest): tmBindWidgetArguments over §10.2's table (fixed null, unbound beside same-key, waiting); scheduler with fake clock (completion-based, tile overrides, conditions and catch-up, denied never run, forced/tile refresh through dashboard.refresh with no details.refresh, stale return clears nothing, foreground ticks in present mode, page-state return with expansion/table view, isStale runs every held tile, lists untouched); harness: page states §12.3, status line incl. omitted-totals note and empty line after failed first run, present mode, two tiles of one summary report without grid-state collision
- **Done when:** a stored dashboard with parameters renders, refreshes silently at its intervals, presents full screen and returns from a drill with held tiles; page chunk ≤ 15 KB gz, report widget chunk ≤ 2 KB gz

### PC-204 — Dashboard editor: canvas, move/resize, gallery, dialogs and save

- **Analysis id:** `0035-d` · **Spec:** 0035 · **Layer:** both · **Tier:** 3 · **Est. LOC:** 7,500
- **Sections:** §13.1–§13.6, §8.4 edit-mode menu, §8.6 invalid state
- **Gated on (DEPS.md):** PC-203, PC-031, PC-196
- **Analyst dependency notes:** 0035-c; 0023 §8.4; 0034-d (parameter field for fixed values)
- **Deliverables:** Editor chunk: TmDashboardEditor on the details host's working copy (n{k} keys, scheduler paused, inert bodies), canvas (wide layout always; fixed 768 px below md with horizontal scroll), grip buttons and corner resize handles with pointer capture, column guides, placeholder, push-down + compaction preview from drag-start snapshot per rAF, one update() per drop, physical→logical x in RTL, edge auto-scroll, keyboard moves/resizes, Move ▸/Size ▸ menus, polite announcements; tmMoveWidget/tmResizeWidget; gallery modal (Built-in coded reports by securable, Saved reports via reports/query, Other types by requires), append-at-bottom with focus and auto-open dialog for unbound required params, widget dialog (titles/descriptions per slot, refresh choice, report parameter binding rows with four choices, settingsEditor), dashboard settings dialog (titles, code, refresh, parameter reorder list with display/format and bound-parameter removal warning), registerField error mapping to dialogs/tiles, create on empty canvas, Save as via clone; prefetch on Edit hover/focus
- **Tests:** unit(vitest): tmMoveWidget/tmResizeWidget (clamp, cascading push, compaction), drag preview from start snapshot (down and back restores), arrow mapping LTR/RTL; harness: editor operations and dialogs, gallery's two groups, error mapping per path, Save as with ReportInvisible tiles; dashboard-keyboard.spec (move, resize, add, remove by keyboard only)
- **Done when:** an author creates, edits, rearranges and saves a dashboard by pointer, keyboard and menu; editor chunk ≤ 16 KB gz; 409 conflict handled by the host

### PC-205 — Dashboards e2e on Acme, geometry, feedback, axe and budgets

- **Analysis id:** `0035-e` · **Spec:** 0035 · **Layer:** both · **Tier:** 3 · **Est. LOC:** 3,500
- **Sections:** §17 Playwright/axe/budgets rows, §18 UI rules table and Acme journeys
- **Gated on (DEPS.md):** PC-204, PC-201, PC-192
- **Analyst dependency notes:** 0035-d; 0034-i; 0033-i
- **Deliverables:** Playwright specs: Acme 'Sales overview' journey (create with §10.2 parameters, Sales by center, coded credit exposure, My assignments; p.Customer; silent refresh under Playwright clock; Refresh; pointer and keyboard edits, a binding, save, 409; share with bell and per-tile audience warning; present mode in/out; viewer without acme.SalesInvoiceLine sees no-access tile), dashboard-feedback.spec, refresh-scheduler.spec, dashboard-hang.spec, dashboard-geometry.spec, width-cap checks at 1920/2560 and full width in present mode, request-interception asserts for Tellma-Priority/Tellma-Activity, axe battery, budget assertions
- **Tests:** e2e(Playwright) at 390/768/992/1440/1920/2560 in en and ar; axe light/dark LTR/RTL; budgets
- **Done when:** §18 UI-rules table pinned by its named specs; Acme journeys green against the real server

## Phase 9d Assignments, support and operations (Tier 3)

### PC-206 — Assignments write side: table, IAssignments, statements, [Assignable] realizer

- **Analysis id:** `0028-a` · **Spec:** 0028 · **Layer:** both · **Tier:** 3 · **Est. LOC:** 5,000
- **Sections:** §2.1, §2.2 (entity), §2.3, §3.1–§3.6, §4.5, §4.7 (composition, meter, logs)
- **Gated on (DEPS.md):** PC-164, PC-033, PC-062, PC-078, PC-035
- **Analyst dependency notes:** 0012 §5.2, §6.3, §13.1; 0015 §2.2, §7.1, §12, §13.1, §14.2; 0014 §3.9; 0021 §6.3; 0001 table types
- **Deliverables:** Tellma.Core.Abstractions.Assignments: Assignment, AssignmentState, AssignableAttribute, AssignmentRequest, AssignmentCaption, IAssignments, BatchAssignments, AssignmentsOptions; .TableTypes AssignmentRequestList; Tellma.Core.Assignments: core.Assignments migration (sequence START WITH 1000, PK, CK_Assignments_Id/Closed, three FKs, six filtered indexes), AssignmentsWriter with the ASSIGN, CLOSE and DELETE fixed-text statements on the internal channel, programmer-error checks, one OnCommitted callback per batch unioning recipients and publishing assignments.changed (failure logged AssignmentsEventFailed), caption serialization with TellmaJson.Options and LocalizedText, [Assignable] realizer (capability 'assignable', AssignableDeleteEffect<T>, startup check 'assignments'), BatchAssignments members on 0015's PersistContext/ActionContext, options validation, tellma.assignments.written meter and AssignmentsWritten log; CoreFeature composition
- **Tests:** unit: programmer errors, the check refusing a Guid-keyed [Assignable] entity, one callback per batch across calls, recipients' union, caption bound; fixture (SQL): contiguous ids, hand-off closes Handed with ClosedById, IsReturn, inactive or service-account assignee → Assignments.AssigneeInvalid, idempotent closes, two concurrent first assigns → 2601 → Assignments.Concurrent, deletes by ids/by query/with descendants removing every state and returning only open parties
- **Done when:** Unit and fixture write suites green; startup check registered; a stack with [Assignable] gains the capability in its descriptor; 0018's Acme assign/post compile against BatchAssignments.
- **Planner note:** BatchAssignments, IAssignments and AssignmentRequest are already seeded by 0015-a: implement and register them.

### PC-207 — Assignments read side: stack, summary, seen, trail extra, access

- **Analysis id:** `0028-b` · **Spec:** 0028 · **Layer:** both · **Tier:** 3 · **Est. LOC:** 3,500
- **Sections:** §2.2 (stack, self-scope), §4.1–§4.4, §4.6
- **Gated on (DEPS.md):** PC-206, PC-062, PC-003
- **Analyst dependency notes:** 0028-a; 0014 §3.3, §5.3, §7; 0015 §5.3; 0016 §2.2, §4.3; 0010 §F.1
- **Deliverables:** AssignmentService (EntityService<Assignment>, Query|Details) with SummaryAsync (one Read batch, ResultSets=2, capped counts, two latest lists, Related users via appended Query<User> with KeySetRestriction) and SeenAsync (Persist write of UserStamps.AssignmentsSeenAt, publishes to caller), AssignmentsSummary/AssignmentsSeenResult/AssignmentTrail records, AssignmentAccessCriteria (self), AssignmentTrailContributor<T> (TrailRowCount+1, HasMore, no CaptionJson), NoActivityStampMetadata on summary, tellma.assignments.summaries meter, MCP read-only exposure
- **Tests:** fixture (SQL, RCSI on): summary caps, New and self-assignment rules, orders and Related; seen returning the SeenAt it wrote; a summary started while a seen is uncommitted returning the earlier SeenAt; trail cap, HasMore, order across a tied AssignedAt, no CaptionJson; self-scope; a plan test that every summary statement seeks an open-row index; web integration: routes and retry classes, summary stamps no activity, assignments.changed reaches exactly the parties (fake publisher), MCP read-only
- **Done when:** §12 server suites green on Windows (LocalDB) and Linux (Testcontainers); routes listed in the projection; §13 server DoD met.

### PC-208 — Attention e2e and axe battery on Acme

- **Analysis id:** `0028-h` · **Spec:** 0028 · **Layer:** both · **Tier:** 3 · **Est. LOC:** 2,500
- **Sections:** §12 (Playwright, axe), §13 UI-rule pinning tests
- **Gated on (DEPS.md):** PC-207, PC-156, PC-157, PC-164, PC-092, PC-167
- **Analyst dependency notes:** 0028-b; 0028-e; 0028-f; 0028-g; 0018 §1.4, §6.3 (Acme assign/post, demo assignments); 0019, 0022 (exports, imports, fake sender); 0035 §9.2 (soft: home lists)
- **Deliverables:** Playwright on Acme at 390/768/992/1440 in en and ar: Sara assigns invoice 23 to Farah (tray accent in three tabs, one toast in the focused tab), return, post completes and home lists update; export live progress and stop from the bell, Track to job page reaching Succeeded and linking the export; import stopped with confirmation and outcome; core.export.ready download from toast; bell row opened in a new tab marked read; Mark as read/Clear on a selection; 5 s offline while typing shows the banner after typing stops; update banner reload keeping a draft; settings save and a test email reaching the fake sender; attention-warm/feedback/hang/layout/keyboard specs; axe on panels, toast stack, banner, Notifications section
- **Tests:** e2e (Playwright multi-tab), axe
- **Done when:** Every §12 suite green on Ubuntu and Windows; spec 0024's warm-launch.spec asserts both badges with every request held.

### PC-209 — Support requests server stack

- **Analysis id:** `0026-e` · **Spec:** 0026 · **Layer:** both · **Tier:** 3 · **Est. LOC:** 5,000
- **Sections:** §12.3, §12.4, §12.6, §12.7, §12.8, §12.1 (SupportOptions)
- **Gated on (DEPS.md):** PC-154, PC-029, PC-066, PC-017, PC-035, PC-145
- **Analyst dependency notes:** 0012 (entities, migrations, [JsonColumn]); 0014 §2, §5.3, §10; 0015 §7.2, §13.1, §13.3; 0016 (ApiAction projection, MaxJsonBodyBytes); 0017 §1.3, §4.1, §5.2, ResolveAsync; 0021 §2.3, §3.4, §3.5; 0011 §4.4 allowNonActive scopes, §5.9 admin surface; 0020 §14.4 (for the retention test)
- **Deliverables:** Tellma.Core: SupportRequest entity (core.SupportRequests, TopLevelEntity, [JsonColumn] DumpJson, [BlobReference] ScreenshotId) and its migration; [Stack(Query|Details|Delete|Enlist)], [ApiResource segment support-requests, MCP hidden], [DefaultSelect]; SupportRequestService.SubmitAsync (validation codes, daily limit in tenant zone, PageUrl/UserAgent cuts, ServerVersion/ExpiresAt, enlisted insert, screenshot attach, administrator lookup, notify in ContributeAsync, withdraw on DeletePersistContext, post-commit counter and SupportRequestSubmitted log); SupportRequestAccessCriteria (self); SupportOptions; SupportTelemetryNames; BlobKind narrowing for support-screenshot; core.support.requested notification type, NotificationCategory_support and resx SupportRequests.*/Notification_*; three admin-surface routes with SupportRequestRead audit; KQL alert on tellma.support.requests
- **Tests:** integration on real SQL (LocalDB/Testcontainers): submit codes, dump stored as sent, UserAgent/PageUrl cuts, screenshot attach and none, daily limit, 403 tenant-read-only and tenant-frozen for submit and upload, self-scope, details get carries DumpJson while a query cannot select it, notification to every administrator but the sender with a LocalizedText actorName, none when sender is the only admin, administratorsNotified both ways, delete (admin and retention) withdraws, counter once per committed submit with both tags, rolled-back submit records nothing, admin routes on Active/ReadOnly/Frozen tenants with 404s and audit logs, retention deletes rows and releases screenshots
- **Done when:** §13 'C# support' suite green; core.enlistments and other startup checks pass with the stack registered; DistributionInfo.support published.

### PC-210 — Screenshot capture and the support dialog

- **Analysis id:** `0026-g` · **Spec:** 0026 · **Layer:** both · **Tier:** 3 · **Est. LOC:** 4,500
- **Sections:** §11 (all), §12.2, Appendix A.1 tm.support.*
- **Gated on (DEPS.md):** PC-209, PC-154, PC-071, PC-020
- **Analyst dependency notes:** 0026-f; 0026-e (wire contract; harness uses the fake API); 0024 §10.1 upload; 0023 §3.3 tm-reference; 0005 tm-alert, 0010 §D.2
- **Deliverables:** /support: TmScreenCapture (supported, capture with getDisplayMedia hints and visibility-hidden dialog, two-frame grab, 3 s timeout, PNG, fromFile, oversize halving to 8 MiB), TM_DISPLAY_MEDIA token, tmFakeDisplayMedia in core-testing; TmSupportDialog six states (Compose, Download only with reasons, Sending with upload progress, Sent with administratorsNotified/providerAccess text and tm-reference, Failed with retry of only the failed step and screenshot re-upload, Unavailable), page-data checkbox/notice/warning variants, View JSON, 2,000-char message with counter, Ctrl+Enter, abort on Escape, RTL isolates, full screen below sm; send flow via TmApi.upload('support-screenshot') then support-requests/submit
- **Tests:** harness: TM_DISPLAY_MEDIA fakes (granted, declined, no frame in 3 s, oversize needing two halvings), dialog hidden during capture, file fallback and extension, every §12.2 state, ReadOnly tenant in Download only (also from the 5xx dialog), box ticked on every open, notice after steps 1 and 3 with the box disabled, submitted includesPageData matching, send with/without screenshot, retry and re-upload, warning per providerAccess, Sent text per flags, other-windows note
- **Done when:** §13 'Capture, support dialog' suite green; axe clean on every dialog state in light/dark, LTR/RTL; /support ≤ 25 KB gzip.

### PC-211 — Support requests stock pages and the 0026 e2e battery

- **Analysis id:** `0026-h` · **Spec:** 0026 · **Layer:** both · **Tier:** 3 · **Est. LOC:** 3,000
- **Sections:** §12.5, §13 (Playwright on Acme, Manual), §14
- **Gated on (DEPS.md):** PC-210, PC-209, PC-156, PC-092
- **Analyst dependency notes:** 0030 (search framework); 0031 (details framework, TmDetailsKit); 0026-e; 0026-g; 0028-e (bell assertion); 0018 §6.2 (Acme support on); 0011/0024 Acme host and client
- **Deliverables:** core-feature declaration tmEntity('core.SupportRequest', …) with visibleWhen; TmSupportRequestDetails (TmDetailsKit markup) and tm-support-dump-summary (parses dumpJson, thumbnail via blob GET, opens full image); Playwright: shortcut copies diagnostics (Chromium clipboard read), 5xx → Report this problem → fake capture → Send → Support requests page and an administrator's bell, pagehide flush reaches the relay, offline hold then flush, telemetry disabled sends nothing, dialog and panel at 390/768/992/1440 in en and ar; warm-launch.spec with the relay held open, support-dialog.spec frame probe, telemetry.spec hung relay, layout-shift.spec, keyboard-support.spec; Acme credit-review tmPage context with a primary action and shortcut, acme.credit-exposure.run event
- **Tests:** e2e (Playwright on Acme, en+ar, 4 widths), axe on the pages and dialog states, manual capture checklist per release
- **Done when:** Every §13 suite green on Ubuntu and Windows; §14's UI-rule pinning tests pass; API goldens and budgets for every §1 entry point.

### PC-212 — Sweep, reconcile, built-in schedules, blobs verify and alert queries

- **Analysis id:** `0017-e` · **Spec:** 0017 · **Layer:** backend · **Tier:** 3 · **Est. LOC:** 3,000
- **Sections:** §7.1-§7.4, §10.1 alert queries, §10.2 sweep/reconcile events, §11 sweep and reconcile rows
- **Gated on (DEPS.md):** PC-089, PC-084, PC-036, PC-097, PC-034
- **Analyst dependency notes:** 0017-c; 0020 §3.1; 0020 §3.3; 0020 §7.9; 0011 §6.1
- **Deliverables:** BlobSweepHandler (claim CTE with OUTPUT, store deletes for primary+variants, IdList row delete WHERE State='Deleting', FK-violation retain+meter, PurgeIncomplete once per run, IJobProgress per batch, cancellation between batches), BlobReconcileHandler (one Maintenance batch per registered kind over OwnerTable/OwnerColumn), BuiltInSchedule(4,'core.blob-sweep','*/15 * * * *') and BuiltInSchedule(5,'core.blob-reconcile','0 2 * * 6') contributions, TellmaMigrator `blobs verify --tenant <id> [--delete-orphans]`, infra/monitoring/blobs/*.kql (sweep.failures > 0, reconcile.released > 0, attach.rejected rate) plus a Tellma.Core.Tests alert-query test checking the queries against BlobTelemetryNames
- **Tests:** integration: claim takes only past-ExpiresAt rows, re-claims after SweepReclaimAfter, deletes rows only in Deleting; reconcile releases an old orphan and leaves a young orphan and a referenced blob; sweep run end to end through the 0020 worker harness on a ReadOnly tenant; verify command reports both orphan sets and deletes only with the flag. unit: alert-query names/tags
- **Done when:** Released and expired-staged blobs disappear from storage and from core.Blobs within one sweep. Both built-ins seed as 0020 HasData rows. The alert-query test fails when an instrument or tag is renamed.

### PC-213 — core.file-retention

- **Analysis id:** `0020-g` · **Spec:** 0020 · **Layer:** backend · **Tier:** 3 · **Est. LOC:** 1,300
- **Sections:** §14.4 (second handler)
- **Gated on (DEPS.md):** PC-145, PC-176, PC-209
- **Analyst dependency notes:** 0020-f; 0019 §12 (Export/Import stacks); 0026 §12.8 (SupportRequest stack); 0017 blob release effect
- **Deliverables:** FileRetentionHandler + BuiltInSchedule(3): pages of 500 expired Export/Import/SupportRequest ids via DeleteByIdsAsync as system user, skipping Imports with a live job
- **Tests:** integration(SQL): expired SupportRequest/Export/Import deleted, Import with live job kept, blob release effect runs
- **Done when:** Handler registered and pinned; below the 2k floor — fold into the checkpoint that lands the later of 0019 §12 and 0026 §12

### PC-214 — Delivery events, verbs, retention and telemetry

- **Analysis id:** `0022-c` · **Spec:** 0022 · **Layer:** backend · **Tier:** 3 · **Est. LOC:** 4,000
- **Sections:** §6, §7.1, §7.3, §8, §9.2 (delivery_updates, retained, log events, alerts), §11
- **Gated on (DEPS.md):** PC-149, PC-145
- **Analyst dependency notes:** 0022-b; 0020-f (retention conventions); 0016 endpoint projection
- **Deliverables:** OutgoingEmailDeliveryHandler (drop Opened/Clicked/Other, strongest per row, tenant-state policy incl. Frozen throw-after-others, ITenantDatabaseFactory handle, monotonic update + unmatched result set); retry/cancel actions (Update ByIds stamping, new job via IJobQueue on retry, column resets) and delete precondition; OutgoingEmailRetentionHandler + BuiltInSchedule(7) with 500-id pages via DeleteByIdsAsync; securables core.OutgoingEmail × Read|Delete|Retry|Cancel, Mcp=ReadOnly; OutboxTelemetryNames + instruments; outbox KQL alerts; ARCHITECTURE.md note
- **Tests:** unit: strongest-event choice and drops, tenant-state policy with a fake registry (Retired drops, ReadOnly/Suspended apply, Frozen throws after others); integration(SQL): duplicates/out-of-order/unknown references leave the strongest status once and return unmatched ids; retry on Failed makes a new job, on Rejected fails Email.NotRetryable, clears listed columns; cancel on ReadOnly tenant; delete of Pending-with-live-job fails Email.NotDeletable; retry/cancel stamp ModifiedAt; retention deletes settled/orphaned rows, releases blobs, keeps unsettled-with-live-job rows
- **Done when:** outgoing-emails/* endpoints green; webhook updates applied; every §9.2 instrument asserted

### PC-215 — Schedules and Outgoing emails pages (incl. the sandboxed email body viewer)

- **Analysis id:** `0032-b1` · **Spec:** 0032 · **Layer:** both · **Tier:** 3 · **Est. LOC:** 3,500
- **Sections:** §9, §10.1–§10.2
- **Gated on (DEPS.md):** PC-180, PC-136, PC-059
- **Analyst dependency notes:** 0032-a; 0028 §11.2 (job labels); 0010 §D.6 (TM_BLOB_FETCHER)
- **Deliverables:** `TmScheduleDetails` (built-in band locks, the 'Built-in' badge, the paused-reason alerts, `tm-job-handler-field` (write-once, schedulable handlers or 'No job can be scheduled yet.'), `TmJobHandlerCell`, `tm-json-field`, the cron hint, nullable `TimeZoneId`, the status aside from the `state` extra, run-now 202 toast + re-read, Delete disabled via the `permissions` extra). `TmOutgoingEmailDetails` (Message, Delivery, Body and Attachments cards; state and delivery badges; Regarding link via `TmUrls.target`; the `attention` filter target). `tm-email-body` (cid: images resolved through `TM_BLOB_FETCHER` to data: URLs ≤2 MiB with abort on destroy; the CSP meta + `<base target=_blank>` prefix with no parsing; a Blob object URL as iframe `src` with `sandbox="allow-popups allow-popups-to-escape-sandbox"`, `referrerpolicy=no-referrer` and revoke on destroy; the HTML/Text segmented control at fixed 480/320 px heights).
- **Tests:** unit: the email document prefix and cid: rewriting; harness: built-in locks, take-over/deactivate hidden, Delete disabled with Schedules.BuiltInImmutable, run-now toast, JSON validation message
- **Done when:** Schedules and emails render on the fake API. The email frame loads nothing remote (verified in a browser-mode unit test).

### PC-216 — Multi-tab e2e scenarios and the hidden-leader soak

- **Analysis id:** `0025-f` · **Spec:** 0025 · **Layer:** both · **Tier:** 3 · **Est. LOC:** 2,500
- **Sections:** §14 (multi-tab, WebKit, soak, staging), §15 (DoD pinning specs)
- **Gated on (DEPS.md):** PC-153, PC-114, PC-075, PC-096, PC-108, PC-107
- **Analyst dependency notes:** 0024-k; 0025-a; 0025-b; 0025-c; 0025-d; 0025-e; 0027 (pins UI); 0028 (bell, jobs); 0030 (tree search page); 0031 (drafts on details); 0032 (ReadOnly/roles admin)
- **Deliverables:** Playwright on the Acme host: store-boot.spec (one transaction, no fetch), prefs-apply.spec, page-state-return.spec, refresher.spec, hub-hang.spec, and the §14 multi-tab scenarios: leader close → failover within 2 s; a debugger-paused leader is stolen; an admin pins edit gives one client/sync; a preference shows across tabs within 100 ms; a new tab sees tree expansion; a draft survives reload but not a new tab; disabled realtime polls only while visible; a role change reaches an idle tab; ReadOnly within 15 s with one sync; dropped job.changed still moves the bell within 75 s; a membership ending disables actions within 3 s. The WebKit @cross-engine hung-leader spec. The nightly 15 min hidden-leader soak job.
- **Tests:** E2E (Playwright, multi-context and multi-tab), @cross-engine WebKit, nightly soak.
- **Done when:** The scenarios whose surfaces exist are green on Ubuntu and Windows; each pending scenario names the spec whose checkpoint will add it. The soak runs nightly.

### PC-217 — Client ID metadata documents (document clients)

- **Analysis id:** `0010-a4` · **Spec:** 0010-A · **Layer:** backend · **Tier:** 3 · **Est. LOC:** 5,500
- **Sections:** §A.3
- **Gated on (DEPS.md):** PC-014, PC-009
- **Analyst dependency notes:** 0010-a3
- **Deliverables:** TellmaIdentityClientMetadataOptions (TellmaIdentityOptions.ClientMetadata). IClientMetadataDocumentResolver and the ClientMetadataDocument record. The named HttpClient Tellma.Identity.ClientMetadata, whose SocketsHttpHandler resolves in ConnectCallback and checks every address (not loopback, link-local, private or multicast; no IP literal), with no proxy and no auto-redirect. Response, size and timeout guards; a per-host rate limit; the cache clamp; serving a cached copy after a fetch failure. Validation: client_id equality, redirect_uris, token_endpoint_auth_method none or private_key_jwt with a same-origin jwks_uri and inline jwks refused, grant_types. Loopback any-port redirect matching. DocumentClientHandler on the authorize, PAR, token and revocation endpoints, upserting the application row (public or private_key_jwt with the fetched JWKS; native when a loopback redirect exists; tellma:document_client set; no rsrc: permission). MaxClients cap (audit reason 'cap'). The hourly Quartz prune of rows with no authorization older than UnusedClientLifetime. Consent showing client_name and host. Discovery advertises client_id_metadata_document_supported and auth method none. The ClientMetadataFetched/Rejected events and the tellma.identity.client_metadata.fetches counter (outcome tag only).
- **Tests:** unit (document validation vectors; fetch guards against a fake handler and resolver, including a rebinding host; cache clamp; row descriptor; client_id over 100 characters refused; the key-set fetch guards; the cap); integration (a full code flow with consent showing the host; the row created and then updated on a changed document; invalid_client after expiry plus a failed fetch; a private_key_jwt code exchange; the prune keeping a consented row; the discovery metadata)
- **Done when:** Every Part A Testing row for §A.3 is green. No client_id URL appears as a metric tag. A test seam for the local document server is documented in the README.
