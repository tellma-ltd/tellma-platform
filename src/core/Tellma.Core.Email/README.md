# Tellma.Core.Email

The email pipeline: everything between "business code composed a message" and "a connector put it on
a wire". Every host adds it explicitly — it is not referenced by `Tellma.Core`, so a worker, the
identity server, or a landing app composes email without dragging in the distribution machinery.

```csharp
services.AddTellmaEmail();                          // this package
services.AddSmtpEmail(builder.Configuration);       // one or more connector adapters
services.AddSingleton(new DeploymentIdentity("etpharma", builder.Environment.EnvironmentName));
services.AddScoped<ISandboxContext, MyTenantSandboxContext>();  // SandboxContext.Never if tenant-less
```

Consumers then take `IEmailSender` and never learn which transport is behind it.

## What it does

**Picks the transport from configuration.** Adapters do not hand `IEmailSender` to DI; they declare
an `EmailTransportRegistration`, and `Email:Provider` names the one that sends. Flipping a deployment
from one transport to another edits configuration, never code, and an adapter that is compiled in but
not selected is never even constructed.

**Applies the sandbox policy centrally.** [EmailRouter](EmailRouter.cs) is the sole registered
`IEmailSender`, so no send path can bypass it:

| Tenant | Audience | What happens |
|---|---|---|
| Live | either | Passed through to the live sender untouched. |
| Sandbox | Internal | [Marked](SandboxMarker.cs) as test-originated, then really sent — it addresses staff of the sending system. |
| Sandbox | External | Sent on the transport's sandbox channel, or withheld entirely when it has none. Either way the outcome is `Sandboxed`. |

`Sandboxed` is success-class and terminal: workflows proceed exactly as if sent, while status UIs
render the truth. It means one thing regardless of mechanism — *no real email went out to the
recipient* — because to the person testing, a provider-validated message and a withheld one are the
same event.

**Ships the Development log sink.** [LogSinkEmailSender](LogSinkEmailSender.cs) writes whole messages
to `ILogger`, which is what makes a fresh clone able to send mail with no configuration at all. It is
Development-only, guarded twice: the startup gate refuses to activate it elsewhere, and its
constructor refuses to build. Staging is deliberately included in the ban — it exists to be a
faithful replica of production, which a log sink is not; a staging deployment points a real transport
at a mail trap instead.

**Routes delivery events.** [EmailDeliveryEventDispatcher](EmailDeliveryEventDispatcher.cs) groups a
provider's callback batch by owner key and hands each owner its share in one call. Handler failures
propagate on purpose: no event queue exists at this tier, so the provider's redelivery is the only
durable retry, and swallowing a failure would lose the event instead.

**Measures every transport identically.** Instruments on the `Tellma.Email` meter and spans on the
`Tellma.Email` activity source, emitted here rather than by the adapters so no transport can be
better or worse instrumented than another. Hosts opt in by adding both names to their OpenTelemetry
configuration.

## What fails at startup, and why

All of it before the host serves traffic, aggregated into one diagnostic so a misconfigured
deployment is fixed in one restart rather than one problem per restart:

- No `Email:Provider` outside Development, or one naming no registered transport — the message lists
  what *is* registered, so a typo is a one-glance fix.
- Two transports under one name, or a name that is not lowercase kebab-case.
- `log-sink` selected outside Development.
- No `DeploymentIdentity` — the correlation wire envelope and the cross-deployment event filter both
  key on it.
- No `ISandboxContext` — a composition that forgot to decide must fail, not silently treat sandbox
  tenants as live.
- Two delivery-event handlers claiming one owner key.

Startup also builds the **active** transport once, so that adapter's own configuration is validated
now rather than on the first send.

## Rules this package lives by

- **Adapters bind options and register an `IValidateOptions<T>`, but never call `ValidateOnStart`.**
  Only the active transport is warmed; eager validation would make a compiled-in but unselected
  adapter fail startup for configuration nobody asked it to have.
- **Recipient addresses and message content appear only in the log sink's own output and at Debug.**
  Information and above carry correlations, provider ids, counts, and provider error texts — enough
  to find the row, never the person. (A provider error text can quote an address back; that is
  accepted, being failure-path-only and operationally essential.)
- **The marker text is fixed English.** It is an operational token, filterable in every locale, and
  the message contract carries no locale to translate against.
- **No ASP.NET dependency.** The one piece of email infrastructure that needs one is the webhook
  fronting, which lives in [Tellma.Core.Webhooks](../Tellma.Core.Webhooks/README.md).
