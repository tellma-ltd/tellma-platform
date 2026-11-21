# PC-F39: secrets in outbound HTTP request headers reach structured logs

## Review 1

Commit reviewed: `61843b2`, parent `61843b2^`.

Verdict: the fix is correct and closes the reported leak by construction in both web hosts and on
the SendGrid client. The tests are real regression tests, and each would fail with its fix removed.
No blocking or major findings. Four minor findings: a host the default does not reach, an
overstated "nothing is lost", gaps in the shared state walker, and an incomplete gate list.

What I checked and found sound:

- **The leak is real, and in the shape the fix targets.** I ran a scratch reproduction, outside the
  repo, on the repo's package versions (Microsoft.Extensions.Http 10.0.11, Serilog.AspNetCore
  10.0.0, OpenTelemetry.Extensions.Hosting 1.16.0). It used the platform's logging shape:
  `RemoveAll<ILoggerProvider>`, then `AddSerilog(writeToProviders: true)`, the OTel logger, and a
  `StateCapturingLoggerProvider` added afterwards. With a factory client at Trace and no fix:
  - the Serilog sink's `RequestPipelineRequestHeader` event carries a property
    `Authorization=["Bearer <token>"]`, while the rendered message reads `Authorization: *`;
  - the capture provider, which is fed through Serilog's provider forwarding, sees the token in 2
    entries;
  - the query value is already redacted (`?*`) by the factory, in both the message and the `Uri`
    property.

  With `ConfigureHttpClientDefaults(h => h.RemoveAllLoggers())`, both counts are 0.
- **The default reaches clients registered before the call.** In a second run I registered the
  client, with `AddStandardResilienceHandler()` (the shape of the identity backchannel-logout
  client), before `ConfigureHttpClientDefaults`. The result was the same: 0 hits. The only
  remaining entries were Polly's, and they hold no secret. So the in-proc identity client, which
  `AddTellmaAspNetCore` registers before `ObservabilityComposition.Register`, is covered.
- **Audit (a), re-run.** The grep over `src` finds two factory registrations: the backchannel
  logout client (`TellmaIdentityServiceCollectionExtensions.cs:167`) and SendGrid. It finds no
  `new HttpClient`, `DelegatingHandler` or custom `HttpMessageHandler` in `src`. There are no
  HttpClient registrations under `distributions/`, `templates/` or `samples/`. I agree with each
  of the implementer's audit rulings:
  - **Backchannel logout.** The logout token travels in the form body. A failure is logged at
    Warning with the client id and sid, not the token. Polly's events carry the status or the
    exception message, not the request.
  - **OIDC token redemption.** `OidcTokenRedeemer.cs:76-94` puts the refresh token and the client
    secret in the form body and sends them through `options.Backchannel`, which is not a factory
    client. Only the `error` field of the response body is surfaced.
  - **Marmin.** The token response is parsed and never logged. `MarminAeRequestException`
    messages carry the status and the parsed error detail, not the body or the token. The routes
    carry no secret.
  - **SendGrid.** The key travels only in the header. `SendGridEmailLog.RequestFailed` logs the
    status and the vendor's error text.
  - **The Azure SDKs.** They use Azure.Core's pipeline, not the factory.
- **Hosts (b).** Every web host composes through `ObservabilityComposition.Register`
  (`TellmaWebApplicationBuilderExtensions.cs:162`): Acme's web host, the in-proc identity, and the
  test host. The standalone identity host has its own call. The migrator and the design-time
  factory do not (see R1-1).
- **Diagnostics (c), ruling.** Removing the default loggers is the right fix, and a redacting
  logger is not needed now.
  - `RedactLoggedHeaders` cannot replace the removal: in my run the structured state kept the raw
    value under the default redact-all predicate, and only the message was redacted.
  - What operators lose is the Information "Start/End processing HTTP request" lines. The
    HttpClient instrumentation covers them as spans and metrics wherever an exporter is
    configured.
  - Spec 0011 §3.6 is binding on every host, and a structured Trace sink is an ordinary
    production troubleshooting setting.
  - A custom `IHttpClientLogger` (method, path without query, status, duration) can come later if
    operators ask for it. The claims in the comments and READMEs need correcting (R1-2).
