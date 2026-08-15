# Tellma.Identity.TestSupport

Helpers shared by the identity server's two suites — `Tellma.Identity.IntegrationTests` and
`Tellma.Identity.E2E` — for reading the mail those tests provoke.

**Not a test project.** It carries no test SDK, so `dotnet test Tellma.slnx` never tries to run it,
and it removes the `using Xunit` that `test/Directory.Build.props` adds tree-wide, because it
references no test framework at all. Keep it that way: adding `Microsoft.NET.Test.Sdk` would make
`IsTestProject` true and turn this into an empty suite.

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
