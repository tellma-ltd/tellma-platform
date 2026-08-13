# Tellma.Connector.SendGrid.Tests

The raw client, offline.

| Folder | Covers |
|---|---|
| `MailSend/` | The wire shape of a mail-send request, snapshotted through a scripted handler: personalization shape, the plain-before-HTML content ordering SendGrid insists on, attachments and inline content ids, custom args, sandbox mode, the bearer header, and the omission of every absent field (SendGrid answers 400 to several explicit nulls). Plus message-id capture and error-body parsing, including a failure body that is not JSON. |
| `Webhook/` | Signature vectors against a locally generated key pair — acceptance, then rejection on a tampered body, a tampered timestamp, the wrong key, and a malformed signature; multi-key acceptance regardless of order; and a PEM-armoured key pasted with line breaks. The DER-versus-IEEE-P1363 case is the one this suite exists for: verifying with the default format would silently reject every genuine delivery. Also the event parser's tolerance of fields it has never seen. |

## Running

```bash
dotnet test test/connector/sendgrid/Tellma.Connector.SendGrid.Tests
```
