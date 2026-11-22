# PC-F70: Emit the ApiSecurableAuditFailed log event

## Implementer notes

### What was built

Spec 0016 §10.1 lists `ApiSecurableAuditFailed(problems)` at Critical before the startup throw.
The platform never emitted it: the endpoint audit's problems reached the host only as the
`TellmaCompositionException`. The event is now logged at both places where the audit fails startup.

- `src/core/Tellma.Core.AspNetCore/Api/ApiLog.cs`: `ApiLog.SecurableAuditFailed(logger, count,
  problems)`, a `[LoggerMessage]` beside the other web-API events (EventName
  `ApiSecurableAuditFailed`, `LogLevel.Critical`, category `Tellma.Core.AspNetCore.Api`). The
  template is `The endpoint audit found {Count} problem(s), so the host does not start: {Problems}`.
  `Problems` joins each problem's `CompositionProblem.Describe()` (source, problem, fix, one line)
  with `"; "`, the way `ApiChildSurfacesProjected` joins its surfaces.
- `src/core/Tellma.Core.AspNetCore/Routing/TellmaEndpointAudit.cs`: `internal static
  LogFailure(ILogger, IReadOnlyList<CompositionProblem>)` logs once when there is at least one
  problem. The startup check's body moved into `FindProblemsAsync`. `CheckAsync` finds the problems,
  logs any it found, and returns them. The realised gate throws only after every check has run, so
  the event precedes the throw. The check takes `ILoggerFactory` as a fourth constructor parameter.
  It is registered by type, so DI supplies the factory.
- `src/core/Tellma.Core.AspNetCore/TellmaEndpointRouteBuilderExtensions.cs`: `MapTellma` logs the
  same event right before it throws on the projection's and the platform audit's problems.
- READMEs: the `Tellma.Core.AspNetCore` audit section names the event and its fields, and the test
  project's `Routing/` row lists the new tests.

Whether and how startup fails is unchanged. The same exception is thrown with the same problems at
the same two points.

### Tests (`test/core/Tellma.Core.AspNetCore.Tests/Routing/EndpointAuditTests.cs`)

- `The_gate_s_audit_logs_its_refusal_at_critical_with_every_problem_before_startup_throws`: a Web
  endpoint requiring an unregistered securable passes `MapTellma`, and the test asserts no event at
  that point. It then fails `StartAsync` with `TellmaCompositionException`. The test asserts one
  `ApiSecurableAuditFailed` event at Critical, with `Count` = 1, `Problems` equal to the
  exception's audit problem rendered by `Describe()`, and the exact rendered message.
- `MapTellma_logs_its_refusal_at_critical_with_every_problem_before_it_throws`: an endpoint mapped
  ahead of `MapTellma` fails it during `Build`. The test makes the same assertions against the
  exception's single problem.
- `A_host_whose_endpoints_pass_the_audit_logs_no_refusal`: a passing host logs no such event.

With the emit removed from `CheckAsync`, the gate test fails, so it can go red. Local runs:
`Tellma.Core.AspNetCore.Tests` in full with CI's unit filter (998/998), `dotnet build Tellma.slnx`
(0 warnings, 0 errors), and `dotnet format --verify-no-changes` (clean).

No log-event catalogue test exists in the repository, and no README table pins the §10.1 events. The
`Tellma.Core.AspNetCore` README describes each event in prose, and the new event is described there
too.

### Deviations

1. **The event covers every endpoint-audit refusal, not only the securable rules.** The spec's
   name suggests the securable rules (0014 §4.5) alone. §10.1 places the event "before the startup
   throw" of §4.6's audit, and that throw carries every audit rule, so the event is logged for any
   `host.endpoint-audit` failure, the projection's rules and the JSON-contract rules included.
   Logging only the securable subset would leave the other failures out of the logs and would need
   problem classification that does not exist. The event name is kept as the spec states it.
2. **Fields are `Count` and `Problems`.** The spec lists `(problems)`. `Count` is added, following
   `ApiChildSurfacesProjected(surfaces)`, which the code already logs with `Count` and `Surfaces`.
   No numeric event id is assigned: the spec names none, and `ApiLog`'s events use `EventName`
   alone.
