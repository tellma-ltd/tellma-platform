# Tellma.Distro.Acme

Acme's composition library: `AcmeComposition.Slug` (`acme`) and `AcmeComposition.Compose`, the one
composition method both hosts run. The web host passes it to `AddTellma`, the migrator to
`TellmaMigrator.RunAsync`, so the two build the same model and the migrations the migrator applies
are the ones the web host reads.

Today it composes the Azure defaults (`UseAzureDefaults`: the email pipeline and its transports) and
the two languages Acme serves. Acme's own feature, with its entities and services under `Entities/`
and `Services/`, joins it here; the library references no ASP.NET package, so whatever it composes
is also composed by the migrator.

```csharp
AcmeComposition.Compose(tellma); // what both hosts call
```
