# Spec: Assignments and Attention Surfaces

- **Author:** Ahmad Akra
- **Date:** 20 October 2026

**Status:** Ready for implementation. Frozen once merged: revised only if implementation forces a
design change, then kept as the historical record of what shipped — never updated thereafter as the
code or its dependencies evolve.

## Context

Work handed from one person to another needs a place to wait and a way to be noticed. Think of an
invoice sent for approval, or a voucher returned for correction. This spec ships:

- **`core.Assignments`**, a hand-off log. A module writes it with one line, in the batch that causes it.
- **The tray**, which shows each user what is waiting on them.
- **The client's attention surfaces**: the bell over spec 0021's inbox, the toast policy, the
  banner service, and four pages (Notifications, Notification settings, Assignments, Jobs).

The bell, the tray and the home lists render from two documents that spec 0025's leader tab keeps
fresh, so a warm launch needs no request.

Other specs that carry parts of this design:

- spec 0012 §5.2, §6.3 — the internal channel carries the assignment statements; `50422` lists `Assignments.AssigneeInvalid`.
- spec 0014 §2, §3.3, §5.3 — `CoreResources.Assignment`, `UserStamps.AssignmentsSeenAt`, the self-scope list.
- spec 0015 §2.2, §7.1, §12, §13.1, §13.3 — the `assignable` capability, `Assignments` on the contexts, the recipe row, the composer list.
- spec 0016 §2.2, §2.4, §4.3 — retry class, the route list, `NoActivityStampMetadata` on `assignments/summary`.
- spec 0018 §1.4 — Acme's assignable sales invoice with its `assign` and `post` actions.
- spec 0019 §12.2 — job labels set at enqueue; `fileId` on `core.export.ready`.
- spec 0020 §2.1, §2.3, §5.2, §5.3, §7.9 — job labels, live progress, the retention schedule (id 8).
- spec 0021 §2.3–§2.5, §5.2–§5.4, §6.3 — types and categories, the inbox summary with actors and job labels, the client protocol, `assignments.changed`.
- spec 0024 §2.2, §5.5, §9.2, §10.5 — the toast host's configuration, the update source, the `me/notifications` and open routes, `TmApi.online`.
- spec 0025 §2.3, §8.6, Appendix A — the two leader documents and the `notifications.toasts` key.
- spec 0026 §9.3, §10.1 — the `settings` page kind; the attention dump section.
- spec 0027 §3.2, §5.1, §7.2, Appendix A — launcher entries, the banner row, the slot order, the two shortcuts.
- spec 0030 §3.1, §12.2 — `selection` page actions in the selection bar.

## Goals / Non-goals

**Goals**

- A module hands a document to a person with one line; the rest needs no module code.
- A hand-off reaches every party's tray within a second of the commit, in every tab and device.
- Badges, panels and the home lists render from the store on a warm launch, with no request.
- One toast policy, and banners that never move the page under a keystroke or a click.
- The four pages are declarations on the search and details frameworks, plus a few cells.

**Non-goals (explicitly out of scope)**

- Operator and tenant-admin announcements — a later spec; `TmBanners` reserves level 2 and the key
  `announcements.dismissed` (spec 0025 Appendix A) for them.
- Assignment workflows (who may assign, approval chains, pooled or role-routed work), due dates and
  overdue reminders — the module that owns the document. This spec keeps one assignee per target.
- The toast host, banner bar, counter and progress ring — spec 0023.
- Leader election, the hub client and polling — spec 0025.
- Tenant markers — spec 0027 §7.4. The home page layout — spec 0035 §15.
- The Schedules, Exports, Imports and Outgoing emails pages — spec 0032.

## 1. Placement and vocabulary

The server half lives in `Tellma.Core`, beside notifications. The client half has an eager entry
point for buttons, badges and toasts, and a lazy one for the pages. `TmBanners` sits in the primary
entry point, because the primary's own code raises banners.

Contract blocks are TypeScript and C# sketches: names and shapes are normative; imports,
documentation comments, bodies and cancellation tokens are omitted.

- **Contracts** live in `Tellma.Core.Abstractions`, namespace `Tellma.Core.Abstractions.Assignments`:
  `Assignment`, `AssignmentState`, `AssignableAttribute`, `AssignmentRequest`, `AssignmentCaption`,
  `IAssignments`, `BatchAssignments`, `AssignmentsSummary`, `AssignmentTrail` and
  `AssignmentsOptions`. The table type `AssignmentRequestList` is in `.TableTypes`.
- **Runtime** lives in `Tellma.Core`, namespace `Tellma.Core.Assignments`: `AssignmentsWriter`,
  `AssignmentService`, `AssignmentAccessCriteria`, `AssignableDeleteEffect<T>`,
  `AssignmentTrailContributor<T>`, `AssignmentRetentionHandler` and the `[Assignable]` realizer. It
  has no SignalR reference; `CoreFeature` composes it (§4.8).
- **Tests** live in `test/core/Tellma.Core.Tests/Assignments/` and
  `test/core/Tellma.Core.IntegrationTests/Assignments/`.

| Client entry point | Members | Budget (gzip, own weight) |
|---|---|---|
| `@tellma/core` (primary) | `TmBanners` and its types (§9.1); the offline and update banner sources (§9.5); `TM_ATTENTION_DOCUMENTS`, the two `TM_LEADER_DOCUMENTS` entries (§5.1); `TM_TOAST_FEEDBACK_MS`, `TM_TOAST_ARRIVAL_MS` (§8.1); the core feature's page declarations (§10) | 2.5 KB |
| `@tellma/core/attention` (eager, modulepreloaded) | `TmBellButton`, `TmTrayButton`, `TmBannerHost`, `TmAttention`, `TmArrivals`, `TmAttentionText`, `TmAssignmentList`; the `@defer`red panels `TmBellPanel` and `TmTrayPanel` | 6 KB; the two panels 9 KB |
| `@tellma/core/attention-pages` (lazy) | `TmNotificationSettingsPage`, `TmNotificationOpenPage`, `TmNotificationTextCell`, `TmAssignmentTargetCell`, `TmJobLabelCell`, `TmJobProgressCell`, `TmJobDetails` | 10 KB, `/search` and `/details` excluded |
| `@tellma/core-testing` | `TmBellPanelHarness`, `TmTrayPanelHarness`, `TmBannerHostHarness`, `TmNotificationSettingsHarness`, `tmFakeInboxSummary()`, `tmFakeAssignmentsSummary()` | — |

- **Folders** under `client/projects/core/tellma-core/`: `src/attention/` (primary members),
  `attention/` and `attention-pages/`. Strings and telemetry use `tm.attention`.
- **Imports.** `/attention` imports only the primary. `/shell` and `/dashboards` import
  `/attention`. The primary's page declarations reach `/attention-pages` through `import()` (spec
  0024 §1). The panel chunks load at idle, and on hover or focus of their buttons, so a panel opens
  in the click's frame.

**Vocabulary**

- *Target* — the record an assignment is about, for example `acme.SalesInvoice` 23.
- *Assignment* — one row of `core.Assignments`: one hand-off of one target to one assignee.
- *Open assignment* — the target's current assignment. A target has at most one.
- *Hand-off* — an assign on a target that has an open assignment, which becomes `Handed`.
- *Return* — a hand-off back to whoever assigned the replaced row (§3.2).
- *Caption* — the language-neutral data a row is displayed by (§11.3).
- *Attention documents* — the leader documents `inbox` (spec 0021 §5.2) and `assignments` (§4.3).
- *Copy* — one stored version of an attention document, stamped with its `savedAt`.
- *Arrival* — a row that appears in an attention document while the app is open (§8.2).
- *Seen overlay* — a tab's local state that hides or neutralizes a badge, from the moment its panel
  opens until a refreshed copy arrives (§5.3).
- *Banner* — one entry of `TmBanners`.
- *The row* — the shell's 32 px banner row (spec 0027 §5.1).
- *Idle gate* — the 2 s without input that a change of the row's height waits for (§9.3).

## 2. `core.Assignments` and the `Assignment` entity

The table is a hand-off log. Each assign closes the target's open row and inserts a new one, so
"assigned by me", "returned to me" and a target's trail are plain reads.

### 2.1 The table

- **Shape.** Non-temporal, no version tag, `[TableType]` (fixture tier only). Ids come from the
  sequence `core.sq_Assignments START WITH 1000`.
- **Writers.** Only §3's statements and §4.7's retention write it.
- **No foreign key to the target.** The delete effect (§3.4) keeps targets consistent instead.
- **No `ON DELETE CASCADE`.** A user is never deleted after `New` (spec 0014 §3.9).

| Column | Type | Null | Constraints | Notes |
|---|---|---|---|---|
| `Id` | `int` | no | PK clustered; `CK_Assignments_Id CHECK ([Id] > 0)` | reserved inside the assign statement (§3.2) |
| `Resource` | `varchar(128)` | no | | the target's entity name, an `[Assignable]` stack resource |
| `TargetId` | `bigint` | no | | the target row's id |
| `State` | `varchar(9)` | no | | `AssignmentState` as text |
| `AssigneeId` | `int` | no | `FK_Assignments_AssigneeId → core.Users(Id)` | an active individual at assign time |
| `AssignedById` | `int` | yes | `FK_Assignments_AssignedById → core.Users(Id)` | null: the system |
| `AssignedAt` | `datetimeoffset(3)` | no | | the hand-off time |
| `IsReturn` | `bit` | no | `DF_Assignments_IsReturn 0` | §3.2 |
| `Comment` | `nvarchar(1024)` | yes | | the assigner's note |
| `CaptionJson` | `nvarchar(4000)` | yes | | §11.3 |
| `ClosedAt` | `datetimeoffset(3)` | yes | `CK_Assignments_Closed` (below) | |
| `ClosedById` | `int` | yes | `FK_Assignments_ClosedById → core.Users(Id)` | who handed on, completed or withdrew; null: the system |

`CK_Assignments_Closed` ties `ClosedAt` to the state:
`((State = 'Open' AND ClosedAt IS NULL) OR (State <> 'Open' AND ClosedAt IS NOT NULL))`.

