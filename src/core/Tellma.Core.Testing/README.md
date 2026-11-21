# Tellma.Core.Testing

Test doubles, executable conformance suites (email senders, blob stores, entity stacks) and the job item probe for the
`Tellma.Core.Abstractions` contracts — the
C# sibling of `@tellma/core-ui-testing` — and the pins every EF migrations assembly is held to.
Referenced by test projects, so distribution and platform suites stop hand-rolling their own email
fakes and their own migration checks.

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

**[BlobStoreConformanceTests](Blobs/BlobStoreConformanceTests.cs)** is the executable form of the
`IBlobStore` contract. Every blob store derives from it and supplies an
[IBlobStoreHarness](Blobs/IBlobStoreHarness.cs), which gives the suite tenants of its own and the
out-of-band access the contract alone cannot: whether a tenant's container or directory exists,
removing one behind the store's back, and planting incomplete artefacts. It pins:

- byte-for-byte round trips through a seekable stream of the object's length, empty objects
  included;
- create-only writes: a second write of a name throws `BlobAlreadyExistsException` and keeps the
  first bytes, eight writers racing to one name leave exactly one winner whose bytes read back, and
  a failing object leaves the call's other writes in place;
- content that yields more or fewer bytes than declared failing with `BlobStoreException` and
  leaving no object;
- a primary and its `thumb` variant written in one call;
- null for a missing object, success for deleting one, and nothing created by a read, delete, list
  or purge in a tenant without storage;
- tenant isolation;
- listing by kind and by fan-out segment in name order, with a kind never matching a longer kind
  that starts with it;
- the purge removing only artefacts older than its cut-off (skipped, via `BlobStoreCapabilities`,
  for a store whose writes are atomic on the service side);
- idempotent `EnsureTenantAsync`, and a write that recreates missing tenant storage and retries;
- every name outside the grammar (traversal strings, backslashes, upper case, a mismatched fan-out,
  a reserved device name) refused by every member before storage is touched, and every
  non-positive tenant id refused.