- **Tests (d).**
  - SendGrid: the control (`A_client_with_the_factorys_default_loggers_would_carry_the_key_in_its_state`)
    is meaningful. It shows the capture sees state that the messages redact.
  - Core.AspNetCore: the Trace marker shows that the level reaches the capture. My reproduction
    shows that the Serilog-forwarded state is walkable by the capture. So that test fails without
    the default.
  - Identity: its `Texts` uses Serilog's property rendering, which includes
    `["Bearer <token>"]`, so that test fails without its fix too.
  - Scopes are walked, the exception is captured through `ToString()`, and nested sequences and
    `KeyValuePair<,>` of any type arguments are walked. The gaps are in R1-3.
- **AGENTS.md (e).** XML docs are on every new type and member. Test methods are undocumented,
  which matches the suites' convention. The changed files carry no PC ids, `plans/` paths, spec
  references or decision ids. The READMEs are updated.
- **Builds and runs.** Each touched project printed "Build succeeded", 0 warnings:
  SendGrid.Adapter.Tests, Core.AspNetCore.Tests, Identity.IntegrationTests and
  Distro.Acme.IntegrationTests. I ran:
  - `SendGridLoggingTests`: 3/3 (`.tmp/pcf39-r1-test-sendgrid.log`);
  - Core.AspNetCore `OutboundHttpLoggingTests`: 1/1 (`.tmp/pcf39-r1-test-core.log`);
  - Identity `OutboundHttpLoggingTests`: 1/1 (`.tmp/pcf39-r1-test-identity.log`);
  - Acme integration (PR-tier filter): 10/11 on the first run (`.tmp/pcf39-r1-test-acme.log`).
    The one failure was a SQL `Execution Timeout Expired` while the migrator provisioned tenant 2,
    before any HTTP call: load on the shared SQL Server, not this change. That test passed 1/1
    when re-run alone (`.tmp/pcf39-r1-test-acme-rerun.log`).

### Findings

**R1-1 (minor). The migrator's host does not get the default, but the code comments and the
commit claim coverage for any host.**

- Where: `src/core/Tellma.Core.Migrator/TellmaMigrator.cs:116-138` and
  `src/core/Tellma.Core.Migrator/Commands/EmitClientAssetsCommand.cs:63-68`.
- What happens: the migrator builds a `HostApplicationBuilder` and calls the core
  `services.AddTellma(...)` with the distribution's own `compose`, so the connectors it registers
  are composed there too. It never calls `ObservabilityComposition.Register`, so no
  `ConfigureHttpClientDefaults` applies.
  - The commit message says a host that wires Marmin through the factory "is covered by the host
    defaults below". The migrator is a host of exactly that kind, and it is not covered.
  - Per D-080, the migrator will call the identity server with the `<slug>-svc` secret.
- Why minor: nothing leaks today.
  - SendGrid removes its own loggers.
  - The PC-055 identity client is to remove its own.
  - The migrator's default providers (console, including the JSON formatter, and EventSource)
    write `HttpHeadersLogValue` through its redacted `ToString()`.
  - The exposure needs a distribution or an operator to add a state-enumerating sink, such as
    Serilog through `MigratorRunSettings.ConfigureServices`, or a new factory client that carries
    a credential.
- Suggested fix: add `ConfigureHttpClientDefaults(static http => http.RemoveAllLoggers())` to the
  migrator's builder, right after its `AddTellma`, and pin it with a small migrator-host test.
  Alternatively, move the default into the core `AddTellma` composition so that every host that
  composes a distribution gets it. That costs Tellma.Core a Microsoft.Extensions.Http reference.
  If neither is wanted, narrow the claim in the commit and comments to the web hosts.

**R1-2 (minor). "Nothing is lost" overstates what remains for a deployment without an exporter.**

- Where:
  - `src/connector/sendgrid/Tellma.Connector.SendGrid.Adapter/SendGridEmailServiceCollectionExtensions.cs:73-74`
  - `src/connector/sendgrid/Tellma.Connector.SendGrid.Adapter/README.md` (the new "Sending"
    paragraph)
  - `src/apps/Tellma.Identity.Web/Program.cs:47-48` and `README.md:5-8`
  - `src/core/Tellma.Core.AspNetCore/Observability/ObservabilityComposition.cs:91-92`
