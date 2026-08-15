# Tellma.Core.Testing

Test doubles and executable conformance suites for the `Tellma.Core.Abstractions` contracts — the
C# sibling of `@tellma/core-ui-testing`. Referenced by test projects, so distribution and platform
suites stop hand-rolling their own email fakes.

It also carries the diagnostics a credentialed suite needs to explain a failure nobody can step
through, which is why this package is the one that takes an xunit dependency by charter.

## What is in it

**[CapturingEmailSender](Email/CapturingEmailSender.cs)** records every message instead of sending
it. Thread-safe, because the code under test may send from several requests or background workers at
once. `WaitForAsync` is event-driven rather than polled, so a test that triggers a send through a
background worker neither sleeps nor flakes. `OnSending` scripts the result per message, keyed by the
message's **lifetime ordinal** — not its index within a batch, because "fail the first attempt,
accept the second" is a statement about attempts.

**`AddCapturingEmail()`** wires it in, two ways:

- `ReplaceSender` swaps out `IEmailSender` outright. Right when a test only cares about what a
  feature composed.
- `Transport` registers it as an ordinary transport named `capture`, selected by setting
  `Email:Provider`. The router, the sandbox policy, and the marking all stay in the loop, so an
  integration test exercises exactly what production runs.

**[DeliveryEvents](Email/DeliveryEvents.cs)** builds plausible delivery-event batches for handler
tests, including `Redeliver()` for the duplicate a provider will eventually send. Deterministic by
default — fixed ids, fixed timestamps — because a fixture built on the wall clock makes every
assertion touching ordering or lag flaky.

**[EmailDeliveryEventTransports](Email/EmailDeliveryEventTransports.cs)** names the transports that
have a delivery-event callback at all. The silent-webhook alert query watches exactly that set, and
two suites that cannot see each other have to agree on it: the core alert-query test checks it
against the query, and each connector's own suite checks its membership against what its composition
registers.

**[EmailSenderConformanceTests](Email/EmailSenderConformanceTests.cs)** is the executable form of the
`IEmailSender` batch contract. Every email transport in the platform derives from it and supplies an
[IEmailSenderHarness](Email/IEmailSenderHarness.cs); anyone writing a new transport inherits the same
suite. It pins:

- one result per message, in input order;
- a structurally invalid message rejected on its own, never reaching the wire;
- at most one wire attempt per message, whatever the refusal — no adapter-level retries;
- an up-front credential failure that throws, because the caller knows nothing went out;
- the same failure *after* a success reported per message instead, because throwing then would force
  the caller to choose between duplicating sent mail and dropping unsent mail;
- a throttling response that stops the batch rather than hammering the endpoint;
- cancellation that propagates rather than becoming a per-message outcome.

Cases a transport cannot express on its own wire are skipped through `SenderCapabilities`, and every
skip reason names the transport limitation — so a skip always reads as "not applicable", never as
"not implemented".

**[TestOutputLoggerProvider](Diagnostics/TestOutputLoggerProvider.cs)** and its
`AddTestOutput()` builder extension route `ILogger` records into the running test's output.
`AddLogging()` registers no provider of its own, so a transport's account of what went wrong —
often the whole diagnosis — otherwise goes nowhere. Written for the live suites, where the failure
someone reads is hours old and cannot be stepped through.

**[LiveTestEnvironment](Diagnostics/LiveTestEnvironment.cs)** reports the settings a live run used,
disclosing shapes rather than values: `MaskMailbox` keeps the domain (the half that explains a
refusal) and drops the mailbox, `DescribeSecret` reports presence and length. A live suite fails
either because the code is wrong or because the environment was not what the run assumed, and a
bare assertion cannot tell those apart — but the log that answers it is published by a public
repository's Actions runs, which is the wrong audience for an address or a key.

## Rules this package lives by

- **It takes an xunit dependency, and nothing else does.** Shipping the conformance suite is the
  point: a transport written outside this repository should inherit the contract rather than
  reimplement a reading of it. Everything else here is framework-agnostic.
- **The doubles answer to the same contract as the real thing.** The capturing sender rejects a
  structurally invalid message exactly as a transport would, and it passes the conformance suite —
  untested test infrastructure produces false green everywhere downstream.
