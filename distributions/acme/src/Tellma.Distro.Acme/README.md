# Tellma.Distro.Acme

Acme's composition library: `AcmeComposition.Slug` (`acme`) and `AcmeComposition.Compose`, the one
composition method both hosts run. The web host passes it to `AddTellma`, the migrator to
`TellmaMigrator.RunAsync`, so the two build the same model and the migrations the migrator applies
are the ones the web host reads.

It composes the Azure defaults (`UseAzureDefaults`: email, blobs and imaging), the general ledger
(`GlFeature`) with Acme's center leaf `MyCenter` in place of GL's `Center`, Acme's own feature, and
the two languages Acme serves. The library references no ASP.NET package, so whatever it composes is
also composed by the migrator.

```csharp
AcmeComposition.Compose(tellma); // what both hosts call
```

## The `acme` feature

`AcmeFeature` is the feature the client app's `acmeFeature` names. It requires GL and contributes:

| Folder | What |
|---|---|
| `Entities/` | `Currency` (cacheable), `Customer` (activatable, with `Contacts`), `SalesInvoice` (with `Lines` and `Attachments`, and the child query surface `acme.SalesInvoiceLine`), and `MyCenter`, which adds `ManagerId` → `core.User` to `gl.Center`. Their annotations alone produce the `acme` tables. |
| `Services/` | `CustomerService` with the `set-credit-limit` action (`SetCreditLimitArguments`), refusing a negative limit on a save and on the action; `SalesInvoiceService` with the `close` action; and `RelatedDocumentsContributor`, the invoices' `related-documents` details extra: the requested invoices' customers' other invoices the caller may read, latest first, at most 10. |
| `AcmeSettings.cs` | The five setting keys: `acme.invoicing.prefix`, `acme.invoicing.padding` (1 to 12), `acme.sales.defaultCurrencyId`, `acme.sales.allowedCustomerTypes` and the secret `acme.integration.marminApiKey`. |
| `Development/` | The development tenants' data: `AcmeSampleCenters`, the 40-node center tree that replaces GL's default, and `AcmeSampleDataStep` (`acme.sample-data`, after GL's step), which writes `AcmeSampleData`'s rows and the end-to-end principals of `AcmeDevelopmentPrincipals`. |
| `Resources/` | `Strings.resx` and `Strings.ar.resx`: the labels of every entity, member, action, enum value and setting, and the validation messages. |

## Development data

In Development only, `migrate` writes into each seed tenant, through the stacks as the system:

- Acme's center tree, four of whose centers end inactive;
- two currencies, and 60 customers of all three types in both currencies, 10 of them with a drawn
  picture (`SamplePortraits`), some with contacts and four inactive;
- the roles `Sales`, `CenterReaders` (a read of the sales centers alone) and `CustomerEditors`
  (the customers' read and save, with the currencies' read);
- the end-to-end principals: `sales@localhost` (`Sales`), `centers@localhost` (`CenterReaders`),
  `editor@localhost` (`CustomerEditors`), in tenant 2 only `sandbox@localhost` (`Sales`), and the
  service account "Acme Integration" (`Sales`, no credentials until a test issues them);
- 20 sales invoices over the invoiced sales centers, the six oldest closed.

Provisioning runs in the migrator, where no identity server is reachable, so the individuals are not
invited: each is bound to the fixed subject the web host's in-proc identity server seeds for its
address (`TellmaIdentity:Seed:DevUsers` in `appsettings.Development.json`), through the platform's
`IDevelopmentSubjects`, and the step records their catalog membership hints. Each then signs in
with the email code the web host's console prints. Rows the tenant already holds are kept, so a
re-run never duplicates them.