- What happens: the shipped `appsettings.json` files set Serilog to Information, with only
  `Microsoft*` overridden, and a single console sink. So a console-only deployment logged
  "Start/End processing HTTP request … - 202" for every SendGrid call and backchannel delivery.
  After the fix it logs nothing per request.
  - The SendGrid sender's own failure log is at Debug (`SendGridEmailSender.cs:157-160`), so it is
    not visible at the default level.
  - Azure Monitor export is config-gated, and no exporter is added on-prem, so "the HttpClient
    instrumentation traces every request" reaches no operator there.
- Why minor: the trade is right (see the ruling on (c) above), but the comments are the record a
  later maintainer reads when someone asks where the outbound lines went.
- Suggested fix:
  - Reword the comments and READMEs: per-request lines are dropped deliberately, and outbound
    requests are observable through the HttpClient spans and metrics when an exporter is
    configured.
  - Record the operator-visible change for the ledger or ARCHITECTURE.md.
  - Optionally, raise the SendGrid failure log to Warning or Information so that a console-only
    deployment still sees refusals.

**R1-3 (minor). The shared state walker misses two shapes, but its doc claims "any form a sink
could serialise".**

- Where: `test/shared/Tellma.Testing.Support/Logging/StateCapturingLoggerProvider.cs:13-24` and
  `:91-107`, and the README row.
- What happens: the walker gives up on two shapes.
  - **Non-generic dictionaries.** A non-generic `IDictionary` (a `Hashtable`, `Exception.Data`, or
    `HttpRequestOptions` via its non-generic view) enumerates as `DictionaryEntry`, which is not a
    `KeyValuePair<,>`. `Convert.ToString` on an entry yields the type name, so the entry's key and
    value are never seen.
  - **Plain objects.** A non-enumerable object with a type-name `ToString` keeps its properties
    hidden. That is the shape Serilog serialises for `{@Value}` destructuring, and that
    OpenTelemetry exporters walk for complex attributes.
- Why it matters: the provider now lives in `Tellma.Testing.Support` as the general tool for
  asserting that a secret reaches no log. A future suite that logs a destructured options object
  holding a key would pass while leaking. The current suites are unaffected, because the factory's
  state is a list of `KeyValuePair<string, object>`.
- Suggested fix:
  - Walk `DictionaryEntry` as a key and a value.
  - For any other non-primitive, non-enumerable value, walk its public instance readable
    properties within the existing depth bound, skipping indexers.
  - Add a small unit test of the walker for each of the two shapes.
  - Alternatively, narrow the remarks to what is walked.

**R1-4 (minor). The gate list omits integration suites that PLAN §7.1 item 4 counts as touched.**

- Where: the commit message's "Gates" paragraph.
- What happens: the commit changes `test/shared/Tellma.Testing.Support`, and under PLAN §7.1
  item 4 that makes every integration suite touched. Independently, `Tellma.Core.AspNetCore` and
  the SendGrid adapter changed, which touches Distro.Acme.IntegrationTests and
  Core.IntegrationTests by their ProjectReference closure. The commit lists only
  Identity.IntegrationTests.
  - The change to `Tellma.Testing.Support` only adds a file and a package reference, so a
    regression there is unlikely.
  - The new default does change the logging of every client in the platform host, which these
    suites exercise.
- Suggested fix: run the remaining integration suites one at a time with
  `--filter "Category=Integration&Category!=Nightly&Live!=true"`, and list them in the fix
  commit's message.
  - I ran Distro.Acme.IntegrationTests. It is green apart from one SQL-timeout flake that passed
    on a rerun (see above). Core.IntegrationTests and the other integration suites remain unrun.

### Responses

- R1-1: fixed in the "PC-F39: review 1 fixes" commit (child of 61843b2). The default now lives in
  one shared place, the core composition root: `TellmaServiceCollectionExtensions.AddTellma`
  registers `ConfigureHttpClientDefaults(http => http.RemoveAllLoggers())` before the pipeline
  runs. That composition is what the web host (`builder.AddTellma`), the migrator's command host
  (`TellmaMigrator.cs`) and `EmitClientAssetsCommand` all call, so all three get it, along with
  any later host that composes a distribution. `Tellma.Core` takes a `Microsoft.Extensions.Http`
  reference for it. The now-redundant call in `ObservabilityComposition` is removed. The
  standalone identity host keeps its own call, because it does not compose a distribution. Pinned
  by `Tellma.Core.Tests/Migrator/MigratorHttpLoggingTests`. That test is a theory over `status`
  and `emit-client-assets`:
  - The distribution's composition registers a factory client on a fake handler, plus the
    capture.
  - A startup check sends a bearer token and a query-string key through the host's factory, logs
    a Trace marker on the HttpClient category, then refuses the start, so no command reaches a
    database.
  - The test asserts the request went out, the marker was captured, and neither credential
    appears in any entry.
  - Both cases fail with the default removed: verified, 2/2 failed.
