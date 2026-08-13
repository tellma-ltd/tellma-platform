# Tellma.Testing.Support

Test doubles shared across the email and webhook suites. Not a test project — it carries no test SDK,
so `dotnet test` never tries to run it — and not a shipped package either: it is referenced only by
projects under `test/`.

The line between this project and the published `Tellma.Core.Testing` is what a **consumer** needs
versus what **our own suites** need. `Tellma.Core.Testing` ships the contract-level doubles any
consumer of `Tellma.Core.Abstractions` benefits from — the capturing email sender, the delivery-event
builders, and the sender conformance suite every transport must pass. This project holds the wire
plumbing those suites happen to need to drive a specific transport, which no consumer would want.

| Folder | Contents |
|---|---|
| `Http/` | [ScriptedHttpMessageHandler](Http/ScriptedHttpMessageHandler.cs) — answers from a script and records request bodies. One handler drives both HTTP-based transports: the SendGrid client takes an `HttpClient` directly, and the Azure pipeline accepts one through its transport. |
| `Tenancy/` | [StubSandboxContext](Tenancy/StubSandboxContext.cs) — a settable sandbox flag, for driving the routing matrix. |
