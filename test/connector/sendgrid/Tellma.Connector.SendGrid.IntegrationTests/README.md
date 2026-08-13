# Tellma.Connector.SendGrid.IntegrationTests

The one thing no offline test can vouch for: that the payloads, credentials, and quotas satisfy the
real SendGrid API. Three cases — a minimal message, a full-feature one, and a small batch — each
asserting acceptance.

Every request goes out on the transport's sandbox channel, which is SendGrid's own validate-only
mode: the payload is fully validated, nothing is delivered, no credits are consumed, and no events
are emitted. Webhook delivery therefore cannot be exercised here; its correctness rests on the
signature vectors and recorded payloads in the offline suites, and SendGrid's dashboard "Test Your
Integration" button covers manual smoke at onboarding.

The sender is composed through the adapter's own registration rather than constructed directly, so
this also proves the composition a deployment uses.

## Running

Marked `Category=Integration` and `Live=true`, so it is excluded from every PR job and runs in the
nightly workflow. Each test skips cleanly when the credentials are absent, so running the filter
locally without them is safe.

```bash
dotnet test test/connector/sendgrid/Tellma.Connector.SendGrid.IntegrationTests --filter "Live=true"
```

| Variable | What it is |
|---|---|
| `TELLMA_SENDGRID_TEST_APIKEY` | A Restricted Access key with **Mail Send** only, on the test subuser. |
| `TELLMA_SENDGRID_TEST_SENDER` | An address on a domain that subuser has authenticated. |
