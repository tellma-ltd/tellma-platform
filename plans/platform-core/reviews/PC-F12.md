# PC-F12: consent-screen flow under resource grants

## Review 1

### Findings

None. Reviewed commit b3a8f07.

- (a) The root cause is right. `AuthorizationController.cs:92` returns the `ProtocolRefusal` before any interaction can begin. Before PC-009, the request for `tellma_api`, a scope the client does not hold, slipped past that point and reached the consent view. The new request, `openid profile email`, asks only for scopes the client holds. `ConsentScopeCatalog` gives all three a title, so the consent list axe scans has three rows. The `tellma_api` row that was dropped uses the same row markup, so the scan loses no structural coverage.
- (b) The permission change is confined to `IdentityServerFixture.CreateConsentClientAsync`, which only the E2E tests use (`a11y-consent` and `e2e-consent`). No product seed or production config changes. The extra `email` permission has no effect on `ConsentTests`, which requests `openid profile`.
- (c) The E2E browser requests (`ConsentTests.cs:96`, `StepUpTests.cs:172`) ask for `openid profile` and send no `resource`. No other E2E request asks for a scope or resource its client lacks. The integration tests send raw protocol requests and passed 178/178 on this commit. `TenantResourceTests.cs:165` already asserts that a refused resource never reaches the sign-in page.
- (d) Ran `AccessibilityTests` once on b3a8f07: 28/28 passed.

### Responses

No findings, so nothing to fix or push back on.