3. The event is logged from both throw sites, `MapTellma` and the realised gate's check. A host
   reaches at most one of them per start, because a `MapTellma` throw stops `Program.cs` before the
   gate runs, so each failed start logs the event once.

### Open risks

- When the gate runs without a host start (`TellmaStartupGate.RunChecksAsync`, for example a
  build-step check), a failing audit also logs Critical. That is correct for a failure, but noisy if
  a tool runs the checks expecting problems. No caller does today.
- The joined `Problems` string can be long when a distribution has many violations. It is the same
  text the exception message carries.

### What reviewers should examine hardest

- Whether deviation 1 (every audit refusal, not the securable subset) is the right reading of
  §10.1.
- Whether any path can log the event twice for one start, or throw without logging it. Check
  `WebApiProjection` problems that reach `MapTellma`, and the gate's "registry did not compose"
  pointer.

## Review 1

### Findings

Counts: 0 blocking, 0 major, 2 minor.

Checked and found sound:

- **Deviation 1 (every audit refusal) is the right reading.** §10.1 ties the event to "the startup
  throw", and §4.6 describes one startup audit whose single `TellmaCompositionException` carries
  spec 0011's rules, the securable rules and the projection's four rules together. Every problem
  `MapTellma` and `FindProblemsAsync` collect has source `host.endpoint-audit`
  (`WebApiProjection.cs:107,246`, `ApiContractsAudit.cs:151,196,206`, `RatePolicies.cs:197`). A
  securable-only event would need a classification that does not exist and would leave the other
  refusals out of the logs. The spec's event name is a misnomer, but not one the code should fix.
- **Deviation 2 (`Count` and `Problems`, no event id).** This matches `ApiChildSurfacesProjected`'s
  `Count`/`Surfaces`. No `[LoggerMessage]` in `ApiLog.cs` sets an `EventId`, so leaving one off is
  consistent. `Describe()` already folds line endings (`CompositionProblem.cs:39`), so the `"; "`
  join stays on one line.
- **Deviation 3 (`ILoggerFactory`).** Only the primary constructor changed; `CheckAsync`'s
  signature is the same. The check is registered by type
  (`TellmaWebApplicationBuilderExtensions.cs:162`), nothing else constructs it, and it is not in the
  gate's built-in fallback list. A factory is needed to log under `ApiLog.Category` beside the other
  web-API events. The migrator's `emit-client-assets` (`EmitClientAssetsCommand.cs:86`) composes
  `AddTellma` without the web registrations, so the check, and the event, never run there.
- **Exactly once per start, at both sites.** In `MapTellma`
  (`TellmaEndpointRouteBuilderExtensions.cs:149-154`), the projection's and the audit's problems
  are logged once and then thrown; `Program.cs` stops there and the gate never runs. When
  `MapTellma` passes, it logs nothing, and `StartingAsync` runs `RunChecksAsync` once, calling
  `CheckAsync` once (`TellmaStartupGate.cs:82,100`). The "registry did not compose" pointer is
  appended inside `FindProblemsAsync`, so it is logged with the rest. The only throw without the
  event is a check that itself crashes. The gate logs that as `CheckThrew` at Error and reports it
  as a check failure, not an audit finding, which is right.
- **The level and the startup outcome.** The level is Critical as §10.1 states. Both sites throw
  the same exception with the same problem list at the same points as before, and the `IsEnabled`
  guard only skips the join.
- **The tests go red without the emit.** Each test asserts `Assert.Single` over every captured
  event named `ApiSecurableAuditFailed`. With `MapTellma`'s emit removed, its test captures none.
  With `CheckAsync`'s emit removed, the gate test captures none, which the implementer also saw. The
  gate test's `Assert.DoesNotContain` after `Build` proves `MapTellma` did not log too, so the
  test pins exactly one event across the whole start.
- **Local runs.** `dotnet build test/core/Tellma.Core.AspNetCore.Tests` succeeded with 0 warnings.
  The project ran in full with CI's unit filter and passed 998/998. `EndpointAuditTests` alone
  passed 54/54. CI for pc/PC-F70 (run
  45028927493) was skipped, so there is no CI run to cite yet.

