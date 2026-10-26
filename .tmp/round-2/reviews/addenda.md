# Orchestrator addenda to the rulings

Binding, with the same force as `rulings.md`. They settle loose ends found while specs 0011–0022
were edited (logs in `edits/applied-*.md`). Where an existing spec still says otherwise, the
consistency pass fixes it; authors write the new specs to these.

1. **Part F numbering (final).** After the cut of parameter expressions:
   - §F.1–§F.7 — the seven amendments moved from spec 0012 §11.2 (key-set restrictions, `level`,
     prologue and body, paging slots, context slots, versioned leaves, `FilterTree.Via`);
   - §F.8 grouping sets and the level indicator; §F.9 tree level navigations; §F.10 omitted
     parameters; §F.11 constant statements (`EvaluateConstant`); §F.12 parameter binding
     (`BindParameters`); §F.13 `'uq'` through a host month map; §F.14 `FilterTree.Exists`;
     §F.15 `ParameterUse.ComparedWith`.
   - Any text in `rulings.md` that cites pre-cut numbers ("F.13 `BindParameters`", "F.14 `'uq'`",
     "F.15 `Exists`", "F.16 `ComparedWith`", "F.12–F.16") means the numbers above.
   - Part F's frame (placement, compatibility, cache keys, codes, corpus) is §F.0.
2. **Support requests are self-scoped.** A member reads the support requests they submitted
   (criterion `CreatedById = me()`, reason `self`); administrators read all. 0026 adopts it; spec
   0014 §5.3's self-scope list gains `core.SupportRequest` beside `core.Assignment`.
3. **Computed extras.** Spec 0015 §5.3 admits an extra computed in C# by the stack's service from
   the loaded rows, when SQL cannot express it. It costs no round trip unless it reads. The Role
   stack's eager `problems` extra (spec 0018 §4.3) is the first user; 0032 cites 0015 §5.3.
4. **Report seeds run last.** `core.report-seeds` has provisioning `Order` 900, after every pack and
   distribution step, so the roles a seed publishes to already exist. 0033 §13 states it; spec 0011
   §6.2's step list follows.
5. **Acme's assignment additions** (`[Assignable]` on `acme.SalesInvoice`, its `assign` and `post`
   actions) are listed in spec 0018 §1.4 with the rest of Acme; 0028 cites them there.
6. **Notification channels in the client metadata** are `{ key, defaultEnabled }[]` (spec 0021
   §2.5), not `string[]`. 0024 and 0028 use that shape.
7. **Early headers.** Spec 0011 §5.1's pipeline stamps `Tellma-Trace-Id` and `Tellma-Build` before
   any early refusal (CSRF 403, 412, 429, tenant 404), as spec 0016 §6.2 requires.
8. **Child-entity queries** run through `IChildQueryService.QueryAsync(resource, request)` (spec 0015
   §5.2); 0033 and 0016 use that name.
9. **Report export plumbing.** The table writer is spec 0019 §4.4, in `Tellma.Core.Excel`. The
   export job's label key is `Core_Job_ExportReport` (server keys are `PascalCase_Underscored`).
10. **The `activity` extra** belongs to the users stack (spec 0018); spec 0020 §7.8 has only the
   Schedule `state` extra.