| Index | Key | Include | Filter | Serves |
|---|---|---|---|---|
| `UX_Assignments_OpenTarget` (unique) | `(Resource, TargetId)` | | `State = 'Open'` | one open row per target; the close statements' seek |
| `IX_Assignments_Assignee` | `(AssigneeId, AssignedAt DESC, Id DESC)` | `(State, AssignedById)` | | tray counts and `Latest`; the "to me" tab |
| `IX_Assignments_Assigner` | `(AssignedById, AssignedAt DESC, Id DESC)` | `(State, AssigneeId)` | `AssignedById IS NOT NULL` | `OpenByMe`, `LatestByMe`; the "by me" tab |
| `IX_Assignments_Target` | `(Resource, TargetId, AssignedAt DESC)` | | | the trail extra (§4.4) |
| `IX_Assignments_Retention` | `(ClosedAt)` | | `State <> 'Open'` | retention pages |

### 2.2 The entity and its stack

```csharp
// Tellma.Core.Abstractions.Assignments
public enum AssignmentState { Open, Handed, Completed, Withdrawn }

[Table("Assignments", Schema = "core"), TableType]
[Stack(Operations = Query | Details, DefaultOrderBy = "AssignedAt desc, Id desc")]
public sealed class Assignment : Entity<int>        // every member is server-owned, never written through Save
{
    public string Resource { get; set; }            // the target's entity name
    public long TargetId { get; set; }
    public AssignmentState State { get; set; }
    public int AssigneeId { get; set; }             // navigation Assignee
    public int? AssignedById { get; set; }          // navigation AssignedBy
    public DateTimeOffset AssignedAt { get; set; }  // datetimeoffset(3)
    public bool IsReturn { get; set; }
    public string? Comment { get; set; }
    public string? CaptionJson { get; set; }        // not [JsonColumn] (nvarchar(4000)); the client parses it
    public DateTimeOffset? ClosedAt { get; set; }   // datetimeoffset(3)
    public int? ClosedById { get; set; }            // navigation ClosedBy
}
```

- **System-written** (spec 0012 §2.1): no audit set and no concurrency token. `[DefaultSelect]` is
  every column; the navigations use spec 0012 §2.5's default related projection.
- **A read-only stack.** Segment `assignments`, resource `core.Assignment` (spec 0014 §2). It has
  `query`, `get` and `get-by-ids` only, so MCP sees it read-only.
- **Self-scope.** Every member reads the rows assigned to them and by them, with no role.
  `AssignmentAccessCriteria : IAccessCriteriaProvider` (`Resource = "core.Assignment"`) returns
  this criterion for every user:
  `AccessCriterion("Read", FilterTree.Leaf("AssigneeId = me() or AssignedById = me()"), "self")`.
- **Wider reads.** A stored grant on `core.Assignment × Read` widens the scope (a supervisor seeing
  who is sitting on what). None is seeded.

### 2.3 States and transitions

| From | To | By |
|---|---|---|
| (none) | `Open` | an assign |
| `Open` | `Handed` | the next assign of the same target; `ClosedById` is the new row's assigner |
| `Open` | `Completed` | a complete: the work is done (posted, approved, closed) |
| `Open` | `Withdrawn` | a withdraw (recalled, cancelled), and the delete effect (§3.4) |

- **Closed states are terminal.** Nothing reopens a row. Assigning a target whose assignment
  completed starts a new `Open` row.
- **Deactivating a user changes nothing.** Their open rows stay, so the work is not lost, and the
  module reassigns it. The Assignments page's All tab, filtered on `Assignee.IsActive`, finds them.

## 3. Writing assignments

The module that owns a target writes its assignments in the same batch as the change that causes
them. It calls `IAssignments`, or the one-line shortcut on its persist or action context. The
platform turns each call into a fixed-text statement on the internal channel, as `INotifier` does
for notifications.

### 3.1 Contracts

```csharp
// Tellma.Core.Abstractions.Assignments
[AttributeUsage(AttributeTargets.Class)]
public sealed class AssignableAttribute : Attribute;          // on the entity: the capability "assignable" (§3.4)

public sealed record AssignmentCaption(
    string? Code = null,                                      // the target's short reference ("INV-0023"); null: "#{TargetId}"
    decimal? Amount = null, string? Currency = null,          // rendered "SAR 12,400.00"; Currency an ISO 4217 code
    object? Detail = null);                                   // ICU arguments of Assignment_<Resource>_Detail (§11.3)

public sealed record AssignmentRequest(
    string Resource, long TargetId, int AssigneeId, string? Comment = null, AssignmentCaption? Caption = null,
    int? AssignedById = null);                                // null: RequestContext.UserId in a user scope, the system in a system scope

public interface IAssignments                                 // scoped
{
    void Assign(IDataBatch batch, IReadOnlyList<AssignmentRequest> requests);        // appends to the caller's batch
    void Complete(IDataBatch batch, string resource, IReadOnlyList<long> targetIds);
    void Withdraw(IDataBatch batch, string resource, IReadOnlyList<long> targetIds);
    Task AssignAsync(IReadOnlyList<AssignmentRequest> requests);              // own Persist batch, current scope
    Task CompleteAsync(string resource, IReadOnlyList<long> targetIds);
    Task WithdrawAsync(string resource, IReadOnlyList<long> targetIds);
}

// PersistContext<T>.Assignments and ActionContext<T>.Assignments (spec 0015 §7.1, §13.1)
public sealed class BatchAssignments                          // each method binds the context's Batch and its stack's Resource
{
    public void Assign(long targetId, int assigneeId, string? comment = null, AssignmentCaption? caption = null);
    public void Complete(IReadOnlyList<long> targetIds);
    public void Withdraw(IReadOnlyList<long> targetIds);
}

public sealed class AssignmentsOptions                         // Tellma:Assignments; validated at startup
{
    public int MaxRequestsPerCall { get; set; } = 10000;      // requests or target ids per call
    public int MaxCommentChars { get; set; } = 1024;          // ≤ 1024 (the column)
    public int MaxCaptionChars { get; set; } = 4000;          // serialized caption; ≤ 4000 (the column)
    public int SummaryCountCap { get; set; } = 100;           // where the summary's counts stop; ≤ 1000
    public int SummaryLatestCount { get; set; } = 10;         // rows in each summary list; ≤ 50
    public TimeSpan ClosedRetention { get; set; } = TimeSpan.FromDays(180);   // closed rows older than this are deleted (§4.7)
    public int RetentionPageSize { get; set; } = 1000;        // rows per retention batch; ≤ 10000
}
```

- **Synchronous and append-only.** Each call appends one statement (§3.2, §3.3), registers the
  batch's one post-commit callback (§4.5) and returns. An empty list appends nothing.
- **Programmer errors** fail the call with `InvalidOperationException` (a 500 with a trace id),
  before anything is appended:
  - a `Resource` that is not an `[Assignable]` stack's;
  - a `TargetId` or `AssigneeId` that is not positive;
  - the same target twice in one call;
  - a comment or serialized caption over its cap;
  - more than `MaxRequestsPerCall` requests or ids.
- **Where it runs.** Hooks and `[EntityAction]` methods use the context's `Assignments`. A job
  handler or an `[ApiAction]` passes its own batch to `IAssignments`. Code with no batch uses the
  `*Async` forms. The internal channel is outside `CallerAuthority` (spec 0012 §5.2), so a job may
  write any stack's assignments.

### 3.2 The assign statement

One statement serves each `Assign` call. It checks the assignees, closes the targets' open rows as
`Handed`, inserts the new rows and returns the users to notify.

Its input `@tb{b}_t0 : AssignmentRequestList` holds one row per request: `Ordinal int PK` from 0,
then `Resource`, `TargetId`, `AssigneeId`, `AssignedById`, `Comment` and `CaptionJson`, typed as
the table's columns. The options are `SqlOptions(Writes = [core.Assignments], ResultSets = 1)`.

```sql
-- ASSIGN (IAssignments.Assign; ids reserved here, never from the allocator's buffer)
DECLARE @tb{b}_now datetimeoffset(3) = SYSUTCDATETIME();
IF EXISTS (SELECT 1 FROM @tb{b}_t0 AS [r] LEFT JOIN [core].[Users] AS [u] ON [u].[Id] = [r].[AssigneeId]
           WHERE [u].[Id] IS NULL OR [u].[IsActive] = 0 OR [u].[Kind] <> 'Individual')
    THROW 50422, N'Assignments.AssigneeInvalid', 1;
DECLARE @tb{b}_closed TABLE ([Resource] varchar(128) NOT NULL, [TargetId] bigint NOT NULL, [AssigneeId] int NOT NULL,
                             [AssignedById] int NULL, PRIMARY KEY ([Resource], [TargetId]));
UPDATE [a] SET [State] = 'Handed', [ClosedAt] = @tb{b}_now, [ClosedById] = [r].[AssignedById]
OUTPUT [deleted].[Resource], [deleted].[TargetId], [deleted].[AssigneeId], [deleted].[AssignedById] INTO @tb{b}_closed
FROM [core].[Assignments] AS [a]
INNER JOIN @tb{b}_t0 AS [r] ON [r].[Resource] = [a].[Resource] AND [r].[TargetId] = [a].[TargetId]
WHERE [a].[State] = 'Open';
DECLARE @tb{b}_first sql_variant, @tb{b}_n int = (SELECT COUNT(*) FROM @tb{b}_t0);
EXEC sys.sp_sequence_get_range @sequence_name = N'core.sq_Assignments', @range_size = @tb{b}_n, @range_first_value = @tb{b}_first OUTPUT;
INSERT INTO [core].[Assignments] ([Id], [Resource], [TargetId], [State], [AssigneeId], [AssignedById], [AssignedAt], [IsReturn], [Comment], [CaptionJson])
SELECT CAST(@tb{b}_first AS int) + [r].[Ordinal], [r].[Resource], [r].[TargetId], 'Open', [r].[AssigneeId], [r].[AssignedById], @tb{b}_now,
       CASE WHEN [c].[AssignedById] = [r].[AssigneeId] THEN 1 ELSE 0 END, [r].[Comment], [r].[CaptionJson]
FROM @tb{b}_t0 AS [r]
LEFT JOIN @tb{b}_closed AS [c] ON [c].[Resource] = [r].[Resource] AND [c].[TargetId] = [r].[TargetId];
SELECT [AssigneeId] AS [UserId] FROM @tb{b}_t0
UNION SELECT [AssignedById] FROM @tb{b}_t0 WHERE [AssignedById] IS NOT NULL
UNION SELECT [AssigneeId] FROM @tb{b}_closed
UNION SELECT [AssignedById] FROM @tb{b}_closed WHERE [AssignedById] IS NOT NULL;   -- the event's recipients (§4.5)
```

- **The assignee check** runs first. A missing, inactive or non-individual assignee aborts the
  batch with `50422`, which spec 0015 §14.2 reports as `Assignments.AssigneeInvalid` at the root.
  This check is the fence; a module that wants a field-level message validates earlier.
