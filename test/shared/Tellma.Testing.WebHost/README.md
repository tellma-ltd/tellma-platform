# Tellma.Testing.WebHost

The web test host the web host's suites share: a real `WebApplication` on a test server, composed
with the real `AddTellma`, `UseTellma` and `MapTellma`, over an in-memory tenant catalog and session
table, a fake clock, a scripted user connection, a static identity server and probe endpoints on
every surface. Not a test project (it carries no test SDK, so `dotnet test` never tries to run it)
and not a shipped package: it is referenced only by projects under `test/`.

Its users are `Tellma.Core.AspNetCore.Tests` (the web host's own suite), `Tellma.Core.Mcp.Tests`
(the tenant MCP server composed into the host, driven through the SDK's client),
`Tellma.Defaults.Azure.Tests` (the realtime line of the Azure web defaults, composed offline) and
`Tellma.Defaults.Azure.AspNetCore.IntegrationTests` (the same line against a real Azure SignalR
Service instance). Its types are internal, visible to those four.

| File | Contents |
|---|---|
| [TellmaTestHost](TellmaTestHost.cs) | The host and its options (`TestHostOptions`): the environment, the settings (sound ones by default, the realtime hub off, a five-minute web timeout that outlasts every test's patience), the catalog rows, the composition and web composition delegates, extra services, and what is mapped. It mints session cookies over stored rows, bearer tokens against the test issuer's key, and the SPA's requests, and moves its clock past the tenant snapshot's staleness bound with the snapshot confirmed again (`AdvanceAsync`, and `AdvanceToAsync` over the clock's armed-timer move), so work after a long move that reads the snapshot never meets it stale: a tenant request, a background tenant scope such as a job or an outbox delivery, a tenant connection's resolution, the readiness check. The refresh's failure fails the move, so a test that wants the snapshot stale, or sets `Catalog.Failure` or `Catalog.Gate`, keeps the clock's own move. |
| [TestClock](TestClock.cs) | The host's fake clock, which also keeps the timers armed on it: a test moves it to an instant once the timers it expects there are armed, never on a guess at how long the host takes to arm them. |
| [Patience](Patience.cs) | How a test waits for what a correct host does on its own (a signal, a condition, a request's end): a two-minute limit that only ends a hang, since a cold host on a loaded runner takes seconds where an idle one takes milliseconds. |
| [ProbeEndpoints](ProbeEndpoints.cs) | Endpoints on every surface that answer the request context they ran under. |
| [TestUsers](TestUsers.cs) | The scripted user connection: user ids, deactivations, a failure every prologue round trip throws, and the count of those round trips. |
| [InMemorySessionRows](InMemorySessionRows.cs) | The session table in memory, with its renewals counted. |
| [FakeMembershipDirectory](FakeMembershipDirectory.cs) | The membership hints in memory, a failure every read throws, and a count of the reads. |
| [FakeReconcileStore](FakeReconcileStore.cs) | The membership reconciliation's store in memory. |
| [FakeIdentityServer](FakeIdentityServer.cs) | The identity server's back channel: the pushed authorization and token requests answered from a script. |
| [Realtime/RealtimeTestHost](Realtime/RealtimeTestHost.cs) | The host with the hub on, in the in-process mode, over five tenants in each state; `HubClient`, a client over the test server's WebSockets that records its events and its close; `ConnectedProbe`, a hub filter that signals when the hub finished a connection's connect, by its id or by the token its WebSocket connected with; and `Envelope`, which reads a received event. |

The tenant catalog's in-memory reader is linked from `Tellma.Core.Tests` rather than copied.
