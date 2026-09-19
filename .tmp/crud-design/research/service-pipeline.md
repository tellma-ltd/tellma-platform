# Research: CRUD service pipeline and capabilities (theme `service-pipeline`, future spec 0014)

Verified on 2026-09-01 unless a finding says otherwise. Every finding is tagged **[Verified]** (read in a
primary source on that date, URL given) or **[Inferred]** (a conclusion drawn from verified facts; treat as a
design input, not a fact). Where a secondary source is cited it is marked *(secondary)*.

Repo facts consulted (read from the working tree on 2026-09-01): `global.json` pins SDK `10.0.300`;
`Directory.Packages.props` pins `Microsoft.Data.SqlClient` **6.1.1** (comment: "kept on the version
`Microsoft.EntityFrameworkCore.SqlServer` resolves to, so the solution has exactly one SqlClient"),
`Microsoft.EntityFrameworkCore.*` **10.0.9**, `OpenTelemetry.Extensions.Hosting` /
`OpenTelemetry.Instrumentation.AspNetCore` / `.Http` / `.SqlClient` **1.16.0**,
`Azure.Monitor.OpenTelemetry.AspNetCore` 1.5.0. No FluentValidation, GreenDonut, or
`Microsoft.Extensions.Validation` package is pinned today. The local EF Core checkout at
`C:\Users\ahmad\workspace\efcore` is on `release/10.0` at `v10.0.9-58-g9d1b795935` (2026-06-09) and was
used to read EF internals.

---

## 1. Validation: .NET 10 Minimal API validation vs FluentValidation for bulk entity validation

### 1.1 The built-in validation (`Microsoft.Extensions.Validation`)

**Package and version.** [Verified] `Microsoft.Extensions.Validation` latest stable is **10.0.11, released
2026-08-11** (10.0.10 on 2026-07-14; 11.0.0-preview series exists); targets `net10.0`; depends only on
`Microsoft.Extensions.DependencyInjection.Abstractions` and `Microsoft.Extensions.Options`. It ships in-box
with ASP.NET Core 10; a plain `Microsoft.NET.Sdk` class library must reference the package explicitly.
Sources: https://www.nuget.org/packages/Microsoft.Extensions.Validation ;
https://learn.microsoft.com/en-us/aspnet/core/fundamentals/validation?view=aspnetcore-10.0 (doc dated
2026-08-14).

**Mechanism.** [Verified] `builder.Services.AddValidation()` enables it; "the implementation automatically
discovers types that are defined in handlers or as base types of the types defined in handlers. An endpoint
filter performs validation on these types and is added for each endpoint." It is a **Roslyn source
generator** (AOT-friendly, no reflection) that "only discovers validatable types in the assembly where
`AddValidation` is called"; endpoints defined in another assembly must call `AddValidation` from an
extension method in that assembly or "invalid requests are processed and return a `200 - OK`". The API "isn't
supported for MVC or Razor Pages". Types the generator cannot see statically are opted in with
`[ValidatableType]`; `[SkipValidation]` skips a parameter/type/property; `.DisableValidation()` disables it
per endpoint. Sources: validation doc above;
https://learn.microsoft.com/en-us/aspnet/core/release-notes/aspnetcore-10.0?view=aspnetcore-10.0.

**Experimental surface in .NET 10.** [Verified] `[ValidatableType]` and `[SkipValidation]` "are published as
*experimental* in .NET 10" (analyzer warning `ASP0029` in plain class libraries; the doc gives three
suppression options or an embedded-attribute workaround); "As of .NET 11, the attributes are no longer
experimental." The release notes add: "the underlying validation resolver APIs are now marked as
experimental. The top-level `AddValidation` APIs and the built-in validation filter remain stable."
Source: validation doc; release notes.

**What it validates and in which order.** [Verified] Parameter validation first (attributes on the
parameter; if the parameter is `IEnumerable`, each non-null element is type-validated), then type validation:
"1. Validate properties on the type. If any errors are found, the validation process stops. 2. Validate
type-level `ValidationAttribute` instances. If any errors are found, the validation process stops.
3. Validate `IValidatableObject` implementations." Property validation: attributes on the property, then
"If the property value is `IEnumerable`, perform type validation for all non-`null` elements." So
**`IValidatableObject` is supported but only runs when no property-level or type-attribute error exists**.
Source: validation doc. The `release/10.0` source confirms the early returns: "If any property-level
validation errors were found, return early" and "If any type-level attribute errors were found, return
early" (https://github.com/dotnet/aspnetcore/blob/release/10.0/src/Validation/src/ValidatableTypeInfo.cs).

**Error key (property path) format.** [Verified from source, `release/10.0`] Nested members are joined
with `.` and collection elements get `[index]`:
`context.CurrentValidationPath = $"{originalPrefix}.{Name}"` and, for enumerable properties,
`context.CurrentValidationPath = $"{currentPrefix}[{index}]"` — i.e. a child error is keyed
**`Lines[3].Quantity`**. `IValidatableObject` results are keyed `$"{errorPrefix}.{memberName}"`. The key uses
the **CLR property `Name`**; `[Display]` only feeds the message. Source:
https://github.com/dotnet/aspnetcore/blob/release/10.0/src/Validation/src/ValidatablePropertyInfo.cs ;
ValidatableTypeInfo.cs (above).

**JSON naming gap.** [Verified] dotnet/aspnetcore issue #61764 "Respect JsonSerializerOptions casing for
property names in validation errors" is **open, milestone ".NET 12 Planning"**: keys are PascalCase CLR names
regardless of the configured naming policy or `[JsonPropertyName]`.
Source: https://github.com/dotnet/aspnetcore/issues/61764.

**Depth and cycles.** [Verified] `ValidationOptions.MaxDepth` defaults to **32** ("A maximum depth prevents
stack overflows from circular references or extremely deep object graphs"); exceeding it throws
`InvalidOperationException("Maximum validation depth of {MaxDepth} exceeded...")`.
Sources: https://github.com/dotnet/aspnetcore/blob/main/src/Validation/src/ValidationOptions.cs ;
ValidatableTypeInfo.cs.

**Async.** [Verified] `IAsyncValidatableObject` / `AsyncValidationAttribute` exist **only from .NET 11**
("For Minimal API validation, Microsoft.Extensions.Validation always calls the asynchronous path"). In
.NET 10 the built-in validation is synchronous and has no hook for pre-loaded context beyond
`ValidationContext.GetService<T>()`. Source: validation doc (moniker `aspnetcore-11.0` section).

**Error response shape.** [Verified] "If validation fails, the runtime returns a 400 Bad Request response
with details of the validation errors" and "Error responses from the validation logic for Minimal APIs can
now be customized by an `IProblemDetailsService` implementation." The payload type is
`HttpValidationProblemDetails : ProblemDetails` with `Errors : IDictionary<string, string[]>` (JSON member
`errors`), also produced by `TypedResults.ValidationProblem(...)`. Sources: release notes;
https://learn.microsoft.com/en-us/dotnet/api/microsoft.aspnetcore.http.httpvalidationproblemdetails?view=aspnetcore-10.0.

**Problem Details standard.** [Verified] RFC 9457 (July 2023) **obsoletes RFC 7807**, same JSON shape
(`type`, `status`, `title`, `detail`, `instance`, media type `application/problem+json`), adds a problem-type
registry, and says "Clients consuming problem details MUST ignore any such extensions that they don't
recognize". ASP.NET Core's `AddProblemDetails()` / `IProblemDetailsService` implement it (the docs cite 7807
on the Minimal API tab and 9457 on the controllers tab; the type is the same).
Sources: https://www.rfc-editor.org/rfc/rfc9457.html ;
https://learn.microsoft.com/en-us/aspnet/core/fundamentals/error-handling-api?view=aspnetcore-10.0.

