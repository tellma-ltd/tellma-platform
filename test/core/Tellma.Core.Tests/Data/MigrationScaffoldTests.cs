// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Design;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.Extensions.DependencyInjection;
using System.Reflection;
using System.Runtime.Loader;
using Tellma.Core.Data;
using Tellma.Core.EntityFrameworkCore.Design;
using Tellma.Core.Tests.Data.Model;

namespace Tellma.Core.Tests.Data
{
    /// <summary>
    ///     The tenant model's initial migration, scaffolded with the design-time services a
    ///     distribution's migrator project carries: it names every constraint and index by the
    ///     platform's scheme, compiles, and its snapshot replays to the model it came from.
    /// </summary>
    public class MigrationScaffoldTests(DataModel model) : IClassFixture<DataModel>
    {
        [Fact]
        public void The_initial_migration_names_every_constraint_and_index_by_the_platforms_scheme()
        {
            ScaffoldedMigration migration = Scaffold(out TellmaDbContext context);
            context.Dispose();

            foreach (string name in (string[])
                [
                    "FK_Widgets_CreatedById", "FK_Widgets_CenterId", "FK_Widgets_ImageId", "FK_WidgetParts_WidgetId",
                    "FK_WidgetExtras_Id", "FK_Nodes_ParentId", "FK_Links_OtherNodeId", "FK_Shipments_JobId",
                    "IX_WidgetParts_WidgetId", "IX_Widgets_CenterId", "IX_Widgets_Kind_Price", "IX_Shipments_State_CreatedAt",
                    "UX_Widgets_Code", "UX_Widgets_Subject", "UX_Widgets_ImageId", "UX_WidgetParts_WidgetId_Ordinal",
                    "UX_Nodes_Node", "UX_Shipments_JobId", "CK_Widgets_Id", "DF_Widgets_IsActive", "DF_Nodes_SubtreeCount",
                    "sq_Widgets", "sq_Shipments",
                ])
            {
                Assert.Contains($"\"{name}\"", migration.MigrationCode, StringComparison.Ordinal);
            }

            // No name the provider would choose survives.
            Assert.DoesNotContain("\"FK_Widgets_Users_", migration.MigrationCode, StringComparison.Ordinal);
            Assert.DoesNotContain("IX_Widgets_CreatedById", migration.MigrationCode, StringComparison.Ordinal);
        }

        [Fact]
        public void The_scaffolded_migration_compiles_creates_every_table_and_its_snapshot_replays_to_the_model()
        {
            ScaffoldedMigration migration = Scaffold(out TellmaDbContext context);
            using (context)
            {
                Assembly assembly = Compile(migration);

                // --- The compiled up operations create every table the model maps ---
                Type migrationType = assembly.GetTypes().Single(static type => type.IsSubclassOf(typeof(Migration)));
                var compiled = (Migration)Activator.CreateInstance(migrationType)!;
                Assert.Equal(
                    model.DesignModel.GetEntityTypes().Select(static entityType => entityType.GetTableName()).Order(StringComparer.Ordinal),
                    compiled.UpOperations.OfType<CreateTableOperation>().Select(static table => table.Name).Order(StringComparer.Ordinal));

                // --- The snapshot replays to the model with no difference ---
                Type snapshotType = assembly.GetTypes().Single(static type => type.IsSubclassOf(typeof(ModelSnapshot)));
                var snapshot = (ModelSnapshot)Activator.CreateInstance(snapshotType)!;
                IModel replayed = context.GetService<IModelRuntimeInitializer>().Initialize(snapshot.Model, designTime: true);
                IMigrationsModelDiffer differ = context.GetService<IMigrationsModelDiffer>();
                Assert.Empty(differ.GetDifferences(replayed.GetRelationalModel(), model.DesignModel.GetRelationalModel()));
            }
        }

        /// <summary>Scaffolds the initial migration of the test model.</summary>
        /// <param name="context">The context it was scaffolded from; the caller disposes it.</param>
        /// <returns>The scaffolded migration.</returns>
        private ScaffoldedMigration Scaffold(out TellmaDbContext context)
        {
            context = model.Factory.CreateContext(DataModel.UnusedConnectionString);

            // The design-time services read the context's own services, which exist once the
            // context has initialised; EF tooling likewise uses a context it has already built.
            _ = context.GetService<IDesignTimeModel>().Model;
            using ServiceProvider services = DesignServices(context);
            using IServiceScope scope = services.CreateScope();
            return scope.ServiceProvider.GetRequiredService<IMigrationsScaffolder>()
                .ScaffoldMigration("Initial", "Tellma.Core.Tests.Scaffolded", subNamespace: null, language: "C#");
        }

        /// <summary>
        ///     The design-time services a migrator project gets, in the order EF tooling builds them:
        ///     the context's own services first, then the table-type extension's (the referenced
        ///     services the migrator assembly names), the SQL Server provider's, and EF's defaults.
        /// </summary>
        /// <param name="context">The context.</param>
        /// <returns>The services.</returns>
        private static ServiceProvider DesignServices(DbContext context)
        {
            ServiceCollection services = new();
            _ = services.AddDbContextDesignTimeServices(context);
            new TableTypesDesignTimeServices().ConfigureDesignTimeServices(services);
#pragma warning disable EF1001 // The provider's design-time services bundle, as EF tooling loads it.
            new Microsoft.EntityFrameworkCore.SqlServer.Design.Internal.SqlServerDesignTimeServices().ConfigureDesignTimeServices(services);
#pragma warning restore EF1001
            _ = services.AddEntityFrameworkDesignTimeServices();
            return services.BuildServiceProvider();
        }

        /// <summary>Compiles the scaffolded files into a collectible assembly.</summary>
        /// <param name="migration">The scaffolded migration.</param>
        /// <returns>The assembly.</returns>
        private static Assembly Compile(ScaffoldedMigration migration)
        {
            Dictionary<string, MetadataReference> references = [];
            if (AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") is string trusted)
            {
                foreach (string path in trusted.Split(Path.PathSeparator))
                {
                    references[Path.GetFileName(path)] = MetadataReference.CreateFromFile(path);
                }
            }

            foreach (Assembly loaded in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (!loaded.IsDynamic && !string.IsNullOrEmpty(loaded.Location))
                {
                    references[Path.GetFileName(loaded.Location)] = MetadataReference.CreateFromFile(loaded.Location);
                }
            }

            var compilation = CSharpCompilation.Create(
                "Tellma.Core.Tests.Scaffolded",
                [
                    CSharpSyntaxTree.ParseText(migration.MigrationCode),
                    CSharpSyntaxTree.ParseText(migration.MetadataCode),
                    CSharpSyntaxTree.ParseText(migration.SnapshotCode),
                ],
                references.Values,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            using MemoryStream stream = new();
            Microsoft.CodeAnalysis.Emit.EmitResult result = compilation.Emit(stream);
            Assert.True(
                result.Success,
                "The scaffolded migration failed to compile:\n" + string.Join("\n", result.Diagnostics.Where(static d => d.Severity == DiagnosticSeverity.Error)));

            stream.Position = 0;
            return new AssemblyLoadContext("Tellma.Core.Tests.Scaffolded", isCollectible: true).LoadFromStream(stream);
        }
    }
}
