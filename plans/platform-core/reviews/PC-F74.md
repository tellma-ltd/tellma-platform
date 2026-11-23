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

### Responses

## Review 2

### Findings

### Responses
