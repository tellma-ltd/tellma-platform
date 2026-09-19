# Spec 0019 — findings applied and rejected

## Applied

- B1 (major, §7.9 cron clash): `core.blob-reconcile` now `0 2 * * 0` (matches 0016 §7.4) and `core.tree-verify` `0 1 * * 0`; §14.5 updated to match; §7.9 table declared the record the contributing specs cite.
- B2 (§14.1 `core.export` restatement): attribute restated verbatim from 0018 §12.3 (`MaxAttempts = 3`); the `Export` row is saved through `ExportService.SaveAsync` in a persist round trip of its own; only `NotifyOnSuccess` rides the completion batch.
- B3 (§8 `Client = "worker"`): kept the value, stated as a member of spec 0015's client set that never reaches a request filter (0015 adds `worker` per the cross-spec entry).
- B4 (`handler_unknown`): dropped from §2.1's `ErrorCode` list (added the already-used `cancelled`); §7.5 now says the condition logs `SchedulerHandlerUnknown` and writes no error code.
- B5 (§10 build-order narration): replaced with the standing composition — `INotifier` always present, `IClientEventPublisher` resolving to 0020's `NullClientEventPublisher` (`TryAdd`) on a host without the hub.
- B6 (ledger ids in review flags 1, 11, 13, 14, 15, 16, 17): deleted.

## Rejected / no spec change

- B7 (§5.4 "after the platform's guards" vs seams.md §8.3 "after `BEGIN TRAN`"): the reviewer agrees the spec is right; the fix belongs to seams.md, so the spec is unchanged.
- A §0011 finding 4 (`IIdAllocator.TakeAsync` example naming 0019's enqueue): a 0011 finding; 0019 §4.1 already states enqueue never uses the allocator, so nothing to change here.
- 0020 finding 1 (type-key grammar divergence): 0019 owns the grammar (§3.7) and is unchanged; 0020 cites it.
