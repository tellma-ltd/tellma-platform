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
