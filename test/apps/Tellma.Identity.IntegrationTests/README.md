# Tellma.Identity.IntegrationTests

Integration tests (`[Trait("Category", "Integration")]`) that exercise every OAuth/OIDC flow
end-to-end against a real SQL Server:

- One SQL Server for the database-backed classes, the shared `SqlServerFixture` of `Tellma.Testing.Support` joined as the
  `SqlServer` collection fixture (so protocol tests that never touch a database run without
  Docker); set `TELLMA_TEST_SQL` to target LocalDB or a local SQL Server instead, and
  `TELLMA_TEST_SQL_IMAGE` to override the container image.
- **Databases come from the engine's model.** `SqlServerFixture.ReserveDatabase` only names one;
  the host starts with migrations on and EF creates it in the platform collation, as a developer's
  first start does. A test that needs a database up front — empty, or in another collation for the
  store check to refuse — creates it with `CreateDatabaseAsync`, which always states the collation:
  the test server's own default is not the platform's, and the store check refuses it.
- **Both hosting compositions**: `WebApplicationFactory` over the standalone `Tellma.Identity.Web`
  host and over the distribution-shaped `Tellma.Identity.TestInProcHost` asset (engine mounted at
  `/id`); the composition-parity suite is the architecture test that both shapes share one
  registration path.
- Protocol flows are driven with raw `HttpClient` calls plus AngleSharp form parsing — the
  assertions are the protocol details themselves. Browser-only ceremonies (passkeys) are covered in
  `Tellma.Identity.E2E`; integration sign-ins use the email-code path via the capturing email sink.
- External-login callbacks are reachable here through `ExternalProviderStub`, which writes the
  Identity external cookie a provider's handler would have written and nothing else. The round trip
  to Google is the one part of that flow a test cannot make; everything the callback then does with
  the assertion — linking, refusing, signing in — is the real code path.