**Limits for batch / child-collection validation.** [Inferred from the verified facts above]
1. It is an **endpoint filter over handler parameters**: it runs before the service layer, has no access to
   loaded DB context, cannot be invoked in bulk from an import path, and stops at the first failing level
   per object (property errors suppress `IValidatableObject`). It cannot express cross-row rules
   (duplicate codes within the batch, parent/child consistency) without an `IValidatableObject` on the
   *array wrapper*, which then only runs when every element is attribute-clean.
2. The path grammar (`Root[3].Lines[1].Quantity`) is the right shape and matches RFC 9457's `errors`
   extension as ASP.NET Core emits it; it uses CLR names, so the client must map PascalCase keys (or the
   server must rewrite keys with the JSON naming policy until #61764 lands).
3. The attribute types (`[Required]`, `[MaxLength]`, `[Range]`) are BCL DataAnnotations and are already
   allowed in `Tellma.Core.Abstractions`; the *runtime* (`Microsoft.Extensions.Validation`) is
   framework code and cannot be referenced from Abstractions.

### 1.2 FluentValidation

**Version and status.** [Verified] Latest stable **12.1.1, released 2025-12-03** (12.1.0 on 2025-11-03;
12.0.0 on 2025-05-05; the 11.x line still received 11.12.0 on 2025-11-03). Apache-2.0. Package targets
`net8.0` (NuGet lists computed compatibility with net9.0/net10.0); 12.0 "Drops support for netstandard2.0,
netstandard2.1, .net 5, .net 6 and .net 7. Minimum supported platform is now .net 8." No release in 2026 as
of today. Sources: https://www.nuget.org/packages/FluentValidation ;
https://github.com/FluentValidation/FluentValidation/releases.

**ASP.NET Core integration.** [Verified] Automatic MVC-pipeline validation is legacy: "We no longer recommend
using this approach for new projects but it is still available for legacy implementations" (reasons: "not
asynchronous", "MVC-only", "harder to debug"). Recommended: inject the validator and call it
("With manual validation, you inject the validator into your controller (or api endpoint), invoke the
validator and act upon the result"); for Minimal APIs, endpoint filters via third-party packages are the
alternative. Source: https://docs.fluentvalidation.net/en/latest/aspnet.html.

**Collection paths.** [Verified from source] `RuleForEach(x => x.Lines).SetValidator(...)` builds the
property name through `PropertyChain.AddIndexer`, which appends `"[" + indexer + "]"` to the last member;
members are joined with `ValidatorOptions.Global.PropertyChainSeparator` (default `.`), so failures are
keyed **`Lines[3].Quantity`** — the same grammar as the built-in validation. A custom `IndexBuilder`
delegate can override the indexer; `{CollectionIndex}` is available in messages.
Sources: https://github.com/FluentValidation/FluentValidation/blob/main/src/FluentValidation/Internal/PropertyChain.cs ;
https://github.com/FluentValidation/FluentValidation/blob/main/src/FluentValidation/Internal/CollectionPropertyRule.cs ;
https://docs.fluentvalidation.net/en/latest/collections.html.

**Async and context.** [Verified] `MustAsync`/`CustomAsync`/`WhenAsync` exist; "Calling ValidateAsync will
run both synchronous and asynchronous rules"; calling `Validate` on a validator with async rules throws.
Arbitrary pre-loaded data can be passed via `ValidationContext<T>.RootContextData["key"]` and read in
`Custom(...)` rules; `PreValidate` can abort early. There is **no batching primitive**: each async rule is
awaited where it is declared; nothing coalesces the I/O of several rules or several instances into one
call. Sources: https://docs.fluentvalidation.net/en/latest/async.html ;
https://docs.fluentvalidation.net/en/latest/advanced.html.

### 1.3 Guidance for bulk entity validation with `Lines[3].Quantity` paths

[Inferred]
- Neither library does what the pipeline needs — validate N top-level entities plus their children in
  memory **after** one batched, deduplicated context load, and report every error (not first-level-only)
  with a stable path. Both are per-instance, I/O-agnostic engines. The right split is: **own the
  orchestration** (a `ValidationContext`-like accumulator keyed by path, a two-phase "declare context
  requests → execute one batch → run rules" loop) and **reuse the two conventions everybody already
  understands**: BCL DataAnnotations attributes for per-property shape rules (they live in Abstractions
  today and are what the built-in validator and the UI both read) and the `Root[i].Child[j].Property`
  path grammar shared by ASP.NET Core and FluentValidation.
- Run attribute validation in the pipeline yourself with the BCL `System.ComponentModel.DataAnnotations.
  Validator.TryValidateObject(instance, ctx, results, validateAllProperties: true)` per entity (it is in
  the BCL, framework-free, and evaluates all properties, unlike the endpoint filter's early return), and
  disable the endpoint filter on the save endpoints (`.DisableValidation()`) so validation happens once,
  in bulk, with context. Keep `AddValidation()` for non-entity request DTOs if wanted.
- Surface results as `HttpValidationProblemDetails` (RFC 9457) with `errors["Lines[3].Quantity"]` and a
  422 status for field errors — the type is already in ASP.NET Core, the client library can consume it
  without a custom envelope, and the shape survives the .NET 11 upgrade. Decide the key casing
  deliberately (issue #61764 says the framework will stay PascalCase until at least .NET 12).
- Do not take a FluentValidation dependency for Core: it adds a second attribute-free rule language that
  distros would have to learn, its async rules cannot be batched, and its only unique asset (the
  fluent DSL) is not needed when rules are C# methods over a loaded context. Its `RootContextData` idea
  (pre-loaded context handed to rules) is the pattern to copy.

---

## 2. Transactions with Microsoft.Data.SqlClient on .NET 10

### 2.1 Driver versions and support

[Verified] Support lifecycle (doc dated 2026-03-17, updated 2026-08-27):

| Version | Released | Latest patch | Patch date | Support | End of support |
|---|---|---|---|---|---|
| 7.0 | 2026-03-17 | 7.0.2 | 2026-06-25 (release notes say 2026-06-24) | STS | — |
| 6.1 | 2025-08-14 | 6.1.6 | listed as "June 25, 2025" (a typo; the 7.0.2/6.1.6 announcement is June 2026) | LTS | 2028-08-14 |
| 6.0 | 2025-01-09 | 6.0.5 | 2026-01-16 | STS | ended 2026-02-14 |

STS releases are supported "three months after a subsequent STS or LTS release"; LTS three years. 7.0.0
(2026-03-17) targets .NET Framework 4.6.2+ and **.NET 8.0+**, adds .NET 10 SDK support, decouples
`Azure.Core`/`Azure.Identity` into `Microsoft.Data.SqlClient.Extensions.Azure` 1.0.0 (Entra auth moved
there), adds pluggable SSPI, Hyperscale read-replica routing, and `SqlConfigurableRetryFactory.
BaselineTransientErrors`; 7.0.2 is security hardening (TDS length bounds checks) plus fixes. No
transaction, `SqlBatch`, or tracing changes in 7.0.x. The repo pins **6.1.1** (LTS line; 6.1.6 is the
current patch). Sources:
https://learn.microsoft.com/en-us/sql/connect/ado-net/sqlclient-driver-support-lifecycle?view=sql-server-ver17 ;
https://github.com/dotnet/SqlClient/blob/main/release-notes/7.0/7.0.0.md ;
https://github.com/dotnet/SqlClient/blob/main/release-notes/7.0/7.0.2.md ;
https://techcommunity.microsoft.com/blog/SQLServer/announcing-microsoft-data-sqlclient-7-0-2-and-6-1-6/4531075.

Implication: staying on 6.1.x is safe until 2028; moving to 7.0 is a dependency reshuffle (Azure auth
package) not a behaviour change for the batch executor.

### 2.2 `SqlBatch` and `SqlTransaction`

[Verified] `SqlBatch : System.Data.Common.DbBatch` (SqlClient 5.2+) has constructor
`SqlBatch(SqlConnection, SqlTransaction)` and property `Transaction` — "Gets or sets the `SqlTransaction`
within which the `SqlBatch` commands execute" — plus `ExecuteReaderAsync`, `ExecuteNonQueryAsync`,
`ExecuteScalarAsync`, `Timeout`, `Cancel`, `Prepare`. **So a `SqlBatch` does participate in an explicit
`SqlTransaction`.** The results of a batch are read as successive result sets with `NextResult()` (the doc
example loops `do { while (reader.Read()) ... } while (reader.NextResult());`). The property list has **no
`RetryLogicProvider`** (it exists on `SqlConnection` and `SqlCommand` only).
Source: https://learn.microsoft.com/en-us/dotnet/api/microsoft.data.sqlclient.sqlbatch?view=sqlclient-dotnet-core-7.0 ;
https://learn.microsoft.com/en-us/dotnet/api/microsoft.data.sqlclient.sqlconnection?view=sqlclient-dotnet-core-7.0.

Implication: `SqlBatch` and concatenated text with `NextResult()` are equivalent for transaction scoping;
the difference is per-command parameters/metadata (and, unverified, whether the OpenTelemetry
instrumentation sees batch executions — see §5.2).

### 2.3 `TransactionScope` with async, versus explicit `SqlTransaction`

[Verified]
- `TransactionScopeAsyncFlowOption.Suppress` (0) "is the default setting if no
  `TransactionScopeAsyncFlowOption` is specified"; `Enabled` (1) "Specifies that transaction flow across
  thread continuations is enabled". A scope that spans `await` **must** pass `Enabled`.
  Source: https://learn.microsoft.com/en-us/dotnet/api/system.transactions.transactionscopeasyncflowoption?view=net-10.0.
- `TransactionScope` "creates a transaction with an IsolationLevel of `Serializable` by default" and its
  default timeout is one minute; a `SqlConnection` opened inside an active scope auto-enlists
  (`Enlist=true` default) as a lightweight local transaction; "When the second connection is opened, the
  transaction is automatically promoted to a full distributed transaction" (even against the same server).
  Source: https://learn.microsoft.com/en-us/dotnet/framework/data/adonet/system-transactions-integration-with-sql-server.
- `SqlConnection.BeginTransaction(IsolationLevel)` / `SqlTransaction.Commit()` / `Rollback()` / `Save()`
  are the explicit API. `SqlConnection` lists no `BeginTransactionAsync` override and `SqlTransaction`
  lists no `CommitAsync`/`RollbackAsync` override; the inherited `DbConnection.BeginDbTransactionAsync`
  "default implementation of this asynchronous method delegates to its synchronous counterpart and returns
  a completed Task, potentially blocking the calling thread". Sources: SqlConnection / SqlTransaction API
  pages above; https://learn.microsoft.com/en-us/dotnet/api/system.data.common.dbconnection.begindbtransactionasync?view=net-10.0.
  [Inferred] Begin/commit are short TDS exchanges, so the sync delegation is a latency detail rather than a
  scalability problem, but each is an extra round trip unless folded into the batch text (§2.5).
- SqlClient's built-in retry "skip[s] retry when a command runs inside an ambient `TransactionScope` or has
  a `SqlTransaction` attached" (§3.1) — both transaction styles disable driver-level retry equally.

Implication: prefer an explicit `SqlTransaction` (or T-SQL transaction control, §2.5) over
`TransactionScope`: the scope's defaults (Serializable, 1 minute, async flow off) are all wrong for this
pipeline, and a single stray second connection silently attempts DTC promotion, which fails on Linux.

### 2.4 Distributed transactions are unavailable cross-platform

[Verified] `TransactionManager.ImplicitDistributedTransactions` (static, default `false`) gates any
escalation: "`true` if transactions APIs are opted into distributed transaction; `false` if a
`NotSupportedException` is thrown when transactions APIs escalate to a distributed transaction." Its
setter is annotated `[SupportedOSPlatform("windows")]`. The proposal to enable it on all platforms
(dotnet/runtime #113805) was closed as a duplicate of #71769; the opt-in was introduced by dotnet/runtime
PR #76376 ("Require global opt-in for distributed transactions"). Sources:
https://learn.microsoft.com/en-us/dotnet/api/system.transactions.transactionmanager.implicitdistributedtransactions?view=net-10.0 ;
https://github.com/dotnet/runtime/issues/113805 ; https://github.com/dotnet/runtime/pull/76376.

Implication: the pipeline must never need two connections in one transaction (SaaS runs on Linux
containers). Blob writes, email, and identity-server calls are non-transactional side effects by
construction; a per-tenant DB with all writes in one `SqlBatch` on one connection is the only supported
shape.

### 2.5 Transaction control inside the batch text (`SET XACT_ABORT ON; BEGIN TRAN ... COMMIT`)

[Verified] "When SET XACT_ABORT is ON, if a Transact-SQL statement raises a run-time error, the entire
transaction is terminated and rolled back. When SET XACT_ABORT is OFF, in some cases only the
Transact-SQL statement that raised the error is rolled back and the transaction continues processing."
"OFF is the default setting in a T-SQL statement"; "Compile errors, such as syntax errors, are not
affected"; "The THROW statement honors SET XACT_ABORT. RAISERROR does not." Source:
https://learn.microsoft.com/en-us/sql/t-sql/statements/set-xact-abort-transact-sql?view=sql-server-ver17.

[Inferred] Emitting `SET XACT_ABORT ON; BEGIN TRAN; ... ; COMMIT;` as the first/last statements of the
persist batch makes the whole save one round trip with no client-side transaction object, keeps the
driver's retry eligibility (no `SqlTransaction` attached), and guarantees that any runtime error rolls
back everything (a PK/unique violation included). The cost is that the "RLS post-check then rollback"
step must be expressed in T-SQL (`IF EXISTS (...) THROW 50000, ...`) or the batch must be split at that
point into a client-controlled `SqlTransaction`; and the unknown-commit window (§3.4) still exists.

---

## 3. Transient-fault retry

### 3.1 SqlClient configurable retry logic

[Verified] (docs dated 2026-08-14)
- Off by default: "Configurable retry logic is off by default. Assign a provider to
  `SqlConnection.RetryLogicProvider` or `SqlCommand.RetryLogicProvider` to enable it for that object." No
  AppContext switch is mentioned any more. Factories: `SqlConfigurableRetryFactory.CreateFixedRetryProvider`,
  `CreateIncrementalRetryProvider`, `CreateExponentialRetryProvider`, `CreateNoneRetryProvider` (default).
  Options: `SqlRetryLogicOption { NumberOfTries (1..60, counts the first attempt), DeltaTime,
  MinTimeInterval, MaxTimeInterval (caps each delay, not the total), TransientErrors, AuthorizedSqlCondition }`;
  fixed/incremental/exponential providers add random jitter. Extension points: `SqlRetryIntervalBaseEnumerator`,
  `SqlRetryLogicBase`, `SqlRetryLogicBaseProvider` (`Execute`, `ExecuteAsync`, `Retrying` event).
- **Default transient list** (`TransientErrors == null`): the 20 numbers in
  `SqlConfigurableRetryFactory.BaselineTransientErrors` — login transport `233, 997, 10060`; database
  availability during login `4060, 4221`; statement-level `1204, 1205, 1222`; resource limit/throttling
  `10928, 10929, 40501, 49918, 49919, 49920`; Azure SQL failover `40143, 40197, 40540, 40613`; dedicated
  SQL pool `42108, 42109`. "Setting `TransientErrors` replaces the built-in list. It doesn't append."
  `BaselineTransientErrors` is public **in 7.0** ("For earlier driver versions, create an
  application-owned collection"). The doc also lists `64, 10053, 10054` as worth adding and `3960`
  (snapshot update conflict) "if your application uses snapshot isolation"; client-side timeout (`-2`) is
  not in the list.
- **Transactions:** "The built-in providers skip retry when a command runs inside an ambient
  `TransactionScope` or has a `SqlTransaction` attached. The command runs once without retry logic.
  Retrying a single statement inside a transaction can duplicate earlier work or violate the transaction's
  intended ordering." "For deadlocks and other retryable failures inside a transaction, roll back and
  retry the entire transaction as one unit."
- `AuthorizedSqlCondition` receives the command text; if it returns `false` "the command runs once without
  retry logic" — the documented way to limit retries to statements the app can safely repeat.
Sources: https://learn.microsoft.com/en-us/sql/connect/ado-net/internal-retry-logic-providers-sqlclient?view=sql-server-ver17 ;
https://learn.microsoft.com/en-us/sql/connect/ado-net/configurable-retry-logic-sqlclient-introduction?view=sql-server-ver17 ;
https://learn.microsoft.com/en-us/sql/connect/ado-net/configurable-retry-logic-core-apis-sqlclient?view=sql-server-ver17.

### 3.2 EF Core `EnableRetryOnFailure`

[Verified from the local `release/10.0` source and the doc]
- Defaults: `ExecutionStrategy.DefaultMaxRetryCount = 6`, `DefaultMaxDelay = 30 s`, exponential base 2,
  coefficient 1 s, random factor 1.1 (`src/EFCore/Storage/ExecutionStrategy.cs`).
  `SqlServerRetryingExecutionStrategy.ShouldRetryOn` = `_additionalErrorNumbers` (from
  `EnableRetryOnFailure(errorNumbersToAdd)`) **or** `SqlServerTransientExceptionDetector.ShouldRetryOn`,
  which matches ~190 `SqlException` numbers (including `1204, 1205, 1222, 4060, 40197, 40501, 40613, 10928,
  10929, 10053, 10054, 10060, 233, 64, 20, 121, 3960, 3966, 41839, 49918-49920`), a `Win32Exception`
  inner error 203 case, and returns `ex is TimeoutException` otherwise. Source:
  https://github.com/dotnet/efcore/blob/release/10.0/src/EFCore.SqlServer/Storage/Internal/SqlServerTransientExceptionDetector.cs
  (read locally at v10.0.9-58).
- Semantics: "each query and each call to `SaveChangesAsync()` will be retried as a unit"; a
  `BeginTransactionAsync()` outside `CreateExecutionStrategy().ExecuteAsync(...)` throws
  "`InvalidOperationException: The configured execution strategy 'SqlServerRetryingExecutionStrategy' does
  not support user-initiated transactions...`"; "Enabling retry on failure causes EF to internally buffer
  the resultset".
- **Commit ambiguity:** "if the connection is dropped while the transaction is being committed the
  resulting state of the transaction is unknown. By default, the execution strategy will retry the
  operation as if the transaction was rolled back, but if it's not the case this will result in an
  exception if the new database state is incompatible or could lead to data corruption if the operation
  does not rely on a particular state, for example when inserting a new row with auto-generated key
  values." Options: do nothing but "avoid using store-generated keys in order to ensure that an exception
  is thrown instead of adding a duplicate row"; rebuild state; `ExecuteInTransactionAsync(operation,
  verifySucceeded)` where `verifySucceeded` "is invoked when a transient error occurs during the
  transaction commit"; or a transaction-tracking row inserted at the start and checked after a failed
  commit. Source: https://learn.microsoft.com/en-us/ef/core/miscellaneous/connection-resiliency.

### 3.3 Azure SQL guidance

[Verified] "Don't directly retry a `SELECT` statement that failed with a transient error. Instead, establish
a fresh connection, and then retry the `SELECT`." "When an `UPDATE` statement fails with a transient error,
establish a fresh connection before you retry the `UPDATE`. The retry logic must ensure that either the
entire database transaction finished or that the entire transaction is rolled back." "We recommend that
you wait for 5 seconds before your first retry ... For each subsequent retry, the delay should grow
exponentially, up to a maximum of 60 seconds." Connection-string `ConnectRetryCount` (default 1) /
`ConnectRetryInterval` (default 10 s) cover open/idle-connection resiliency only and compound with
application retries. Source:
https://learn.microsoft.com/en-us/azure/azure-sql/database/troubleshoot-common-connectivity-issues?view=azuresql.

### 3.4 Guidance for retrying batches that contain non-idempotent statements

[Inferred from §3.1-3.3 and §2]
1. Driver-level retry is inert for anything transactional and cannot see the per-statement `MayRetry`
   flag; EF's strategy is irrelevant to a raw `SqlBatch` executor. **The batch executor owns retry**: it
   re-runs the whole batch, never a statement, and only when the batch is retry-eligible.
2. Eligibility rules that follow from the facts: (a) a read-only batch is always eligible; (b) a batch that
   is one T-SQL transaction with `XACT_ABORT ON` is eligible for errors raised **before commit** (deadlock
   1205, lock timeout 1222, 3960, throttling) because nothing was applied; (c) the only ambiguous case is a
   connection loss **during/after commit** (commit acknowledged or not) — for that case app-assigned ids
   are the friend EF's doc asks for: a retry of the same INSERTs collides on the PK (2627) instead of
   duplicating rows, and a cheap `verifySucceeded` probe (`SELECT Id, ModifiedAt FROM ... WHERE Id IN
   (@ids)` compared with the stamp the batch wrote) settles it; (d) statements that reserve ids or bump
   tags are idempotent by design and can carry `MayRetry = true`; a batch with any `MayRetry = false`
   statement is retried only under rule (b)/(c), never for a mid-stream failure after results started
   flowing.
3. Use SqlClient's baseline error list plus `3960` (and, for lease statements, `1205`) as the transient
   set; take it from `SqlConfigurableRetryFactory.BaselineTransientErrors` once on 7.0, or copy the 20
   numbers with a test that pins them until then. Give the executor its own bounded exponential-with-jitter
   schedule (Azure's "5 s first, 60 s cap" is tuned for outages; for a UI save, 3-4 attempts within a few
   seconds and then surface the error is the realistic budget — a judgment call for the designers).

---

## 4. The DataLoader pattern as a reference for the validation-context loader

### 4.1 Facebook DataLoader semantics (the reference contract)

[Verified] "DataLoader will coalesce all individual loads which occur within a single frame of execution
(a single tick of the event loop) before calling your batch function." A custom `batchScheduleFn`
"receives a callback ... and must call that callback in the immediate future to execute the batch request"
(manual dispatch is a first-class variant). Batch function contract: "The Array of values must be the same
length as the Array of keys"; "Each index in the Array of values must correspond to the same index in the
Array of keys"; return an `Error` instance for a missing value rather than omitting it, and "If a batch
function returns an Error instance for an individual value, that Error will be cached." Caching is
per-request memoization by key ("DataLoader caching does not replace Redis, Memcache, or any other shared
application-level cache"); `clear(key)`, `clearAll()`, `prime(key, value)`, `cacheKeyFn`, `cacheMap`;
`maxBatchSize` (1 disables batching); `loadMany` resolves to "either a value or an Error instance" per key.
Source: https://github.com/graphql/dataloader/blob/main/README.md.

### 4.2 GreenDonut (the .NET port)

[Verified]
- Latest stable **GreenDonut 16.6.2, released 2026-08-28** (16.7.0-p.2 prerelease 2026-09-01); MIT; targets
  net8.0/9.0/10.0/11.0; depends on `GreenDonut.Abstractions`, `Microsoft.Extensions.DependencyInjection.
  Abstractions`, `Microsoft.Extensions.ObjectPool`; no Hot Chocolate dependency. Source:
  https://www.nuget.org/packages/GreenDonut.
- API: `abstract class BatchDataLoader<TKey, TValue> : DataLoaderBase<TKey, TValue>, IBatchDataLoader<TKey,
  TValue> where TKey : notnull`, ctor `(IBatchScheduler batchScheduler, DataLoaderOptions options)`,
  `protected abstract Task<IReadOnlyDictionary<TKey, TValue>> LoadBatchAsync(IReadOnlyList<TKey> keys,
  CancellationToken)`; keys missing from the dictionary resolve to `null` (no exception).
  `GroupedDataLoader` (one-to-many, `ILookup`), `CacheDataLoader` (per-key fetch, per-request cache), and a
  `[DataLoader]` source-generated attribute form. `DataLoaderOptions { MaxBatchSize = 1024 (0 = unbounded),
  Cache : IPromiseCache?, DiagnosticEvents }`. Scheduling: `LoadAsync` adds the key to the current batch and
  calls `IBatchScheduler.Schedule` **when a new batch is created** (first key, or when the previous batch
  reached `MaxBatchSize`); dedup is via the promise cache (`Cache.GetOrAddTask(cacheKey, _ =>
  CreatePromise())`); dispatch calls `FetchAsync(batch.Keys, buffer, context, ct)`. The standalone
  `AutoBatchScheduler` "immediately dispatches batches without coordination or batching optimization"
  (`Task.Run(() => batch.DispatchAsync())`) — Hot Chocolate's own scheduler is what delays dispatch until
  "no more resolver work is immediately ready". DI: `AddDataLoader<T>()` (scoped) registers
  `IBatchScheduler → AutoBatchScheduler` (scoped), `DataLoaderOptions` (scoped), `IDataLoaderScope`,
  plus singleton registrar/factory. Sources:
  https://github.com/ChilliCream/graphql-platform/tree/main/src/GreenDonut/src/GreenDonut
  (`BatchDataLoader.cs`, `DataLoaderBase.cs`, `DataLoaderOptions.cs`, `AutoBatchScheduler.cs`,
  `DependencyInjection/DataLoaderServiceCollectionExtensions.cs`);
  https://chillicream.com/docs/hotchocolate/v15/fetching-data/dataloader.

### 4.3 Implication for the validation-context loader

[Inferred]
- Copy the **contract**, not the library: per-request instance; `Load(key)` returns a promise; keys are
  deduplicated by a structural cache key; the batch function receives the distinct keys and must answer
  every key (missing → explicit "not found", which is itself cached); `MaxBatchSize`; `Prime` so that the
  entities already in the save payload or the connect call seed the cache; explicit `Dispatch()` (Facebook's
  `batchScheduleFn`, not GreenDonut's auto scheduler) because the pipeline knows exactly when a round of
  validators has finished declaring requests.
- The one thing neither library provides is the thing the brain dump asks for: **many loaders' fetches
  folded into one round trip**. In both, each loader's batch function performs its own I/O. The Tellma
  loader therefore needs a second batching level: every "context request" (a Queryex query, a raw SQL
  reader, an ids-through-TVP lookup) is appended to the same batch builder, executed once, and the readers
  are routed back to the requesting loader by ordinal — i.e. the DataLoader sits *on top of* the T2 batch
  abstraction, and the dedup key must include the statement identity (query text + parameter values +
  Select), which is why requests that differ only by `Select` should be canonicalized (union the selects)
  rather than deduplicated.
- Bound and meter the rounds: DataLoader's "one tick per batch" becomes "one DB round trip per validation
  round"; the pipeline caps rounds (e.g. 3) and records the count (§5.4) so an O(n) validator is a metric,
  not a mystery.

---

## 5. OpenTelemetry database semantic conventions and the SqlClient instrumentation

### 5.1 The conventions

[Verified]
- Status: database spans and the `db.client.operation.duration` metric are **Stable**; connection-pool
  metrics are still Development. The core DB conventions were declared stable in **semconv v1.33.0
  (2025-05-02)**: "Mark database semantic conventions as stable for MariaDB, Microsoft SQL Server, MySQL,
  and PostgreSQL". Latest release is **v1.44.0 (2026-08-04)** (v1.43.0 2026-07-03 clarified
  `db.operation.batch.size` "including multi-operand operations, parameterized operation batch APIs, and
  empty batches"). Sources: https://opentelemetry.io/docs/specs/semconv/db/database-spans/ ;
  https://opentelemetry.io/docs/specs/semconv/db/database-metrics/ ;
  https://github.com/open-telemetry/semantic-conventions/releases (tags v1.33.0, v1.44.0).
- Span attributes: `db.system.name` (Required; value `microsoft.sql_server`), `db.namespace`,
  `db.collection.name`, `db.operation.name` (Conditionally Required), `db.query.text` (Recommended;
  parameterized text captured by default, non-parameterized only sanitized with literals replaced by `?`),
  `db.query.summary` (Recommended; low cardinality, max 255 chars), `db.query.parameter.<key>` (Opt-In),
  `db.operation.batch.size` (Recommended for batches of 2+), `db.response.status_code`,
  `db.response.returned_rows`, `db.stored_procedure.name`, `error.type` (Conditionally Required on failure),
  `server.address`/`server.port`. Span name: `{db.query.summary}`, else `{db.operation.name} {target}`, else
  `{target}`, else `{db.system.name}`. Span kind CLIENT; duration "covers full logical operation including
  retries".
- Metric `db.client.operation.duration`: Histogram, unit `s`, Required `db.system.name`, Conditionally
  Required `db.collection.name`, `db.namespace`, `db.operation.name`, `db.response.status_code`,
  `error.type`, `server.port`; Recommended `db.query.summary`, `server.address`; Opt-In `db.query.text`;
  advisory buckets `[0.001, 0.005, 0.01, 0.05, 0.1, 0.5, 1, 5, 10]`.

### 5.2 `OpenTelemetry.Instrumentation.SqlClient` conformance

[Verified]
- Versions: **1.18.0 (2026-08-21)** is the latest; the package went **stable (non-beta) at 1.15.0
  (2026-01-28)**; 1.16.0 (2026-06-24, the version pinned in this repo) added Native AOT support for .NET 8+;
  1.17.0 (2026-07-17) signed assemblies; 1.18.0 "Updated to Semantic Conventions v1.44.0", fixed query
  sanitization, added `db.response.returned_rows`. The `db.client.operation.duration` histogram was added in
  1.10.0-beta.1 (2024-12-09). `OTEL_SEMCONV_STABILITY_OPT_IN` (`database` / `database/dup`) was added in
  1.10.0-beta.1 and **removed in 1.14.0-rc.1 (2026-01-13)** "after the new database semantic conventions
  is marked stable" — the package now emits only the stable names. README: "This component is based on
  v1.44 of database semantic conventions"; "Stability: Stable". Sources:
  https://github.com/open-telemetry/opentelemetry-dotnet-contrib/blob/main/src/OpenTelemetry.Instrumentation.SqlClient/CHANGELOG.md ;
  https://github.com/open-telemetry/opentelemetry-dotnet-contrib/blob/main/src/OpenTelemetry.Instrumentation.SqlClient/README.md.
- What it emits: traces with `db.system.name`, `db.namespace`, `db.operation.name`, `db.query.summary`,
  `db.query.text`, `db.response.status_code`, `db.stored_procedure.name`, `server.address`, `server.port`,
  `error.type`; one metric, `db.client.operation.duration` (Histogram, `s`) with the same attributes,
  enabled by `AddSqlClientInstrumentation()` on the `MeterProviderBuilder`. Source constants:
  `db.system.name = "microsoft.sql_server"`, semantic-conventions version `1.44.0`, buckets as in §5.1;
  `ActivitySource`/`Meter` are created via the contrib shared factory from the instrumentation assembly
  (`ActivitySourceFactory.Create<SqlTelemetryHelper>(SemanticConventionsVersion)`,
  `Metrics.MeterFactory.Create<SqlTelemetryHelper>(...)`) — [Inferred] the resulting names are the assembly
  name `OpenTelemetry.Instrumentation.SqlClient`, which is what `AddSqlClientInstrumentation()` registers.
- Mechanism: on .NET it subscribes to the `DiagnosticSource` listener **`SqlClientDiagnosticListener`**
  (events `Microsoft.Data.SqlClient.WriteCommandBefore/After/Error`, plus the `System.Data.SqlClient`
  twins); on .NET Framework it uses an `EventSource` listener. Instrumentation "is not working with
  `Microsoft.Data.SqlClient` v3.*". Options (`.NET only`): `EnrichWithSqlCommand`, `Filter`,
  `RecordException` (default false); internal/opt-in `SetDbQueryParameters` (false), `RecordReturnedRows`
  (false), `EnableTraceContextPropagation` (false; sends `traceparent` to the server for `CommandType.Text`
  only). Sources: `SqlClientInstrumentation.cs`, `Implementation/SqlTelemetryHelper.cs`,
  `SqlClientTraceInstrumentationOptions.cs` in the contrib repo (paths under
  `src/OpenTelemetry.Instrumentation.SqlClient/`).
- **Unverified:** whether `SqlBatch.Execute*` raises the same `WriteCommandBefore/After` events (and thus
  gets a span and a duration sample) and whether `db.operation.batch.size` is ever set; the README does not
  list that attribute. If the executor adopts `SqlBatch`, test this on day one; if batches are invisible,
  concatenated text through `SqlCommand` is the observable option.

### 5.3 SqlClient's own OpenTelemetry tracing

[Verified] dotnet/SqlClient issue #2210 "Emit OpenTelemetry tracing" (opened 2023-11-07 by roji) is **open,
milestone 8.0.0**, backlog; SqlClient 7.0.x ships no native `ActivitySource` (7.0.0 only extended its
strongly-typed diagnostic events to .NET Framework). Source: https://github.com/dotnet/SqlClient/issues/2210 ;
7.0.0 release notes (§2.1).

Implication: the contrib package remains the only source of DB spans/metrics for the foreseeable future;
keep pinning it (bump 1.16.0 → 1.18.0 when convenient for `db.response.returned_rows`).

### 5.4 Per-request DB-call counters

[Verified building blocks]
- The request `Activity` is reachable via `HttpContext.Features.Get<IHttpActivityFeature>()?.Activity`
  ("Feature to access the Activity associated with a request"), on which `SetTag` works. Source:
  https://learn.microsoft.com/en-us/dotnet/api/microsoft.aspnetcore.http.features.ihttpactivityfeature?view=aspnetcore-10.0.
- `http.server.request.duration` "supports tag enrichment by using `IHttpMetricsTagsFeature`"
  (`tagsFeature.Tags.Add(new KeyValuePair<string, object?>("mkt_medium", source))`); "The feature is present
  on the context only if someone is listening to the metric"; "Tags that are too numerous or have an unbound
  range create many tag combinations, resulting in high dimensions." Custom meters come from `IMeterFactory`
  (`meterFactory.Create("Contoso.Web")`, `CreateCounter<int>("contoso.product.sold")`), testable with
  `MetricCollector<T>`. Source: https://learn.microsoft.com/en-us/aspnet/core/log-mon/metrics/metrics?view=aspnetcore-10.0.
- OTel instrument selection: use a Histogram when "you want to record or time something, and the statistics
  about this thing are likely to be meaningful"; a Counter when "the value is monotonically increasing".
  Source: https://opentelemetry.io/docs/specs/otel/metrics/supplementary-guidelines/.
- Repo rule (ARCHITECTURE.md, Observability): meters named after the package via `IMeterFactory`, instrument
  names `tellma.<area>.<name>` as `const`s in the `.Abstractions` package, durations in seconds, bounded tag
  sets, no per-tenant tags, alert queries under `infra/monitoring/` cross-checked by a test.

[Inferred pattern] A request-scoped `DbCallBudget` service (incremented by the batch executor per round
trip, with the elapsed time) and an endpoint filter/middleware that, at the end of the request, (1) records
`tellma.data.db_calls_per_request` (Histogram<int>, unit `{call}`) and `tellma.data.db_time_per_request`
(Histogram<double>, `s`) tagged with a low-cardinality operation identity (`tellma.operation` = the route
template or `<Entity>.<Operation>` closed set, never tenant or user), (2) sets `tellma.db.calls` /
`tellma.db.duration` tags on the request span so a trace shows the count without counting child spans, and
(3) optionally enriches `http.server.request.duration` with a **bucketed** tag (`tellma.db.calls.bucket` ∈
`1|2|3-5|6+`) so the standard dashboard can slice by round-trip count without cardinality blow-up. The
validation loader reports its round count into the same budget (a `tellma.data.validation_rounds`
histogram) so "O(1) with input cardinality" is an alert, not a code-review promise. `db.client.operation.
duration` from the SqlClient instrumentation stays the per-call view; the per-request histogram is the
per-operation view the brain dump asks for.

---

## 6. SQL Server row versioning when a batch reads then writes in one transaction

### 6.1 READ COMMITTED SNAPSHOT (RCSI) semantics and defaults

[Verified]
- "When the `READ_COMMITTED_SNAPSHOT` database option is set `ON`, which is the default setting in Azure SQL
  Database, the `READ COMMITTED` isolation level uses row versioning to provide statement-level read
  consistency. Read operations require only the schema stability (`Sch-S`) table level locks and no page or
  row locks ... the Database Engine uses row versioning to present each statement with a transactionally
  consistent snapshot of the data as it existed at the start of the statement." "Locks aren't used to
  protect the data from updates by other transactions." `READ_COMMITTED_SNAPSHOT` is OFF by default on
  SQL Server and ON by default on Azure SQL Database and SQL database in Fabric. Sources:
  https://learn.microsoft.com/en-us/sql/relational-databases/sql-server-transaction-locking-and-row-versioning-guide?view=sql-server-ver17 ;
  https://learn.microsoft.com/en-us/sql/t-sql/statements/set-transaction-isolation-level-transact-sql?view=sql-server-ver17.
- Writes are unaffected by the isolation level: "A transaction always holds an exclusive lock to perform
  data modification, and holds that lock until the transaction completes, regardless of the isolation level
  set for that transaction"; "an update made at the `READ COMMITTED` isolation level uses update locks on the
  data rows selected, whereas an update made at the `SNAPSHOT` isolation level uses row versions to select
  rows to update." Changing RCSI requires no other active connections to the database (relevant to the
  migrator on-prem). `READPAST` "can't be specified when the `READ_COMMITTED_SNAPSHOT` database option is set
  to `ON`" for a READ COMMITTED session unless `READCOMMITTEDLOCK` is also specified (relevant to T10's lease
  statements). Sources: locking guide; table hints page below.
- **Optimized locking** (TID locking + lock-after-qualification, LAQ) is "Yes (always enabled)" in Azure SQL
  Database, Fabric SQL database and Azure SQL MI (AUTD/2025 policies); "No (can be enabled per database)" on
  SQL Server 2025; unavailable on 2022 and older. With LAQ (requires RCSI), DML "predicates can be
  optimistically checked on the latest committed version of the row without taking any locks ... If the
  predicate is satisfied, an `X` row lock is taken to update the row", released "as soon as the row update
  is complete, before the end of the transaction"; a documented behaviour change follows ("Concurrent
  workloads under RCSI that rely on strict execution order of transactions might experience differences in
  query behavior"), with the advice "use stricter isolation levels such as `REPEATABLE READ` and
  `SERIALIZABLE`" or hints. LAQ is not used when `UPDLOCK`, `READCOMMITTEDLOCK`, `XLOCK` or `HOLDLOCK` hints
  are present, for `MERGE`, or for statements with an `OUTPUT` clause returning a result set. Source:
  https://learn.microsoft.com/en-us/sql/relational-databases/performance/optimized-locking?view=sql-server-ver17.

### 6.2 SNAPSHOT isolation: update conflicts, not write-skew protection

[Verified] Under `SNAPSHOT` "data read by any statement in a transaction is the transactionally consistent
version of the data that existed at the start of the transaction"; `ALLOW_SNAPSHOT_ISOLATION` must be ON;
"Snapshot isolation uses an optimistic concurrency model. If a snapshot transaction attempts to commit
modifications to data that has changed since the transaction began, the transaction will roll back and an
error will be raised" — error **3960**, severity 16: "Snapshot isolation transaction aborted due to update
conflict. You cannot use snapshot isolation to access table '%.*ls' directly or indirectly ... to update,
delete, or insert the row that has been modified or deleted by another transaction. Retry the transaction
or change the isolation level for the update/delete statement." "You can avoid this by using UPDLOCK hints
for SELECT statements that access data to be modified." The conflict check is per **row written**; the
documentation nowhere claims SNAPSHOT prevents write skew (two transactions reading overlapping data and
writing disjoint rows), and the isolation-level table lists only dirty/non-repeatable/phantom reads.
Sources: https://learn.microsoft.com/en-us/dotnet/framework/data/adonet/sql/snapshot-isolation-in-sql-server ;
https://learn.microsoft.com/en-us/sql/relational-databases/errors-events/database-engine-events-and-errors-3000-to-3999?view=sql-server-ver17 ;
locking guide.

[Inferred] A uniqueness check done in C# ("load rows with this Code; none found; insert") is exactly the
write-skew shape: under RCSI each `SELECT` sees the latest committed state at statement start, under
SNAPSHOT the state at transaction start, and in both cases two concurrent saves of the same new Code each
see "absent" and both insert without any row-level conflict (there is no row to conflict on). RCSI's
statement-level snapshot also means the validation `SELECT` and the persist `INSERT/UPDATE` in the same
transaction can straddle another commit; with LAQ enabled (all of Azure SQL) the `UPDATE` in the persist
batch may qualify rows on a newer committed version than the one validation read.

### 6.3 Standard mitigations

[Verified]
- **Unique index/constraint as the last line of defence.** Constraints are enforced by the engine on the
  write regardless of isolation level; a race surfaces as error **2601** ("Cannot insert duplicate key row
  in object '%.*ls' with unique index '%.*ls'. The duplicate key value is %ls.") for a unique index or
  **2627** ("Violation of %ls constraint '%.*ls'. Cannot insert duplicate key in object '%.*ls'. The
  duplicate key value is %ls.") for a PK/UNIQUE constraint, both severity 14. Source:
  https://learn.microsoft.com/en-us/sql/relational-databases/errors-events/database-engine-events-and-errors-2000-to-2999?view=sql-server-ver17.
- **`UPDLOCK, HOLDLOCK` range checks.** `UPDLOCK`: "update locks are to be taken and held until the
  transaction completes"; `HOLDLOCK`: "Equivalent to `SERIALIZABLE` ... holding [shared locks] until a
  transaction is completed"; `SERIALIZABLE` "Other transactions can't insert new rows with key values that
  would fall in the range of keys read by any statements in the current transaction until the current
  transaction completes." Key-range locks "prevent phantom insertions into a set of records accessed by a
  transaction". The locking guide's own recommendation for select-then-update is "'select a row with
  `UPDLOCK` hint, then update the row' pattern" to avoid the S→X conversion deadlock. Sources:
  https://learn.microsoft.com/en-us/sql/t-sql/queries/hints-transact-sql-table?view=sql-server-ver17 ;
  isolation-level page; locking guide. The classic write-up of the check-then-insert race and the
  `WITH (UPDLOCK, HOLDLOCK)` fix inside an explicit transaction is Dan Guzman, "Conditional INSERT/UPDATE
  Race Condition" (2007), https://weblogs.sqlteam.com/dang/2007/10/28/conditional-insertupdate-race-condition/
  *(secondary)*.
- **`MERGE` needs `HOLDLOCK` for upsert races.** "In some scenarios where unique keys are expected to be
  both inserted and updated by the `MERGE`, specifying the `HOLDLOCK` will prevent against unique key
  violations"; "At scale, `MERGE` might introduce complicated concurrency issues"; "When heavy concurrency
  is expected, separate `INSERT`, `UPDATE`, and `DELETE` logic might perform better, with less blocking, than
  a `MERGE` statement." Source: https://learn.microsoft.com/en-us/sql/t-sql/statements/merge-transact-sql?view=sql-server-ver17.

### 6.4 Implications for the save pipeline

[Inferred]
1. **Every uniqueness rule validated in C# must be backed by a unique index** on the same columns (filtered
   for soft-delete/tenant shapes as needed), and the persist step must translate 2601/2627 into the same
   field-level validation error the validator would have produced (parse the constraint/index name to the
   property path). This is the only correct mitigation that costs nothing in the common case; the C#
   check exists for the error message and the batch-internal duplicates, not for correctness.
2. Do not try to close the race with `UPDLOCK, HOLDLOCK` on validation-context reads by default: it
   serializes concurrent saves on the same key range, disables LAQ, and turns the "start the transaction
   late" question into "hold range locks across the validation round trips". Reserve hints for the few
   invariants a unique index cannot express (sequence-like counters, "exactly one default per parent"),
   and for those run the check **inside the persist batch** with `WITH (UPDLOCK, HOLDLOCK)` immediately
   before the write, in the same transaction, never in the validation phase.
3. Because RCSI is statement-scoped, the transaction boundary can safely start at the persist batch: nothing
   read earlier was protected anyway, so opening the transaction before validation buys no consistency and
   costs lock duration. The persist batch should re-read what it depends on (concurrency stamps, RLS
   post-check) inside the transaction.
4. Add `3960` to the transient set only if the pipeline ever uses `SNAPSHOT`; under RCSI the equivalent
   failures are 1205/1222, already in the baseline list. A deadlock inside the persist batch is a retry of
   the whole batch (§3.4), not a user error.
5. The optimistic-concurrency stamp check (`(Id, ExpectedStamp)` TVP compared inside the persist batch) is
   itself a row-level write conflict check, which row versioning does handle correctly: the `UPDATE ...
   WHERE Id = @Id AND ModifiedAt = @Expected` takes the X lock and qualifies on the latest committed
   version (LAQ or not), so a lost update is impossible; only insert-uniqueness needs the index.

---

## 7. Findings most likely to change a design decision (one line each)

- SqlClient's built-in retry is inert for any command with a `SqlTransaction` or ambient scope, so the batch
  executor must own retry (whole-batch, eligibility-gated), and `SqlBatch` has no `RetryLogicProvider`.
- Distributed transactions throw on Linux; one connection, one transaction, one batch is the only shape.
- `TransactionScope` defaults (Serializable, 60 s, async flow suppressed) are all wrong here; use
  `SqlTransaction` or T-SQL `SET XACT_ABORT ON; BEGIN TRAN ... COMMIT` inside the batch text.
- Uniqueness validated in C# is write-skew-prone under RCSI and SNAPSHOT alike; the unique index is the
  guarantee and 2601/2627 must map to field errors.
- Azure SQL always runs optimized locking (LAQ), which changes read-then-write ordering under RCSI.
- The .NET 10 validation is a per-parameter endpoint filter with early returns and PascalCase keys; keep its
  attributes and the `Lines[3].Quantity` path grammar and RFC 9457 `errors` shape, own the bulk engine.
- FluentValidation 12.1.1 offers no batching; GreenDonut 16.6.2 batches per loader, not across loaders.
- OpenTelemetry DB conventions are stable (v1.33.0); the SqlClient instrumentation is stable (1.15.0+) and
  emits only stable names; whether `SqlBatch` executions are instrumented is unverified.

## 8. Unverified or open items

- Whether `SqlBatch.Execute*` raises `SqlClientDiagnosticListener` command events (spans/metrics) and
  whether `db.operation.batch.size` is emitted.
- The exact `ActivitySource`/`Meter` name of the SqlClient instrumentation (inferred from the shared
  factory as the assembly name).
- `SqlConnection.BeginTransaction` / `SqlTransaction.Commit` are documented as synchronous only; the cost
  of the extra round trips was not measured.
- The 6.1.6 patch date in the lifecycle table ("June 25, 2025") conflicts with the June 2026 announcement;
  treat 2026-06-25 as correct.
- FluentValidation's docs do not state whether async rules on collection elements run sequentially or
  concurrently; the `CollectionPropertyRule` source loops and awaits per element (sequential).
