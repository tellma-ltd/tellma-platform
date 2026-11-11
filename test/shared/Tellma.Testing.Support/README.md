# Tellma.Testing.Support

Test doubles shared across the email and webhook suites, and the SQL Server fixture every
integration suite shares. Not a test project — it carries no test SDK,
so `dotnet test` never tries to run it — and not a shipped package either: it is referenced only by
projects under `test/`.

The line between this project and the published `Tellma.Core.Testing` is what a **consumer** needs
versus what **our own suites** need. `Tellma.Core.Testing` ships the contract-level doubles any
consumer of `Tellma.Core.Abstractions` benefits from — the capturing email sender, the delivery-event
builders, and the sender conformance suite every transport must pass. This project holds the wire
plumbing those suites happen to need to drive a specific transport, which no consumer would want.

| Folder | Contents |
|---|---|
| `Http/` | [ScriptedHttpMessageHandler](Http/ScriptedHttpMessageHandler.cs) — answers from a script, synchronously or asynchronously, and records every request: bodies and request headers, plus content headers and a per-exchange snapshot on `Exchanges`. An asynchronous responder is what lets a suite hold a response open while it arranges something else — a second caller reaching a single-flight gate, a clock advanced past a timeout — rather than racing a real one. One handler drives both HTTP-based transports: the SendGrid client takes an `HttpClient` directly, and the Azure pipeline accepts one through its transport. [SingleClientHttpClientFactory](Http/SingleClientHttpClientFactory.cs) — hands that one client to an adapter that resolves `IHttpClientFactory`, whatever name it asks for. |
| `Options/` | [StaticOptionsMonitor](Options/StaticOptionsMonitor.cs) — an `IOptionsMonitor<T>` over a fixed instance, so a sender that reads options per batch can be driven without a configuration stack. |
| `Sql/` | [SqlServerFixture](Sql/SqlServerFixture.cs) — one SQL Server for a suite, registered as an assembly or collection fixture: a Testcontainers container by default (`TELLMA_TEST_SQL_IMAGE` overrides the image), or the server `TELLMA_TEST_SQL` names. It hands out uniquely named databases (`NewDatabaseName`, `ReserveDatabase` for a host that creates its own, `CreateDatabaseAsync` in a stated collation), drops them at the end on an existing server, and runs batches and scalar queries. Referencing it brings Testcontainers and xunit's extensibility core along. |
| `Tenancy/` | [StubSandboxContext](Tenancy/StubSandboxContext.cs) — a settable sandbox flag, for driving the routing matrix. |