- **`IsReturn`** is 1 when the new assignee is the one who assigned the row being replaced.
  **Example —** Farah assigns invoice 23 to Omar, and Omar assigns it back. Farah's new row reads
  "Returned by Omar Haddad".
- **The same assignee.** Assigning a target to its current assignee is still a hand-off, so the row
  is new again in the tray.
- **Races.** The `UPDATE` locks the open row, so two hand-offs of one target serialize. When two
  batches both find no open row, the second insert raises 2601 on `UX_Assignments_OpenTarget`.
  Spec 0015 §14.2 maps it to `Unique` at the root, and the user retries.

### 3.3 The close statement (complete, withdraw)

One statement serves each `Complete` or `Withdraw` call. It closes the targets' open rows and
returns the users to notify. It is idempotent: a target with no open row is left alone.

Its input `@tb{b}_t0 : BigIdList` holds the target ids. `@tb{b}_p0 varchar(128)` is the resource,
`@tb{b}_p1 varchar(9)` is `Completed` or `Withdrawn`, and `@tb{b}_p2 int` is the actor
(`RequestContext.UserId`, null in a system scope). The options are as in §3.2.

```sql
-- CLOSE (IAssignments.Complete / Withdraw; the delete effect binds the deleted-key table instead of @tb{b}_t0)
DECLARE @tb{b}_users TABLE ([AssigneeId] int NOT NULL, [AssignedById] int NULL);
UPDATE [a] SET [State] = @tb{b}_p1, [ClosedAt] = SYSUTCDATETIME(), [ClosedById] = @tb{b}_p2
OUTPUT [deleted].[AssigneeId], [deleted].[AssignedById] INTO @tb{b}_users
FROM [core].[Assignments] AS [a] INNER JOIN @tb{b}_t0 AS [t] ON [t].[Id] = [a].[TargetId]
WHERE [a].[Resource] = @tb{b}_p0 AND [a].[State] = 'Open';
SELECT [AssigneeId] AS [UserId] FROM @tb{b}_users UNION SELECT [AssignedById] FROM @tb{b}_users WHERE [AssignedById] IS NOT NULL;
```

### 3.4 `[Assignable]`: the capability, the delete effect, the trail extra

`[Assignable]` on an entity is the whole opt-in. Its realizer adds three things: the capability
`assignable` (spec 0015 §2.2, §12), a delete effect and a trail extra. The capability reaches the
client's entity summary (spec 0029 §2.2).

- **The delete effect.** `AssignableDeleteEffect<T> : IPersistEffect<T>` acts on every
  `DeletePersistContext<T>`: by ids, by query, with descendants, or enlisted. It appends §3.3's
  statement with `Withdrawn`, bound to `context.AffectedIds`. The tray never shows a deleted target.
- **The trail extra.** `AssignmentTrailContributor<T> : IDetailsContributor<T>` answers the extra
  `assignments` (§4.4), so a details page can show "With Omar Haddad since 2 h" with no module code.
- **Startup check `assignments`.** It verifies that every `[Assignable]` entity has `Query` and
  `Details`, which `TmUrls.target` opens, and that the options are within bounds.
- **Ordering.** Statements run where they are appended, so an assign follows the module's own
  write. Two assigns of one target in one batch are two hand-offs.

### 3.5 What a distribution writes

A distribution puts `[Assignable]` on the class. Then it writes one line where the document is
handed on, and one where the work ends. **Example —** Acme's sales invoice (spec 0018 §1.4):

```csharp
public sealed record AssignInvoiceArguments(
    [property: EntityRef("core.User")] int AssigneeId, string? Comment);   // the generated dialog shows a user picker

[EntityAction("assign")]                                    // POST /{t}/api/web/sales-invoices/assign { ids, arguments }
public Task AssignAsync(ActionContext<SalesInvoice, int> context, AssignInvoiceArguments args)
{
    foreach (var invoice in context.Entities)
        context.Assignments.Assign(invoice.Id, args.AssigneeId, args.Comment,
            new AssignmentCaption(Code: invoice.SerialNumber.ToString(CultureInfo.InvariantCulture)));
    return Task.CompletedTask;
}

// in the post action, after its own statements:
context.Assignments.Complete(context.Entities.Select(i => (long)i.Id).ToList());
```

Optional: a resource `Assignment_<Resource>_Detail` per language (§11.3), and a channel-only
notification type for email (§3.6). The platform supplies the rest: delete cleanup, the tray,
badges, toasts, home lists, pages, events and the assign dialog.

### 3.6 Assignment email (the hatch)

The platform raises no notification for an assignment. A module that wants email registers a
channel-only type (`Inbox = false`, spec 0021 §2.3) and raises it in the same batch. The type's
switches show on the Notification settings page (§10.3), and its text is the module's own resource.
**Example —** inside §3.5's assign loop:

```csharp
context.Notify(new NotificationRequest("acme.invoice.assigned", [args.AssigneeId],
    new { number = invoice.SerialNumber, comment = args.Comment },
    "acme.SalesInvoice", invoice.Id, context.Context.UserId, DedupKey: null));
```

## 4. Reading assignments: the stack, the summary, seen, events, retention

The standard stack serves the Assignments page. Two member-only actions, `summary` and `seen`,
serve the tray and the home lists, the way spec 0021's inbox serves the bell.

### 4.1 `AssignmentService` and its routes

```csharp
// Tellma.Core.Assignments (runtime)
// POST /{t}/api/web/assignments/{query,get,get-by-ids,summary,seen}, all retried (spec 0016 §2.2)
public sealed class AssignmentService : EntityService<Assignment>     // Operations = Query | Details, self-scoped
{
    [ApiAction("summary", MemberOnly = true, Idempotent = true, Mutation = false)]   // NoActivityStampMetadata (§4.3); no body
    public Task<AssignmentsSummary> SummaryAsync();
    [ApiAction("seen", MemberOnly = true, Idempotent = true)]
    public Task<AffectedResult> SeenAsync();                           // no body; Count = 1
}
```

### 4.2 `AssignmentsSeenAt`

`core.UserStamps.AssignmentsSeenAt` (spec 0014 §3.3) is written only by `assignments/seen`. A row
is *new* when it is open, was assigned to the caller after `AssignmentsSeenAt` (any time, while that
is null), and was not assigned by the caller.

```sql
-- assignments/seen (Persist; Writes = core.UserStamps); publishes assignments.changed to [caller] on the batch
UPDATE [core].[UserStamps] SET [AssignmentsSeenAt] = SYSUTCDATETIME() WHERE [UserId] = @tm_UserId;
```

### 4.3 The summary

`assignments/summary` returns the tray's counts and both home lists in one call.

```csharp
// Tellma.Core.Abstractions.Assignments
public sealed record AssignmentsSummary(
    int Open, int New, int OpenByMe,                 // capped at SummaryCountCap ("99+" in the UI)
    DateTimeOffset? SeenAt,                          // the caller's AssignmentsSeenAt
    IReadOnlyList<Assignment> Latest,                // ≤ SummaryLatestCount open rows assigned to the caller, newest first
    IReadOnlyList<Assignment> LatestByMe,            // ≤ SummaryLatestCount open rows the caller assigned to others, newest first
    RelatedEntities Related);                        // core.User: the assigners of Latest, the assignees of LatestByMe
```

The summary is one `Read` batch through spec 0014 §7's `IGuardedBatchRunner.Run(Read, …)`. Its
statement has `ResultSets = 2`, `@tb{b}_p0 = SummaryCountCap` and `@tb{b}_p1 = SummaryLatestCount`:

```sql
DECLARE @tb{b}_seenAt datetimeoffset(3) = (SELECT [AssignmentsSeenAt] FROM [core].[UserStamps] WHERE [UserId] = @tm_UserId);
SELECT
  (SELECT COUNT(*) FROM (SELECT TOP (@tb{b}_p0) 1 AS [x] FROM [core].[Assignments]
     WHERE [AssigneeId] = @tm_UserId AND [State] = 'Open') AS [o]) AS [Open],
  (SELECT COUNT(*) FROM (SELECT TOP (@tb{b}_p0) 1 AS [x] FROM [core].[Assignments]
     WHERE [AssigneeId] = @tm_UserId AND [State] = 'Open' AND [AssignedAt] > COALESCE(@tb{b}_seenAt, '0001-01-01')
       AND ([AssignedById] IS NULL OR [AssignedById] <> @tm_UserId)) AS [n]) AS [New],
  (SELECT COUNT(*) FROM (SELECT TOP (@tb{b}_p0) 1 AS [x] FROM [core].[Assignments]
     WHERE [AssignedById] = @tm_UserId AND [State] = 'Open' AND [AssigneeId] <> @tm_UserId) AS [b]) AS [OpenByMe],
  @tb{b}_seenAt AS [SeenAt];
DECLARE @tb{b}_rows TABLE ([List] tinyint NOT NULL, [Id] int NOT NULL PRIMARY KEY, [UserId] int NULL);
INSERT INTO @tb{b}_rows SELECT TOP (@tb{b}_p1) 0, [Id], [AssignedById] FROM [core].[Assignments]
  WHERE [AssigneeId] = @tm_UserId AND [State] = 'Open' ORDER BY [AssignedAt] DESC, [Id] DESC;
INSERT INTO @tb{b}_rows SELECT TOP (@tb{b}_p1) 1, [Id], [AssigneeId] FROM [core].[Assignments]
  WHERE [AssignedById] = @tm_UserId AND [State] = 'Open' AND [AssigneeId] <> @tm_UserId ORDER BY [AssignedAt] DESC, [Id] DESC;
SELECT [r].[List], [a].* FROM @tb{b}_rows AS [r] INNER JOIN [core].[Assignments] AS [a] ON [a].[Id] = [r].[Id]
ORDER BY [r].[List], [a].[AssignedAt] DESC, [a].[Id] DESC;
DECLARE @tb{b}_users TABLE ([Id] int NOT NULL PRIMARY KEY);
INSERT INTO @tb{b}_users SELECT DISTINCT [UserId] FROM @tb{b}_rows WHERE [UserId] IS NOT NULL;
```

- **The users** collected in `@tb{b}_users` reach `Related` the way spec 0021 §5.3's actors do: an
  appended `Query<User>` with `KeySetRestriction("Id", "@tb{b}_users")` (spec 0010 §F.1), the User
  stack's `RelatedSelect`, and no row filter.
