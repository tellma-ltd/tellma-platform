# Tellma.Core.AspNetCore.IntegrationTests

SQL Server tests (`Category=Integration`) of the platform's web surface, end to end through the
composed web host of the Acme reference distribution: every request is a real HTTP request a
browser's client would send (`Sec-Fetch-Site`, `Origin`, `Tellma-Client: test/1` and the session
cookie), answered by the host Acme's `Program.cs` builds, over databases the migrator created.

## The deployment

One deployment serves every class (`AcmeDeployment`, an assembly fixture), so the CI job pays for
one SQL Server container, one `migrate` and one host:

- the Acme catalog and its two Development tenants, migrated from an empty server: tenant 1 live
  and tenant 2 a sandbox;
- the members: the seeded administrator, the seeded centers reader (a read filtered to sale
  centers, the restricted member), the seeded sales member and sandbox member; an editor holding a
  role that reads centers and saves sale centers but may not delete them; a deactivated former
  member; and a stranger whose subject no tenant knows;
- a third tenant provisioned through the migrator's `provision --admin-email` while the identity
  engine is unreachable, so it stays `Provisioning` until a case resumes it.

Sessions are minted as the host's own sign-in stores them: an authentication ticket in the session
store and the cookie that names it, with the assurance and the authentication time a case needs.
The classes run one at a time: a role save moves the tenant's permissions tag, which the platform
answers as stale for any save running across it, and some cases hold locks, measure round trips or
stop a host.

| Folder | Covers |
|---|---|
| `Stacks` | Every projected center, user and role operation: trees saved with temporary ids and read every way, a stale resend refused as `409 concurrency-conflict` naming the last modifier, activation, deletes with and without descendants; users saved with a role, invited through the identity engine, their invitation status, their own profile, locale and preferences, another user's preferences and a service account's credentials; roles with permissions; the child query surfaces as their parent's readers and a restricted member see them; and the projected route list. |
| `Security` | The verdict matrix: a stranger and a deactivated member answered the same `404`; a sandbox member at their own tenant only; a save without the grant, a delete without the grant and a save outside the filter refused as `403 forbidden` naming no role or filter; the step-up `401` and its challenge for a user change below the bar, then served once stepped up; a bearer token and no session; unknown routes; navigation outside a filtered member's related projection refused as `403 restricted-path` in select, filter and order; and a tenant driven through every state, each verdict on a read, a save and a locale change. |
| `Surface` | The headers: the version tags in order, agreeing with the envelope and the tenant's database, a cached entity's save answering with the entities tag it moved, the trace and build on every answer, none of the tags on an answer refused before any batch; an outdated contract refused as `412` with nothing run; compression; the health probes; the background activity marker; the API's instruments, the refusal and child-surface log events, and the problem log with the request's scope. |
| `Serial` | A read aborted while waiting on a lock leaving the server within seconds, well under the command timeout, and a save aborted after its persist was sent still committing (both over Kestrel, where an abort is a closed connection); the round trips of every center, user and role operation and of the access and notification-preference operations within the platform's budget, each request matched to its own measurement by its exact operation tag; a member past the rate refused, counted and logged as rate limited, and a stopping instance finishing the request in flight before it stops. |

Two of the web API's log events are left to `Tellma.Core.AspNetCore.Tests`, which can reach them
without breaking the reference distribution: `ApiUnhandledException` needs an endpoint that throws
outside the mapped set, and a failing securable audit needs a composition that fails at startup.

```bash
dotnet test test/core/Tellma.Core.AspNetCore.IntegrationTests --filter "Category=Integration"
```
