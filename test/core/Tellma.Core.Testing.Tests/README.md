# Tellma.Core.Testing.Tests

Tests for the shipped test package, and for the piece of the shared test plumbing whose silence the
leak suites rely on. Untested test infrastructure produces false green everywhere downstream, which
is the whole reason this project exists.

| Folder | Covers |
|---|---|
| `Database/` | The collation pin passing a model in the platform collation whose initial migration carries an annotation-only database operation, and naming a model in no collation, a model ahead of its migrations, and a migration that changes the collation up or down. The latest-migration pin accepting the last migration and refusing a stale constant or an assembly without migrations. |
| `Email/` | The capturing sender's thread safety, its lifetime-ordinal scripting, its event-driven wait (including the timeout), and the fact that it rejects an invalid message like any other transport. Both `AddCapturingEmail` modes, with the transport mode proving the router and its sandbox marking stay in the loop. The delivery-event builder's determinism and its verbatim redelivery. |
| `Jobs/` | The job probe driving a sample handler: a fresh item marked for a retry with its error, an item resumed from a seeded checkpoint succeeding with its notification, a requested cancel stopping the handler and leaving the item unmarked, the reason escalation, and a checkpoint stored under the platform's rules and cleared. |
| `Sql/` | The shared database creation's retry policy: only SQL error 1807 (alone or with the 1802 that follows it) is transient, never 1802 alone, an existing database, a deadlock, a timeout or a non-SQL exception; the jittered doubling waits and their ceiling; the bound on runs, after which the last run's own exception surfaces; and a non-transient failure or a cancellation surfacing at once. Under a real held `model` lock (`Category=Integration`, on a container of the class's own), SQL Server's own 1807 is classified as transient, a one-run policy surfaces it unchanged, and six concurrent creations each log a retry and all exist once the lock is released. |
| `Logging/` | The shared capturing logger provider's walk, which the suites that assert a secret reaches no log depend on: a value is found in a pair sequence whose message redacts it, in a non-generic dictionary's value and key, in a destructured object nested in a member, in an exception's data and in a scope; a cyclic state ends with the value behind the cycle still found; a throwing getter is skipped; and nothing is found where the value is absent. |

## Running

```bash
dotnet test test/core/Tellma.Core.Testing.Tests --filter "Category!=Integration"
dotnet test test/core/Tellma.Core.Testing.Tests --filter "Category=Integration"   # needs Docker, or TELLMA_TEST_SQL
```
