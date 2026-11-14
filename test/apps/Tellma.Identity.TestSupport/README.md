# Tellma.Identity.TestSupport

Helpers shared by the identity server's two suites — `Tellma.Identity.IntegrationTests` and
`Tellma.Identity.E2E` — and by the distributions' integration suites, which sign in through the
engine mounted in process: reading the mail those tests provoke, and driving OAuth/OIDC flows over
HTTP.

**Not a test project.** It carries no test SDK, so `dotnet test Tellma.slnx` never tries to run it,
and it removes the `using Xunit` that `test/Directory.Build.props` adds tree-wide, because it
references only xunit's assertion and extensibility libraries, never the `xunit.v3` metapackage.
Keep it that way: adding `Microsoft.NET.Test.Sdk` or the metapackage would make `IsTestProject`
true and turn this into an empty suite.

## What is here, and why it is not somewhere else

Identity's tests assert on the mail the server sends: the sign-in code in it, the invitation or
reset link in it, and which address it went to. Three projects could plausibly own that, and none
of them should:

| | Why not |
|---|---|
| `Tellma.Core.Testing` | Ships the platform's `CapturingEmailSender`, which deliberately only collects. What counts as "the code" is the sending product's business, not the test double's. |
| `test/shared/Tellma.Testing.Support` | Domain-free plumbing shared by every suite — a scripted HTTP handler, a static options monitor. An eight-digit-code regex is identity's, not the platform's. |
| Either identity suite | Both need it, and neither should reference the other. |

So `CapturedEmailExtensions` extends the platform's sender with identity's own reading of it:
`LatestFor`, `LatestCodeFor`, `LatestLinkFor`, and the `WaitFor*` forms for mail that a background
worker delivers after the request has already returned.

Before the platform's email pipeline existed, each suite carried its own whole `IEmailSender`
implementation to get these four lookups. This project is what is left of that once the sender
itself became shared.

## The protocol driver

`OidcFlowClient` drives OAuth/OIDC flows over raw HTTP the way a browser and a confidential client
would: a cookie-holding browser client that never follows redirects on its own, so a test asserts
every hop, and a cookie-less back channel for PAR and the token endpoint. It signs a user in with
the emailed one-time code read from the captured outbox. It is built over any test server's clients:

```csharp
using OidcFlowClient flow = new(factory.CreateClient, emails, pathBase: "/id");
```

The identity suite's factories wrap that as `CreateFlowClient()`; a distribution's suite passes its
web host's factory, its capturing sender and the path base the engine is mounted at.