- **Cost.** Every count and list is a capped seek on the assignee or assigner index, and a warm
  call is one round trip. A plan test pins every seek (§12).
- **No activity stamp.** The route carries `NoActivityStampMetadata` (spec 0016 §4.3), so polling
  keeps no session alive.
- **Self-assigned rows** count in `Open` and `Latest`, never in `New`, `OpenByMe` or `LatestByMe`.

**Example —** Farah's summary on the wire (nulls omitted):

```json
{ "open": 4, "new": 1, "openByMe": 2, "seenAt": "2026-10-20T07:58:01.120+00:00", "latestByMe": [ … ],
  "latest": [ { "id": 1874, "resource": "acme.SalesInvoice", "targetId": 23, "state": "Open", "assigneeId": 42, "assignedById": 7,
                "assignedAt": "2026-10-20T08:12:44.501+00:00", "isReturn": false, "comment": "For your kind approval", "captionJson": "{\"code\":\"23\"}" } ],
  "related": { "core.User": [ { "id": 7, "name": "Sara Ahmed", "name2": "سارة أحمد", "imageId": 812 } ] } }
```

### 4.4 The trail extra

A details read can ask for a target's hand-off history. `include: ["assignments"]` on a `get` of
any assignable stack returns `extras.assignments`.

```csharp
public sealed record AssignmentTrail(IReadOnlyList<Assignment> Rows, RelatedEntities Related); // ≤ 20 newest rows of the target, any state
```

- **Access.** Like every extra, the trail is read under the target's `Read` decision (spec 0014
  §5.3). Whoever reads the document reads its trail, comments included.
- **Cost.** One `Rows` statement on `IX_Assignments_Target` and the users' query ride the details
  batch, so the trail adds no round trip.

### 4.5 The `assignments.changed` event

After a commit that changed assignments, every party receives `assignments.changed`, and their
leader tabs refresh the `assignments` document. `CoreFeature` registers the event with no payload
(spec 0021 §6.3).

1. The first assign or close on a batch registers one `OnCommitted` callback.
2. After the commit, the callback unions the user ids every statement returned: new and old
   assignees, and the assigners of new and closed rows.
3. It calls `IClientEventPublisher.PublishAsync(new ClientEvent("assignments.changed", userIds, null))`.
   A failure is logged (`AssignmentsEventFailed`), never thrown.

`seen` publishes to the caller alone.

### 4.6 Securables and MCP

The stack registers one securable pair, `core.Assignment × Read`. Every member holds it through
the self-scope (§2.2). Nothing is sensitive, and `summary` and `seen` are member-only. MCP's
`tellma_query` and `tellma_get` over `core.Assignment` answer under the self-scope.

### 4.7 Retention

A daily job deletes closed rows older than `ClosedRetention`; open rows are never deleted. Its
handler `[JobHandler("core.assignment-retention", LeaseSeconds = 600)]` runs one `Maintenance`
batch per page, until a page deletes fewer than `RetentionPageSize` rows. The schedule is built-in
id 8, `15 4 * * *` (spec 0020 §7.9).

```sql
-- one retention page (@tb{b}_p0 = RetentionPageSize, @tb{b}_p1 = ClosedRetention in seconds)
DELETE TOP (@tb{b}_p0) FROM [core].[Assignments]
WHERE [State] <> 'Open' AND [ClosedAt] < DATEADD(second, -@tb{b}_p1, SYSUTCDATETIME());
```

### 4.8 Composition, telemetry, logs

- **`CoreFeature` contributes** `Entity<Assignment, AssignmentService>()`, `Scoped<IAssignments,
  AssignmentsWriter>()`, the access criteria, the realizer, the client event, the retention handler
  and schedule, `Options<AssignmentsOptions>`, the table type and the startup check.
- **Meter `Tellma.Core`.** `tellma.assignments.written`, tagged `op` (`assign`, `hand-off`,
  `complete`, `withdraw`) and `resource`; `tellma.assignments.summaries`;
  `tellma.assignments.retained`. None is tagged by tenant or user.
- **Logs.** `AssignmentsWritten` (debug) and `AssignmentsEventFailed`.

## 5. The client attention model

Every tab renders the bell, the tray and the home lists from the two stored attention documents.
Only the leader tab refreshes them on events. A tab that opens a panel refreshes that panel's
document once itself (§5.3).

### 5.1 Leader documents

`provideTellma` adds the two documents to spec 0025 §8.6's registry, whose rule says when they
refresh. A warm launch renders the badges from them before any call. On the client, `assignments`
is §4.3's record in camelCase, its rows typed `TmEntityType<'core.Assignment'>` (spec 0029 §15).

```ts
// @tellma/core (primary)
const TM_ATTENTION_DOCUMENTS: readonly TmLeaderDocument[] = [
  { name: 'inbox', route: 'inbox/summary', events: ['inbox.changed', 'job.changed'], formatVersion: 1 },
  { name: 'assignments', route: 'assignments/summary', events: ['assignments.changed'], formatVersion: 1 },
];   // formatVersion bumps when a stored shape changes
```

### 5.2 `TmAttention`

`TmAttention` turns the two documents into badges and runs the attention verbs. Each verb applies
in the same frame through a local overlay, then calls the server.

```ts
// @tellma/core/attention
type TmPanel = 'bell' | 'tray';
// the badges follow the table below; label is the button's accessible name
interface TmBellBadge { readonly count: number | null; readonly tone: 'accent' | 'attention'; readonly jobsRunning: number; readonly label: string }
interface TmTrayBadge { readonly count: number | null; readonly tone: 'accent' | 'neutral'; readonly label: string }
declare class TmAttention {                                   // root of the tenant shell's injector
  readonly inbox: Signal<InboxSummary | undefined>;           // TmLaunchCache.docs.inbox with this tab's overlays applied
  readonly assignments: Signal<AssignmentsSummary | undefined>;
  readonly bell: Signal<TmBellBadge>; readonly tray: Signal<TmTrayBadge>;
  readonly open: Signal<TmPanel | null>;                      // the open panel, if any
  readonly live: Signal<'live' | 'paused'>;                   // paused: hubStatus neither connected nor disabled for 5 s
  readonly writable: Signal<boolean>;                         // the tenant is Active and TmApi.online() (§5.4)
  toggle(panel: TmPanel, via: 'click' | 'shortcut' | 'toast'): void;
  refresh(document: 'inbox' | 'assignments'): Promise<void>;  // §5.3 step 3 alone
  read(ids: readonly number[]): Promise<void>; readAll(): Promise<void>;          // these four: overlay, then
  clear(ids: readonly number[]): Promise<void>; clearRead(): Promise<void>;       // spec 0021 §5.2's verb
  cancelJob(id: number): Promise<void>;                       // jobs/cancel { ids: [id] } (spec 0020 §11.1)
}
```

- **Overlays, not store writes.** `read`, `readAll`, `clear` and `clearRead` first mark rows read,
  or remove them, in this tab's overlay. Then they call spec 0021 §5.2's verb, which publishes
  `inbox.changed` so the leader refreshes. The overlay drops when a copy saved after the response
  arrives. A failed verb drops it at once and toasts (§8.1).

| Badge | Count | Tone | Extra |
|---|---|---|---|
| Bell | `unseen`; hidden while the bell's seen overlay holds | `attention` while `maxUnseenSeverity` is `Warning` or `Error`, else `accent` | the job ring while `running` has a `Pending` or `Running` row (not `Held`: it waits on an administrator) |
| Tray | `open`; never hidden by opening | `accent` while `new` is above 0 and the tray's seen overlay does not hold, else `neutral` | — |

- **Counts.** A count of 0 or null renders nothing, and a count past the cap reads "99+". The
  counter has a fixed box, so 9 → 10 moves nothing.
- **Accessible names.** The overlay counter is `aria-hidden` (spec 0023 §4.1). The button's name
  joins these parts with the language's list separator, for example "Notifications, 3 new, needs
  attention, 1 job running":
  - bell: "Notifications", "{n} new", "needs attention", "{n} job running" or "{n} jobs running";
  - tray: "My assignments", "{n} open", "{n} new".

### 5.3 Opening a panel

Opening a panel shows its content at once, quiets its badge, then refreshes in the background.

1. **Same frame.** The panel renders from the stored document, with no request, and `open`
   changes. The seen overlay applies: the bell hides its count, or the tray turns its tone
   `neutral`.
2. **When the document shows something new** (`unseen` or `new` above 0) and `writable()` holds,
   the tab calls `TmApi.write('inbox/seen', {})` or `TmApi.write('assignments/seen', {})`.
3. **Then** the tab reads the document:
   `TmApi.read('inbox/summary' | 'assignments/summary', {}, { signal, background: true })`.
   Closing the panel aborts the read. The tab saves the result with `TmClientStore.put`, so every
   tab re-renders; spec 0025 §2.5 lets a follower put a document it read.
   The seen overlay lifts when a copy arrives that was saved after the `seen` response, or after
   the open when no `seen` was sent. So an older refresh never brings the count back.
4. **Rows in an open panel** update in place, keyed by id. New rows insert at the top only while
   the list is scrolled to its top. Otherwise a pinned "{n} new" button heads the list; activating
   it scrolls up and inserts them. A polite announcement says "{n} new notifications".

- **"New" marks.** The tray marks rows "New" against the `seenAt` the panel opened with.
- **Closing.** Escape, an outside click, choosing a row or the button closes a panel. Focus returns
  to the button (spec 0027 §7.2).

### 5.4 States that limit the verbs

Four states limit what the bell and the tray can do. `writable()` (§5.2) is false in the first two.

| State | Bell and tray |
|---|---|
| Tenant `ReadOnly` (`TmLaunchCache.docs.tenant`) | panels open and the counts stay; no `seen`, `read` or `clear`, which spec 0011 refuses as mutations; "Mark all as read", Clear and Cancel are absent |
| Offline: `TmApi.online()` false | panels open from the store; verbs are disabled with the tooltip "You're offline"; no refresh is attempted |
| No document yet (cold, sync pending) | buttons without a count; panels show three skeleton rows of final geometry |
| `TmAttention.live()` is `paused` | the bell panel's status line (§6.2) |

## 6. The bell

The bell shows the inbox: notifications, the user's running jobs and the state of live updates.
Its badge counts what arrived since the user last opened it.

### 6.1 The button

`tm-bell-button` is a 30 × 30 icon button, placed in the shell's slot order (spec 0027 §7.2).

- **Anatomy.** The `bell` icon, and a `tm-counter placement="overlay"` in the badge's tone. While
  jobs run, a `tm-progress-ring value=null` is added, static under `prefers-reduced-motion` (spec
  0023 §2.2).
