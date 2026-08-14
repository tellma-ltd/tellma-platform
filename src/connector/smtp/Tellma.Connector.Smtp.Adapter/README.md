# Tellma.Connector.Smtp.Adapter

The SMTP transport: a MailKit-based `IEmailSender` for on-prem smarthosts, air-gapped installations,
staging mail traps, and the local-development path when you want to see real rendered mail.

```csharp
services.AddSmtpEmail(builder.Configuration);   // declares the "smtp" transport
```

MailKit, not `System.Net.Mail.SmtpClient` — Microsoft marks the latter compat-only and names MailKit
as the replacement, and MimeKit underneath gives correct multipart, encoding, and
internationalization behaviour for free.

## Configuration

```jsonc
"Email": {
  "Provider": "smtp",
  "Smtp": {
    "Host": "smtp.contoso.local",
    "Port": 587,
    "SecureSocket": "StartTls",       // None | Auto | SslOnConnect | StartTls | StartTlsWhenAvailable
    "Username": "…",
    "Password": "<from the secret store>",
    "From": { "Address": "no-reply@contoso.com", "DisplayName": "Contoso ERP" },
    "TimeoutSeconds": 30,

    // Optional. A mail trap that receives a sandbox tenant's external mail, so it is inspectable
    // instead of delivered. Absent → that mail is withheld entirely.
    "Sandbox": { "Host": "mailtrap.staging.local", "Port": 1025, "SecureSocket": "None" }
  }
}
```

`SecureSocket` defaults to `StartTls`: mandatory TLS that fails the connection when the server cannot
upgrade. That is the fail-closed default for credentialed submission — a session that silently
downgraded to plaintext would be worse than one that failed. The opportunistic modes are an explicit
choice for legacy relays. The sandbox channel keeps its own host, port, and credentials but inherits
the live `From`, and a local Mailpit therefore needs an explicit `"SecureSocket": "None"`.

Adding or removing the `Sandbox` section takes a restart — whether the transport has a sandbox
channel at all is what the pipeline needs to know at registration. Every other setting is re-read per
batch, so rotating a password does not.

## How a batch goes out

One MailKit client per call: the client is not thread-safe and holds a single connection, and the
batch contract already amortizes the setup — connect, authenticate, send every message on the reused
connection, quit. No connection pooling; a future throughput need can add it behind the same
contract.

| Failure | Result |
|---|---|
| Connect or authenticate | **Throws.** Nothing was attempted, so the caller can retry the whole batch safely. |
| A 4xx reply to a message | `TransientFailure` — the SMTP transient-negative class: greylisting, throttling, mailbox busy. |
| A 5xx reply | `Rejected` — permanent-negative: unknown user, policy refusal, message too large. |
| The connection dies mid-batch | That message is `TransientFailure`, the adapter reconnects once **per batch**, and the remainder continues. If the reconnect fails — or the connection drops again after it, since the budget is spent against the batch rather than against each loss — every remaining message is `TransientFailure`. Never an exception once the batch has started. |

A structurally invalid message, or one whose addresses MailKit cannot parse, is rejected on its own
without touching the wire: one malformed row must not poison a bulk dispatch. When the server
advertises SMTPUTF8, sends use MailKit's international format so non-ASCII addresses pass through
unmangled.

`ProviderMessageId` is the generated `Message-Id`, which is what a receiving server's logs and
support tickets can be searched by. The smarthost's own `250` acceptance line is logged at Debug.

## What this transport cannot do

**Delivery events, structurally.** SMTP's only feedback is the synchronous accept, which means "the
smarthost took responsibility", not "delivered", so every result reports
`ExpectsDeliveryEvents = false`. Bounces arrive out of band as messages to the return path, and
nothing here reads a mailbox. **Relay hygiene and bounce handling belong to whoever operates the
smarthost**; there is no provider suppression layer on this path.

## Local development

Run [Mailpit](https://mailpit.axllent.org/) — SMTP endpoint in, web UI and REST API out — and point
the transport at it:

```jsonc
"Email": { "Provider": "smtp", "Smtp": { "Host": "localhost", "Port": 1025, "SecureSocket": "None", "From": { "Address": "no-reply@localhost" } } }
```

Configuration only, no code. The same mechanism is what a staging deployment uses to make its mail
interceptable rather than absent.
