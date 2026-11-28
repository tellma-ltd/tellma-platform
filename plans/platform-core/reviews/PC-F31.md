# PC-F31: scope the SqlClient span assertion to the test's own commands

## Review 1

Reviewed commit `e26a10b` (`test/core/Tellma.Core.IntegrationTests/Observability/SqlQueryTextTests.cs`
only).

Verdict: the fix is right. It removes the flake by construction and keeps the test's teeth. No
blocking or major findings. One minor finding about a comment that misstates where the foreign
spans come from.

What I checked and found sound:

- **The instrumentation's process-wide state, read from the source.** I decompiled
  `OpenTelemetry.Instrumentation.SqlClient` 1.16.0. `SqlClientInstrumentation.Instance` is a
  singleton. Every provider's `AddSqlClientInstrumentation` adds its options to one list. The
  listener reads a merged snapshot at `WriteCommandBefore`, and the merge keeps
  `EnrichWithSqlCommand` only when every active provider's delegate is equal. It is `null`
  otherwise.
  - So while the control test's provider is alive, every command in the process gets an
    unenriched span.
  - The production delegate is a `static` lambda, cached as one instance, so two host providers
    still merge to `RemoveQueryText`.
  - In this assembly, only `SqlQueryTextTests` composes SqlClient tracing. xUnit runs the two tests
    of one class in sequence, and `await using` disposes the provider (and so removes its handle)
    before `RunAsync` returns. So the two windows never overlap, and `A_commands_…`'s own span is
    always enriched by its own delegate.
  - Spans of other classes' commands, started in the control's window and stopped in the next
    one, are exactly what the parent filter now drops.
- **Parenting.** The listener starts the span with `default(ActivityContext)`, so its parent is
  `Activity.Current`. `WriteCommandBefore` fires synchronously inside `ExecuteScalarAsync`, in the
  test's execution context, where `run` is current. A foreign command runs in its own flow and
  cannot inherit `run`. The sampler (parent-based over always-on) records the child of a recorded
  local parent, and `Assert.NotNull(run)` guards against the run source going unsampled.
- **No vacuous pass.** `A_commands_…` still asserts `Assert.Contains(spans, db.system.name != null)`
  over the filtered list, and the control asserts that the marker is present. To probe this, I
  replaced `parent = run.SpanId` with a random span id: both tests failed with
  "Assert.Contains() Failure: Filter not matched in collection". Reverted.
- **Leak proof, re-run myself (both reverted; `git status` clean after each):**
  - Variant A removed `"db.query.text"` from `QueryTextTags`. `A_commands_…` failed with "1 out of
    1 items", `Actual: "SELECT ? AS [TellmaQueryTextMarker];"`, which is its own span. The control
    passed.
  - Variant B set `EnrichWithSqlCommand = null` in `ConfigureSqlClient`. The failure was the same,
    on its own span.
  - My first variant A attempt hit the build-breaking style rules and ran the stale DLL green.
    The re-run confirmed "Build succeeded" before testing.
- **AGENTS.md.** The new constant and the rewritten `RunAsync` summary have XML docs. The new
  sections have inline demarcation comments. The `[Fact]` methods stay undocumented, as in every
  sibling test file. The source name `Tellma.Core.IntegrationTests.SqlQueryText` and all messages
  are free of spec, PC-id and `plans/` references.
- **Runs.**
  - Build: 0 warnings.
  - `SqlQueryTextTests` alone, 3 runs: 2/2, 2/2, 2/2.
  - Core integration project (`Category=Integration&Category!=Nightly&Live!=true`), 1 run:
    260/260.
  - Logs: `.tmp/pcf31-r1-*`.

### Findings

**R1-1 (minor). The comment names the wrong source of the foreign spans.**
`SqlQueryTextTests.cs:101-102` says a foreign span is "started under a provider of its own".
No other class in this assembly composes SqlClient tracing. The foreign spans are other classes'
commands that the singleton instrumentation traces while one of *this* class's providers is alive.
The ones that carry query text are those started in the control run's window, where the merged
enrichment is `null`, and stopped after the next run's provider is built.

- Failure scenario: a maintainer reads the comment and looks for (or adds) a second provider
  elsewhere to explain a leak. They miss the real cause: the instrumentation is a singleton, its
  enrichment is merged process-wide, and the control test nulls it for the whole process.
- Suggested fix: reword lines 101-102 to say this. The instrumentation traces every command in the
  process while any provider is alive. Its enrichment is merged across providers, so a command
  started during the control run's window carries its text. Such a span can end while this run's
  provider listens, and is left out here.

### Responses

- R1-1: fixed in the "PC-F31: review 1 fixes" commit. The comment now says the instrumentation
  traces every command in the process while any provider is alive, and merges its enrichment
  across providers. While the control run's provider lives, that merge is none, so another class's
  command started then carries its text. Such a span can end while this run's provider listens,
  and is left out.