- **ARIA and shortcut.** `aria-haspopup="dialog"`, `aria-expanded`, `aria-controls` and
  `aria-keyshortcuts="Alt+Shift+N"`. The shortcut `tm.attention.notifications` (spec 0027
  Appendix A) calls `toggle('bell', 'shortcut')`.

### 6.2 The panel

From top to bottom: the header, In progress, the status line, the notification rows, and the
footer link "See all notifications" (`TmUrls.search('core.Notification')`).

- **Header.** `h2` "Notifications", then "Mark all as read" while `unread` is above 0, then a
  `tm-menu` "More options" with "Notification settings" (`TmUrls.page('me/notifications')`) and
  "Clear read notifications". The latter asks first through `TmConfirm.confirm` (spec 0023 §5.5):
  "Clear read notifications?", "They are deleted for you.", Clear (destructive) and Cancel.
- **In progress** lists the inbox's `running` jobs, newest first, at most three, then "+{n} more in
  My jobs". "My jobs" links to `TmUrls.search('core.Job', { tab: 'mine' })`, which is
  `/{t}/jobs?tab=mine`. **Example —** "Exporting Sales invoices · 62%" over a bar and "5,580 of
  9,000 rows", with Cancel.

| Job row | Title | Bar | Line 2 | Control |
|---|---|---|---|---|
| `Pending` | the job's label (§11.2) | indeterminate | "Waiting to start" | Cancel |
| `Running`, percent known | the label, the percent at the end | determinate | the progress message (`progressKey`) | Cancel |
| `Running`, no percent | the label | indeterminate | the progress message, else "Working…" | Cancel |
| `Running` with `cancelRequestedAt` | the label | as before | "Cancelling…" | none |
| `Held` | the label and a `warning` badge "Held" | none | the `errorCode` message ("Paused after a service interruption") | Cancel |

- **Cancel** calls `cancelJob(id)`, self-scoped by spec 0020 §11.2. The row shows "Cancelling…" in
  the click's frame. There is no confirmation, because a cancelled export re-runs in one click.
- **Notification rows** are the inbox's `latest`, newest first. Each row has:
  - a severity icon in its tone: `Success` `circle-check`, `Warning` `triangle-alert`, `Error`
    `circle-x`, `Info` `info`;
  - the title over the detail (§11.1), else over the actor's display name;
  - a type action, which only `core.export.ready` has: "Download", a real link to
    `TmUrls.blob('export-file', fileId, { download: true })` (spec 0019 §12.2);
  - the time (`tm-relative-time style="narrow"`), and Clear.

  Unread rows are bold, with a dot.
- **Activation.** A row is a real link to `/{t}/notifications/{id}/open` (§10.2). A click or Enter
  closes the panel and follows it. Mod+click and a middle click open a new tab, where the route
  marks the row read the same way. A target the user can no longer open shows that page's
  not-found or forbidden state.
- **Withdrawn rows.** A withdrawn row (spec 0021 §3.3) disappears with the next copy. Focus on it
  moves to the next row, else the previous, else the heading.
- **Replaced rows.** A replaced row (same id, fresh `createdAt`) moves to the top, unread.
- **Status line.** While `live()` is `paused`, it reads "Live updates paused · Updated {time}"
  (the copy's `savedAt`), with Refresh calling `refresh('inbox')`. While the hub is connected or
  disabled, nothing shows; degraded polling keeps the document fresh (spec 0025 §8.7).
- **Empty.** `tm-empty-state size="panel"`, icon `check-check`, "You're all caught up".

### 6.3 Keyboard and ARIA (bell and tray panels)

- **The panel** is a `role="dialog"` labelled by its heading. It is non-modal in the popover and
  modal in the drawer. Opening focuses the first row, else the first header control.
- **Tab order.** The header, the jobs list, the notifications list, the footer. Each list is one
  tab stop: a `ul` with a roving tabindex.
- **Keys.** Up and Down move between rows; Home and End jump to the first or last. Left and Right,
  direction-mapped, move among a row's controls. Enter activates. Delete on a notification row
  clears it and announces "Notification cleared".
- **Names.** A row link reads the title, detail, time and state, for example "Export is ready,
  Customers · 1,284 rows, 2 minutes ago, unread". A job's progress bar is named by its label.
- **Row controls.** Cancel and Clear show on row hover and focus. While hidden, they stay in the
  accessibility tree and keep their box.

### 6.4 Responsive and RTL

- **At `lg` and above:** a `tm-popover size="md"` (440 px, spec 0010 §D.3) below the button,
  aligned to its inline-end edge. It is at most `min(560px, 100dvh - var(--tm-shell-top) - 16px)`
  tall. The lists scroll; the header and footer stay.
- **Below `lg`:** a `TmModal` with `placement: 'end'` and `size: 'sm'` (360 px; full width below
  `sm`), titled "Notifications" (spec 0010 §D.2). Below `sm`, rows are 44 px tall, as spec 0010
  §C.10's menu rows are.
- **RTL.** The panel aligns to the button's inline-end edge, which is the left edge in Arabic.
  Pointing icons mirror. Codes and numbers inside text sit in `<bdi>`. Badge digits are
  Arabic-Indic in `ar-SA`.

## 7. The tray

The tray shows the work waiting on the user: their open assignments, newest first. Its badge counts
open assignments and turns accent while some are new.

### 7.1 The button

`tm-tray-button` has the bell's box and anatomy (§6.1) with the `inbox` icon, the tray badge (§5.2)
and no ring. The shortcut `tm.attention.assignments` (`Alt+Shift+A`, spec 0027 Appendix A) calls
`toggle('tray', 'shortcut')`.

### 7.2 The panel

- **Header.** `h2` "My assignments" with an inline `tm-counter` of `open` ("{n} open").
- **Rows.** `latest` (at most 10) through `tm-assignment-list variant="tray" party="to-me"`.
  **Example —** a row reads "Sales invoice 23 · New · 1 h" over "Sara Ahmed: For your kind
  approval".
- **Footer.** "See all assignments" (`TmUrls.search('core.Assignment')`). Empty: icon `inbox`,
  "Nothing is waiting on you".
- **Activation** closes the panel and navigates to `TmUrls.target(resource, targetId)`. Nothing is
  marked: the count drops when the module completes or hands the work on (§2.3).
- **Everything else** follows §6.3 and §6.4. The drawer's title is "My assignments".

### 7.3 `tm-assignment-list`

```ts
// @tellma/core/attention — one row anatomy for the tray and the home lists
declare class TmAssignmentList {
  readonly items: InputSignal<readonly TmEntityType<'core.Assignment'>[]>;
  readonly related: InputSignal<Readonly<Record<string, readonly TmJson[]>>>; // the summary's related users
  readonly party: InputSignal<'to-me' | 'by-me'>;        // rows assigned to the user, or by the user
  readonly variant: InputSignal<'tray' | 'home'>;        // the row anatomy (table below)
  readonly seenAt: InputSignal<string | null>;           // to-me rows after it, not self-assigned, show "New"
  readonly loading: InputSignal<boolean>;                // skeleton rows of final geometry
}
```

| Part | `tray` | `home` |
|---|---|---|
| Icon | the target entity's icon (spec 0029 §2.2), else `file-text` | to-me: the entity's icon; by-me: `send` |
| Title | "{entity} {code}" (§11.3), the row's link | "{code}" as the link, then the detail |
| Line 2 | see below | the detail, else the comment |
| End | the time of `assignedAt` and the "New" dot | the amount and the time |

- **Line 2 of a `tray` row:**
  - with a comment: "{assigner}: {comment}" or "Returned by {assigner}: {comment}";
  - else the detail and amount;
  - else "Assigned by {assigner}";
  - by the system: "Assigned automatically";
  - by-me rows: "With {assignee}".
- **Names** come from `TmDisplayNames` after `rememberRelated(related)` (spec 0029 §5.3).
- **Layout.** It fills its container's block size and scrolls inside: a `ul` with §6.3's roving rows.

### 7.4 The home lists

Spec 0035 §9.2's widgets `core.assignments-to-me` and `core.assignments-by-me` render `latest` and
`latestByMe` through `tm-assignment-list variant="home"` from the stored document, up to
`SummaryLatestCount` rows each.

## 8. The toast policy

A toast interrupts the user. So it is reserved for arrivals that deserve one, and for outcomes with
no inline home: no place on the page that can show them. Everything else waits in a badge, a panel
or an inline state. `TmToasts` (spec 0023) obeys this policy.

### 8.1 What toasts

| Trigger | Toast | Shown in | Tone | Duration | Action |
|---|---|---|---|---|---|
| A notification arrives whose type has `Toast = true` | its title and detail (§11.1) | the visible, focused tab (§8.2) | by severity: `Success` success, `Warning` warning, `Error` error, `Info` neutral | 6 s; `Error` sticky | "Download" for `core.export.ready`; else "View" when it has a target |
| One assignment arrives, not self-assigned | "{title} assigned to you" or "{title} returned to you"; detail "{assigner}: {comment}" | same | accent, icon `inbox` | 6 s | "Open" |
| Several assignments arrive in one copy | "{n} documents assigned to you" | same | accent | 6 s | "View" (opens the tray) |
| The tenant turns `ReadOnly` | "{tenant} is now read-only. You can view data but not change it." | same | warning | 6 s | — |
| An outcome with no inline home (below) | the outcome | the acting tab | neutral, accent, success; error for a failure | 4 s; a failure sticky | as listed |

**Never toasted:**

- notification types with `Toast = false`: shares, "added to tenant", every activity type;
- saves, activations, validation errors and request errors, which pages show inline (specs 0030,
  0031);
- connection lost and update available, which are banners (§9).

**Outcomes with no inline home.** Each toasts once. This list is closed:

| Outcome | Copy | Action | Raised by |
|---|---|---|---|
| A delete that leaves the details page | "{Entity} deleted." | — | spec 0031 §10.2 |
| A bulk action's result | "{n} items deactivated." (the action's own past tense) | — | spec 0030 §12.3 |
| A `202 JobAccepted` start | "Started in the background." | Track | specs 0030 §13.2–§13.3, 0031 §10.4, 0034 §9 |
| A synchronous export in progress | "Exporting…", sticky, with a progress line | Cancel | specs 0030 §13.2, 0034 §9 |
| A save that navigates away | "Report saved." | — | spec 0034 §12.9 |
| A report deleted from its page | "Report deleted." | — | spec 0034 §10.3 |
| The share dialog closed after a change | "Sharing updated." | — | spec 0034 §10.4 |
| A pin added with no visible chip (the bar hidden, or the pin in More) | "Pinned {label}." | — | spec 0027 §8.5 |
| Diagnostics copied, or not | "Diagnostics copied." / "Couldn't copy. Open About, then Diagnostics, to see the details." | — | spec 0026 §8.3 |
| Caches refreshed | "Caches refreshed." | — | spec 0032 §7.7 |
| Sign-out failed | "Couldn't sign out. Check your connection and try again." | — | spec 0024 §8.4 |
| An attention verb failed (sticky) | "Couldn't update your notifications. Try again." | Try again | §5.2 |
| A test message was sent | "Test message sent." | — | §10.3 |