R1-1 (minor): **`MapTellma`'s event can be lost to batched exporters.**
`TellmaEndpointRouteBuilderExtensions.cs:152-153`. On the gate path the event is durable:
`app.Run()` is `RunAsync`, whose `finally` disposes the host and so flushes every logger provider.
On the `MapTellma` path, the exception escapes the distribution's top-level `Program.cs`
(`distributions/acme/src/Tellma.Distro.Acme.Web/Program.cs:17`) with the `WebApplication`
undisposed. The synchronous Serilog console sink writes the event, but the OpenTelemetry logging
pipeline, with Azure Monitor's exporter behind it (`AzureWebDefaultsTellmaBuilderExtensions.cs:106`),
batches records. The process ends before that batch is exported. A distribution that maps a bad
endpoint before `MapTellma`, or whose projection fails, therefore never gets this Critical event
into Application Insights. The README and the XML doc say the host's logs name the problems, but
that holds only for the console. Suggested fix: before the throw, flush OpenTelemetry's logger
provider, for example
`app.Services.GetService<OpenTelemetry.Logs.LoggerProvider>()?.ForceFlush(timeoutMilliseconds)`
with a short bound. Alternatively, defer `MapTellma`'s problems into the realised gate, as §4.6
words it ("reports into the realised composition gate"), so that one disposed path logs them. If
neither is wanted, state in the README that the `MapTellma`-time event reaches synchronous sinks
only.

R1-2 (minor): **"Every problem" is not pinned.** `EndpointAuditTests.cs:425-434`
(`AssertFailureLogged`) checks only a one-problem refusal: `Count` = `"1"` and `Problems` = that one
`Describe()`. Suppose `LogFailure` logged only the first problem, used `"\n"` instead of the
one-line join, or passed a count that disagreed with the list. Both tests would still pass. The
test README's claim "with its count and every problem" would then go unproven. Suggested fix: have
the `MapTellma` test map two violations (for example `/plain` and `/plain-2` ahead of `MapTellma`).
Then make `AssertFailureLogged` take the exception's whole list and assert
`Count == problems.Count` and
`Problems == string.Join("; ", problems.Select(problem => problem.Describe()))`.

### Responses

R1-1: fixed in the "PC-F70: review 1 fixes" commit (child of 5c274bc6). Following the ruling, `MapTellma`'s problems still throw from `MapTellma`. Nothing is deferred into the gate, and startup behaviour is unchanged. `Tellma.Core.AspNetCore` already references OpenTelemetry. After logging the event, and before the throw, `MapTellma` now resolves the optional `OpenTelemetry.Logs.LoggerProvider` from `app.Services` and calls `ForceFlush` with a 5 s bound (`FailureFlushMilliseconds`). A comment explains why: the host never runs, so it is never disposed, and a failed gate needs no flush because the run disposes the host. The event's XML remarks and the README now state that a sink buffering outside OpenTelemetry, such as a buffered Serilog file sink, may still lose a `MapTellma`-time event. No package was added. The `MapTellma` test registers an OpenTelemetry log processor that records which events it held when flushed, and asserts the event was among them. With the `ForceFlush` call removed, the test fails. On the spec: §4.6 says the audit "reports into the realised composition gate", which describes the shared gate's problem model. It does not require `MapTellma`'s own run to defer its problems. Spec 0011 §5.3 also has the audit "run by `MapTellma` and again by the realised gate". Failing at `MapTellma` is existing behaviour, pinned by `MapTellma_audits_what_is_mapped_when_it_runs`.
R1-2: fixed in the "PC-F70: review 1 fixes" commit (child of 5c274bc6). The `MapTellma` test maps `/plain` and `/plain-2` ahead of `MapTellma` and asserts that the exception carries two problems. `AssertFailureLogged` now takes the exception's audit problems and asserts that `Count` equals their count, and that `Problems` (and the rendered message) equals their `Describe()` strings joined by `"; "` in order. The gate test passes its one-element list.
