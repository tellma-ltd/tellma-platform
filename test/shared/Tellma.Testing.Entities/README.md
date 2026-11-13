# Tellma.Testing.Entities

The data layer's shared SQL fixture. Every integration suite of the runtime package that needs a
tenant-shaped database references it. It holds the fixture schema, the remaining stand-in core tables, the
fixture context with its migrations, and the helpers that create a fixture database and open a
tenant over it. It references no test framework, so `dotnet test` never runs it.

## What lives here

| File | Contents |
|---|---|
| `FixtureEntities.cs` | Schema `fixture`: `Widgets` (top-level, system-versioned, activatable, natural key, multilingual name, write-once unique subject, an optional enum, a due date, JSON settings, an image reference), its child `WidgetParts`, its grandchild `WidgetPartNotes` and its sibling `WidgetExtras`; the tree `Nodes` (`MaxDepth = 8`) and its child `Links`, which has a second foreign key to its parent; `Shipments`, a job entity with a `long` key. |
| `PlatformStandIns.cs` | Stand-ins for the core tables the platform's statements name but no feature contributes yet: `core.Jobs` and `core.VersionTags`. Each is replaced when the feature that owns the production table contributes it, and the migrations are regenerated. Core itself contributes the production access tables (`core.Users`, `core.Roles`, `core.RoleMemberships`, `core.Permissions`, `core.UserStamps`, `core.UserPreferences`, with their seeds) and `core.Blobs`, so every audit and blob foreign key of the fixture resolves to a production-shaped table. |
| `FixtureFeature.cs`, `FixtureComposition.cs` | The feature that contributes the fixture entities and the stand-ins, and the composition a fixture host and the fixture context are both built from, so the two share one model and one storage fingerprint. |
| `TellmaFixtureDbContext.cs` | The fixture context, which builds the composition's tenant model as a tenant context builds it, under the table-type sweep scope `tellma.fixture`, and its design-time factory. |
| `Migrations/` | The fixture migrations, scaffolded by `dotnet-ef` and never edited by hand. |
| `FixtureDatabase.cs` | Creates a fixture database the way provisioning creates a tenant database: platform collation, read-committed snapshot, the `tellma_app` role, then the migrations, then change tracking on every table outside `dbo` except the history tables, then the fingerprint row the schema guard reads. |
| `FixtureTenant.cs` | A fixture host bound to one fixture database as tenant `1`. Its scopes carry a request context for the platform's own principal, so a scope's `ITenantDatabase` runs batches against the database, and `ITenantDatabaseFactory` opens handles to it. |
| `ChangeTrackingAudit.cs` | The test-mode audit the host registers. After every successful batch it compares the tables change tracking saw written with the batch's declared writes, and fails the batch on an undeclared write. It reads on connections of its own and is exact while one batch at a time writes the database. |

## Regenerating the migrations

After a fixture entity or a stand-in changes, remove the old migration and scaffold a fresh one.
Scaffolding never connects to a database.

```bash
dotnet tool restore
dotnet ef migrations remove --project test/shared/Tellma.Testing.Entities --startup-project test/shared/Tellma.Testing.Entities
dotnet ef migrations add Initial --project test/shared/Tellma.Testing.Entities --startup-project test/shared/Tellma.Testing.Entities --namespace Tellma.Testing.Entities.Migrations --output-dir Migrations
```

## Using it

```csharp
await using FixtureTenant tenant = await FixtureTenant.CreateAsync(masterConnectionString, uniqueDatabaseName);
await using AsyncServiceScope scope = tenant.CreateScope();
IDataBatch batch = scope.ServiceProvider.GetRequiredService<ITenantDatabase>().CreateBatch(BatchPurpose.Read);
```

The caller owns the database name and drops the database when its run ends.