- **Raising an outcome.** The acting feature calls `inject(TmToasts).show({ tone, title, detail?,
  action?, key?, duration: TM_TOAST_FEEDBACK_MS })`. The primary exports
  `TM_TOAST_FEEDBACK_MS = 4000` and `TM_TOAST_ARRIVAL_MS = 6000`.
- **Track** always opens `TmUrls.target('core.Job', jobId)`.
- **The overflow pill.** Spec 0024 §2.2 calls `provideTellmaToasts({ maxVisible: 3, overflow })`
  with this spec's action. The pill reads "{n} more · Open notifications" and navigates to
  `TmUrls.search('core.Notification')`. It cannot open the bell, because the primary cannot reach
  `/attention`.
- **The per-user switch** `notifications.toasts` (spec 0025 Appendix A; fallback `true`) turns
  arrival toasts off. Outcomes and failures still toast.

### 8.2 The arrival engine (`TmArrivals`)

`TmArrivals` is a root service of the tenant shell. It watches the stored `inbox`, `assignments`
and `tenant` documents through the store mirror, and applies one rule in three parts:

1. **Baseline.** A tab's baseline is the first copy written after the tab started: its `savedAt`
   is at or after the tab's start. The baseline and earlier copies toast nothing, so what arrived
   before the tab started never pops up.
2. **Arrival.** In each later copy, the arrivals are the `latest` rows newer than the previous
   copy's newest:
   - a notification, compared by `createdAt`, that is unread and of a `Toast = true` type;
   - an assignment, compared by `assignedAt`, that the user did not assign;
   - a `tenant` state that turned `ReadOnly`.

   Every tab moves its "previous copy" on with every copy, so a tab that gains focus later replays
   nothing.
3. **Which tab.** Only a tab that is visible (`document.visibilityState` is `visible`) and focused
   (`document.hasFocus()`) when the copy lands shows the toasts. Other tabs update badges only.
   Toasts come from document contents, never from hub events (spec 0025 §8.4).

**Replaced rows toast again.** Spec 0021's `Replace` gives the row a fresh `createdAt`, so it is an
arrival. Its key (§8.3) replaces any open toast for it in place.

**Example —** Farah has three tabs open, and the second has focus. Sara assigns invoice 23 to her.

1. The leader tab refreshes `assignments` and stores a new copy.
2. All three tabs see the copy. Row 1874 is newer than the previous copy's newest, so it arrives.
3. Every tab's tray badge turns accent. Only the second tab toasts "Sales invoice 23 assigned to you".
4. Farah switches to the third tab. Nothing replays there.

### 8.3 Keys, withdrawals and limits

- **Keys** (spec 0023 §5.1): `'n:<id>'` for a notification, `'a:<id>'` for an assignment, and
  `'a:many'` for the grouped toast.
- **Grouping and limit.** Two or more assignment arrivals in one copy show the grouped toast. Of
  more than three notification arrivals, the newest three show and the overflow pill counts the rest.
- **Actions.** "View", "Open" and "Download" run the panel row's activation (§6.2, §7.2), then close
  the toast. A toast whose row was withdrawn or read meanwhile stays until its timer ends.

## 9. Banners: `TmBanners`

The banner row shows one condition at a time, by a fixed precedence, with a pager for the rest.
Its height changes wait for an idle moment, so the page never moves under a keystroke or a click.

### 9.1 Contract

```ts
// @tellma/core (primary)
type TmBannerLevel = 1 | 2 | 3 | 4 | 5;   // 1 connection lost; 2 critical announcement (reserved); 3 update; 4 warning; 5 info
interface TmBannerOptions {
  readonly id: string;                    // 'tm.offline', 'tm.update', 'acme.period-closing'; show() with a live id updates it
  readonly level: TmBannerLevel;          // 1–3 are the platform's: a distribution banner there throws in development
  readonly tone: 'info' | 'warning' | 'error';
  readonly message: Signal<string>;       // localized; at most 45 characters, so one 32 px line fits 390 px
  readonly icon?: string;                 // a registry name; default per tone
  readonly action?: { readonly label: Signal<string>; readonly run: () => void }; // "Reload", "Learn more"
  readonly dismissible: boolean;          // never for levels 1 and 2
}
interface TmBannerRef { update(patch: Partial<Omit<TmBannerOptions, 'id' | 'level'>>): void; close(): void }
interface TmBannerView { readonly options: TmBannerOptions; readonly index: number; readonly count: number }   // index and count feed the pager
declare class TmBanners {
  show(options: TmBannerOptions): TmBannerRef;
  readonly current: Signal<TmBannerView | null>;  // what the row renders now, after the idle gate
  readonly visible: Signal<boolean>;              // the row is open; spec 0027 §5.1 sets --tm-banner-height from it
}
```

### 9.2 Precedence and the pager

- **Order.** Entries sort by level ascending, then by the time `show` first ran.
- **Head.** The *head* is the first entry that is neither dismissed nor closed. The row shows the
  head, or the entry the user paged to. A banner of a higher level (a lower number) takes the row
  at once; one of a lower or equal level only adds to the count.
- **Pager.** With two or more entries, `tm-banner-bar`'s position shows "1 of 3" with Previous and
  Next. Paging is the user's choice, and it sticks until that entry closes or is dismissed.
- **Dismiss** hides the entry for the rest of this tab's life. The update banner stays dismissed
  for that version, which is the value of `TmUpdates.available()` (spec 0024 §5.5).
- **Update and close.** `update` of a live id changes the entry in place; `close()` removes it.

### 9.3 The input-idle gate

- **What waits.** Only changes of the row's height: opening (0 → 32 px) and closing. A message swap
  in an open row is immediate.
- **When the gate opens.** After 2 s without `keydown`, `pointerdown`, `pointermove`, `wheel` or
  `touchstart` on the window, observed by passive capture listeners.
- **Applied at once:** the user's own dismiss, and any change while the page is hidden, so the user
  returns to a settled layout.
- **An emptied row.** When the last entry closes while the row waits for the gate, the row stays
  open and empty, with no message and no actions, so nothing stale shows.

**Example —** Farah is typing in a grid editor when the connection drops. After 5 s, the live
region announces "Connection lost" at once (§9.4), but the row opens only after 2 s without typing.

### 9.4 `tm-banner-host`

Spec 0027 §5.2 places the host in both shells, between the app bar and the pinned bar.

- **Rendering.** It renders `tm-banner-bar` (spec 0023 §5.2) from `TmBanners.current` and wires its
  outputs back. Focus never moves to a banner (spec 0027 §5.3).
- **Announcement.** It owns one visually hidden live region: `assertive` for level 1, `polite`
  otherwise. It writes the message when an entry becomes the head, before the gate opens, so
  keyboard and screen-reader users hear it at once. The bar itself is not a live region.

### 9.5 The platform's banners

| Id | Level | Tone, icon | Message | Action | Dismissible | Shows while |
|---|---|---|---|---|---|---|
| `tm.offline` | 1 | error, `wifi-off` | "Connection lost. Reconnecting…" | — | no | `TmApi.online()` has been false for 5 s (spec 0024 §10.5); closes when it turns true |
| `tm.update` | 3 | info, `refresh-cw` | "A new version is available." | "Reload": `TmUpdates.reload()` | yes, for that version | `TmUpdates.available()` is not null (spec 0024 §5.5) |

- **Where they run.** `provideTellma` starts both sources in the primary, so they run in every
  shell, the tenantless one included.
- **Hub loss** shows no banner, only the bell's status line (§6.2).
- **Other levels.** Level 2 waits for critical announcements. Levels 4 and 5 serve distribution
  banners (§9.6) and, later, announcements.

### 9.6 A distribution's banner (the hatch)

A distribution shows its own banner at level 4 or 5, for a condition it watches itself, and closes
it when the condition ends. **Example —** Acme warns that the period is closing:

```ts
const ref = inject(TmBanners).show({ id: 'acme.period-closing', level: 4, tone: 'warning', dismissible: true,
  message: computed(() => t('Acme_Banner_PeriodClosing')) });   // "The period closes on Friday."; later: ref.close()
```

## 10. The four pages

The pages are the core feature's declarations on the search and details frameworks (specs 0030,
0031). Their only custom code is four cells, the job details layout, the settings page and the
notification open route.

### 10.1 Shared rules

- **Rows open their target.** `rowLink` (spec 0030 §7.2) returns `TmUrls.target(…)`, or a
  notification's open route (§10.2). It runs in the search host's injection context, like
  `pageActions.run`. The result is a real link, and Enter opens it.
- **No push refreshes a page.** A page re-reads through its Refresh button (spec 0030 §13.4, spec
  0031 §5.2). A page action refreshes what it changed through `search.refresh()`. The bell is the
  live view.
- **Bulk actions** are the stack's entity actions and the `selection` page actions (spec 0030
  §12.2).

| Page | Route | Launcher (spec 0027 §3.2) | Declaration |
|---|---|---|---|
| Notifications | `/{t}/notifications`, `?tab=unread`; `/{t}/notifications/{id}/open` | search-only; none for the open route | `tmEntity('core.Notification', …)`; `tmPage('notifications/:id/open', …)` |
| Notification settings | `/{t}/me/notifications` | search-only, `core.me.notifications` | a platform route (spec 0024 §9.2) |
| Assignments | `/{t}/assignments`, `?tab=by-me` or `?tab=all` | search-only | `tmEntity('core.Assignment', …)` |
| Jobs; job details | `/{t}/jobs`, `?tab=mine`; `/{t}/jobs/{id}` | Administration, order 60 | `tmEntity('core.Job', …)` |

### 10.2 Notifications (`/{t}/notifications`)

