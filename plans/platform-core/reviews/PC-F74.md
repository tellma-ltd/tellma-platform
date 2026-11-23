# PC-F74: Windows build-test exceeds its 30-minute job timeout: Tellma.Core.AspNetCore.Tests per-case host cost

## Implementer notes

### Where a case spent its time

There is no fixed wait. No delay, timeout, retry loop or blocking I/O sits in a case's path. Each
case started its own `TellmaTestHost` and paid for that host in CPU, and about eight classes did so
at once, so a case's wall time grew with the machine's load. ProblemMappingTests ran its 68 cases
one after another, because xUnit runs a class's cases serially, at 5–28 s each.

I instrumented one host (the probe is not committed). This is its steady-state breakdown on this
machine under the other slots' load:

| Phase | Before | After the product fix |
|---|---|---|
| `CreateBuilder` + `AddTellma` | ~25 ms | unchanged |
| `MapTellma` (its endpoint audit, the projection, the tenant model) | 1.5–3 s | ~1.3 s |
| `StartAsync` (startup checks, the gate's audit, the authorization policy cache) | 2–5 s | ~0.85 s |
| First request (`UseRouting`'s matcher build) | 1.1–2.5 s | 20–50 ms |
| Dispose | ~1 ms | unchanged |

The coordinator's lead is confirmed. The first request's time in `UseRouting` was the DFA matcher
re-materialising every endpoint.

### Root cause (product)

`RouteEndpointDataSource` and `GroupEndpointDataSource`, the minimal-API sources, rebuild every
endpoint on every `.Endpoints` read. Each rebuild runs the route's conventions again and compiles a
new request delegate (`RequestDelegateFactory`, about 9 ms per web endpoint, about 0.8–1 s for the
host's 121 endpoints). Their change token is a `NullChangeToken`, so no reader can cache.

A Tellma host read the table five times as it started. I found the readers from stack traces:

1. MapTellma's audit.
2. The gate's `host.endpoint-audit`.
3. `AuthorizationPolicyCache`'s constructor, over the DI root's `CompositeEndpointDataSource`, during
   the pipeline build.
4. The router's own composite, on the first request.
5. `RouteMisses.Candidates()`, on the first route miss.

Readers 2 to 5 each paid for the whole table again. A production host pays this too: about 3–4 s
of CPU at start and on its first request. It is a product defect, not only a test cost.

### What was built

- `src/core/Tellma.Core.AspNetCore/Routing/SealedEndpointDataSource.cs` (new) wraps a source. It
  builds the endpoints on the first read and answers every later read with the same instances
  until the inner source's change token fires, taking the token before the read so a change during
  the build is not lost. `GetGroupedEndpoints` and `GetChangeToken` pass through. `Dispose`
  forwards, because the composites dispose the sources they hold.
- `MappedEndpoints.Seal()` replaces each captured source in `app.DataSources`, in place, in order
  and once, by a `SealedEndpointDataSource`.
  - `TellmaEndpointAudit.FindProblemsAsync`, the gate's audit, calls it before reading.
  - The gate runs in `StartingAsync`, before `GenericWebHostService` builds the pipeline. That
    build copies `app.DataSources` into `RouteOptions.EndpointDataSources` and constructs the
    policy cache and the router from it. So readers 3, 4 and 5 all read the wrappers and get the
    audit's build.
  - MapTellma's own audit runs before a distribution maps, so it still builds afresh. Spec 0011
    §5.3 and 0016 §4.6 require the audit at both places, so I kept it.
- Test-harness host sharing, where nothing leaks between cases:
  - Nested `Host : IAsyncLifetime` class fixtures (the pattern CsrfTests, StepUpTests and
    TenantVerdictTests already use) in ProblemMappingTests (12 cases), FrameworkProblemsTests (10),
    ResponseHeadersTests (8), WebApiProjectionTests (25), WebOpenApiTests (4) and
    CompressionAndCachingTests (5).
  - Cases that change configuration or the environment, read logs or instruments, or watch the
    output cache store its first copy keep their own hosts.
  - WebApiProjectionTests' shared host records calls in a `PipelineLog`, so the class's
    constructor calls a new `PipelineLog.Reset()` before each case. It clears the calls and
    workbooks and restores `SkipWitness` and `Refusal`.
  - Four WebApiProjection cases became synchronous (`void`), because they no longer await
    anything (IDE0390).
- Docs:
  - ARCHITECTURE.md (realised gate paragraph): the gate's audit seals the sources, and an endpoint
    mapped after startup is never served.
  - The Tellma.Core.AspNetCore README: the same.
  - The test README: the sharing rule, and the Routing row's new coverage.

### Tests pinning the fix

`test/core/Tellma.Core.AspNetCore.Tests/Routing/SealedEndpointsTests.cs`:

- `/counted`'s builds, counted by a `Finally` convention over two requests, a route miss and a
  `MappedEndpoints` read, are exactly 1. MapTellma's audit runs before it is mapped.
- An endpoint mapped on the captured web group after start answers `route-not-found` and is absent
  from `MappedEndpoints`.
- A sealed source keeps its instances until its inner token fires, then reads again once.
- A sealed source passes its change token and grouped endpoints through.
- `Seal` wraps in place and in order, and is idempotent.

With `mapped.Seal()` removed, the first two tests fail.

### Durations

All local runs ran on this machine, with about 50 other dotnet processes from other slots. Each
run is Debug unless marked Release.

| Run | Tests | Duration |
|---|---|---|
| Full project, before | 1000 | 36 m 34 s |
| Full project, after the seal fix and 3 fixtures | 1005 | 10 m 2 s |
| Full project, after | 1005 | 8 m 50 s |
| ProblemMappingTests alone, before | 68 | 14 m 6 s |
| ProblemMappingTests alone, after (Release) | 68 | 19 s |
| CI's unit filter, after (Release, `dotnet test test/core/Tellma.Core.AspNetCore.Tests -c Release --no-build --filter "Category!=Integration&Category!=E2E&Category!=Nightly&Live!=true"`) | 1005 | 11 m 46 s |

- Summed case time fell from 19,408 s before to 3,854 s after, in Debug.
- The Release filtered run shared the machine with my format gate and D-151 runs. Its summed case
  time was 6,033 s, so it ran under heavier load, not slower code.
- The after-run's ProblemMappingTests cases sum to 58 s.
- The first targeted after-run covered ProblemMapping, WebApiProjection and SealedEndpoints: 138
  tests in 1 m 8 s.
- On CI, the assembly took 15–22 min (21 m 58 s in run 45088988534's windows job), against 36 m
  locally before. Scaling by that ratio, the 8 m 50 s after-run projects to about 5 min on
  windows-latest.
- That CI time is a projection. This push carries no ": integrate", so its pc run skips every job,
  and the integration run is the first to measure it.

The CI unit filter selects every test in this project, because it has no traits.

### Deviations

- Behaviour change: an endpoint mapped after the host's startup gate is no longer served.
  - Before, such an endpoint was served if it was mapped before the router's first request, but
    the audit never saw it, so it bypassed the audit's guarantees.
  - Now the host serves exactly the endpoints it audited. I consider this the intended meaning of
    0011 §5.3 / 0016 §4.6 ("every endpoint the host maps" is audited before traffic), and I
    documented it in ARCHITECTURE.md.
- None against a spec otherwise.

### Open risks

- A source whose change token does fire, for example the in-proc identity server's MVC controllers
  and pages, is re-read after a change exactly as before. InProcIdentityTests and SpaHostingTests
  pass.
- I did not share BffEndpointTests, EndpointAuditTests, SessionLifecycleTests, the hub tests,
  BackchannelLogoutTests or AdminEndpointsTests. They assert over the fake identity server's
  request queue, the session store, the catalog or startup failures, which a shared host would
  leak between cases. They are now the long tail, at 2–4 min summed per class locally.
- The remaining per-host cost is about 2 s of CPU:
  - MapTellma's mandated audit build (~0.8 s).
  - The tenant model, built per host by design: `TellmaModelCacheKeyFactory` keys by model
    definition.
  - The startup checks `core.time-zones` (~0.33 s), `core.stack-projections` (~0.27 s) and
    `core.options` (~0.23 s).

  These are candidates if the suite grows again. I left them alone, because the bar is met without
  changing them.

### What reviewers should examine hardest

- `SealedEndpointDataSource`'s caching:
  - Whether keeping instances can break a reader that expects fresh `Endpoint` objects. The
    framework's composites already cache between change-token fires.
  - The token-before-read ordering.
  - That `GetGroupedEndpoints` must never be served from the cache, because a group's prefix and
    conventions produce different endpoints.
- That `Seal()` runs before the pipeline build in every host shape, so every later reader holds the
  wrappers:
  - the gate runs in `StartingAsync`;
  - `UseEndpoints` copies `app.DataSources` at build;
  - `AuthorizationPolicyCache` reads the DI root composite, which is built over the same
    collection.
- Each converted case, for hidden state on the shared host. Each class's `Host` remarks say what it
  admits. Look closest at WebApiProjectionTests' `PipelineLog.Reset()` coverage, and at
  CompressionAndCaching's `/99` 404 case and tenant-cache case on a host that has served other
  requests.

## Review 1

### Findings

Scope checked: 0011 §5.3 and 0016 §4.6 (the audit at `MapTellma` and again in the realised gate
before traffic), PLAN §6-§7, D-238/D-241, the product diff, every converted test class and the
implementer notes. Confirmed:

- The root cause is measured, not assumed. `SealedEndpointsTests`' first case counts builds through
  a `Finally` convention across two served requests, a route miss and a `MappedEndpoints` read, and
  gets 1. So the policy cache, the router and the route-miss index all read the wrappers.
- I checked the framework outside Tellma, in a scratch app on ASP.NET Core 10.0.12 that wraps
  `app.DataSources` in `StartingAsync` the way `Seal` does:
  - `StartingAsync` runs before `GenericWebHostService` builds the pipeline.
  - `CompositeEndpointDataSource(IEnumerable)` copies the collection when it is constructed.
  - Unsealed, a top-level endpoint and a group endpoint mapped after `StartAsync` were both served
    (200), which is the old hole. Sealed, both answered 404, and the DI root composite held only the
    early endpoints.
- A request cannot race the seal. The server starts only after the pipeline build, and the build
  follows every `StartingAsync`.
- Routing results do not change. The wrappers keep the sources in order and hand back the same
  `Endpoint` instances with the same metadata. The framework composites already cached between
  change-token fires, so the router always used one set of instances.
  - `GetGroupedEndpoints` passes through, and only top-level sources are wrapped. Nested group
    sources are read through their parent group's `GetGroupedEndpoints`, so a group still builds
    with its own prefix and conventions.
  - A change-token source (MVC/Razor Pages for the in-proc identity) is re-read after its token
    fires, because the token is taken before the read.
- No legitimate mapping breaks:
  - Every platform mapping runs inside `MapTellma`: health checks, OpenAPI in Development, the BFF,
    admin, in-proc identity MVC/pages, the hub, the projection, blobs, the SPA and
    `IPlatformEndpointSource` (webhooks).
  - Acme's `Program.cs` maps nothing after `MapTellma`. No hosted service or startup filter in
    `src/` or `distributions/` maps endpoints.
  - SignalR's `MapHub` and the OpenAPI/ApiExplorer readers resolve the DI composite, which now
    holds the wrappers.
- The shared hosts leak nothing I could find:
  - `PipelineLog.Reset` covers every mutable member: calls, workbooks, `SkipWitness`, `Refusal`.
  - No problem mapping has host-level side effects. Nothing in `Problems/` invalidates or evicts.
  - Each shared case awaits its full response, and `centers/rebuild` records at invocation, so no
    server work outlives a case into the next one's log.
  - A shared host's request volume stays well under the 600/min user rate and the 60/min anonymous
    rate.
  - The cases that read logs or meters, change configuration, the environment or the clock, or
    watch the output cache store its first copy all keep their own hosts.
  - The activity case filters by its own trace id, and `ActivitySource` listeners are dynamic.
- No timeout raise, retry or quarantine. The diff touches no workflow, `timeout-minutes` or trait.
- AGENTS.md: every new type and member has XML docs, and no spec, `docs/` or PC id appears in XML
  docs, messages or test names.

Tests I ran (Debug, this worktree):

- `dotnet build test/core/Tellma.Core.AspNetCore.Tests`: Build succeeded, 0 warnings.
- The six converted classes plus `SealedEndpointsTests`, 193 cases, under two xUnit seeds (`:12345`
  and `:987`) to vary case order on the shared hosts: 193/193 green both times, about 56 s each.
- A real-host integration subset, filtered `Category!=Nightly`, `Live!=true`:
  - Tellma.Distro.Acme.IntegrationTests: WebHost, RealtimeHub, UserIdentity (in-proc identity MVC
    and pages with a live change token), Blob, Limits and SelfService. 18/18 green.
  - Tellma.Core.AspNetCore.IntegrationTests: Access, ProjectedRoute, Header and Health. 17/17
    green.

R1-1 (minor): after the seal, `MappedEndpoints` still reads the live collection, so a top-level
source added later reaches the route-miss index but not the router.
`src/core/Tellma.Core.AspNetCore/Routing/MappedEndpoints.cs:39-44` (`Endpoints()`) reads the
captured `app.DataSources` as it stands. After `Seal` (lines 65-87), a late top-level
`app.MapGet/MapPost(...)` gets a new `RouteEndpointDataSource`, because the framework's
`GetOrAddRouteEndpointDataSource` no longer recognises the wrapped one. The same holds for a late
`MapControllers`. My probe confirmed it: after the seal the list read `Wrap, Wrap,
RouteEndpointDataSource`.

- The router never sees the new source, because it copied the list at pipeline build.
- `MappedEndpoints.Endpoints()`, and through it `RouteMisses.Candidates()`
  (`Problems/RouteMisses.cs:91`), does see it. Suppose the first route miss comes after such a
  mapping, say a late `app.MapPost("/api/x", …)`.
  - `GET /api/x` answers `405` with `Allow: POST` for a route the host does not serve.
  - The class remarks say "the endpoints the gate audited are the ones the host serves". For
    `MappedEndpoints` itself that holds only for late group mappings.
- `An_endpoint_mapped_after_the_host_started_is_neither_audited_nor_served` covers only the group
  path (an endpoint added inside a wrapped group). Nothing pins the top-level path, whose non-serving
  rests on the framework copying the list.

Suggested fix: at the seal, snapshot the wrappers into an array field, and have `Endpoints()` read
that snapshot once sealed. Then the index, the audit and the router read the same set by
construction. Add a case to the late-mapping test that maps on `application` (the
`WebApplication`) after start. It should assert `route-not-found` for the late route, absence from
`MappedEndpoints`, and no `405` for another verb on its path.

R1-2 (minor): ARCHITECTURE.md overstates the guarantee for sources that signal a change.
`ARCHITECTURE.md:817` (the realised-gate paragraph, last sentence) says "the host serves exactly
the endpoints it audited and an endpoint mapped after startup is never served". The class remarks
of `MappedEndpoints.cs:16-21` make the same claim. But `SealedEndpointDataSource.Endpoints`
(`SealedEndpointDataSource.cs:49-53`) re-reads a source whose change token fires. The in-proc
identity's MVC/Razor Pages source does this when its action descriptors change, for example under
hot reload. The router then serves the new endpoints, and the gate never audits them. This is not a
regression, since such endpoints were never audited before either, and the package README states
the exception. Still, ARCHITECTURE.md is where the platform rule now lives (D-241: "no endpoint may
be added after startup"), and it states the rule without the exception.

Suggested fix: add the README's qualifier to the ARCHITECTURE.md sentence and to the
`MappedEndpoints` remarks: "a source that signals a change (the in-proc identity server's
controllers and pages) is read again, unaudited, after it does". Alternatively, decide that a
re-read after the seal is audited or refused, and record that decision.

R1-3 (minor): the commit's test list leaves out the integration suites of the hosts whose routing
it changes (PLAN §7.1 item 4).

- The seal changes what every real host builds and serves:
  - the in-proc identity's MVC and pages sources, with live change tokens;
  - the hub;
  - the blob surface;
  - the Acme host's composition.
- The commit message lists only unit filters: Core.AspNetCore.Tests, plus the D-151 runs of
  Defaults.Azure.Tests, Webhooks.Tests and Acme.Web.Tests.
- Two projects start real hosts under `Category=Integration`, and the commit did not run them:
  - Tellma.Distro.Acme.IntegrationTests, through `AcmeHost`;
  - Tellma.Core.AspNetCore.IntegrationTests, through `AcmeDeployment`.
- The subsets I ran are green (see above), so I expect no defect. The integration CI run will cover
  the rest.

Suggested fix: at fix 1, run both projects under `Category=Integration&Category!=Nightly&Live!=true`
and list them in the commit's Tests line.

### Responses

R1-1: fixed in the "PC-F74: review 1 fixes" commit.
- `MappedEndpoints.Seal` keeps the wrappers as a snapshot (`_sealed`, an array). Once sealed,
  `Endpoints()` reads the snapshot alone, so the audit, the route-miss index and the router read the
  same set by construction, and a source added to `app.DataSources` later is never read.
- New `A_top_level_endpoint_mapped_after_the_host_started_is_neither_served_nor_offered_to_another_verb`:
  - It maps `application.MapPost("/late-top", …)` after start and asserts the late source exists.
  - POST and GET both answer `404 not-found` with no `Allow`. The path is tenantless, so the
    fallback's code is `not-found`, not `route-not-found`.
  - The route is absent from `MappedEndpoints`.
- New `Once_sealed_the_endpoints_are_read_from_the_sealed_sources_alone`: a source added after
  `Seal` is never read.
- With the snapshot read reverted, both new cases fail, and GET answers `405`, reproducing the
  finding.

R1-2: fixed in the same commit. The qualifier now appears in three places: the ARCHITECTURE.md
realised-gate sentence, the `MappedEndpoints` remarks and the package README. A source that
signals a change, such as the in-proc identity server's controllers and pages under hot reload, is
read again after it does, and what it then adds is served without being audited. The README's
closing sentence also claimed "exactly", so it now carries the qualifier too. Auditing that re-read
stays out of scope, per the orchestrator's ruling.

R1-3: fixed in the same commit. Runs and counts are below, Debug in this worktree. The commit's
Tests line lists them.

| Run | Filter | Result | Duration |
|---|---|---|---|
| Tellma.Distro.Acme.IntegrationTests | `Category=Integration&Category!=Nightly&Live!=true` | 48/48 green | 5 m 54 s |
| Tellma.Core.AspNetCore.IntegrationTests | same | 39/39 green | 42 s |
| Tellma.Core.AspNetCore.Tests, in full | the unit filter | 1007/1007 green | 7 m 57 s |

## Review 2

### Findings

### Responses
