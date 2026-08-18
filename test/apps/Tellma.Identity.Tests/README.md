# Tellma.Identity.Tests

Unit tests for the [`Tellma.Identity`](../../../src/apps/Tellma.Identity/README.md) engine: pure
in-memory tests (fake `TimeProvider`, in-memory stores) covering the authentication-policy engine
(`acr`/`amr` derivation, allowed-methods enforcement, step-up evaluation), the single-use email-code
and one-time-token services, invitation batching, claim destinations, signing-key selection, the
return-URL validator, the per-response widening of the `form-action` policy, branding resolution,
lifecycle transitions, options validation, email templates, what the error page is given to show,
and the logout-token factory.

No database, no network: everything requiring SQL Server or a browser lives in
`Tellma.Identity.IntegrationTests` and `Tellma.Identity.E2E`.