```ts
export const tmNotifications = tmEntity('core.Notification', {
  columns: [{ path: 'Type', select: ['Severity', 'ArgumentsJson', 'ReadAt', 'TargetResource', 'TargetId'],
              display: () => import('@tellma/core/attention-pages').then((m) => m.TmNotificationTextCell) },
            'ActorUser', 'CreatedAt', 'ReadAt'],
  orderBy: 'CreatedAt:desc',
  tabs: [{ key: 'all', labelKey: 'tm.attention.tab.all' },
         { key: 'unread', labelKey: 'tm.attention.tab.unread', queryex: 'ReadAt is null' }],
  rowLink: (row) => inject(TmUrls).page(`notifications/${row.id}/open`),   // the open route marks the row read
  pageActions: [
    { id: 'attention.notifications.read-all', labelKey: 'tm.attention.readAll', icon: 'check-check',
      run: async (search) => { await inject(TmApi).write('inbox/read-all', {}); search.refresh(); } },
    { id: 'attention.notifications.clear-read', labelKey: 'tm.attention.clearRead', group: 'menu', destructive: true,
      confirm: { titleKey: 'tm.attention.clearRead.title', messageKey: 'tm.attention.clearRead.message' },
      run: async (search) => { await inject(TmApi).write('inbox/clear-read', {}); search.refresh(); } },
    { id: 'attention.notifications.read', labelKey: 'tm.attention.markRead', icon: 'check', selection: true,
      run: async (search, ids) => { await inject(TmApi).write('inbox/read', { ids }); search.refresh(); } },
    { id: 'attention.notifications.clear', labelKey: 'tm.attention.clear', selection: true, destructive: true,
      confirm: { titleKey: 'tm.attention.clear.title', messageKey: 'tm.attention.clear.message' },
      run: async (search, ids) => { await inject(TmApi).write('inbox/clear', { ids }); search.refresh(); } },
  ],
});
export const tmNotificationOpen = tmPage('notifications/:id/open', { titleKey: 'tm.attention.notifications',
  component: () => import('@tellma/core/attention-pages').then((m) => m.TmNotificationOpenPage) });
```

- **The text cell** shows the severity icon, title and detail (§11.1), bold with a dot while
  unread. Being client-rendered, it is neither sortable nor filterable (spec 0021 §2.4).
- **Actions.** The stack has no save, delete, actions or Excel (spec 0021 §2.2), so the page has no
  Create, Export or Import. "Mark as read" and "Clear" act on the selection (spec 0030 §12.2).
- **The open route.** `TmNotificationOpenPage` shows the `blank` skeleton while it:
  1. finds the row in the stored `inbox` document's `latest`, else reads it through
     `notifications/get`;
  2. calls `TmAttention.read([id])` (§5.2) when the row is unread and `writable()` holds (§5.4);
  3. without waiting for that write, replaces the URL with the row's target
     (`TmUrls.target(targetResource, targetId)`), else with `/{t}/notifications`.

  A row gone or hidden (`404`) shows spec 0024 §11's not-found view in place.

### 10.3 Notification settings (`/{t}/me/notifications`)

The page turns arrival pop-ups and each notification type's channels on or off, and sends test
messages. Every change saves itself.

- **Route and data.** The platform route (spec 0024 §9.2) has route data `{ kind: 'settings',
  skeleton: 'details', titleKey, load }`; `load` imports `TmNotificationSettingsPage` from
  `/attention-pages`. The page reads `notification-preferences/get` (spec 0021 §4.3) and the
  metadata's `notificationTypes` and `notificationChannels`.
- **Pop-up messages.** A card with one `tm-switch`, "Show a pop-up when a notification or an
  assignment arrives". It is bound to `notifications.toasts` through `TmPreferences`, and applies at
  once (spec 0025 §9).
