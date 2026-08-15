# Tellma.Connector.Smtp.Adapter.Tests

The SMTP adapter against a real SMTP endpoint hosted inside the test process. There is no canonical
"the" SMTP server to run a live suite against, so this is the honest equivalent: MailKit over a real
socket, real MIME on the wire, and no external infrastructure — which is why this is an ordinary
`*.Tests` project rather than an `IntegrationTests` one.

| Folder | Covers |
|---|---|
| `Infrastructure/` | The in-process server: a free loopback port found by probing (the whole solution's suites run in parallel, so a fixed port would collide), readiness by connection probe rather than by sleeping, raw MIME captured and re-parsed, and scripted reply codes and authentication failures. |
| `Protocol/` | Full round-trip fidelity — multipart ordering, `cid:` linked resources, ordinary attachments, non-ASCII subjects and display names, the default sender, the reported `Message-Id` — plus the reply-code outcome map, authentication success and failure, the fail-closed TLS default, and the mid-batch reconnect. |
| `Conformance/` | The SMTP transport answering the shared `IEmailSender` contract. |

The reconnect cases are driven at the client seam rather than by tearing down a socket: socket-level
teardown timing is platform-sensitive, while the behaviour that matters — exactly one reconnect, the
in-flight message reported transient and never re-sent — is exact there.

## Running

```bash
dotnet test test/connector/smtp/Tellma.Connector.Smtp.Adapter.Tests
```
