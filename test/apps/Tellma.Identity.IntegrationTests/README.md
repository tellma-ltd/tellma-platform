# Tellma.Identity.IntegrationTests

Integration tests (`[Trait("Category", "Integration")]`) that exercise every OAuth/OIDC flow
end-to-end against a real SQL Server:

- One Testcontainers SQL Server per assembly (same pattern as
  `Tellma.Core.EntityFrameworkCore.IntegrationTests`); set `TELLMA_TEST_SQL` to target LocalDB or a
  local SQL Server instead, and `TELLMA_TEST_SQL_IMAGE` to override the container image.
- **Both hosting compositions**: `WebApplicationFactory` over the standalone `Tellma.Identity.Web`
  host and over the distribution-shaped `Tellma.Identity.TestInProcHost` asset (engine mounted at
  `/id`); the composition-parity suite is the architecture test that both shapes share one
  registration path.
- Protocol flows are driven with raw `HttpClient` calls plus AngleSharp form parsing — the
  assertions are the protocol details themselves. Browser-only ceremonies (passkeys) are covered in
  `Tellma.Identity.E2E`; integration sign-ins use the email-code path via the capturing email sink.
