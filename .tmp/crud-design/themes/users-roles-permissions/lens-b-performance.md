# Users, roles, and permissions — design proposal (performance and operations)

Theme key `users-roles-permissions`, future spec 0013. Written for a reader who has not seen any other
file: every name below is final-looking, every statement is the one the spec author can copy.

The optimisation targets of this proposal, in order: round trips per user action, write amplification
on hot rows (temporal history in particular), plan-cache stability, lock duration, cache correctness
across many app instances with no shared state other than the tenant database, and instruments that
exist on day one. Where a simpler-for-the-distro-author or a stricter-security alternative exists and
costs nothing measurable, it is adopted; where it costs a round trip, the round trip wins and the
alternative is recorded as a review flag.

---

## 1. Critique

The brain dump's user/role/permission model is a port of the monolith's model (verified against the
legacy `dbo.Users`, `dbo.Roles`, `dbo.Permissions`, `dbo.RoleMemberships`, `dbo.UserSettings` tables and
the `dal.OnConnect` / `dal.Permissions__Load` procedures — see §8). That model is sound and proven; the
problems are in what it costs per request and per write, and in what it leaves unstated.

### 1.1 General design

1. **The connect step is a separate database call.** Every read costs two round trips (connect, then
   the query) and every save four (connect, RLS pre-check, validation context, persist). The connect
   step's three jobs — resolve subject to user, stamp activity, read version tags — are all one-row
   reads and one throttled write against tiny tables; nothing about them needs a round trip of their
   own. Folding them into a T-SQL prologue of every batch, with a guard that skips the batch body when
   the cached premises no longer hold, makes a read one round trip and a save two without giving up
   freshness (D5, D6).
2. **`LastActive` is written on every user-initiated request.** On a temporal `User` table that is one
   history row per request; even on a non-temporal sibling it is one logged write per request on a hot
   row. Throttling the stamp to once per minute per user turns it into a no-op UPDATE almost always
   (the predicate fails, no row is written, no log record) and removes the row from the temporal table
   entirely (D3).
3. **The version stamps live on the temporal row.** Agreed with the dump's own remark: they move to a
   non-temporal sibling table keyed by `UserId` (D3).
4. **`State` is stored.** A stored state column is a second source of truth beside the facts that
   define it (`InvitedAt`, `ActivatedAt`, `Subject`). The legacy schema had already made it a
   persisted computed column; this design keeps that (D2).
5. **The RLS pre-check is a round trip of its own** (save flow step 3). It is one predicate query that
   belongs in the validation-context batch, which every update save issues anyway (D6).
6. **`UserSettings` has a surrogate `Id`.** A key/value row is not an entity: it needs no sequence
   draw, no child synchronisation, no audit. A composite clustered key `(UserId, Key)` and a dedicated
   upsert statement are cheaper on every axis (D17).
7. **`SavedById` on weak entities** is redundant once the rule is that the aggregate root's
   `ModifiedAt`/`ModifiedById` are bumped whenever any child row changes — a rule the concurrency token
   needs anyway (D19, D20).
8. **The audit vocabulary is inconsistent** across the dump's own tables (`User`: `SavedAt` + `SavedById`
   + period; `Role`: `SavedById` + period; `Center`: four columns, no period). One vocabulary — four
   audit columns on every top-level entity, system-versioning as an additive capability — is adopted
   (D1, D20). `SavedAt` and `ValidFrom` measure the same instant; only one survives, and it is the one
   that is a real column in the row image and can be indexed (`ModifiedAt`).
9. **Inbox tracking has no shape.** The only per-request cost that matters is the two badge counters
   on every page load; materialised counters on the sibling row make them free at connect time (D3).
10. **Notification settings as one JSON field** is the wrong shape for the question the notification
    writer asks in bulk ("of these 300 recipients, who wants email for type X"): that is a set-based
    join against a child table, not a `JSON_VALUE` per row (D18).