- R1-2: fixed in the "PC-F39: review 1 fixes" commit.
  - **Trade-off now stated.** The comments in `AddTellma`, the SendGrid registration and the
    standalone host's `Program.cs` say outright that the per-request "Start/End processing HTTP
    request" lines are deliberately no longer logged. They also say outbound requests stay
    observable as HttpClient spans and metrics only where an exporter is configured, and that
    each sender logs its own failures. "Nothing is lost" is gone.
  - **Operator notes.** A "What operators see change" note is in the `Tellma.Core` README (a new
    "Outbound HTTP logging" section, linked from the composition steps), the `Tellma.Identity.Web`
    README (a new section) and the SendGrid adapter README. The single bullet on this in the
    `Tellma.Core.AspNetCore` README moved to the `Tellma.Core` README.
  - **SendGrid failure log.** `SendGridEmailLog.RequestFailed` is raised from Debug to Warning,
    and so is the sender's `IsEnabled` guard. It carries the status and SendGrid's error text and
    never the key, so a refusal now shows at the shipped Information level.
  - **Spec text to align.** Spec 0007's §9.2 table lists the central "Message failed" event at
    Warning, and its PII rule allows provider error texts at Information and above, so the level
    fits that section. But the live-suite paragraph (spec 0007 around line 1801) still says the
    adapters "record a refusal's status and error code" at Debug. The live suite logs at Debug and
    above, so it still sees the line; that sentence is for the spec owner to align.
- R1-3: fixed in the "PC-F39: review 1 fixes" commit.
  - **What the walk covers now.** `StateCapturingLoggerProvider` walks a `DictionaryEntry` as its
    key and value. Any other non-scalar, non-enumerable object is walked through its readable
    public instance properties: indexers are skipped, and a getter that throws is skipped.
  - **Guards.** A reference-identity visited set ends cycles, and the existing depth bound of 8
    still applies.
  - **Where the walk stops.** It stops at strings, primitives, enums and the framework's
    value-like types. It also stops at reflection objects, delegates and `IAsyncResult` (tasks),
    so it never walks the runtime's reflection graph and never blocks on a task's `Result`.
  - **Docs.** The remarks and the `Tellma.Testing.Support` README row say exactly what is walked.
  - **Tests.** The walk is pinned by
    `Tellma.Core.Testing.Tests/Logging/StateCapturingLoggerProviderTests`, 9 tests. A value is
    found in each of these places:
    - a pair sequence whose message redacts it;
    - a non-generic dictionary, both its value and its key;
    - a destructured object nested in a member;
    - an exception's `Data`;
    - a scope.

    The other tests: a cyclic state ends, and the value behind the cycle is still found; a
    throwing getter is skipped; nothing is found where the value is absent. That project now
    references `Tellma.Testing.Support`, and its README says so.
- R1-4: fixed in the "PC-F39: review 1 fixes" commit. Every integration suite in `Tellma.slnx` was
  run, one at a time, with `--filter "Category=Integration&Category!=Nightly&Live!=true"` on
  Testcontainers:
  - Core.IntegrationTests: 269/269
  - Core.EntityFrameworkCore.IntegrationTests: 10/10
  - Core.Queryex.IntegrationTests: 289/289
  - Distro.Acme.IntegrationTests: 11/11
  - Identity.IntegrationTests: 206/206
  - AcsEmail, MarminAe and SendGrid IntegrationTests: no test matches the filter. Those suites
    are live-only (`Live=true`).

  No suite failed, so none needed a rerun. Unit filter on the touched test projects:
  - SendGrid.Adapter.Tests: 40/40
  - Core.AspNetCore.Tests: 493/493
  - Core.Tests: 3800 passed, 1 skipped, of 3801
  - Core.Testing.Tests: 53/53

  `dotnet build Tellma.slnx` succeeded with 0 warnings, and `dotnet format --verify-no-changes`
  was clean.
