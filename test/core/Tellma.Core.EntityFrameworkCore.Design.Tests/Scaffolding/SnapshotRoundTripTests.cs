// Copyright (c) 2026 Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Design;
using Microsoft.Extensions.DependencyInjection;
using Tellma.Core.EntityFrameworkCore.Design.Tests.Infrastructure;
using Tellma.Core.EntityFrameworkCore.TableTypes;
using Tellma.Core.EntityFrameworkCore.Tests.Infrastructure;

namespace Tellma.Core.EntityFrameworkCore.Design.Tests.Scaffolding
{
    /// <summary>
    ///     The standard EF snapshot round-trip: model → snapshot C# → compile → diff against the
    ///     live model must be empty. This is the proof that the table-type annotations (config and
    ///     derived definitions, including column order) survive the model snapshot with stock
    ///     snapshot code generation.
    /// </summary>
    public class SnapshotRoundTripTests
    {
        [Fact]
        public void Snapshot_round_trip_produces_an_empty_diff()
        {
            using ModelTestContext context = TestModel.CreateContext(mb =>
            {
                mb.Entity<Order>(e =>
                {
                    e.ToTable("Orders", "gl");
                    e.HasTableType();
                    e.IsMemoryOptimizedTableType();
                    e.HasTableTypeGrants("tellma_app");
                    e.Property(o => o.Memo).ExcludeFromTableType();
                });
                mb.Entity<Product>(e => e.ToTable("Products", "inv"));
                mb.Entity<Plain>(e => e.ToTable("Plains", "dbo"));
                mb.HasTableType("IdStateList", schema: "dbo", type => type
                    .Column<int>("Id")
                    .Column<short>("State")
                    .HasKey("Id")
                    .HasGrants("tellma_app"));
            });

            using ServiceProvider services = DesignTestHelpers.BuildDesignServices(context);
            IMigrationsCodeGenerator codeGenerator = services.GetRequiredService<IMigrationsCodeGenerator>();
            IModel liveModel = TestModel.GetFinalizedModel(context);

            string snapshotCode = codeGenerator.GenerateSnapshot(
                "RoundTrip.Migrations", typeof(ModelTestContext), "RoundTripModelSnapshot", liveModel);

            // Definitions appear as readable fluent calls (one line per column in diffs), never as
            // raw JSON HasAnnotation strings; replaying them rebuilds the definition annotations
            // byte-for-byte (the diff contract), which the empty diff below proves. Standalone
            // CONFIG annotations (raw registration input) are live-model-only and stay out of
            // snapshots entirely — the definition is the contract.
            Assert.Contains(".HasTableTypeDefinition(", snapshotCode, StringComparison.Ordinal);
            Assert.DoesNotContain(
                $"HasAnnotation(\"{TableTypeAnnotationNames.DefinitionPrefix}", snapshotCode, StringComparison.Ordinal);
            Assert.DoesNotContain(TableTypeAnnotationNames.StandalonePrefix, snapshotCode, StringComparison.Ordinal);

            Assembly assembly = DesignTestHelpers.Compile(snapshotCode, "RoundTripSnapshot");
            Type snapshotType = assembly.GetType("RoundTrip.Migrations.RoundTripModelSnapshot")!;
            var snapshot = (ModelSnapshot)Activator.CreateInstance(snapshotType)!;

            IModelRuntimeInitializer initializer = context.GetService<IModelRuntimeInitializer>();
            IModel snapshotModel = initializer.Initialize(snapshot.Model, designTime: true);

            // The differ sees no difference between the snapshot model and the live model...
            IMigrationsModelDiffer differ = context.GetService<IMigrationsModelDiffer>();
            Assert.Empty(differ.GetDifferences(snapshotModel.GetRelationalModel(), liveModel.GetRelationalModel()));
            Assert.False(differ.HasDifferences(snapshotModel.GetRelationalModel(), liveModel.GetRelationalModel()));

            // ...and the metadata API reads identical definitions from both sides.
            Assert.Equal(
                liveModel.GetTableTypes().Select(TableTypes.Json.TableTypeJson.Serialize),
                snapshotModel.GetTableTypes().Select(TableTypes.Json.TableTypeJson.Serialize));
        }
    }
}
