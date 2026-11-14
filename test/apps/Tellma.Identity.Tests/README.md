# Tellma.Identity.Tests

Unit tests for the [`Tellma.Identity`](../../../src/apps/Tellma.Identity/README.md) engine: pure
in-memory tests (fake `TimeProvider`, in-memory stores) covering the authentication-policy engine
(`acr`/`amr` derivation, allowed-methods enforcement, step-up evaluation), the single-use email-code
and one-time-token services, invitation batching, claim destinations, signing-key selection, the
return-URL validator, the per-response widening of the `form-action` policy, branding resolution,
lifecycle transitions, options validation (distribution seed entries included), email templates,
what the error page is given to show, and the logout-token factory.

Two groups need more than a pure function:

- `Store/` runs the collation pin and the latest-migration pin over `Tellma.Identity.Migrations`
  (built by its design-time factory, never connected), and the store check's collation and remedy
  decisions, the Azure SQL Database branch included.
- `Provisioning/` drives the distribution seed through OpenIddict's real application and scope
  managers over in-memory stores (`InMemoryOpenIddictStores.cs`), so secrets are hashed and
  descriptors validated as against SQL Server, and a test can tell a convergence that wrote
  nothing from one that rewrote a row with the same values.

No database, no network: everything requiring SQL Server or a browser lives in
`Tellma.Identity.IntegrationTests` and `Tellma.Identity.E2E`.
