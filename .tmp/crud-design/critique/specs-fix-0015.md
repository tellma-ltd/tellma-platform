# Fix record — spec 0015 (`docs/specs/0015-crud-web-api.md`), 2026-09-04

Sources: specs-review-A.md (no §0015 section; cross-spec: `ClientNames` matches 0010 CSRF rule 3 — no drift), specs-review-B.md §0015 findings 1–5 and the cross-spec entry (0019 finding 3, `notification-preferences/get`).

## Applied

- B-2 — §1.2 now says `McpFeature` declares `[Requires<CoreFeature>]` (0014 has no separate stack feature; matches §11.1).
- B-3 — §2.4 route list gains `notification-preferences/get` beside `save` (0020 §4.3). seams.md §13.1's route line still lacks it — delta for seams.md.
- B-4 — "round-trip ledger" → "round-trip budget" in §10.1, §10.2 and §13.3 (three occurrences; the review named two).
- B-5 — `all` body is a new record `AllRequest(Select: string?, Include: list<string>?)` in §3.3, referenced in §2.3 and mapped to `GetAllCachedAsync(DetailsRequest)` in §4.2. Chosen over `GetRequest.Id: long?` (a required-by-one-operation nullable is a weaker contract); seams.md §13.1 needs the record added.
- Cross-spec (0019 finding 3) — `worker` added to the `tellma.client` closed tag set (§10.1) and to the `RequestContext.Client` values in §6.1 (set by job scopes, never reaches the CSRF filter). `ClientNames` stays `{ web, cli }` — it is the CSRF allow-list only.

## Rejected

- B-1 (`IdsRequest.Arguments` not in seams.md §13.1) — no spec change: 0015 owns seam 13 and `Arguments` is needed for `[EntityAction]` typed arguments (§2.3, §4.2); a separate body type would give actions two request shapes. seams.md §13.1 (and 0014's restatement) should absorb the member; that is a seams.md/0014 edit, not a 0015 one.
