# Tellma.Defaults.Azure.Tests

Unit tests of the two Azure defaults bundles, `Tellma.Defaults.Azure` and
`Tellma.Defaults.Azure.AspNetCore`: what each registers, what each leaves unregistered until its
configuration is set, and that a second call changes nothing. Each bundle is composed into a plain
service collection through `AddTellma`, so no host starts and no Azure resource is reached.

The endpoint audit over a host that composes the web bundle is the Acme distribution's web suite's
(`distributions/acme/test/Tellma.Distro.Acme.Web.Tests`).

```bash
dotnet test test/defaults/Tellma.Defaults.Azure.Tests
```
