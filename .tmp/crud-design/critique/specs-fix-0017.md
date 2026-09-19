# Spec 0017 — fix record (2026-09-04)

Findings from `specs-review-B.md` §"Spec 0017" and the cross-spec entries naming 0017 in reviews A and B.

## Applied

- B-1 (major) — §3.3 `PartialFailureException` now `(Code = "identity-unavailable", Results = prefix InviteResults, Failed = not-attempted ids as strings)`; 502 body described as `errorDetails.results` / `errorDetails.failed[]`; `RemainingIds`/`Cause`/`remainingIds` removed; §9 test wording aligned.
- B-2 (major) — §3.3 now cites spec 0014's partial-failure rule for actions (verified present in 0014: the pipeline executes RT2 with what the method composed, then rethrows); the stale "`ExecuteActionAsync` contract" wording replaced. Review flag 2 kept as is.
- B-3 (major) — §7 `TenantMembershipList` removed from the tenant migration's standalone types (named as spec 0010's catalog type).
- B-5 — §3.5 `Details = DetailsRequest(Select = request.Select, Include = request.Include)`.
- B-6 — §6.1 `TenantBootstrapRequest(Email = AdminEmail, …)`.
- B-7 — §4 the `members` extra joins the details plan's `IdsSource` (opaque; never spelled) instead of `@tb{b}_main`.
- B-8 — §5.2 "`CenterType` is stored under … `varchar(12)`".
- B-9 — §8.2 `tellma.data.tree.recomputes` tag named `entity`.
- B-10 — §3.3 `core.user.added` arguments `{ tenantName, actorName }` with the descriptor cited as spec 0020's; §1.2 no longer says `CoreFeature` registers the type here.
- B-11 — ledger ids `S22`, `S23`, `S14`, `S31`, `S7` deleted from review flags 5, 6, 8, 9, 10.
- B-12 — §7 model-parity test placed in `distributions/acme/test/Tellma.Distro.Acme.IntegrationTests` (matches §1.1 and §9).

## Rejected / no change

- B-4 — `GlFeature.Contribute` = exactly three items, no `Securables`: the spec is correct (seam 3: entity securables come from the descriptor); the delta belongs to seams.md §21, not this spec.
- A cross-spec (`core.settings` step in §6.3) — review A itself concludes 0017 needs no change once 0010/seams §18 add the step.
