# Tellma.Identity.Web

The **standalone host** for the Tellma Identity Server: a thin ASP.NET Core app that composes the
[`Tellma.Identity`](../Tellma.Identity/README.md) engine with hosting concerns — configuration,
Serilog logging, and the OpenTelemetry pipeline (Azure Monitor export is config-gated). All identity
behavior lives in the engine; this project must stay composition-only (an architecture test asserts
it references no OpenIddict types directly).

Run locally:

```bash
dotnet run --project src/apps/Tellma.Identity.Web
```

In the Development environment the host seeds a dev admin (`admin@localhost`), generates persisted
self-signed signing/encryption certificates, and — because the email pipeline defaults to its log
sink when no transport is named — writes invitation and recovery mail to the console instead of
sending it. Configuration schema: see the `TellmaIdentity` section in `appsettings.json` and the
options documentation in the engine README.

## Email

This host composes the platform's email pipeline; the engine only asks for `IEmailSender`. All three
transports are registered and `Email:Provider` decides which one is constructed, so a deployment
changes transport by configuration alone:

| `Email:Provider` | Transport | Its own settings |
|---|---|---|
| `acs-email` | Azure Communication Services | `Email:AcsEmail` |
| `sendgrid` | SendGrid | `Email:SendGrid` |
| `smtp` | Any SMTP relay — the on-prem path | `Email:Smtp` |
| `log-sink` | Writes to the log; **Development only** | — |

The key is required outside Development, where the pipeline defaults to the log sink. The active
transport's own section is validated at startup, so an incomplete one fails the host rather than the
first person to request a sign-in code. Identity registers `SandboxContext.Never` — it has no
tenants, so none of its mail is ever withheld — and names itself `identity` in email telemetry.

## Failures

Outside Development the host routes its own unhandled exceptions to the engine's error page, ahead of
routing — an exception handler registered behind it would re-execute the request against a pipeline
that has already matched an endpoint, and answer a blank 404. Development keeps the developer
exception page instead, which is more use there than a sanitized one. Either way the response carries
the request's trace identifier as a quotable reference, and the trace it names is the one the logs and
any configured collector already correlate on.

## Behind a reverse proxy

Per-IP rate limiting and audit forensics need the real client IP, which a reverse proxy delivers in
`X-Forwarded-For`. Enable the forwarded-headers middleware and **list the proxy explicitly**:

```json
"ForwardedHeaders": {
  "Enabled": true,
  "KnownProxies": [ "10.0.0.4" ],
  "KnownNetworks": [ "10.0.0.0/8" ]
}
```

The middleware skips its known-proxy check entirely when both lists are empty — that would let any
direct caller spoof the client IP — so startup fails on `Enabled` with no proxy or network
configured. Without this section, the host uses the direct connection's address (correct when
Kestrel terminates client connections itself). An in-proc host that mounts the engine behind a
proxy must configure the same middleware in its own pipeline, ahead of everything else.

**Do not set `ASPNETCORE_FORWARDEDHEADERS_ENABLED`.** That framework switch (some Azure App Service
guidance still recommends it) enables the same middleware with *both* known lists empty — the
spoofable state this section exists to avoid — and bypasses the checks above. The host refuses to
start when it is set; use the `ForwardedHeaders` configuration section instead.

Per-IP rate limiting (sign-in codes, password reset) and audit records use whatever address this
resolves to. Behind a proxy with forwarding unconfigured, every request appears to come from the
gateway, so those limits become effectively global — configure this section in any proxied
deployment.