11. **The self-lockout rule "cannot strip your own admin permissions"** is both expensive (it needs the
    caller's *future* effective permission set inside the save) and wrong for ordinary administration
    (an admin handing over and stepping down). The cheap and sufficient invariant is "after this
    transaction at least one active user still holds an unfiltered `All/All` grant through an active
    role", checked by one `EXISTS` inside the persist transaction, plus "you cannot deactivate or
    delete yourself" in memory (D13).

### 1.2 Detailed choices

- `Subject` has no type. The OIDC `sub` is at most 255 ASCII characters and **case-sensitive**
  (OpenID Connect Core 1.0 §2, verified); the identity server issues 36-character GUID strings, but the
  authority is swappable, so the column is `varchar(255)` under a binary collation with a filtered
  unique index — not `uniqueidentifier`, not `nvarchar(450)` (D2).
- `Permission.Resource` "e.g. invoices" and `Action` "read/save/..." have no naming scheme, no
  wildcard rule, no maximum length, and `Filter` "only on resources and actions that support it" is
  contradicted by `Resource = all` carrying a filter. The registry fixes all four (D10).
- Nothing bounds the permission set: a user in 40 roles each with 200 filtered grants on the same
  resource yields a disjunction that blows Queryex's 512-slot ceiling at compile time. Per-role and
  per-user caps, filter deduplication, and a dedicated limits profile bound it (D8, D23).
- `Name2`/`Name3` "removed from the Queryex schema if the language is not configured" makes the schema
  a function of tenant settings; therefore every batch compiled against that schema must also be
  guarded by the tenant settings tag, not only by the permissions tag (D5).
- No index is named anywhere; the prologue's hot path needs exactly three seeks and they are listed
  (§4).
- Nothing says who bumps `PermissionsVersion` or when. The answer must be a rule the emitter applies
  from the tables a batch declares it writes, not a line each service remembers (D11).
- The dump keeps the tenant-level "all users" grant open between a system role, an `IsPublic` flag,
  and a settings table; the legacy schema used `IsPublic` and it composes into one `UNION`-free load
  query (D9).
- Write-once columns: agreed with the dump's own inclination — one UDTT, the service resets or rejects
  (D2).

### 1.3 Gaps

- The multi-instance story: where caches live, what invalidates them, what the worst-case staleness
  window is. Answer: per-instance caches, the database tags as the only channel, one in-flight
  request as the window (D5, D7).
- The cold path (first request of a user on an instance) and the stale path (after a role edit), each
  with its round-trip cost (D6).
- What happens to a user deactivated mid-session, on this tenant only (D21).
- The system user for background work and seeding (D16).
- Instruments and logs (D22).
- The state the identity server owns versus the state the tenant stores (D2).
- Language versioning of stored filters when the engine version moves (D8).

---

## 2. Decisions

### D1 — Naming and vocabulary

**Decision.** Schema `core`; plural table names (`core.Users`, `core.Roles`, `core.Permissions`,
`core.RoleMemberships`, `core.UserStamps`, `core.UserSettings`, `core.UserNotificationSettings`);
temporal history tables `<Table>History` in the same schema; sequences `sq_<Table>`; id type `int`
everywhere in this theme. Audit columns on every top-level entity: `CreatedAt datetime2(7)`,
`CreatedById int`, `ModifiedAt datetime2(7)`, `ModifiedById int` (all UTC, all server-stamped, §D20).
Free-text remark columns are named `Notes` (`nvarchar(1024)`). The opaque cache validators are called
**version tags**; columns end in `Tag` (`PermissionsTag`, `SettingsTag`); the hardcoded cache shape
number is the **format version**. The permission target tuple is a **securable**. The activatable
action is `Activate` (one action, both directions).

**Rationale.** Plural matches ARCHITECTURE.md (`gl.Invoices`), spec 0001's default UDTT names
(`InvoicesList`), and the legacy schema. `Notes` over `Memo`: it is the word the UI already uses.
"Version tag" over "etag" (HTTP reserves it for representations) and "fingerprint" (implies a content
hash, which these are not). "Securable" is SQL Server's own word for the same concept.

**Rejected.** Singular table names (the dump) — would change ARCHITECTURE.md for no gain. `bigint`
ids — 8 bytes on every FK and index for tables that never reach 2^31 rows.

**Confidence.** High. **Review flag:** none.

### D2 — `core.Users`: columns, the subject, the derived state

**Decision.** `core.Users` is a temporal (system-versioned) top-level entity holding only columns that
change by user-visible action. Columns (full DDL in §4):

- `Id int` — app-assigned from `sq_Users`; `1` is the seeded system user (D16).
- `Kind varchar(16)` — `Human | System` (enum-as-string; `Service` is reserved for service accounts,
  which then share this table with a `ClientId` column added by an expand migration rather than a
  separate table).
- `Subject varchar(255) COLLATE Latin1_General_100_BIN2 NULL` — the OIDC `sub`; null until invited;
  filtered unique index. Binary collation because `sub` is case-sensitive by specification.
- `Name nvarchar(255) NOT NULL`, `Name2`, `Name3` — the multilingual convention.
- `Email nvarchar(255) NULL` — null only for `Kind = System`; filtered unique index.
- `ImageId varchar(64) NULL` — the blob key; image fit and size live wherever the blob theme puts them
  (they are not read on any hot path here).
- `PreferredLanguage varchar(35) NULL`, `PreferredCalendar varchar(16) NULL`, `TimeZone varchar(64) NULL`
  — typed preference columns (D17).
- `ContactEmail nvarchar(255) NULL`, `ContactMobile varchar(32) NULL` — notification addresses (D18).
- `InvitedAt datetime2(7) NULL`, `InvitationStatus varchar(16) NULL` (`Invited | Reinvited | Active`,
  verbatim from the identity server's invite result), `InvitationError nvarchar(1024) NULL` (the
  server's free-text per-user error, verbatim, for troubleshooting only), `ActivatedAt datetime2(7) NULL`
  (first authenticated request on this tenant).
- `State AS (CASE WHEN ActivatedAt IS NOT NULL THEN 'Active' WHEN InvitedAt IS NOT NULL THEN 'Invited' ELSE 'New' END) PERSISTED`
  — a persisted computed column (`varchar(16)`), excluded from the UDTT by spec 0001's rule, declared
  to Queryex as a `String` property so `State = 'Active'` filters and Excel exports work.
- `IsActive bit NOT NULL`.
- The four audit columns.

**What is stored versus queried live.** Stored: everything the tenant did or learned once
(`Subject`, `InvitedAt`, `InvitationStatus`, `InvitationError`, `ActivatedAt`). Queried live, on
demand, never persisted: the identity server's delivery state
(`NotFound | Pending | Sent | Delivered | Bounced | Complained | Rejected | Abandoned | Accepted`, with
`expectsDeliveryEvents` and `reason`), through the bulk delivery-status API for the users the admin is
looking at. The identity server's own lifecycle (`Active | Orphaned | Disabled | Purged`) is never
visible to the tenant and is not modelled.

**The tenant state machine.** `New` (row exists, no subject) → `Invited` (invite call succeeded with
any of the three statuses; `Subject` and `InvitedAt` set) → `Active` (the prologue observed the user's
first authenticated request and set `ActivatedAt`). Re-invite refreshes `InvitedAt` and
`InvitationStatus` for an `Invited` user; an `Active` user is never re-invited. An invite result of
`Active` (the person already had a credential; no email was sent) leaves the tenant state `Invited` and
is displayed as "added, no email sent" from `InvitationStatus`; the user service sends its own "you were
added" notice (owned by the reference stack). The dump's list of states is exhaustive for the tenant's
own view; the finer states belong to the identity server and are read live.

**Write-once columns** (`Subject`, `Email` once `Subject` is set, `Kind`): one UDTT; the save pipeline
marks them `[ServerOwned]` (the pipeline overwrites client values with the loaded pre-image) or, for
`Email`, validates against the pre-image already loaded for the concurrency check. The data layer stays
dumb.

**Rationale.** Every column here changes at most a handful of times over a user's life, so temporal
history is cheap and useful (who changed whose name, roles, email). Everything that churns is in D3.
The derived `State` cannot drift and costs nothing on read (persisted). Typed preference columns are
filterable and exportable and are read by background work without parsing a bag.

**Rejected.** `Subject uniqueidentifier` — smaller index, but ties the schema to one authority's
format; `nvarchar(450)` (legacy) — twice the bytes for a value that is ASCII by specification. A
separate service-account table (the dump) — a `Kind` column costs nothing and keeps one connect path.
A stored `State` — drift risk, no read benefit.

**Confidence.** High on shape; medium on keeping `PreferredLanguage/Calendar/TimeZone` typed rather
than in the bag. **Review flag:** typed preference columns on the temporal row versus keys in
`core.UserSettings`; the former is chosen because background jobs and admins need them without parsing
JSON and because they change rarely.

### D3 — `core.UserStamps`: the non-temporal sibling for everything that churns

**Decision.** One row per user, keyed and clustered by `UserId`, created by the same batch that
inserts the user (a companion `INSERT … SELECT FROM @tm_UserIds WHERE NOT EXISTS`), deleted by cascade
of the user row:

| Column | Type | Meaning |
|---|---|---|
| `UserId` | `int PK FK core.Users ON DELETE CASCADE` | |
| `PermissionsTag` | `uniqueidentifier NOT NULL` | bumped whenever the user's effective permissions may have changed (D11) |
| `SettingsTag` | `uniqueidentifier NOT NULL` | bumped whenever the user's settings snapshot may have changed (D11, D17) |
| `LastActiveAt` | `datetime2(0) NULL` | throttled activity stamp |
| `InboxUnreadCount` | `int NOT NULL DEFAULT 0` | materialised: items not yet read |
| `InboxUnseenCount` | `int NOT NULL DEFAULT 0` | materialised: items since the inbox was last opened |

**The activity stamp is throttled to 60 seconds** by the statement's own predicate
(`LastActiveAt IS NULL OR LastActiveAt < DATEADD(second, -60, @tm_Now)`), so on the common request the
UPDATE matches zero rows: a clustered seek, no write, no log record, no version-store row. The batch
builder additionally skips emitting the statement when its own per-instance memo says it stamped this
user within the last 60 seconds. Endpoints that are background polling (inbox counters, tag checks)
carry `NoActivityStampMetadata` and bind `@tm_StampActivity = 0`.

**The inbox counters are materialised**, maintained by the notification insert statement that rides
the save batch (`UPDATE core.UserStamps SET InboxUnreadCount += 1, InboxUnseenCount += 1 WHERE UserId IN
(recipients)`), reset by "open inbox" (`InboxUnseenCount = 0`) and "mark read" (`InboxUnreadCount -= n`),
and repaired by a periodic reconcile job from the inbox table, which is the source of truth. The
background-tasks theme owns the item table and those statements; this theme owns the columns and the
fact that the connect row returns them, so a page load gets its badges with zero additional queries.

**Rationale.** The row is written at most once a minute per active user and once per notification;
`core.Users` is written only by user-visible actions. `UsersHistory` therefore records edits, not
traffic — an integration test asserts that 1,000 requests add zero history rows.

**Rejected.** Making `core.Users` non-temporal — loses the audit that matters. Timestamps
(`LastInboxCheck`) with `COUNT` queries at connect — two extra seeks per page load for no benefit
once the counters are maintained set-based. A tenant-wide permissions tag instead of a per-user tag —
see D4.

**Confidence.** High. **Review flag:** materialised counters (drift risk, reconciled) versus counted on
demand (two indexed `COUNT`s per connect).

### D4 — Version tags are app-generated GUIDs; per-user tags here, tenant tags in the settings theme's table

**Decision.** Every tag is a `uniqueidentifier` minted by `NEWID()` inside the bumping statement, or by
`Guid.NewGuid()` when the app writes one. Tags are compared for equality only, never ordered, never
indexed, never exposed as anything but an opaque string. Two per-user tags live in `core.UserStamps`;
tenant-level tags (`Settings`, one per cacheable entity type, anything else the settings theme defines)
live in that theme's single small table, which this design reads as `core.VersionTags (Name varchar(64)
PK, Tag uniqueidentifier NOT NULL)` and expects to contain a row named `Settings`. Each cache kind
carries a `const int FormatVersion`; the client-side cache key is `{FormatVersion}:{Tag}`, so a deploy
that changes a cached DTO's shape invalidates browsers without touching the database.

**Rationale.** A random 128-bit value needs no coordination across instances, survives a database
restore without ever reading "unchanged" for a stale cache (a monotonic counter can re-reach a cached
value after a restore), and is minted inside the same statement that writes the change. `rowversion`
is unusable as a selective tag: it moves on every update including bookkeeping.

**Rejected.** Monotonic `bigint` — adds nothing where only equality is needed and is restore-unsafe.
`rowversion` — see above. A single tenant-wide permissions tag — one role edit would invalidate every
user's cache on every instance and every browser; with the per-user tag only the affected members
pay, and the fan-out bump is one set-based UPDATE (D11).

**Confidence.** High.

### D5 — The connect prologue and the batch guard

**Decision.** Every batch that runs on behalf of a caller begins with a fixed-text T-SQL prologue that
resolves the caller, stamps activity (throttled), flips `Invited → Active` on the first request, reads
every tag, returns the connect row and the tenant tags, returns the caller's permission rows **only
when the caller's cached permissions tag is stale**, returns the caller's settings only when that tag
is stale, and computes a guard bit. The batch's own statements are wrapped in
`IF @tm_Guard = 1 BEGIN … END`, so they execute only when the premises the batch was composed under —
the user id, the permissions tag, the tenant settings tag — still hold. A guard failure costs the
prologue's few seeks, not a wasted query and not a query run under stale permissions.

Parameters bound by the builder (the `@tm_` prefix is reserved for platform prologue names, as `@qx`
is reserved for Queryex):

| Parameter | Type | Value |
|---|---|---|
| `@tm_Subject` | `varchar(255)` | the caller's `sub` claim |
| `@tm_Now` | `datetime2(7)` | the app's UTC clock, for the activity stamp only |
| `@tm_StampActivity` | `bit` | `0` for background-polling endpoints |
| `@tm_ExpectedUserId` | `int` | the cached user id; `NULL` on the cold path |
| `@tm_ExpectedPermissionsTag` | `uniqueidentifier` | the cached permissions tag; `NULL` when not cached |
| `@tm_ExpectedSettingsTag` | `uniqueidentifier` | the cached settings tag; `NULL` when not cached |
| `@tm_ExpectedTenantTag` | `uniqueidentifier` | the tenant `Settings` tag the Queryex schema and time zone were taken from |

The prologue (normative for shape):

```sql
DECLARE @tm_UserId int, @tm_Kind varchar(16), @tm_IsActive bit, @tm_ActivatedAt datetime2(7),
        @tm_PermissionsTag uniqueidentifier, @tm_SettingsTag uniqueidentifier,
        @tm_Unread int, @tm_Unseen int, @tm_TenantTag uniqueidentifier,
        @tm_PermissionsStale bit = 0, @tm_SettingsStale bit = 0, @tm_Guard bit = 0;

-- Resolve the caller: one seek on the Subject index, one key lookup, one clustered seek on UserStamps.
SELECT @tm_UserId = U.[Id], @tm_Kind = U.[Kind], @tm_IsActive = U.[IsActive], @tm_ActivatedAt = U.[ActivatedAt],
       @tm_PermissionsTag = S.[PermissionsTag], @tm_SettingsTag = S.[SettingsTag],
       @tm_Unread = S.[InboxUnreadCount], @tm_Unseen = S.[InboxUnseenCount]
FROM [core].[Users] AS U
JOIN [core].[UserStamps] AS S ON S.[UserId] = U.[Id]
WHERE U.[Subject] = @tm_Subject;

SELECT @tm_TenantTag = [Tag] FROM [core].[VersionTags] WHERE [Name] = 'Settings';

IF @tm_UserId IS NOT NULL AND @tm_IsActive = 1
BEGIN
    -- Throttled activity stamp: matches zero rows on the common request.
    IF @tm_StampActivity = 1
        UPDATE [core].[UserStamps] SET [LastActiveAt] = @tm_Now
        WHERE [UserId] = @tm_UserId
          AND ([LastActiveAt] IS NULL OR [LastActiveAt] < DATEADD(second, -60, @tm_Now));

    -- First authenticated request on this tenant: Invited -> Active. Once per user, ever.
    -- Deliberately does not touch ModifiedAt: activation is bookkeeping, not a user-visible edit.
    IF @tm_ActivatedAt IS NULL
        UPDATE [core].[Users] SET [ActivatedAt] = @tm_Now
        WHERE [Id] = @tm_UserId AND [ActivatedAt] IS NULL;

    SET @tm_PermissionsStale = CASE WHEN @tm_ExpectedPermissionsTag IS NULL
                                      OR @tm_ExpectedPermissionsTag <> @tm_PermissionsTag THEN 1 ELSE 0 END;
    SET @tm_SettingsStale    = CASE WHEN @tm_ExpectedSettingsTag IS NULL
                                      OR @tm_ExpectedSettingsTag <> @tm_SettingsTag THEN 1 ELSE 0 END;
    SET @tm_Guard = CASE WHEN @tm_UserId = @tm_ExpectedUserId
                          AND @tm_PermissionsStale = 0
                          AND @tm_TenantTag = @tm_ExpectedTenantTag THEN 1 ELSE 0 END;
END

-- Result set 0: the connect row. Always present, exactly one row.
SELECT @tm_UserId AS [UserId], @tm_Kind AS [Kind], @tm_IsActive AS [IsActive],
       @tm_PermissionsTag AS [PermissionsTag], @tm_SettingsTag AS [SettingsTag],
       @tm_TenantTag AS [TenantSettingsTag],
       @tm_Unread AS [InboxUnreadCount], @tm_Unseen AS [InboxUnseenCount],
       @tm_PermissionsStale AS [PermissionsStale], @tm_SettingsStale AS [SettingsStale],
       @tm_Guard AS [GuardPassed];

-- Result set 1: every tenant-level tag. Always present; a dozen rows.
SELECT [Name], [Tag] FROM [core].[VersionTags];

-- Result set 2: the caller's effective permission rows. Present iff PermissionsStale = 1.
IF @tm_PermissionsStale = 1 AND @tm_Kind <> 'System'
    SELECT P.[Id], P.[RoleId], R.[IsPublic], P.[Resource], P.[Action], P.[Filter], P.[FilterLanguageVersion]
    FROM [core].[Permissions] AS P
    JOIN [core].[Roles] AS R ON R.[Id] = P.[RoleId]
    WHERE R.[IsActive] = 1
      AND (R.[IsPublic] = 1
           OR EXISTS (SELECT 1 FROM [core].[RoleMemberships] AS M
                      WHERE M.[RoleId] = R.[Id] AND M.[UserId] = @tm_UserId));

-- Result sets 3 and 4: the caller's settings snapshot. Present iff SettingsStale = 1.
IF @tm_SettingsStale = 1
BEGIN
    SELECT [Name], [Name2], [Name3], [Email], [ImageId], [PreferredLanguage], [PreferredCalendar], [TimeZone]
    FROM [core].[Users] WHERE [Id] = @tm_UserId;
    SELECT [Key], [Value] FROM [core].[UserSettings] WHERE [UserId] = @tm_UserId;
END

IF @tm_Guard = 1
BEGIN
    -- The batch body: Queryex queries, save statements, raw SQL, tag bumps, notification inserts,
    -- verbatim. A persist body opens its own transaction here (SET XACT_ABORT ON; BEGIN TRAN ... COMMIT),
    -- so the prologue's stamp and activation flip never sit inside a user transaction.
END
```

**Reader contract.** Result sets 0 and 1 are always present. Set 2 is present iff
`PermissionsStale = 1` (and the caller is not the system user); sets 3–4 iff `SettingsStale = 1`; the
body's result sets follow iff `GuardPassed = 1`. The reader consults the connect row and knows exactly
how many `NextResult()` calls to make. Nothing in the prologue is inside a transaction; the stamp and
the activation flip are autocommit single-row statements, idempotent, so the prologue is retry-safe.

**Two prologue variants** exist besides the subject-keyed one: `ConnectPrologue.ForUser(userId)` (looks
up by `U.[Id] = @tm_UserId`; no activity stamp, no activation flip; used by background scopes running as
a user) and `ConnectPrologue.System` (no user lookup at all; reads tenant tags only and guards on
`@tm_ExpectedTenantTag`; used by the migrator's seeds and by built-in schedules).

**Plan cache.** The prologue text is byte-identical for every batch of the same variant and its
parameters are always bound with the same SQL types, so it never fragments the plan cache; a batch's
plan differs from another's only by its body, exactly as without the prologue. Conditional blocks and
`DECLARE`s inside `IF … BEGIN … END` are ordinary T-SQL; variables are batch-scoped.

**Concurrency window.** Under read-committed snapshot each statement reads its own snapshot, so a role
edit committing between the prologue's tag read and a body statement is not observed by that body; the
next request sees the new tag. That window — one in-flight request — is the same as any cache design
has and is the accepted worst-case staleness. There is no polling and no cross-instance channel other
than the tags themselves.

**Rationale.** One round trip for a read, two for a save (D6), zero polling, per-request freshness
checks, and the stale path self-heals inside the same round trip: the permission rows the app needs to
recompose arrive with the failed guard.

**Rejected.** A separate connect call (the dump) — doubles round trips. The orchestrator's variant
(run the body optimistically, discard and re-run on mismatch) — executes queries under stale
permissions, which is a leak for reads and a hazard for writes, and wastes the discarded work; the
`IF` guard costs nothing and avoids both. Reading only the tags in the prologue and loading
permissions on a separate round trip — one more round trip on every stale path for no gain.

**Confidence.** High. **Review flag:** the guard skips the body via T-SQL `IF` (chosen) versus the
optimistic run-then-discard the orchestrator suggested; the difference is whether a body ever
executes under stale premises.

### D6 — Round trips per operation, the cold path, and the guarded runner

**Decision.** The pipeline never issues the prologue by itself except on the **cold path**: the first
request of a user on an instance (or after the cache evicted the entry) issues a prologue-only batch
(`@tm_Expected* = NULL`, no body), which returns the user, tags, permission rows and settings in one
round trip; the operation then proceeds with its normal batch. Every other batch carries the guard.
The runner that owns this is `IGuardedBatchRunner` (§3): it obtains the `ConnectedUser` (cache or
cold prologue), invokes the caller's composer with it, executes, and on `GuardPassed = 0` applies the
connect result to the cache (fresh permission rows, fresh settings; a changed tenant tag triggers the
settings theme's reload) and re-invokes the composer **once**. A second failure raises
`ConnectGuardException` (the web theme maps it to 409): two permission changes racing one request is
not a case worth a third attempt.

Round trips, warm cache, per operation (the common case; a "+1" is the cold or stale path, which costs
exactly one extra prologue-only round trip, once per user per instance per permission change):

| Operation | Round trips | Contents of each |
|---|---|---|
| Query (with capped count, with ancestors) | 1 | prologue; page query; count query; ancestors query |
| Details | 1 | prologue; main row; children; related entities; extras; row echo |
| Save, update | 2 | (1) prologue; validation context; RLS pre-check; (2) prologue; `BEGIN TRAN`; persist; RLS post-check; tag bumps; notifications; read-back; `COMMIT` |
| Save, create, no validation context needed | 1 | prologue; transaction; persist; read-back |
| Activate / deactivate / delete by ids | 1 | prologue; transaction; RLS-filtered update or delete with affected-count check; tag bumps; lockout invariant; `COMMIT` |
| Role save | 2 | (1) validation context (name/code pre-image, membership existence for public roles); (2) persist; fan-out bump; lockout invariant |
| Invite (reference stack) | 2 + 1 API call | (1) mark users, load emails; identity bulk invite; (2) write subjects and statuses |
| "Can I, and why" for the caller | 0 | in-memory |
| "Can I, and why" for another user | 1 | prologue; that user's permission rows |
| SPA connect at app start | 1 | prologue only (everything returned) |

**Failure modes, stated.** Unknown subject (`UserId IS NULL`): `NotTenantMemberException` → 403 and
the entry is cached negatively for 60 seconds so a stranger cannot make every request cost a lookup.
Inactive user: `UserDeactivatedException` → 403 (the session cookie survives; the user may be active on
another tenant of the distribution; the hub connections for this tenant are closed by the deactivate
action, D21). Stale permissions: transparent, one recompose. Stale tenant settings: the settings
theme reloads its snapshot (one round trip of its own) and the batch recomposes. Stale settings tag:
no guard failure (settings do not change the batch), the snapshot is refreshed from result sets 3–4.
RLS pre-check ordering: the pre-check runs in the validation batch under a guard, the post-check in the
persist batch under a guard; if permissions changed in between, the persist guard fails and the runner
recomposes the persist batch, whose post-check evaluates the new filter — the pre-check is not
repeated because the post-check is the authoritative one for the rows as saved, and the pre-check's
purpose (refusing to touch rows the caller may not see) is re-established by the recomposed persist
statements, which are themselves RLS-filtered.

**Rationale.** The counts are the point of the theme under this lens; every row of the table is one
command text walked with `NextResult()`.

**Rejected.** Retrying the guard more than once. Reading permissions on the cold path lazily (a query
that needs no filter could skip them) — the cold path is once per user per instance; not worth a
second shape.

**Confidence.** High.

### D7 — The permission cache

**Decision.** A per-instance, per-cache-kind `MemoryCache` (`Microsoft.Extensions.Caching.Memory`,
private instance, `SizeLimit` in entries with `Size = 1`, `TrackStatistics = true`) holds
`ConnectedUserEntry` keyed `(TenantId, Subject)`:

```
ConnectedUserEntry
  UserId, Kind, IsActive
  PermissionsTag, UserPermissionSet   (parsed grants; filter texts interned per instance)
  SettingsTag, UserSettingsSnapshot
  TenantTags                         (name -> tag, as last read)
  LastActivityStampedAt              (the 60 s memo)
  InboxUnreadCount, InboxUnseenCount (as last read)
```

Defaults: `SecurityOptions.MaxConnectedUsers = 10_000` entries per instance, sliding expiration
4 hours, negative entries (unknown subject) 60 seconds. Single-flight per key
(`ConcurrentDictionary<key, Lazy<Task<ConnectedUserEntry>>>`) so a burst from one user on a cold
instance issues one prologue, not many. The entry is immutable; a refresh replaces it. `UserPermissionSet`
is built once per reload: rows are parsed, each grant resolved against the securable registry (D10),
each filter validated with `QueryexEngine.Validate` (cached by text inside the engine, so a filter
shared by many users binds once per instance), and unresolved grants marked (D14). Filter strings are
interned in a per-instance bounded pool so a role's filter text is one object however many members it
has.

**Memory.** A grant is ~150 bytes plus the interned filter; a user with 200 effective grants is ~40 KB;
10,000 entries is ≤ 400 MB worst case and typically far less (most entries are small and most users
are not concurrently active). The cap is per instance and configurable; the gauge in D22 makes the
real number visible from day one.

**Meters** (D22): hits, misses, stale refreshes, evictions, entry count, load duration.

**Rationale.** The settings theme's research recommends exactly this shape over `HybridCache` for
tag-validated caches (`HybridCache` serialises every write and cannot validate its stamps against a
database value); this theme adopts that recommendation rather than inventing a second cache.

**Rejected.** Caching permissions per role and composing per user (halves memory for large tenants) —
adds an invalidation edge between two caches; deferred until the gauge says it is needed
(**review flag**). Distributed cache — no cross-instance state is needed; the tags are the channel.

**Confidence.** High on shape, medium on the default cap.

### D8 — Permission evaluation semantics

**Decision.** `UserPermissionSet.Evaluate(Securable securable, FilterTree? bespoke = null)` is a pure
function over the cached grants:

1. Candidates are the **resolved** grants (D14) whose `Resource` equals the securable's resource or
   is `All`, and whose `Action` equals the securable's action, or is `All`, or — when the securable's
   action is `Read` — is any action at all (**any grant on a resource implies `Read` on it, with the
   same filter**).
2. No candidate and no bespoke criterion → denied (`IsAllowed = false`, no filter). The evaluator never
   compiles anything for a denial; the `FilterTree.Or([])`-is-false rule is the same fact stated by the
   engine, but the decision short-circuits before reaching it.
3. Any candidate with a null filter → allowed, **unrestricted** (`Filter = null`); no tree is built.
4. Otherwise allowed with `Filter = Or(distinct filter texts of the candidates ∪ bespoke)`. Texts are
   deduplicated ordinally before composition, so ten roles carrying the same criterion contribute one
   leaf.
5. Filters attach only to securables whose `FilterRoot` is non-null; a grant with a filter on a
   securable that supports none is unresolved (`GrantStatus.FilterNotSupported`) and excluded.
6. Inactive roles are excluded at load (the prologue's `R.IsActive = 1`); an inactive user never
   reaches evaluation (the prologue refuses).
7. **Bespoke criteria** (a document visible to its assignee) are passed by the service as a
   `FilterTree`; they are disjoined with the permission filters, and they alone can allow access when
   the user holds no grant at all — which is the shape "assigned to me" needs.
8. The system user (`Kind = System`) has `UserPermissionSet.Unrestricted`: every decision is allowed
   with no filter, without loading rows.
9. **Language version.** A grant whose `FilterLanguageVersion` differs from `QueryexLanguage.Version`
   is unresolved (`GrantStatus.LanguageMismatch`) and excluded. A compilation has one language version
   (spec 0008 `QueryCompilationOptions.LanguageVersion`), and a tree cannot mix versions; spec 0008's
   migration rewrites stored expressions on a version change, so the window in which a stamp differs is
   the deploy itself.

The service pipeline conjoins the decision's filter with the caller's own filter
(`FilterTree.And([userFilter, decision.Filter])`) and compiles with the `QueryexLimits.Permissions`
profile (D23). The number of distinct compiled shapes per entity equals the number of distinct role
combinations in use, not the number of users; Queryex's template cache serves the rest.

**Rationale.** Grant-only, disjunctive, resource × action × optional filter is the model every surveyed
product converges on (Odoo group rules, Salesforce sharing rules, Dataverse cumulative roles) and the
one the monolith ran. "Any action implies read" is the dump's rule generalised: `Save` implies `Read`,
and so does `Activate`, `Invite`, or any distro action — you cannot act on what you cannot see.

**Rejected.** Intersecting "global" rules (Odoo) — a documented foot-gun. Per-leaf language
versions — an engine change with no consumer today.

**Confidence.** High.

### D9 — Public permissions: `IsPublic` on `Role`

**Decision.** `core.Roles.IsPublic bit NOT NULL DEFAULT 0`. A public role's permissions apply to every
active user of the tenant; a public role may not have memberships (validation refuses them; the
membership editor hides public roles). Several public roles are allowed (an admin can keep "Public
lookups" and "Public reports" apart). The load statement (D5, result set 2) covers both cases in one
predicate; no `UNION`, no second cache.

**Rationale.** Reuses the entire role UI, validation against the registry, temporal history, Excel
import/export, and the tag-bump rule. This is what the legacy schema did.

**Rejected.** A seeded well-known "Everyone" role — an admin can rename, deactivate, or delete it, and
implicit membership is a special case in every query. A separate role-less permissions table — a second
editor and a second validation path.

**Confidence.** High.

### D10 — The securable registry, the naming scheme, and endpoint hardness

**Decision.** A **securable** is `(Resource, Action, FilterRoot?)`:

- `Resource` is the Queryex logical entity name for entity-shaped resources (`User`, `Role`,
  `Center`) — unique across the schema by construction — and a dotted PascalCase name for everything
  else (`Settings.General`, `Reports.TrialBalance`). ASCII, ≤ 255 characters (`varchar(255)`).
- `Action` is a PascalCase verb, ≤ 64 characters (`varchar(64)`): platform actions `Read`, `Save`,
  `Delete`, `Activate`; distro actions as declared (`Invite`, `Post`).
- `FilterRoot` is the Queryex entity the filter binds against (`User` for `User/Read`); null means
  filters are not supported for this securable (`Settings.General/Save`).
- `All` is the wildcard for both `Resource` and `Action`; the registry refuses any securable named
  `All`. A grant with `Resource = All` cannot carry a filter (validation).

The registry (`ISecurableRegistry`, singleton, immutable) is built at `AddTellma` realize time from three
sources: capability recipes (the service pipeline theme contributes `(EntityName, Read|Save|Delete|
Activate…)` with `FilterRoot = EntityName` — declared once, projected to service checks and endpoint
metadata), `[RequiresSecurable]` attributes on custom service methods, and `RequireSecurable(...)`
metadata on custom Minimal API endpoints, harvested from `EndpointDataSource`. It exposes
`Tag` — the SHA-256 of the sorted definitions — which the SPA uses to cache the securables list for the
role editor and which changes only on deploy. Renames are non-breaking for one minor release through
`Alias(oldResource, newResource)`: the registry resolves stored permissions by either name, and the
migrator's data step rewrites them.

**Hard to leave unsecured.** Three layers, none of which is per-endpoint discipline: the tenant route
group carries `RequireAuthorization()` (authenticated by the cookie) and the fallback policy is
`RequireAuthenticatedUser()`; every endpoint under the tenant group must carry
`SecurableEndpointMetadata` or `PublicEndpointMetadata` ("any active member", used by connect and
settings reads), and must not carry `IAllowAnonymous` — the startup audit over `EndpointDataSource`
adds every violation to `AddTellma`'s aggregated diagnostic and refuses to serve; and every securable
named by metadata must exist in the registry. The **authoritative** check is in the service
(`IAuthorizer.Require(securable)`, in memory against the cached set), because MCP tools and the public
API reach services without endpoints; the endpoint filter performs the same check first as a
defence-in-depth fail-fast (free: it is the same in-memory lookup). ASP.NET's `IAuthorizationRequirementData`
is deliberately **not** used for the securable check: it would run before the tenant filter has resolved
the caller, and a cold cache would then need a database round trip inside an authorization handler.

**Rationale.** Logical entity names are the names role editors already see in filters; a rename of
the table does not touch permissions. The audit makes forgetting a securable a build failure rather
than an incident.

**Rejected.** URL-shaped resources (`users`) — couple permissions to routing. `*` as the wildcard —
`All` reads better in Excel and in the role editor; collisions are excluded by the registry.

**Confidence.** High.

### D11 — Tag bump rules: emitted from declared writes, before the writes, inside the transaction

**Decision.** Every statement added to a batch declares the tables it writes (a contract the batch
abstraction owns); this theme registers three `TagBumpRule`s the emitter applies automatically, so no
service ever remembers to bump a tag:

| Batch writes | Bump emitted (before the write, same transaction) |
|---|---|
| `core.Roles`, `core.Permissions` (save or delete of roles `@tm_RoleIds`) | if any saved role is or was public → `UPDATE core.UserStamps SET PermissionsTag = NEWID()` (every user); else members of those roles |
| `core.RoleMemberships`, `core.Users` (save or delete of users `@tm_UserIds`) | `PermissionsTag` and `SettingsTag` of those users |
| `core.UserSettings`, `core.UserNotificationSettings` (users `@tm_UserIds`) | `SettingsTag` of those users |

The role statement (normative for shape; `@tm_RoleIds` is an `IdList` TVP the emitter already binds,
`@tm_AnyPublic` is computed by the app from the payload):

```sql
IF @tm_AnyPublic = 1
   OR EXISTS (SELECT 1 FROM [core].[Roles] AS R JOIN @tm_RoleIds AS X ON X.[Id] = R.[Id] WHERE R.[IsPublic] = 1)
    UPDATE [core].[UserStamps] SET [PermissionsTag] = NEWID();
ELSE
    UPDATE S SET [PermissionsTag] = NEWID()
    FROM [core].[UserStamps] AS S
    WHERE EXISTS (SELECT 1 FROM [core].[RoleMemberships] AS M
                  JOIN @tm_RoleIds AS X ON X.[Id] = M.[RoleId]
                  WHERE M.[UserId] = S.[UserId]);
```

The bump precedes the write so that `R.[IsPublic]` is the pre-image (a role that *was* public and is
being made private must still invalidate everyone) and so that members of a role being deleted are
found before their memberships go. New users get their `core.UserStamps` row from a companion insert
emitted right after the `core.Users` insert:

```sql
INSERT [core].[UserStamps] ([UserId], [PermissionsTag], [SettingsTag], [InboxUnreadCount], [InboxUnseenCount])
SELECT X.[Id], NEWID(), NEWID(), 0, 0
FROM @tm_UserIds AS X
WHERE NOT EXISTS (SELECT 1 FROM [core].[UserStamps] AS S WHERE S.[UserId] = X.[Id]);
```

**Lock duration.** The targeted bump holds row X locks on the members' `UserStamps` rows for the
remainder of a save transaction (milliseconds). The everyone bump on a public role touches every row
and may escalate to a table lock for the same milliseconds; it is an admin action performed a few times
a year. Under read-committed snapshot, concurrent prologues are not blocked by either; on-prem without
snapshot they wait milliseconds, and the migrator enables `READ_COMMITTED_SNAPSHOT` per tenant database
anyway.

**Rationale.** Set-based, one statement, declared once, ordered so the pre-image is available; the
users bump is deliberately unconditional (saving a user always bumps that user's two tags) because
proving "no membership changed" costs more than the bump.

**Rejected.** Bumping everyone on every role write — one round trip more per active user after each
edit, where targeted costs one UPDATE. Bumping in C# after commit — a crash between commit and bump
leaves stale caches forever.

**Confidence.** High.

### D12 — Weak-entity path rewriting is an engine node, not a text rewrite

**Decision.** When a query's root is a weak entity (`InvoiceLine`) and the securable's `FilterRoot` is
its parent (`Invoice`), the evaluator wraps the permission filter in `FilterTree.Via("Invoice", tree)`:
a node the engine binds by resolving the inner tree's paths from the entity reached through the named
navigation. This is a Queryex amendment (spec 0011 documents engine amendments, spec 0008 being
frozen); the closed set of node kinds gains `ViaNode(Navigation, Inner)`. Until weak entities become
queryable roots (the dump defers reports over them), the node is unused; the evaluator API takes the
query's root entity today so the wrap is added without changing any caller.

**Rationale.** A textual prefix rewrite needs a parser the engine keeps internal and would mangle
`me()`/`today()`/parameters; the structural node costs nothing at run time (it is compile-time) and
composes with caching (the leaf texts are unchanged, so their bound trees are shared).

**Rejected.** A per-securable second filter authored for the child — doubles the admin's work and
drifts.

**Confidence.** Medium (depends on the data-access theme accepting the amendment). **Review flag:**
ship the node now (unused) or when the first weak entity becomes a root.

### D13 — Self-lockout guards

**Decision.** Three rules, two in memory and one in the transaction:

1. The caller cannot deactivate or delete their own user (validation, no database).
2. The caller cannot remove their own last administrator membership *while they are the only
   administrator* — subsumed by rule 3.
3. **Invariant:** after any transaction that writes `core.Users`, `core.Roles`, `core.Permissions`, or
   `core.RoleMemberships` on behalf of a human caller, at least one user remains that is active,
   human, invited or active (not `New`), and holds an unfiltered `All/All` grant through an active
   role. Emitted by the same rule mechanism as tag bumps, after the writes, before `COMMIT`:

```sql
IF NOT EXISTS (
    SELECT 1
    FROM [core].[Users] AS U
    JOIN [core].[RoleMemberships] AS M ON M.[UserId] = U.[Id]
    JOIN [core].[Roles] AS R ON R.[Id] = M.[RoleId]
    JOIN [core].[Permissions] AS P ON P.[RoleId] = R.[Id]
    WHERE U.[IsActive] = 1 AND U.[Kind] = 'Human' AND U.[InvitedAt] IS NOT NULL
      AND R.[IsActive] = 1
      AND P.[Resource] = 'All' AND P.[Action] = 'All' AND P.[Filter] IS NULL)
    THROW 51013, N'LastAdministrator', 1;
```

`XACT_ABORT ON` rolls the transaction back; the batch executor maps SQL error numbers 51000–51999 to
`InvariantViolationException(code)` (a platform exception the service pipeline theme owns), which the
web theme renders as a 422 with the code `Security.LastAdministrator`. The invariant is skipped when
the acting context is the system user (tenant bootstrap creates the first admin from zero, D16).

**Rationale.** One `EXISTS` over four tiny tables per administrative save; no need to compute anyone's
future permission set; matches the common denominator of Entra ID, GitHub, and Odoo (self-removal
blocked; last holder protected; recovery out of band).

**Rejected.** "Cannot narrow your own permissions" (the dump) — blocks legitimate hand-over and needs
the future set. A seeded break-glass tenant user — the identity server's break-glass administrator
plus a documented "promote a user to admin with the migrator" procedure is the recovery path; a
permanent hidden admin row is an attack surface. **Review flag:** the dump asked for the stricter
self-narrowing rule; this proposal argues it is unnecessary given the invariant.

**Confidence.** High on the invariant; medium on dropping the self-narrowing rule.

### D14 — Drift policy: fail closed, per grant, surfaced everywhere

**Decision.** At reload each grant is resolved against the registry and the schema:

| Condition | `GrantStatus` | Effect |
|---|---|---|
| resource or action unknown (aliases considered) | `UnknownSecurable` | grants nothing |
| filter present but `FilterRoot` null | `FilterNotSupported` | grants nothing |
| `Validate` fails (renamed column, dropped navigation) | `FilterInvalid` | grants nothing |
| `FilterLanguageVersion` ≠ current | `LanguageMismatch` | grants nothing |
| otherwise | `Resolved` | evaluated |

An unresolved grant never blocks the user's other grants. It is counted (`tellma.security.permissions.drift`,
tagged by status), logged once per (tenant, permission id) per instance lifetime at warning level,
shown in the role editor (the role details load carries each permission's status from the same
resolver), and listed by the migrator's post-deploy report, which runs `Validate` over every stored
filter of every tenant database so a deploy that breaks a permission is known before a user notices.
`All/All` grants never drift: new actions are covered the moment they are registered.

**Rationale.** Fail-closed is the only safe default for a security predicate; per-grant granularity
keeps a broken criterion from locking a user out of everything.

**Rejected.** Shimming renamed columns at run time — logic that hides drift instead of fixing it.
Blocking users until migrated — turns a deploy into an outage.

**Confidence.** High.

### D15 — "Can I, and why"

**Decision.** `PermissionExplanation Explain(UserPermissionSet set, Securable securable)` returns
`IsAllowed`, the effective `FilterTree?`, and one `Because` entry per candidate grant (permission id,
role id and name, `IsPublic`, the action that implied it when the asked action is `Read`, the filter
text, and its `GrantStatus`), plus unresolved grants that *would* have applied, with their status. For
the caller it is in-memory; for another user the user service runs a batch whose body is the D5
permission-row statement with the target user id (one round trip) and explains the result. The
endpoint is the reference stack's (`POST …/users/{id}/permissions/explain`).

**Confidence.** High.

### D16 — Tenant bootstrap: the seeded system user and administrator role

**Decision.** `HasData` in the reserved id band (`1..999`; every `sq_<Table>` starts at `1000`):

- `core.Users` id `1`: `Kind = System`, `Name = 'System'`, no `Subject`, no `Email`, `IsActive = 1`,
  `CreatedById = ModifiedById = 1` (a self-referencing row is valid in one insert).
- `core.UserStamps` for user `1` with constant tags.
- `core.Roles` id `1`: `Name = 'Administrator'`, `Code = 'ADMIN'`, `IsPublic = 0`, `IsActive = 1`.
- `core.Permissions` id `1`: role `1`, `All`, `All`, no filter.

The administrator role's `All/All` permission is server-owned: the role can be renamed and given more
permissions but that permission row cannot be removed and the role cannot be deactivated or deleted
(validation; the lockout invariant is the backstop). The **first human administrator** is not seeded:
tenant provisioning (the migrator's single-tenant run) takes an email, inserts the user through the
bulk pipeline as the system user (state `New`, membership in role `1`), and the reference stack's
invite flow moves it to `Invited`; in local development the same step creates the admin with the
identity server's fixed dev subject `00000000-0000-0000-0000-000000000001` and `admin@localhost`
(spec 0003 §10.4), Development environment only.

**Rationale.** The system user is the `CreatedById` of every seeded row, the identity of built-in
schedules, and the bypass for the lockout invariant; a positive low band keeps ids readable in URLs.

**Rejected.** Negative ids for seeds — legal, but ugly in every list and URL. Seeding a human admin
by `HasData` — needs a subject the tenant does not have yet.

**Confidence.** High. **Review flag** (vocabulary seam): band `1..999` with sequences starting at
`1000` versus negative ids.

### D17 — User settings: typed columns plus a key/value bag

**Decision.** `core.UserSettings (UserId int, Key nvarchar(128), Value nvarchar(max))`, clustered
primary key `(UserId, Key)`, not temporal, not an entity: saved by a dedicated statement
(`UserSettingList` TVP → delete-absent/upsert-present for one user) from the self-service endpoint,
bumping `SettingsTag`. Keys are dotted lower-case (`grid.users.columns`, `nav.pinned`,
`tour.dismissed`); values are JSON documents ≤ 32 KB; at most 256 keys per user (validation). The
snapshot the app caches and the SPA receives is `UserSettingsSnapshot { Tag, Name, Name2, Name3,
Email, ImageId, PreferredLanguage, PreferredCalendar, TimeZone, Items }`. Pinned screens stay a key
(`nav.pinned`); an admin-managed default for less technical users belongs in tenant settings
(settings theme) as the fallback when the key is absent — no third table.

**Rationale.** A composite key needs no sequence, no child synchronisation, no audit; the typed
columns (D2) serve queries and background work; JSON serves the UI's shapeless needs.

**Rejected.** Surrogate `Id` (the dump); one JSON column per user — every keystroke-sized change
rewrites the document.

**Confidence.** High.

### D18 — Notification settings: a child table, not JSON

**Decision.** `core.UserNotificationSettings (UserId int, NotificationType varchar(64), Email bit,
Sms bit, Push bit)`, clustered primary key `(UserId, NotificationType)`, not temporal, edited with the
user (weak, but keyed naturally, so synchronised by key rather than by `Id`). An absent row means the
type's declared default. The notification writer resolves channels set-based:
`SELECT … FROM @recipients R LEFT JOIN core.UserNotificationSettings S ON S.UserId = R.Id AND S.NotificationType = @t`.
Types that cannot be muted (your export is ready) are declared so in the type registry the
background-tasks theme owns and are not editable here. Push subscriptions (one per device: endpoint,
keys, user agent) are a separate `core.UserPushSubscriptions` table whose columns that theme fixes;
this theme reserves the name and the `UserId` cascade.

**Confidence.** Medium (the background-tasks theme may reshape channels). **Review flag:** bits per
channel versus a `Channels varchar` flag set; bits are indexable and simplest for the join.

### D19 — Roles, permissions, memberships: columns and indexes

**Decision.**

- `core.Roles` (temporal, `[TableType]`): `Id`, `Name nvarchar(255) NOT NULL` unique, `Name2`, `Name3`
  (unique where not null), `Code nvarchar(50)` (unique where not null), `IsPublic bit`, `IsActive bit`,
  four audit columns.
- `core.Permissions` (temporal, weak under `Role`, `[TableType]`): `Id`, `RoleId int FK`,
  `Resource varchar(255) NOT NULL`, `Action varchar(64) NOT NULL`, `Filter nvarchar(4000) NULL`,
  `FilterLanguageVersion int NULL` (non-null iff `Filter` is), `Notes nvarchar(1024) NULL`. Index
  `(RoleId) INCLUDE (Resource, Action, Filter, FilterLanguageVersion)` — the load by role reads the
  index only. No unique index (`Filter` exceeds the 1,700-byte key limit); exact duplicates are a
  validation error.
- `core.RoleMemberships` (temporal, weak under `User`, `[TableType]`): `Id`, `UserId int FK`,
  `RoleId int FK`, `Notes nvarchar(1024) NULL`. Unique `(UserId, RoleId)`; index `(RoleId, UserId)` for
  "members of role" (the fan-out bump, the role's members tab, the lockout invariant).
- No `SavedById` on weak entities. The aggregate root's `ModifiedAt`/`ModifiedById` are stamped
  whenever any child row is inserted, updated, or deleted, even when no root column changed — a rule
  the save emitter implements (the "skip unchanged rows" optimisation for temporal churn must not skip
  a root whose children changed).
- Child → parent foreign keys of client-edited children (`Permissions → Roles`,
  `RoleMemberships → Users`, `RoleMemberships → Roles`) are `NO ACTION`; the pipeline deletes children
  explicitly before parents, in emitted order. System-owned rows (`UserStamps`, `UserSettings`,
  `UserNotificationSettings`, `UserPushSubscriptions`) cascade from `Users`.

**Rationale.** The permissions index makes the D5 load an index-only seek per role; the membership
composite index serves every "by role" query; dropping child audit columns removes a column, an FK, and
a UDTT column from every weak table platform-wide.

**Confidence.** High.

### D20 — Concurrency token: server-stamped `ModifiedAt`; `rowversion` rejected

**Decision.** The concurrency token of every top-level entity is `ModifiedAt datetime2(7)`, stamped
server-side with `SYSUTCDATETIME()` by the save emitter (never from the app clock: instances' clocks
skew; the database clock is one source per tenant). The rule: user-visible saves and actions stamp
`ModifiedAt`/`ModifiedById`; bookkeeping never does — `ActivatedAt` (D5), activity, counters and tags
live on `core.UserStamps` or are written without touching `ModifiedAt`. The client echoes the stamp it
loaded; the persist batch checks `(Id, ExpectedStamp)` from a TVP inside the transaction
(`UPDATE … WHERE Id = X.Id AND ModifiedAt = X.ExpectedStamp`, affected-count compared), which row
versioning handles correctly (the update qualifies on the latest committed row). The override flag
skips the check.

**Rationale.** No extra column, no `rowversion` that moves on bookkeeping, meaningful to users ("modified
by Y at T"), identical for temporal and non-temporal entities; `datetime2(7)` collisions require two
commits in the same 100 ns tick on one row, which the row's own X lock serialises.

**Rejected.** `rowversion` — moves on every update, cannot be excluded from bookkeeping, and is an 8-byte
column whose only reader is the check. Content hashes — cost a hash per row per save.

**Confidence.** High (this is also the orchestrator's hint; the server-side stamping is the addition).

### D21 — Deactivation semantics

**Decision.** `Activate` on `User` flips `IsActive` (temporal row, stamps `ModifiedAt`). The next
prologue on any instance refuses the user (403 `UserDeactivated`); the entry is dropped from the cache
on the refusing instance and expires elsewhere within the request that observes it. The action's
post-commit side effect asks the hub to close the user's connections **for this tenant** (self-hosted:
tracked abort; Azure SignalR: the close-connections data-plane call), per spec 0003 §7.4. The
distribution session cookie is not touched: the user may be a member of other tenants of the same
distribution; ending the whole session is the identity server's back-channel logout, a different event.

**Confidence.** High.

### D22 — Observability

Meter `Tellma.Core`; names as `const`s in `Tellma.Core.Abstractions` (`SecurityTelemetryNames`); no
per-tenant tags:

| Instrument | Kind | Unit | Tags |
|---|---|---|---|
| `tellma.security.connect.results` | counter | `{request}` | `result` ∈ `warm`, `cold`, `stale_permissions`, `stale_settings`, `stale_tenant`, `unknown_subject`, `inactive` |
| `tellma.security.connect.prologue.duration` | histogram | `s` | `variant` ∈ `subject`, `user`, `system` |
| `tellma.security.permissions.cache.entries` | observable gauge | `{entry}` | — |
| `tellma.security.permissions.cache.events` | counter | `{event}` | `event` ∈ `hit`, `miss`, `refresh`, `evict`, `negative` |
| `tellma.security.permissions.load.duration` | histogram | `s` | — (parse + resolve + validate) |
| `tellma.security.permissions.grants` | histogram | `{grant}` | — (effective grants per loaded user) |
| `tellma.security.permissions.drift` | counter | `{grant}` | `status` ∈ `unknown_securable`, `filter_not_supported`, `filter_invalid`, `language_mismatch` |
| `tellma.security.decisions` | counter | `{decision}` | `outcome` ∈ `allowed`, `allowed_filtered`, `denied`; `action` ∈ `read`, `save`, `delete`, `activate`, `other` |
| `tellma.security.tag_bumps` | counter | `{row}` | `tag` ∈ `permissions`, `settings`; `scope` ∈ `targeted`, `everyone` |
| `tellma.security.lockout_guard` | counter | `{check}` | `outcome` ∈ `passed`, `violated` |

Logs: guard failures (information, with the stale tag kind), drift (warning, once per tenant and
permission per instance), lockout violations (warning), everyone-bumps (information). The request
activity gets `tellma.connect.result` as a tag. Alert queries under `infra/monitoring/` for drift > 0
and for a cache hit ratio below 90 % over 15 minutes (a sign the cap is too small).

### D23 — Limits

`SecurityOptions`: `MaxPermissionsPerRole = 300`, `MaxRolesPerUser = 32` (validation on role and user
saves), `MaxFilterLength = 4000`, `MaxConnectedUsers = 10_000`, `NegativeCacheDuration = 60 s`,
`ActivityStampInterval = 60 s`, `MaxUserSettingKeys = 256`, `MaxUserSettingValueBytes = 32_768`.
`QueryexLimits.Permissions` — the profile used to validate stored filters and to compile
permission-bearing queries: `MaxInputLength = 4000`, `MaxParameters = 1024`, `MaxJoins = 64`, other
ceilings default. A compile that still exceeds a ceiling raises `PermissionsTooComplexException`
(500, logged with the diagnostics and the role ids involved); it is an administrator's configuration
problem, surfaced rather than silently denied.

### D24 — Testing

- Unit (`Tellma.Core.Tests`): the evaluator as a pure function (implied read, `All`, dedup,
  unrestricted short-circuit, bespoke-only access, unresolved exclusion, system user); `Explain`;
  registry building, aliases, `Tag` stability; the startup audit over a fake `EndpointDataSource`;
  the prologue reader against scripted result sets (every presence combination of sets 2–4 and the
  body).
- Integration on LocalDB (`Category=Integration`, fixture entities of the data-access theme plus the
  five tables here): the prologue end to end (cold, warm, stale permissions, stale settings, stale
  tenant tag, unknown subject, inactive, activation flip exactly once); the throttled stamp writes
  once per minute under 1,000 requests; `UsersHistory` gains zero rows from traffic; each tag-bump
  rule (targeted, everyone, was-public, delete before membership loss); the companion insert; the
  lockout invariant (violation rolls back the whole transaction, system context bypasses); concurrent
  bump versus prologue (no lost update, guard observes the new tag on the next request).
- Nightly: the migrator's drift report against a database seeded with a filter over a column the
  next migration renames.

### D25 — Connect surface for the SPA

`ConnectResult` (returned by the connect endpoint at app start, from a prologue-only batch) carries
the user (`UserId`, names, image, `Kind`), the permission set in wire shape (for UI gating),
`PermissionsTag`, the settings snapshot and `SettingsTag`, the tenant tags, the securables `Tag`, the
two inbox counters, and each cache kind's format version. Every API response then carries a
`Tellma-Tags` header (the web theme owns headers) with the caller's current `PermissionsTag`,
`SettingsTag`, and the tenant `Settings` tag as observed by that request's prologue; the SPA compares
and refetches only what changed, so browsers stay fresh with zero polling and zero extra round trips.

---

## 3. Contracts

Contracts live in `Tellma.Core.Abstractions.Security` (EF-free, BCL DataAnnotations only);
entities and implementations in `Tellma.Core.Security`. Code blocks are normative for shape.

### 3.1 Securables

```csharp
namespace Tellma.Core.Abstractions.Security;

/// <summary>A permission target: a resource and an action, optionally filterable by a Queryex
///     predicate rooted at <see cref="FilterRoot"/>.</summary>
/// <param name="Resource">The Queryex logical entity name for entity resources ("User"), or a dotted
///     PascalCase name for others ("Settings.General"). ASCII, at most 255 characters.</param>
/// <param name="Action">A PascalCase verb ("Read", "Save", "Delete", "Activate", "Invite"). ASCII, at
///     most 64 characters.</param>
/// <param name="FilterRoot">The Queryex entity a row filter binds against; null when the securable
///     supports no filter.</param>
public sealed record Securable(string Resource, string Action, string? FilterRoot = null)
{
    /// <summary>The wildcard for <see cref="Resource"/> and <see cref="Action"/>. Refused as a real name.</summary>
    public const string All = "All";

    /// <summary>The platform actions every capability recipe uses.</summary>
    public static class Actions
    {
        public const string Read = "Read";
        public const string Save = "Save";
        public const string Delete = "Delete";
        public const string Activate = "Activate";
    }
}

/// <summary>The immutable set of securables a deployment knows. Built once at composition; identical
///     on every instance of the same build.</summary>
public interface ISecurableRegistry
{
    /// <summary>Every securable, ordered by resource then action.</summary>
    IReadOnlyList<Securable> Securables { get; }

    /// <summary>Finds a securable by resource and action, resolving registered aliases; null when unknown.</summary>
    Securable? Find(string resource, string action);

    /// <summary>The securables of one resource, aliases resolved.</summary>
    IReadOnlyList<Securable> ForResource(string resource);

    /// <summary>A stable content tag of the registry (SHA-256 of the sorted definitions, hex). Changes
    ///     only when a deploy changes the set; the client caches the securables list under it.</summary>
    string Tag { get; }
}

/// <summary>Collects securable declarations during composition. Features, capability recipes, and the
///     endpoint audit contribute; <c>AddTellma</c> freezes the result into <see cref="ISecurableRegistry"/>.</summary>
public sealed class SecurableRegistryBuilder
{
    /// <summary>Declares a securable. Declaring the same tuple twice with the same filter root is
    ///     idempotent; with a different filter root it is a composition error.</summary>
    public SecurableRegistryBuilder Add(string resource, string action, string? filterRoot = null);

    /// <summary>Declares that stored permissions naming <paramref name="oldResource"/> resolve to
    ///     <paramref name="newResource"/> for one release, so a rename is not a lockout.</summary>
    public SecurableRegistryBuilder Alias(string oldResource, string newResource);
}

/// <summary>A composition-time contributor of securables; every feature that exposes operations
///     implements one (capability recipes do so on the author's behalf).</summary>
public interface ISecurableContributor
{
    /// <summary>Adds this feature's securables.</summary>
    void Contribute(SecurableRegistryBuilder builder);
}

/// <summary>Marks a service method with the securable it enforces. Harvested into the registry and
///     copied onto the projected endpoint's metadata.</summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class RequiresSecurableAttribute(string resource, string action, string? filterRoot = null) : Attribute
{
    public string Resource { get; } = resource;
    public string Action { get; } = action;
    public string? FilterRoot { get; } = filterRoot;
}

/// <summary>Endpoint metadata naming the securable an endpoint enforces. Required on every endpoint
///     under the tenant route group unless <see cref="PublicEndpointMetadata"/> is present.</summary>
public sealed record SecurableEndpointMetadata(string Resource, string Action);

/// <summary>Endpoint metadata declaring that any active tenant member may call the endpoint (connect,
///     settings reads, self-service). Authentication is still required.</summary>
public sealed record PublicEndpointMetadata;

/// <summary>Endpoint metadata declaring that requests to the endpoint are background polling and must
///     not stamp user activity.</summary>
public sealed record NoActivityStampMetadata;
```

### 3.2 Permission sets and decisions

```csharp
namespace Tellma.Core.Abstractions.Security;

/// <summary>Why a stored permission row does or does not participate in evaluation.</summary>
public enum GrantStatus
{
    /// <summary>Resolves against the registry and the schema; evaluated.</summary>
    Resolved,
    /// <summary>Its resource or action is not in the registry (aliases considered). Grants nothing.</summary>
    UnknownSecurable,
    /// <summary>It carries a filter but the securable supports none. Grants nothing.</summary>
    FilterNotSupported,
    /// <summary>Its filter no longer validates against the schema. Grants nothing.</summary>
    FilterInvalid,
    /// <summary>Its filter was stamped under a language version other than the current one. Grants nothing.</summary>
    LanguageMismatch,
}

/// <summary>One effective permission row of a user, as loaded and resolved.</summary>
public sealed record PermissionGrant(
    int PermissionId,
    int RoleId,
    bool IsPublic,
    string Resource,
    string Action,
    string? Filter,
    int? FilterLanguageVersion,
    GrantStatus Status);

/// <summary>The outcome of evaluating one securable for one user.</summary>
/// <param name="IsAllowed">False when no grant and no bespoke criterion applies.</param>
/// <param name="Filter">The row filter to conjoin with the query; null when access is unrestricted.
///     Never non-null when <paramref name="IsAllowed"/> is false.</param>
/// <param name="Because">The grants that produced the decision, in role order.</param>
public sealed record PermissionDecision(bool IsAllowed, FilterTree? Filter, IReadOnlyList<PermissionGrant> Because)
{
    /// <summary>Access denied: no grant, no criterion.</summary>
    public static PermissionDecision Denied { get; }

    /// <summary>Access allowed with no row filter.</summary>
    public static PermissionDecision Unrestricted(IReadOnlyList<PermissionGrant> because);
}

/// <summary>A user's effective permissions, immutable, cached per instance under its tag. Evaluation is
///     a pure function: no I/O, no clock, no ambient state.</summary>
public sealed class UserPermissionSet
{
    /// <summary>The tag the rows were loaded under.</summary>
    public Guid Tag { get; }

    /// <summary>True for the system user: every decision is allowed and unrestricted.</summary>
    public bool IsUnrestricted { get; }

    /// <summary>Every loaded grant, resolved or not.</summary>
    public IReadOnlyList<PermissionGrant> Grants { get; }

    /// <summary>The set that allows everything; the system user's.</summary>
    public static UserPermissionSet Unrestricted { get; }

    /// <summary>Builds a set from loaded rows, resolving each against the registry and validating each
    ///     filter against the schema (cached by text inside the engine).</summary>
    public static UserPermissionSet Build(
        Guid tag,
        IReadOnlyList<PermissionGrant> rows,
        ISecurableRegistry registry,
        QueryexEngine engine,
        QueryexSchema schema);

    /// <summary>Evaluates a securable. Any grant on a resource implies Read on it with the same filter;
    ///     an unfiltered grant makes access unrestricted; otherwise the distinct filters of the applicable
    ///     grants and <paramref name="bespoke"/> are disjoined. A bespoke criterion alone can allow access.
    ///     When <paramref name="queryRoot"/> is a weak entity whose securable filter root is its parent,
    ///     the filter is wrapped to bind through the parent navigation.</summary>
    public PermissionDecision Evaluate(Securable securable, string? queryRoot = null, FilterTree? bespoke = null);

    /// <summary>Explains a decision: every grant that applied or would have applied, with its status.</summary>
    public PermissionExplanation Explain(Securable securable);

    /// <summary>The hardcoded shape version of the cached and wire form; bumped when the shape changes.</summary>
    public const int FormatVersion = 1;
}

/// <summary>The answer to "can this user do this, and why".</summary>
public sealed record PermissionExplanation(
    bool IsAllowed,
    FilterTree? Filter,
    IReadOnlyList<PermissionExplanationEntry> Entries);

/// <summary>One grant considered by an explanation.</summary>
/// <param name="ImpliedByAction">When the asked action is Read and the grant is for another action, that action.</param>
public sealed record PermissionExplanationEntry(PermissionGrant Grant, string? RoleName, string? ImpliedByAction);

/// <summary>The scoped authorizer services call. Throws the platform's forbidden exception; never
///     returns a denied decision to a caller that did not ask for one.</summary>
public interface IAuthorizer
{
    /// <summary>Requires the securable for the connected user and returns the decision (with its filter).</summary>
    PermissionDecision Require(Securable securable, string? queryRoot = null, FilterTree? bespoke = null);

    /// <summary>Evaluates without throwing.</summary>
    PermissionDecision Evaluate(Securable securable, string? queryRoot = null, FilterTree? bespoke = null);
}
```

### 3.3 Connect

```csharp
namespace Tellma.Core.Abstractions.Security;

/// <summary>The kind of a tenant user row.</summary>
public enum UserKind
{
    /// <summary>A person who signs in through the identity server.</summary>
    Human,
    /// <summary>The seeded system user: seeds, built-in schedules, invariant bypass. Never signs in.</summary>
    System,
}

/// <summary>The tenant's view of a user's onboarding.</summary>
public enum UserState
{
    New,
    Invited,
    Active,
}

/// <summary>The resolved caller for the current scope, populated by the connect step.</summary>
public sealed record ConnectedUser(
    int UserId,
    UserKind Kind,
    Guid PermissionsTag,
    UserPermissionSet Permissions,
    Guid SettingsTag,
    UserSettingsSnapshot Settings,
    IReadOnlyDictionary<string, Guid> TenantTags,
    int InboxUnreadCount,
    int InboxUnseenCount)
{
    /// <summary>The tenant Settings tag the caller's Queryex schema and time zone were taken from.</summary>
    public Guid TenantSettingsTag => TenantTags["Settings"];
}

/// <summary>The caller's cached settings: the typed preference columns and the key/value bag.</summary>
public sealed record UserSettingsSnapshot(
    Guid Tag,
    string Name, string? Name2, string? Name3,
    string? Email, string? ImageId,
    string? PreferredLanguage, string? PreferredCalendar, string? TimeZone,
    IReadOnlyDictionary<string, string> Items)
{
    public const int FormatVersion = 1;
}

/// <summary>The scoped holder the request context exposes; null until connected.</summary>
public interface IUserContext
{
    /// <summary>The caller's subject claim; null in system scopes.</summary>
    string? Subject { get; }

    /// <summary>The connected user, or null before the connect step ran in this scope.</summary>
    ConnectedUser? User { get; }
}

/// <summary>Resolves the caller: from the per-instance cache, or by a prologue-only round trip.</summary>
public interface IUserConnector
{
    /// <summary>Returns the connected user for the current scope, issuing a cold prologue when the
    ///     cache has no entry. Throws <c>NotTenantMemberException</c> or <c>UserDeactivatedException</c>.</summary>
    ValueTask<ConnectedUser> ConnectAsync(CancellationToken cancellationToken);

    /// <summary>Applies a prologue result read from any batch: refreshes the cache entry and the scoped
    ///     holder with fresh tags, permission rows, and settings when the result carried them.</summary>
    ConnectedUser Apply(ConnectResult result);

    /// <summary>Connects a background scope as a specific user (prologue by id; no activity stamp).</summary>
    ValueTask<ConnectedUser> ConnectAsUserAsync(int userId, CancellationToken cancellationToken);

    /// <summary>Connects a background or migrator scope as the system user (no user lookup).</summary>
    ValueTask<ConnectedUser> ConnectAsSystemAsync(CancellationToken cancellationToken);
}

/// <summary>What the prologue read: the connect row, the tenant tags, and the conditional sets.</summary>
public sealed record ConnectResult(
    int? UserId,
    UserKind? Kind,
    bool IsActive,
    Guid? PermissionsTag,
    Guid? SettingsTag,
    Guid TenantSettingsTag,
    IReadOnlyDictionary<string, Guid> TenantTags,
    int InboxUnreadCount,
    int InboxUnseenCount,
    bool PermissionsStale,
    bool SettingsStale,
    bool GuardPassed,
    IReadOnlyList<PermissionGrant>? PermissionRows,
    UserSettingsSnapshot? Settings);

/// <summary>The premises a batch was composed under; the prologue guards the body on them.</summary>
/// <param name="Subject">The caller's subject; null for the by-user and system variants.</param>
/// <param name="UserId">The user id for the by-user variant.</param>
/// <param name="ExpectedUserId">The cached user id; null on the cold path.</param>
/// <param name="ExpectedPermissionsTag">The cached permissions tag; null when not cached.</param>
/// <param name="ExpectedSettingsTag">The cached settings tag; null when not cached.</param>
/// <param name="ExpectedTenantSettingsTag">The tenant Settings tag the schema was built from.</param>
/// <param name="StampActivity">False for background polling.</param>
public sealed record ConnectGuard(
    string? Subject,
    int? UserId,
    int? ExpectedUserId,
    Guid? ExpectedPermissionsTag,
    Guid? ExpectedSettingsTag,
    Guid ExpectedTenantSettingsTag,
    bool StampActivity)
{
    /// <summary>The guard for a cold prologue-only batch.</summary>
    public static ConnectGuard Cold(string subject, Guid expectedTenantSettingsTag, bool stampActivity);

    /// <summary>The guard for a warm batch composed under <paramref name="user"/>.</summary>
    public static ConnectGuard For(ConnectedUser user, string subject, bool stampActivity);
}

/// <summary>Runs one guarded round trip: connects, composes, executes, and recomposes once when the
///     guard failed. The only way a service executes a batch on behalf of a caller.</summary>
public interface IGuardedBatchRunner
{
    /// <summary>Composes with the connected user, executes, reads. On a failed guard the connect
    ///     result is applied and the composer is invoked once more; a second failure throws
    ///     <c>ConnectGuardException</c>.</summary>
    Task<TResult> RunAsync<TResult>(
        Func<ConnectedUser, TellmaBatch> compose,
        Func<ConnectedUser, BatchResult, TResult> read,
        CancellationToken cancellationToken);
}
```

### 3.4 Tag bump rules and the emitter seam

```csharp
namespace Tellma.Core.Abstractions.Security;

/// <summary>A rule the emitter applies when a batch declares writes to given tables: statements
///     emitted before those writes (bumps) or after them (invariants), inside the same transaction.</summary>
public sealed record TagBumpRule(
    IReadOnlyList<string> TriggeringTables,     // "[core].[Roles]", ...
    Func<BatchWriteContext, string> BeforeWrites, // returns SQL text; may be empty
    Func<BatchWriteContext, string> AfterWrites);

/// <summary>What the emitter knows about the writes of one batch when a rule runs.</summary>
public sealed record BatchWriteContext(
    string Table,
    string IdListParameter,     // the @tm_*Ids TVP name carrying the affected root ids
    bool IsDelete,
    IReadOnlyDictionary<string, object?> Facts); // e.g. "AnyPublic" -> true

/// <summary>Registers this theme's rules with the emitter at composition.</summary>
public interface ITagBumpRuleSource
{
    IReadOnlyList<TagBumpRule> Rules { get; }
}
```

### 3.5 Entities (`Tellma.Core.Security`)

```csharp
namespace Tellma.Core.Security;

/// <summary>A tenant user. Temporal; every column changes by user-visible action only.</summary>
[Table("Users", Schema = "core")]
[TableType]
[Temporal]
public class User : AuditedEntity, IActivatable, IMultilingual
{
    [ServerOwned] public UserKind Kind { get; set; } = UserKind.Human;

    /// <summary>The OIDC subject; ASCII, case-sensitive, at most 255 characters. Null until invited.</summary>
    [ServerOwned, MaxLength(255)] public string? Subject { get; set; }

    [Required, MaxLength(255)] public string Name { get; set; } = null!;
    [MaxLength(255)] public string? Name2 { get; set; }
    [MaxLength(255)] public string? Name3 { get; set; }

    /// <summary>The sign-in email; write-once after invitation; null only for the system user.</summary>
    [EmailAddress, MaxLength(255)] public string? Email { get; set; }

    [MaxLength(64)] public string? ImageId { get; set; }

    [MaxLength(35)] public string? PreferredLanguage { get; set; }
    [MaxLength(16)] public string? PreferredCalendar { get; set; }
    [MaxLength(64)] public string? TimeZone { get; set; }

    [EmailAddress, MaxLength(255)] public string? ContactEmail { get; set; }
    [Phone, MaxLength(32)] public string? ContactMobile { get; set; }

    [ServerOwned] public DateTime? InvitedAt { get; set; }
    [ServerOwned, MaxLength(16)] public string? InvitationStatus { get; set; }
    [ServerOwned, MaxLength(1024)] public string? InvitationError { get; set; }
    [ServerOwned] public DateTime? ActivatedAt { get; set; }

    /// <summary>Persisted computed: Active when ActivatedAt is set, Invited when InvitedAt is set, else New.</summary>
    [ServerOwned] public UserState State { get; private set; }

    public bool IsActive { get; set; } = true;

    /// <summary>The user's role memberships; not an EF navigation.</summary>
    [NotMapped] public List<RoleMembership>? RoleMemberships { get; set; }

    /// <summary>The user's notification settings; not an EF navigation.</summary>
    [NotMapped] public List<UserNotificationSetting>? NotificationSettings { get; set; }
}

/// <summary>The non-temporal sibling of <see cref="User"/>: everything that churns.</summary>
[Table("UserStamps", Schema = "core")]
public class UserStamp
{
    [Key] public int UserId { get; set; }
    public Guid PermissionsTag { get; set; }
    public Guid SettingsTag { get; set; }
    public DateTime? LastActiveAt { get; set; }
    public int InboxUnreadCount { get; set; }
    public int InboxUnseenCount { get; set; }
}

/// <summary>A key/value row of a user's settings bag; keyed naturally, saved by a dedicated statement.</summary>
[Table("UserSettings", Schema = "core")]
[TableType]
public class UserSetting
{
    public int UserId { get; set; }
    [Required, MaxLength(128)] public string Key { get; set; } = null!;
    [Required] public string Value { get; set; } = null!;
}

/// <summary>A user's channel choices for one notification type; absent means the type's default.</summary>
[Table("UserNotificationSettings", Schema = "core")]
[TableType]
public class UserNotificationSetting
{
    public int UserId { get; set; }
    [Required, MaxLength(64)] public string NotificationType { get; set; } = null!;
    public bool Email { get; set; }
    public bool Sms { get; set; }
    public bool Push { get; set; }
}

/// <summary>A role: a named bundle of permissions. Public roles apply to every active user.</summary>
[Table("Roles", Schema = "core")]
[TableType]
[Temporal]
public class Role : AuditedEntity, IActivatable, IMultilingual
{
    [Required, MaxLength(255)] public string Name { get; set; } = null!;
    [MaxLength(255)] public string? Name2 { get; set; }
    [MaxLength(255)] public string? Name3 { get; set; }
    [MaxLength(50)] public string? Code { get; set; }
    public bool IsPublic { get; set; }
    public bool IsActive { get; set; } = true;

    [NotMapped] public List<Permission>? Permissions { get; set; }
}

/// <summary>One grant of a role: resource, action, optional Queryex filter with its language stamp.</summary>
[Table("Permissions", Schema = "core")]
[TableType]
[Temporal]
[WeakEntity(ParentKey = nameof(RoleId))]
public class Permission : WeakEntity
{
    public int RoleId { get; set; }
    public Role? Role { get; set; }
    [Required, MaxLength(255)] public string Resource { get; set; } = null!;
    [Required, MaxLength(64)] public string Action { get; set; } = null!;
    [MaxLength(4000)] public string? Filter { get; set; }
    [ServerOwned] public int? FilterLanguageVersion { get; set; }
    [MaxLength(1024)] public string? Notes { get; set; }
}

/// <summary>A user's membership in a role; edited with the user.</summary>
[Table("RoleMemberships", Schema = "core")]
[TableType]
[Temporal]
[WeakEntity(ParentKey = nameof(UserId))]
public class RoleMembership : WeakEntity
{
    public int UserId { get; set; }
    public User? User { get; set; }
    public int RoleId { get; set; }
    public Role? Role { get; set; }
    [MaxLength(1024)] public string? Notes { get; set; }
}

/// <summary>Well-known ids in the reserved band.</summary>
public static class WellKnownIds
{
    public const int SystemUser = 1;
    public const int AdministratorRole = 1;
    public const int AdministratorPermission = 1;
}
```

### 3.6 Shapes this theme needs from other themes' seams

From the data-access theme (0011): `AuditedEntity` (int `Id` + the four audit columns), `WeakEntity`
(int `Id`) with `[WeakEntity(ParentKey)]`, `IActivatable`, `IMultilingual`, `[Temporal]`,
`[ServerOwned]`, `[TableType]` (spec 0001), `TellmaBatch` with: `Prologue(string sql, params)`;
`Guarded` statements wrapped in `IF @tm_Guard = 1 BEGIN … END`; per-statement `TableWrites`
declarations that drive `TagBumpRule`s; `IdList` TVP binding of affected root ids under `@tm_<Root>Ids`;
result readers indexed by statement; mapping of SQL errors 51000–51999 to `InvariantViolationException`;
the reserved parameter prefix `@tm_`; server-side `ModifiedAt = SYSUTCDATETIME()` stamping; the
"root stamped when any child changed" rule; the `FilterTree.Via` engine amendment. From the settings
theme (0012): `core.VersionTags (Name, Tag)` with a `Settings` row; `ITenantSettingsCache.ReloadAsync`
for the stale-tenant path; the cache infrastructure (`TagValidatedCache<T>`) if it ships one this
theme can reuse. From the service pipeline theme (0014): `ForbiddenException`,
`NotTenantMemberException`, `UserDeactivatedException`, `ConnectGuardException`,
`InvariantViolationException(code)`, `PermissionsTooComplexException`; the pipeline calls
`IAuthorizer.Require` before composing and applies the decision's filter to every read, pre-check, and
post-check. From the web theme (0015): the tenant route group carrying `RequireAuthorization()`, the
endpoint filter that performs the metadata check, the `Tellma-Tags` response header, the status
mapping (403 for not-member/deactivated/forbidden, 409 for guard, 422 for invariants). From the
host theme (0010): the scoped `IUserContext` holder populated by the tenant filter with the subject
claim, copied into background scopes. From the background-tasks theme (0019): the inbox item table
and the counter statements; the notification-type registry with "cannot mute" flags; the hub close
call.

---

## 4. Schema

All tables in schema `core`; all timestamps UTC `datetime2`; every foreign key named explicitly.

```sql
CREATE SEQUENCE [core].[sq_Users] AS int START WITH 1000 INCREMENT BY 1 CACHE 50;
CREATE SEQUENCE [core].[sq_Roles] AS int START WITH 1000 INCREMENT BY 1 CACHE 50;
CREATE SEQUENCE [core].[sq_Permissions] AS int START WITH 1000 INCREMENT BY 1 CACHE 200;
CREATE SEQUENCE [core].[sq_RoleMemberships] AS int START WITH 1000 INCREMENT BY 1 CACHE 200;
```

```sql
CREATE TABLE [core].[Users] (
    [Id]                 int            NOT NULL CONSTRAINT [PK_Users] PRIMARY KEY CLUSTERED,
    [Kind]               varchar(16)    NOT NULL CONSTRAINT [DF_Users_Kind] DEFAULT ('Human')
                                        CONSTRAINT [CK_Users_Kind] CHECK ([Kind] IN ('Human', 'System')),
    [Subject]            varchar(255)   COLLATE Latin1_General_100_BIN2 NULL,
    [Name]               nvarchar(255)  NOT NULL,
    [Name2]              nvarchar(255)  NULL,
    [Name3]              nvarchar(255)  NULL,
    [Email]              nvarchar(255)  NULL,
    [ImageId]            varchar(64)    NULL,
    [PreferredLanguage]  varchar(35)    NULL,
    [PreferredCalendar]  varchar(16)    NULL,
    [TimeZone]           varchar(64)    NULL,
    [ContactEmail]       nvarchar(255)  NULL,
    [ContactMobile]      varchar(32)    NULL,
    [InvitedAt]          datetime2(7)   NULL,
    [InvitationStatus]   varchar(16)    NULL
                                        CONSTRAINT [CK_Users_InvitationStatus] CHECK ([InvitationStatus] IN ('Invited', 'Reinvited', 'Active')),
    [InvitationError]    nvarchar(1024) NULL,
    [ActivatedAt]        datetime2(7)   NULL,
    [State]              AS (CONVERT(varchar(16),
                              CASE WHEN [ActivatedAt] IS NOT NULL THEN 'Active'
                                   WHEN [InvitedAt] IS NOT NULL THEN 'Invited'
                                   ELSE 'New' END)) PERSISTED,
    [IsActive]           bit            NOT NULL CONSTRAINT [DF_Users_IsActive] DEFAULT (1),
    [CreatedAt]          datetime2(7)   NOT NULL,
    [CreatedById]        int            NOT NULL CONSTRAINT [FK_Users_CreatedById] REFERENCES [core].[Users] ([Id]),
    [ModifiedAt]         datetime2(7)   NOT NULL,
    [ModifiedById]       int            NOT NULL CONSTRAINT [FK_Users_ModifiedById] REFERENCES [core].[Users] ([Id]),
    [ValidFrom]          datetime2(7)   GENERATED ALWAYS AS ROW START NOT NULL,
    [ValidTo]            datetime2(7)   GENERATED ALWAYS AS ROW END   NOT NULL,
    PERIOD FOR SYSTEM_TIME ([ValidFrom], [ValidTo]),
    CONSTRAINT [CK_Users_SystemHasNoSubject] CHECK ([Kind] <> 'System' OR ([Subject] IS NULL AND [Email] IS NULL)),
    CONSTRAINT [CK_Users_HumanHasEmail]      CHECK ([Kind] <> 'Human'  OR [Email] IS NOT NULL)
) WITH (SYSTEM_VERSIONING = ON (HISTORY_TABLE = [core].[UsersHistory]));

CREATE UNIQUE NONCLUSTERED INDEX [UX_Users_Subject] ON [core].[Users] ([Subject])
    INCLUDE ([Kind], [IsActive], [ActivatedAt]) WHERE [Subject] IS NOT NULL;   -- the prologue's seek, no key lookup
CREATE UNIQUE NONCLUSTERED INDEX [UX_Users_Email]   ON [core].[Users] ([Email]) WHERE [Email] IS NOT NULL;
```

```sql
CREATE TABLE [core].[UserStamps] (
    [UserId]            int              NOT NULL CONSTRAINT [PK_UserStamps] PRIMARY KEY CLUSTERED
                                         CONSTRAINT [FK_UserStamps_UserId] REFERENCES [core].[Users] ([Id]) ON DELETE CASCADE,
    [PermissionsTag]    uniqueidentifier NOT NULL,
    [SettingsTag]       uniqueidentifier NOT NULL,
    [LastActiveAt]      datetime2(0)     NULL,
    [InboxUnreadCount]  int              NOT NULL CONSTRAINT [DF_UserStamps_Unread] DEFAULT (0),
    [InboxUnseenCount]  int              NOT NULL CONSTRAINT [DF_UserStamps_Unseen] DEFAULT (0)
);
```

```sql
CREATE TABLE [core].[UserSettings] (
    [UserId]  int            NOT NULL CONSTRAINT [FK_UserSettings_UserId] REFERENCES [core].[Users] ([Id]) ON DELETE CASCADE,
    [Key]     nvarchar(128)  NOT NULL,
    [Value]   nvarchar(max)  NOT NULL,
    CONSTRAINT [PK_UserSettings] PRIMARY KEY CLUSTERED ([UserId], [Key])
);
```

```sql
CREATE TABLE [core].[UserNotificationSettings] (
    [UserId]            int          NOT NULL CONSTRAINT [FK_UserNotificationSettings_UserId] REFERENCES [core].[Users] ([Id]) ON DELETE CASCADE,
    [NotificationType]  varchar(64)  NOT NULL,
    [Email]             bit          NOT NULL,
    [Sms]               bit          NOT NULL,
    [Push]              bit          NOT NULL,
    CONSTRAINT [PK_UserNotificationSettings] PRIMARY KEY CLUSTERED ([UserId], [NotificationType])
);
```

```sql
CREATE TABLE [core].[Roles] (
    [Id]            int            NOT NULL CONSTRAINT [PK_Roles] PRIMARY KEY CLUSTERED,
    [Name]          nvarchar(255)  NOT NULL,
    [Name2]         nvarchar(255)  NULL,
    [Name3]         nvarchar(255)  NULL,
    [Code]          nvarchar(50)   NULL,
    [IsPublic]      bit            NOT NULL CONSTRAINT [DF_Roles_IsPublic] DEFAULT (0),
    [IsActive]      bit            NOT NULL CONSTRAINT [DF_Roles_IsActive] DEFAULT (1),
    [CreatedAt]     datetime2(7)   NOT NULL,
    [CreatedById]   int            NOT NULL CONSTRAINT [FK_Roles_CreatedById]  REFERENCES [core].[Users] ([Id]),
    [ModifiedAt]    datetime2(7)   NOT NULL,
    [ModifiedById]  int            NOT NULL CONSTRAINT [FK_Roles_ModifiedById] REFERENCES [core].[Users] ([Id]),
    [ValidFrom]     datetime2(7)   GENERATED ALWAYS AS ROW START NOT NULL,
    [ValidTo]       datetime2(7)   GENERATED ALWAYS AS ROW END   NOT NULL,
    PERIOD FOR SYSTEM_TIME ([ValidFrom], [ValidTo])
) WITH (SYSTEM_VERSIONING = ON (HISTORY_TABLE = [core].[RolesHistory]));

CREATE UNIQUE NONCLUSTERED INDEX [UX_Roles_Name]  ON [core].[Roles] ([Name]);
CREATE UNIQUE NONCLUSTERED INDEX [UX_Roles_Name2] ON [core].[Roles] ([Name2]) WHERE [Name2] IS NOT NULL;
CREATE UNIQUE NONCLUSTERED INDEX [UX_Roles_Name3] ON [core].[Roles] ([Name3]) WHERE [Name3] IS NOT NULL;
CREATE UNIQUE NONCLUSTERED INDEX [UX_Roles_Code]  ON [core].[Roles] ([Code])  WHERE [Code]  IS NOT NULL;
```

```sql
CREATE TABLE [core].[Permissions] (
    [Id]                     int            NOT NULL CONSTRAINT [PK_Permissions] PRIMARY KEY CLUSTERED,
    [RoleId]                 int            NOT NULL CONSTRAINT [FK_Permissions_RoleId] REFERENCES [core].[Roles] ([Id]),
    [Resource]               varchar(255)   NOT NULL,
    [Action]                 varchar(64)    NOT NULL,
    [Filter]                 nvarchar(4000) NULL,
    [FilterLanguageVersion]  int            NULL,
    [Notes]                  nvarchar(1024) NULL,
    [ValidFrom]              datetime2(7)   GENERATED ALWAYS AS ROW START NOT NULL,
    [ValidTo]                datetime2(7)   GENERATED ALWAYS AS ROW END   NOT NULL,
    PERIOD FOR SYSTEM_TIME ([ValidFrom], [ValidTo]),
    CONSTRAINT [CK_Permissions_FilterVersion] CHECK (([Filter] IS NULL) = ([FilterLanguageVersion] IS NULL)),
    CONSTRAINT [CK_Permissions_AllHasNoFilter] CHECK ([Resource] <> 'All' OR [Filter] IS NULL)
) WITH (SYSTEM_VERSIONING = ON (HISTORY_TABLE = [core].[PermissionsHistory]));

CREATE NONCLUSTERED INDEX [IX_Permissions_RoleId] ON [core].[Permissions] ([RoleId])
    INCLUDE ([Resource], [Action], [Filter], [FilterLanguageVersion]);
```

```sql
CREATE TABLE [core].[RoleMemberships] (
    [Id]        int            NOT NULL CONSTRAINT [PK_RoleMemberships] PRIMARY KEY CLUSTERED,
    [UserId]    int            NOT NULL CONSTRAINT [FK_RoleMemberships_UserId] REFERENCES [core].[Users] ([Id]),
    [RoleId]    int            NOT NULL CONSTRAINT [FK_RoleMemberships_RoleId] REFERENCES [core].[Roles] ([Id]),
    [Notes]     nvarchar(1024) NULL,
    [ValidFrom] datetime2(7)   GENERATED ALWAYS AS ROW START NOT NULL,
    [ValidTo]   datetime2(7)   GENERATED ALWAYS AS ROW END   NOT NULL,
    PERIOD FOR SYSTEM_TIME ([ValidFrom], [ValidTo])
) WITH (SYSTEM_VERSIONING = ON (HISTORY_TABLE = [core].[RoleMembershipsHistory]));

CREATE UNIQUE NONCLUSTERED INDEX [UX_RoleMemberships_UserId_RoleId] ON [core].[RoleMemberships] ([UserId], [RoleId]);
CREATE NONCLUSTERED INDEX        [IX_RoleMemberships_RoleId_UserId] ON [core].[RoleMemberships] ([RoleId], [UserId]);
```

Read by this theme, owned by the settings theme:

```sql
CREATE TABLE [core].[VersionTags] (
    [Name]  varchar(64)      NOT NULL CONSTRAINT [PK_VersionTags] PRIMARY KEY CLUSTERED,
    [Tag]   uniqueidentifier NOT NULL
);
-- seeded rows include ('Settings', <constant guid>)
```

Reserved for the background-tasks theme (columns to be fixed there):

```sql
CREATE TABLE [core].[UserPushSubscriptions] (
    [Id]        int           NOT NULL CONSTRAINT [PK_UserPushSubscriptions] PRIMARY KEY CLUSTERED,
    [UserId]    int           NOT NULL CONSTRAINT [FK_UserPushSubscriptions_UserId] REFERENCES [core].[Users] ([Id]) ON DELETE CASCADE,
    [Endpoint]  nvarchar(1024) NOT NULL,
    [P256dh]    varchar(256)  NOT NULL,
    [Auth]      varchar(256)  NOT NULL,
    [UserAgent] nvarchar(256) NULL,
    [CreatedAt] datetime2(7)  NOT NULL
);
```

Seeds (`HasData`, reserved band): `Users` `(1, 'System', 'System', IsActive 1, CreatedById 1, ModifiedById 1)`;
`UserStamps` `(1, '00000000-0000-0000-0000-000000000001', '00000000-0000-0000-0000-000000000002', 0, 0)`;
`Roles` `(1, 'Administrator', Code 'ADMIN')`; `Permissions` `(1, RoleId 1, 'All', 'All', NULL, NULL)`.

Hot-path index accounting for the subject prologue: `UX_Users_Subject` (covering; one seek, no key
lookup), `PK_UserStamps` (one seek), `PK_VersionTags` (one seek plus one small scan for result set 1);
the stale-permissions load adds `IX_Permissions_RoleId` seeks per role of the user and
`UX_RoleMemberships_UserId_RoleId` seeks.

---

## 5. Answers

| Brain-dump question (abridged) | Answer |
|---|---|
| "How do we best store, retrieve, and modify the user profile picture?" | `ImageId` on the row; blob mechanics are the blob theme's (D2). |
| "`ImageFitJson` here or in a centralized blob metadata table?" | Not read on any hot path here; the blob theme decides; this row carries only the key (D2). |
| "Which record + blobs pattern?" | Blob theme; nothing here depends on the choice. |
| "Is JSON the right shape for user preferences? Pinned screens in a distinct table?" | Key/value bag with JSON values, composite key; typed columns for language, calendar, time zone; pinned screens stay a key with a tenant-settings default (D17). |
| "What to store for notification settings; one JSON field?" | `ContactEmail`/`ContactMobile` on the row; a `(UserId, NotificationType)` child table with channel bits; push subscriptions in their own table (D18). |
| "Separate SettingsVersion and PermissionsVersion or one?" | Separate: `PermissionsTag` gates the batch, `SettingsTag` does not (D3, D5). |
| "Version, ETag, or fingerprint?" | Version tag (D1, D4). |
| "Shape of inbox tracking?" | Two materialised counters on `core.UserStamps` (D3). |
| "Are the user states exhaustive?" | For the tenant: `New`, `Invited`, `Active`, derived; finer states are read live from the identity server (D2). |
| "Write-once columns: two UDTTs or service rule?" | One UDTT; `[ServerOwned]` overwrite or pre-image validation in the service (D2). |
| "Global permissions: system role, `IsPublic`, or separate table?" | `IsPublic` on `Role` (D9). |
| "Do we need `SavedById` on the weak entities?" | No; the root is stamped whenever a child changes (D19, D20). |
| "How to keep hierarchyId in sync / cycles / CenterType" | Reference-stack and data-access themes; not touched here. |
| "Best convention to encode the Resource a permission secures?" | Queryex logical entity name, dotted PascalCase otherwise; `All` wildcard (D10). |
| "Is 'securable' the right word?" | Yes (D1). |
| "How to make registering securables and enforcing access hard to forget?" | Registry harvested from recipes, attributes, and endpoint metadata; startup audit over `EndpointDataSource`; authoritative check in the service, fail-fast copy in the endpoint filter (D10). |
| "Permissions invalidated by schema change: shim or block?" | Neither: fail closed per grant, surfaced in the editor, the counter, and the migrator report (D14). |
| "If permissions are cached, can DB calls #1 and #2 collapse?" | Yes, and further: the connect folds into every batch as a guarded prologue; a read is one round trip, a save two (D5, D6). |
| "Does user X have permission for Y on Z, subject to what filter, because of which roles?" | `Explain` (D15). |
| "What is a good alternative to RowVersion?" | Server-stamped `ModifiedAt` checked from a TVP inside the persist transaction (D20). |
| "How do we guarantee the cache version is invalidated when a cacheable entity is updated?" | Tag-bump rules keyed on declared table writes, emitted before the writes in the same transaction (D11); tenant-level rules are the settings theme's instance of the same mechanism. |
| "Is 'version' / 'metaversion' the accurate name?" | Version tag; format version (D1). |
| "A user cannot delete or deactivate their own user / strip their own admin permissions" | Self-deactivate and self-delete refused in memory; the last-administrator invariant in the transaction; self-narrowing allowed while another administrator remains (D13). |
| "Accessing a record I cannot read returns the same as non-existent" | The decision's filter is conjoined with every read; a filtered-out row is a 404 (D8; pipeline applies). |
| "Weak entities' permissions are those of the parent with paths adjusted" | `FilterTree.Via` engine node (D12). |

---

## 6. Seams

1. **Batch abstraction (T2 owns).** Needed: `Prologue` text with `@tm_` parameters; body statements
   wrapped as guarded; per-statement declared table writes driving `TagBumpRule`s (before-write and
   after-write hooks, same transaction); the affected-root-ids TVP under a predictable name; a reader
   that consumes the connect result sets by the connect row's flags; mapping of `THROW` numbers
   51000–51999 to `InvariantViolationException`; the persist body's transaction opened inside the
   guarded block, never around the prologue; retry eligibility: the prologue is always retry-safe.
2. **Entity class vs wire shape (T2/T5/T6).** `[NotMapped]` child lists on `User` and `Role`;
   `[ServerOwned]` on every column the pipeline owns (`Subject`, `Kind`, `InvitedAt`,
   `InvitationStatus`, `InvitationError`, `ActivatedAt`, `State`, `FilterLanguageVersion`);
   `ModifiedAt` echoed as the concurrency stamp.
3. **One capability, declared once (T5).** The activatable recipe contributes `(Entity, Activate,
   FilterRoot = Entity)` to the registry and the `Activate` action bypasses no tag rule; the CRUD
   recipe contributes `Read`, `Save`, `Delete`. `User` and `Role` are the first consumers.
4. **Queryex schema per tenant configuration (T2/T3).** The schema is a function of the tenant
   `Settings` tag; every batch guards on that tag; permission filters are validated against the schema
   at set build and rebuilt when the schema instance changes (the cache entry holds the schema
   reference it validated against and is refreshed when the settings theme swaps the schema).
5. **Version tags (T3).** GUID tags; `core.VersionTags` with a `Settings` row read by the prologue;
   per-user tags in `core.UserStamps`; bump rules as the one mechanism; format versions per cache kind.
6. **Feature composition (T1).** `ISecurableContributor` and `ITagBumpRuleSource` are contributed at
   realize; the securable audit joins `AddTellma`'s aggregated startup validation.
8. **Background-task columns (T10).** Nothing emitted here; the system user (`Id = 1`) and
   `ConnectAsSystemAsync`/`ConnectAsUserAsync` are what schedules run under; the inbox counters live
   on `core.UserStamps`.
9. **Request context (T1).** `IUserContext` (subject in; connected user out) is a member of the scoped
   holder; background scopes get it from the connector variants, never from `AsyncLocal`.
10. **Platform exceptions (T5/T6).** `NotTenantMemberException` → 403, `UserDeactivatedException` → 403,
    `ForbiddenException` → 403 (or 404 for filtered single-row reads, per the pipeline), `ConnectGuardException`
    → 409, `InvariantViolationException("Security.LastAdministrator")` → 422, `PermissionsTooComplexException` → 500.
11. **Permission evaluation API (this theme owns).** `IAuthorizer.Require(securable, queryRoot, bespoke)`
    → `PermissionDecision { IsAllowed, Filter, Because }`; `UserPermissionSet.Explain`.
14. **Telemetry (T2 owns the DB-call budget).** The prologue counts as part of its batch's one round
    trip; this theme's instruments are in D22.
15. **Notification enqueue riding the save batch (T10).** The counter increments on `core.UserStamps`
    are part of that statement; the channel resolution joins `core.UserNotificationSettings`.
16. **Connect-call collapse (this theme with T5).** Folded into every batch with a T-SQL guard; cold
    path one prologue-only round trip; stale path recompose once within the same runner call.
17. **Vocabulary.** Plural tables, `core` schema, four audit columns everywhere, `Notes`, `int` ids,
    reserved band `1..999`, `Activate` as the single activatable action, `All` wildcard, "version
    tag" / "format version" / "securable".

---

## 7. Departures

- **From ARCHITECTURE.md, none of substance.** The identity section's rule that no roles, permissions,
  or tenant membership appear in tokens is honoured; the data-layer rules (entity class as the source
  of shape, no parent→child EF navigations, every reference an FK, no persisted logic) are honoured —
  the prologue, bumps, and the lockout `THROW` are runtime-emitted SQL, the same category as Queryex
  and the save emitter. The startup validation gate gains the securable audit (additive). The
  endpoint projection "read → GET" is the web theme's question.
- **From the breakdown's wording:** "temporal top-level" bases become an additive `[Temporal]`
  capability on one `AuditedEntity` base (the orchestrator's hint), so `Users` and `Center` share one
  audit vocabulary.
- **From the brain dump:** no separate `OnConnect` call; `State` derived; `SavedAt`/`SavedById` replaced
  by the four audit columns; no `SavedById` on weak entities; `UserSettings` without a surrogate id;
  the self-narrowing lockout rule replaced by the last-administrator invariant; `LastActive` throttled.

---

## 8. Verification

Relied on from `research/users-roles-permissions.md` (verified 2026-09-01 there): native RLS mechanics
and the case against it (§1); ASP.NET Core 10 authorization middleware order, `AllowAnonymous` being
absolute, `IAuthorizationRequirementData` honoured on Minimal API endpoints only, route-group metadata
inheritance (§2); Odoo/Salesforce/Dataverse permission shapes and lockout guards (§3); GUID versus
`rowversion` versus `bigint` for tags, Guid v7 not sequential in SQL Server order (§4); the identity
server's invite and delivery-status contracts as implemented, the `sub` format and the dev admin's
fixed subject (§5). From the sibling research files: RCSI defaults and statement-level snapshots,
unique indexes as the uniqueness guarantee, the concurrency stamp check inside the persist batch
(`service-pipeline.md` §6); enum-as-string conventions and temporal write behaviour, including a
history row on every UPDATE (`data-access.md` §2, §7); `IMemoryCache`-based tag caches over
`HybridCache` and the Rails/Django precedents for tag and format version (`settings-cache-l10n.md`
§1.8, §6); scoped holder over `AsyncLocal`, route-group filters (`host-tenancy.md` §6, §7); the identity
integration facts and the legacy `Centers` schema (`core-gl-stacks.md` §3, §5.2).

Verified myself on 2026-09-01:

- Legacy monolith schema (`tellma-ltd/tellma`, `master`): `dbo.Users` (`ExternalId nvarchar(450)`,
  `State` persisted computed from `ExternalId`/`InvitedAt`, `PermissionsVersion`/`UserSettingsVersion`
  `uniqueidentifier DEFAULT NEWID()`, `LastAccess`, `LastInboxCheck`, `LastNotificationsCheck`,
  `PreferredChannel`, per-type notification bits, four audit columns, filtered unique `Email`);
  `dbo.Roles` (`IsPublic`, `IsActive`, `Code`, temporal with `SavedById`); `dbo.Permissions`
  (`View`, `Action`, `Criteria nvarchar(1024)`, `Mask`, `Memo`, temporal, cascade FK); `dbo.RoleMemberships`
  (`UserId`, `RoleId`, `Memo`, temporal, cascade FKs); `dbo.UserSettings` (`(UserId, Key)` clustered
  PK, `Value nvarchar(max)`); `dbo.Settings` (non-temporal; `SettingsVersion`, `DefinitionsVersion`,
  `SchedulesVersion` GUIDs); `dal.OnConnect` (reads the user by external id or email, stamps `LastAccess`
  when `@SetLastActive = 1`, returns one row with the user and tenant versions); `dal.Permissions__Load`
  (returns `PermissionsVersion`, then a `UNION` of role-membership permissions and public-role
  permissions filtered on `Roles.IsActive`). Sources:
  `https://raw.githubusercontent.com/tellma-ltd/tellma/master/Tellma.Database.Application/dbo/Tables/dbo.{Users,Roles,Permissions,RoleMemberships,UserSettings,Settings}.sql`,
  `…/dal/Stored Procedures/dal.OnConnect.sql`, `…/dal.Permissions__Load.sql`.
- OpenID Connect Core 1.0 §2: `sub` "MUST NOT exceed 255 ASCII characters" and "is a case-sensitive
  string" — basis for `varchar(255)` under a binary collation.
  `https://openid.net/specs/openid-connect-core-1_0.html`
- `MemoryCache.GetCurrentStatistics()` exists in the .NET 10 API (`Microsoft.Extensions.Caching.Memory`),
  returns null unless `MemoryCacheOptions.TrackStatistics` is true — basis for the cache gauges.
  `https://learn.microsoft.com/en-us/dotnet/api/microsoft.extensions.caching.memory.memorycache.getcurrentstatistics?view=net-10.0-pp`
- Temporal considerations page (ms.date 2026-08-18): row-level security predicates, indexes,
  constraints and triggers are not replicated to the history table; period columns must be
  `datetime2`; `INSTEAD OF` triggers not permitted; no cascade limitation is listed for SQL Server
  2017+ (the 2016-era restriction is absent from the current page), which is why cascades are used
  only on system-owned child rows and client-edited children are deleted explicitly — nothing here
  rests on cascade support.
  `https://learn.microsoft.com/en-us/sql/relational-databases/tables/temporal/considerations-limitations`

Unverified, reasoned from documented semantics:

- That an ad hoc parameterised batch whose text differs only in its guarded body reuses the plan of
  the prologue statements: plans are per batch text, so the prologue's statements are compiled once
  per distinct batch text — the claim in D5 is that the prologue adds no *further* fragmentation, not
  that it is compiled once per instance. A measurement on LocalDB (`sys.dm_exec_query_stats` grouped by
  `query_hash`) belongs in the integration suite.
- That `THROW` with numbers 51000–51999 under `XACT_ABORT ON` inside a single command text rolls back
  the open transaction and surfaces `SqlException.Number` unchanged to `Microsoft.Data.SqlClient` 6.1
  — standard T-SQL behaviour; to be pinned by the invariant integration test.
- The memory estimate per grant (~150 bytes plus interned filter) — to be measured; the gauge exists
  for that reason.
- Whether `Properties<Enum>().HaveConversion<string>()` covers `UserKind`/`UserState` on nullable
  properties in EF 10 — the data-access theme's convention test.