**[InMemoryBlobStore](Blobs/InMemoryBlobStore.cs)** is an `IBlobStore` over a dictionary per
tenant that passes the conformance suite above: create-only writes, the contract's name and length
validation, and the out-of-band reads a test asserts with (`Find`, `Names`, `TenantExists`,
`RemoveTenant`, and `Purges`, each purge's cut-off, which `ClearPurges` forgets). `FailWrites` makes every write throw and
`FailDeletes` keeps the objects it matches and throws after deleting the rest, for the failure
paths of the code that writes and the code that deletes.

**[StubImageProcessor](Blobs/StubImageProcessor.cs)** is an `IImageProcessor` that decodes nothing:
it hands the input back as the normalised image at the policy's dimensions and renders a fixed
thumbnail, and it scripts a rejection (`RejectReason`), a busy refusal (`Busy`) or no thumbnail
(`RenderThumbnails`), counting its calls.

**`AddInMemoryBlobs()`** replaces whatever store and processor the composition registered with one
of each and returns the store. Every host composes the core model, whose user image and signature
kinds need a store and a processor for the startup check to pass, so a test host that runs neither
real one calls it.

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

**[CollationPin](Database/CollationPin.cs)** holds a migrations assembly and its model to the
platform collation (`DatabaseCollations.Platform`) without a database: the design-time model must
declare it, the model must have no change its migrations do not capture, and no migration's up or
down operations may hold an `AlterDatabaseOperation` that changes the collation — an initial
migration's annotation-only one passes. A database's collation is set once by `CREATE DATABASE`, and
Azure SQL Database refuses the `ALTER DATABASE … COLLATE` a changed model would scaffold, so every
suite that owns a migrations assembly runs `CollationPin.Verify` over a context built the way its
design-time factory builds one.

**[LatestMigrationPin](Database/LatestMigrationPin.cs)** keeps a published constant naming a
migrations assembly's last migration — the row a host's startup check requires in its store's
history, read so that the host need not carry the migrations — equal to that assembly's last
migration.

**[JobItemProbe](Jobs/JobItemProbe.cs)** plays the worker's part in a job handler's unit test:
it builds the items a claim would hand the handler (`ForJob`, `ForItem`), seeds the checkpoint a
resume reads (`SetCheckpoint`, stored under the platform's JSON rules), records why an item is being
cancelled (`SetCancellationReason`, with the worker's escalation rule), and reads back what the
handler marked (`OutcomeOf`, a [JobItemOutcome](Jobs/JobItemOutcome.cs)) and the notifications it
queued for success. It reads and writes what the worker does and nothing more: an unmarked item
stays unmarked, where the worker would settle it from how the handler ended.

The two pins read EF Core's relational model and migration operations, which is why the package
references `Microsoft.EntityFrameworkCore.Relational` (never a provider).

**[FixedTenantSettingsCache](Settings/FixedTenantSettingsCache.cs)** serves a tenant's settings
without a tenant database, so a host composed over an in-memory catalog can complete its tenant
scopes: every tenant scope runs the locale negotiation, which reads the tenant's settings. It builds
the settings from the scope's tenant descriptor (its declared languages, the platform's first
calendar, UTC, no stored entries) and serves them as a hit under one fixed tag.
`UseFixedTenantSettings(configure)` replaces the composed cache after the composition; `configure`
reshapes the settings, such as a zone or a region a test needs.

**[StackConformanceTests<TEntity, TKey>](Crud/StackConformanceTests.cs)** is the executable form
of an entity stack's contract. A distribution closes it once per stack, over a
[IStackConformanceHarness](Crud/IStackConformanceHarness.cs) on its own tenant database and a
`NewValidEntity(ordinal)`, and every case reads the rest from the stack's descriptor: the declared
operations reachable, each answering as it should for a row that does not exist, and the absent
ones refused; enlisted writes from a declared writer at no round
trip of their own and an undeclared writer refused; the securables with their filter roots, and each
child query surface's read through its owner; the round-trip budget of every read and write, held
to the driver's own count when the harness observes it and measured over two rows (and two child
rows at every depth) so a statement per row shows; a row the read grant hides answering as a
missing one on every operation that names ids; stamp conflicts, overrides and rows deleted since
they were read; write-once and server-owned values; null and empty collections and foreign child
ids at every depth; a stack whose deletes are judged serving no delete by query; unique and natural-key values, and rows still referenced; the activatable, tree, cacheable and
searchable recipes; and the instruments. A case that does not apply to the stack's declaration is
skipped with its reason; one that needs what only the distribution can supply (a writer of the
stack, a row of another entity that references it, the rows of a stack that saves none) fails,
naming the member to override. What no descriptor says is the closing class's to state: what the
stack's validators load and in how many dependent rounds, the arguments of its entity actions and
the bodies of its API actions, and which references to the stack the in-use case exercises.

**[CrudInstrumentCollector](Crud/CrudInstrumentCollector.cs)** collects every measurement of the
pipeline's `tellma.crud.*` instruments on one host's meter factory and holds each to its
instrument's closed tag set: the tags it must carry, the ones it may, and the values each closed tag
may take. `AssertClosed` fails with every measurement that leaves its shape. It listens through
`MetricCollector`, which is why the package references `Microsoft.Extensions.Diagnostics.Testing`.

**[ValidationContexts](Crud/ValidationContexts.cs)** builds the contexts a stack service's
validation hooks see after the pipeline's first round: a save's payload with which rows are new and
their before images, and a delete's ids, loaded targets and whether it removes their descendants.
The pipeline sets those parts itself and keeps their setters internal, so a module's unit tests,
which never see the pipeline, drive their rules through these with a scripted `IContextLoader` and
no database. The contexts' write host refuses confirmations, which a rule under test does not raise.

## Rules this package lives by

- **It takes an xunit dependency, and nothing else does.** Shipping the conformance suites is the
  point: a transport or store written outside this repository should inherit the contract rather
  than reimplement a reading of it. Everything else here is framework-agnostic, the pins aside: they
  report through xunit's `Assert.Fail`, and expose the same findings framework-free through
  `FindViolations`.
- **The doubles answer to the same contract as the real thing.** The capturing sender rejects a
  structurally invalid message exactly as a transport would, and it passes the conformance suite —
  untested test infrastructure produces false green everywhere downstream.
