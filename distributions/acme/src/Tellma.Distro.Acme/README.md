# Tellma.Distro.Acme

Acme's composition library: `AcmeComposition.Slug` (`acme`) and `AcmeComposition.Compose`, the one
composition method both hosts run. The web host passes it to `AddTellma`, the migrator to
`TellmaMigrator.RunAsync`, so the two build the same model and the migrations the migrator applies
are the ones the web host reads.

Today it composes the Azure defaults (`UseAzureDefaults`: the email pipeline and its transports),
the two languages Acme serves and Acme's own feature, `AcmeFeature` (`acme`). The feature is the one
the client app's `acmeFeature` names, and it declares and contributes nothing yet: its entities and
services join it under `Entities/` and `Services/`. The library references no ASP.NET package, so
whatever it composes is also composed by the migrator.

```csharp
AcmeComposition.Compose(tellma); // what both hosts call
```