- **The grid.** One card per category, in the metadata's order, titled
  `NotificationCategory_<Category>`. Each card has one row per type (`NotificationType_<Key>`) and
  one column per channel, in registration order (`NotificationChannel_<Key>`; `inbox` reads "In the
  app"). Below `md`, each type's switches stack under its label.
- **A cell** is a `tm-switch` named "{type}, {channel}". It shows the stored deviation, else the
  channel's `defaultEnabled`. A `Mutable = false` type's `inbox` switch is on, disabled and
  described "Always on". A channel-only type shows "—" in the `inbox` column.
- **Saving.** A change applies in its frame. 600 ms after the last change, the page sends the whole
  set to `notification-preferences/save`, omitting rows equal to the defaults. One save is in
  flight at a time; the latest state follows it.
- **Status.** The header reads "Saving…", "All changes saved", or "Couldn't save your changes" with
  Try again. Switches keep the user's values. A `422` (a type gone after a deployment) reloads the
  set and shows spec 0024 §11's 400 dialog.
- **Test delivery.** One row per channel other than `inbox`. It shows the address the channel
  would use: `me`'s contact email, else its email, as spec 0021 §4.4 resolves it, or "No address"
  when neither exists. Its "Send a test" calls `users/me/test-notification { channel }` (spec 0018
  §3.5). The results:
  - `Sent` toasts "Test message sent.";
  - `NoContactAddress` shows "Add an email address in My profile first." with a link to
    `TmUrls.page('me')`;
  - `NotConfigured` shows "Email isn't set up for this server."
- **Page context.** Kind `settings`, state `{ category: 'notifications', mode: 'edit', dirty }`
  (spec 0026 §9.3). Its actions are `notifications.settings.set { type, channel, enabled }`, which
  saves like a click, and `notifications.settings.test { channel }`.

### 10.4 Assignments (`/{t}/assignments?tab=to-me|by-me|all`)

```ts
export const tmAssignments = tmEntity('core.Assignment', {
  columns: [{ path: 'Resource', select: ['TargetId', 'CaptionJson'],
              display: () => import('@tellma/core/attention-pages').then((m) => m.TmAssignmentTargetCell) },
            'Comment', 'Assignee', 'AssignedBy', 'AssignedAt', 'State', 'ClosedAt'],
  tabs: [{ key: 'to-me', labelKey: 'tm.attention.tab.toMe', queryex: "AssigneeId = me() and State = 'Open'" },
         { key: 'by-me', labelKey: 'tm.attention.tab.byMe', queryex: "AssignedById = me() and AssigneeId != me() and State = 'Open'" },
         { key: 'all', labelKey: 'tm.attention.tab.all' }],
  rowLink: (row) => inject(TmUrls).target(row.get('Resource') as string, row.get('TargetId') as number),
});
```

- **Tabs.** "Assigned to me" and "Assigned by me" are the open work behind the tray and the home
  lists. "All" is every row the user may read, in any state: their own history, or a supervisor's
  team under a grant (§2.2).
- **The target cell** shows the entity's icon, "{entity} {code}" and the detail (§11.3).
- **No writes.** There is no Create, Export, Import or action: writes belong to the module's
  document (§3).

### 10.5 Jobs (`/{t}/jobs?tab=mine`) and the job details page (`/{t}/jobs/{id}`)

```ts
const cells = () => import('@tellma/core/attention-pages');
export const tmJobs = tmEntity('core.Job', {
  section: 'administration', order: 60, icon: 'loader',
  columns: [{ path: 'HandlerKey', select: ['LabelKey', 'LabelArgumentsJson'], display: () => cells().then((m) => m.TmJobLabelCell) },
            'Status',
            { path: 'ProgressPercent', select: ['Status', 'ProgressKey', 'ProgressArgumentsJson', 'ErrorCode', 'ErrorArgumentsJson'],
              display: () => cells().then((m) => m.TmJobProgressCell) },
            'RequestedBy', 'CreatedAt', 'CompletedAt'],
  tabs: [{ key: 'all', labelKey: 'tm.attention.tab.allJobs' },
         { key: 'mine', labelKey: 'tm.attention.tab.myJobs', queryex: 'RequestedById = me()' }],
  details: () => cells().then((m) => m.TmJobDetails),
});
```

- **Who sees what.** A member sees their own jobs (spec 0020 §11.2's self-scope). An unrestricted
  `core.Job × Read` sees everyone's.
- **The progress cell** shows the bar and percent while running, and the `errorCode` message when
  `Failed` or `Held`.
- **Bulk actions** are Cancel, Retry and Resume (spec 0020 §11.1).
- **`TmJobDetails`** is view mode only (no `save`). It shows the label, status, progress and
  message, handler key, attempts, the times, requested by, run as, the schedule as a link, the
  error with `errorTraceId` in a `tm-reference`, and the arguments as collapsed JSON.
  `core.job.failed` notifications open here.

## 11. Rendering server text on the client

Notification texts, job labels and assignment captions travel as keys and arguments. The client
renders them in the reader's language through spec 0024 §7.5's `TmServerText` and re-renders them
on a language or calendar switch, with no request.

### 11.1 `TmAttentionText`

```ts
// @tellma/core/attention
declare class TmAttentionText {
  notification(type: string, argumentsJson: string | null): Signal<{ title: string; detail: string | null }>;   // below
  job(job: { handlerKey: string; labelKey?: string | null; labelArgumentsJson?: string | null }): Signal<string>;   // §11.2
  jobMessage(key: string | null, argumentsJson: string | null): Signal<string | null>;   // a progressKey or errorCode
  caption(row: TmEntityType<'core.Assignment'>): Signal<{ title: string; detail: string | null; amount: string | null }>;   // §11.3
}
```

- **Keys.** The title is `Notification_<Key>`; a missing title renders the type key. The detail is
  `Notification_<Key>_Detail`, when `TmServerText.has` finds it (spec 0021 §2.4).
- **Formatting.** Both go through `TmServerText.message(key, args)`, with spec 0021 §2.4's
  argument rules.

### 11.2 Job labels

- **Order** (spec 0020 §2.3), first match wins:
  1. the job's `labelKey` with its arguments;
  2. the handler's `labelKey` from the metadata's `jobHandlers` (spec 0020 §3.7);
  3. the handler's `labelFallback`.
- **A bare handler key** renders too, as the Schedules page needs (spec 0032 §9).
- **The `entity` argument** becomes `TmEntities.label(name, true)()`, the plural label in the active
  language, before formatting: `Excel.Export.Label` with `{ entity: 'acme.SalesInvoice' }` reads
  "Exporting Sales invoices".

### 11.3 Assignment captions

- **Stored form.** `AssignmentCaption` in the platform JSON options, every member optional:
  `{ "code": "INV-0023", "amount": 12400.00, "currency": "SAR", "detail": { "customer": "Al Faris Group" } }`.
- **Title.** `tm.attention.assignment.title`, "{entity} {code}". `{entity}` is the singular label
  (spec 0029 §4.1). `{code}` is the code in `<bdi>`, else `#{targetId}`.
- **Detail.** `Assignment_<Resource>_Detail`, with `.` replaced by `_`, formatted with `detail` as
  its ICU arguments. Without the key, there is no detail.
- **Amount.** `Intl.NumberFormat(locale, { style: 'currency', currency, currencyDisplay: 'code' })`,
  for example "SAR 12,400.00".
- **One language.** A name inside `detail` is the text the module passed when it assigned.

**Example —** with `Assignment_acme_SalesInvoice_Detail` reading "{customer}", an English reader
sees the stored form above as the title "Sales invoice INV-0023", the detail "Al Faris Group" and
the amount "SAR 12,400.00".

## 12. Testing

Every suite runs on PR; none is nightly. Each bullet names the suite, its tier and what it pins.

- **Assignments unit (C#)** — unit. §3.1's programmer errors; one callback per batch across calls;
  the recipients' union; the caption bound.
- **Assignments fixture (C#)** — LocalDB.
  - Writes: contiguous ids; a hand-off closes `Handed` with `ClosedById`; `IsReturn`; an inactive
    or service-account assignee fails `Assignments.AssigneeInvalid`; closes are idempotent; two
    concurrent first assigns raise 2601 → `Unique`; deletes by ids, by query and with descendants
    withdraw.
  - Reads: the summary's caps, `New` and self-assignment rules, orders and `Related`; `seen`; the
    trail extra; the self-scope (another user's row is not found); retention; a plan test that
    every summary statement seeks.
- **Assignments web (C#)** — integration. Routes and retry classes; `summary` stamps no activity;
  `assignments.changed` reaches exactly the parties (a fake publisher); MCP is read-only.
- **`TmBanners`** — unit. Precedence, the pager, dismiss per tab and per version. The gate with a
  fake clock: entry and exit wait; swaps, a hidden page and the user's dismiss apply at once; the
  empty row. The queue-time announcement.
- **`TmAttention`, `TmArrivals`, `TmAttentionText`** — unit. The badge table; overlays and their
  release. The baseline, both arrival rules, the replaced row, the focused-tab rule,
  `notifications.toasts` off, grouping and keys. Title and detail keys, the label order, the
  `entity` argument, captions with and without each member, a language switch.
- **Harnesses.** The bell panel's states (§6.2's job rows, empty, offline, read-only, the status
  line); the tray; the settings page (locked, channel-only, save statuses, test results).
- **Playwright on Acme** — e2e, at 390, 768, 992 and 1440 px, in `en` and `ar`:
  - Sara assigns invoice 23 to Farah, whose tray turns accent in three tabs while one toast shows
    in her focused tab. Farah returns it ("Returned by Farah Nasser"). Posting completes it, and
    both home lists update.
  - An export shows live progress and cancels from the bell. `core.export.ready` downloads from its
    toast. A bell row opened in a new tab is marked read. Mark as read and Clear act on a selection.
  - 5 s offline while typing in a grid editor shows the banner only after typing stops. The update
    banner reloads and keeps a draft.
  - Settings save themselves, and a test email reaches the fake sender.
- **axe.** Both panels, a toast stack, a banner and the settings page; light and dark, LTR and RTL.

## 13. Definition of done

This spec is done when both halves ship as below and each UI rule has a pinning test.

- **Server.** §2–§4 ship with §12's server suites green and the startup check `assignments`
  registered. The members that the Context list names in other specs exist.
- **Client.** The three entry points stay within §1's budgets, with API goldens.
- **UI rules:**

| Rule | How this design meets it | Pinning test |
|---|---|---|
| Instant warm launch | badges, the job ring, panels' first content and the home lists render from the stored documents; banners need no request | spec 0024's `warm-launch.spec` asserts both badges with every request held; `attention-warm.spec` opens both panels offline |
| Instant feedback | panels open in the click's frame from prefetched chunks; badges clear in the same frame; read, clear and cancel apply through overlays; switches apply before saving | `attention-feedback.spec` (a frame probe after click and shortcut); unit: overlays apply synchronously |
| Non-blocking | panel reads abort on close; summaries are background reads; no verb blocks navigation | unit: closing aborts the read; `attention-hang.spec`: a hung `inbox/summary` delays neither opening nor navigating |
| Stable layout | fixed counter boxes; hidden controls keep their boxes; the idle gate; the pinned "{n} new"; no push reloads a page | `attention-layout.spec`: boxes unchanged across 9 → 10, a job ring, and a banner queued while typing in a grid editor; zero layout shift |
| RTL | logical properties, inline-end alignment, mirrored arrows, `<bdi>` codes, `ar-SA` digits | every attention e2e runs in `ar`; harness snapshots in both directions |
| WCAG AA | dialog panels with roving lists, named badges, an assertive level-1 announcement at queue time, toasts that never take focus, switches named "{type}, {channel}" | §12's axe battery; `attention-keyboard.spec`: shortcuts, roving rows, Delete, Escape |

- **Page-context registry.**
  - The list pages and job details publish through their hosts. The settings page publishes
    `settings` (§10.3).
  - An open panel registers an overlay context: kind `custom`, route template `panel:bell` or
    `panel:tray`. Its `visible` rows carry id, type or resource, title and state. Its actions are
    `attention.open-item { id }`, `attention.read-all` and `attention.cancel-job { id }`
    (destructive, confirmed).
  - The dump section `attention` (spec 0026 §10.1) holds
    `{ open, bell: { count, tone, jobs }, tray: { count, tone }, banners: [{ id, level }] }`.
- **Telemetry** (area `tm.attention`):

| Event | Kind | Properties |
|---|---|---|
| `tm.attention.panel-opened` | event | `panel`; `via`; `badge`: `0`, `1-9`, `10+` |
| `tm.attention.item-opened` | event | `panel`; `kind`: `notification`, `assignment`, `job`; `position`: `1`, `2-3`, `4-10` |
| `tm.attention.verb` | event | `verb`: `seen`, `read`, `read-all`, `clear`, `clear-read`, `cancel-job`; `outcome`: `ok`, `failed` |
| `tm.attention.toast` | event | `kind`: `notification`, `assignment`, `grouped`, `tenant-state`; `outcome`: `timeout`, `dismiss`, `action`, `replaced` |
| `tm.attention.banner` | timing, queued to shown | `id`; `level` |
| `tm.attention.settings-saved` | event | `changes`: `1`, `2-5`, `6+`; `outcome` |

- **Acme.** Spec 0018 §1.4 lists Acme's assignment additions. Its client adds nothing.

## Decisions record

1. **A hand-off log, at most one open row per target** — "by me", "returned" and the trail are plain
   reads — (§2.1). Alternatives: a mutable row with temporal history; a second history table.
2. **`IAssignments` composes fixed text on the internal channel, like `INotifier`** — no extra round
   trip — (§3.1). Alternative: an `Enlist` stack that every document service writes.
3. **`[Assignable]` is a capability whose delete effect withdraws open rows** — no module forgets the
   cleanup — (§3.4). Alternative: each module withdraws on delete.
4. **Four states; "returned" is derived at insert** — a module passes only who and why — (§2.3,
   §3.2). Alternatives: a flag and a reason column; a return flag every module computes.
5. **Captions are language-neutral data rendered at read time** — each reader's language — (§11.3).
   Alternative: a title stored in the assigner's language.
6. **The summary carries both home lists and their users** — tray and home render warm — (§4.3).
   Alternative: two `assignments/query` calls on the home page.
7. **`assignments.changed` reaches assignees and assigners** — "Assigned by me" moves when others
   act — (§4.5). Alternative: assignees only.
8. **A panel overlays its badge, marks seen and refreshes the document itself** — the badge clears
   in the click's frame — (§5.3). Alternative: wait for the leader's refresh.
9. **Arrival toasts come from document changes, in the visible, focused tab** — once, where the
   user looks — (§8.2). Alternatives: the leader decides; hub events, which repeat on a steal.
10. **Outcomes with no inline home toast once; nothing a page shows inline toasts** — feedback only
    where the page cannot give it — (§8.1).
11. **Only height changes wait for the idle gate; the host announces at queue time** — nothing moves
    under input, nobody waits to hear a banner — (§9.3, §9.4). Alternative: gate swaps too.
12. **The pages are declarations, and no push refreshes them** — stable layout, no page code —
    (§10.1). Alternatives: `search` hatch bodies; refreshing named rows in place.
13. **A notification opens through `/{t}/notifications/{id}/open`, which marks it read** — rows and
    bell items are real links, and a new tab marks read too — (§6.2, §10.2). Alternatives: mark
    read in the click, which a new tab skips; read on visiting the target.

## Review flags

1. **Closed rows live 180 days** (§4.7). Alternative: 90 or 365. Flips if users want older "by me"
   history, or the volume hurts.
2. **A deactivated assignee keeps their open rows** (§2.3). Alternative: withdraw them on
   deactivation. Flips if orphaned work goes unnoticed.
3. **The trail shows comments to whoever reads the document** (§4.4). Alternative: only the
   parties. Flips on a privacy concern about hand-off notes.
4. **No platform assignment email** (§3.6). Alternative: a platform channel-only type with flat
   text. Flips if most distributions need email on assignment.
5. **Caption details are one language** (§11.3). Alternative: entity references resolved through
   `TmDisplayNames`. Flips if bilingual tenants complain.
6. **Attention verbs are hidden on a read-only tenant** (§5.4). Alternative: make `seen`, `read` and
   `clear` non-mutations. Flips if read-only tenants are common and stale badges bother users.
7. **Cancel in the bell asks nothing** (§6.2), though a cancelled import keeps its committed chunks.
   Alternative: confirm for imports. Flips on a support case about partial imports.
8. **Outcomes without an inline home toast once** (§8.1). Alternative: never toast feedback. Flips
   on a preference for a quieter UI.
9. **Toasts show only in the visible, focused tab** (§8.2), so a second-monitor watcher typing
   elsewhere gets none. Alternative: the last focused visible tab. Flips on user feedback.
10. **The idle gate has no maximum hold** (§9.3); screen readers hear the banner at once.
    Alternative: a 10 s cap. Flips if sighted users miss "Connection lost" during long typing.
11. **Notification settings save themselves** (§10.3). Alternative: an explicit Save. Flips on a
    preference for one confirmation.
12. **Pages refresh only by their Refresh button** (§10.1), so a finished job leaves the Jobs page
    stale. Alternative: refresh named rows on `job.changed`. Flips if administrators watch for it.
