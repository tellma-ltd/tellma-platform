// Copyright (c) 2026 Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Tellma.Core.Abstractions.TableTypes;
using Tellma.Core.EntityFrameworkCore.TableTypes;
using Tellma.Core.EntityFrameworkCore.TableTypes.Json;
using Tellma.Core.EntityFrameworkCore.TableTypes.Operations;
using Tellma.Core.EntityFrameworkCore.Tests.Infrastructure;

namespace Tellma.Core.EntityFrameworkCore.Tests.Versioning
{
    /// <summary>
    ///     Content-addressed naming and sweep scoping (spec 0001 §3): physical names are stable and
    ///     content-derived, the metadata API exposes them, and <c>ExcludeFromMigrations()</c> keeps a
    ///     shared type bindable without this context creating or keeping it.
    /// </summary>
    public class VersioningAndScopingTests
    {
        [Fact]
        public void Physical_name_is_logical_name_plus_eight_hex_and_is_stable()
        {
            string json = TableTypeJson.Serialize(new TableTypeDefinition
            {
                Name = "OrdersList",
                Schema = "gl",
                Columns = [new TableTypeColumnDefinition { Name = "Id", StoreType = "int" }],
                PrimaryKey = ["Id"],
            });

            (string hash, string physical) = TableTypeNaming.Resolve("OrdersList", json);

            Assert.Equal(64, hash.Length);
            Assert.Equal($"OrdersList_{hash[..8]}", physical);
            // Deterministic across calls (same machine, same content).
            Assert.Equal(physical, TableTypeNaming.Resolve("OrdersList", json).PhysicalName);
        }

        [Fact]
        public void Metadata_api_exposes_logical_and_physical_names_that_match_the_differ()
        {
            using ModelTestContext context = TestModel.CreateContext(mb => mb.Entity<Order>(e =>
            {
                e.ToTable("Orders", "gl");
                e.ExcludesRowVersionFromTableType();
                e.HasTableType();
            }));

            TableTypeDefinition definition = Assert.Single(TestModel.GetFinalizedModel(context).GetTableTypes());
            Assert.Equal("OrdersList", definition.Name);
            Assert.StartsWith("OrdersList_", definition.PhysicalName);

            // The metadata physical name equals the one the differ freezes into the create.
            IMigrationsModelDiffer differ = context.GetService<IMigrationsModelDiffer>();
            CreateTableTypeOperation create = differ
                .GetDifferences(null, TestModel.GetRelationalModel(context))
                .OfType<CreateTableTypeOperation>()
                .Single();
            Assert.Equal(definition.PhysicalName, create.PhysicalName);
        }

        [Fact]
        public void Excluded_type_is_bindable_but_neither_created_nor_kept()
        {
            using ModelTestContext owner = TestModel.CreateContext(mb =>
            {
                mb.Entity<Plain>();
                mb.HasTableType<IdList>(schema: "dbo");
            });
            using ModelTestContext nonOwner = TestModel.CreateContext(mb =>
            {
                mb.Entity<Plain>();
                mb.HasTableType<IdList>(schema: "dbo", buildAction: t => t.ExcludeFromMigrations());
            });

            // Both contexts expose IdList in the metadata API (it is bindable at runtime).
            Assert.Contains(TestModel.GetFinalizedModel(owner).GetTableTypes(), d => d.Name == "IdList");
            Assert.Contains(TestModel.GetFinalizedModel(nonOwner).GetTableTypes(), d => d.Name == "IdList");

            // The owner creates and keeps it.
            IReadOnlyList<MigrationOperation> ownerOps =
                owner.GetService<IMigrationsModelDiffer>().GetDifferences(null, TestModel.GetRelationalModel(owner));
            Assert.Contains(ownerOps.OfType<CreateTableTypeOperation>(), c => c.Name == "IdList");
            Assert.Contains(ownerOps.OfType<CleanupTableTypesOperation>().Single().KeepList!, n => n.StartsWith("IdList_", StringComparison.Ordinal));

            // The non-owner neither creates it nor keeps it (another context owns the physical type).
            IReadOnlyList<MigrationOperation> nonOwnerOps =
                nonOwner.GetService<IMigrationsModelDiffer>().GetDifferences(null, TestModel.GetRelationalModel(nonOwner));
            Assert.DoesNotContain(nonOwnerOps.OfType<CreateTableTypeOperation>(), c => c.Name == "IdList");
            CleanupTableTypesOperation? nonOwnerCleanup = nonOwnerOps.OfType<CleanupTableTypesOperation>().SingleOrDefault();
            Assert.True(nonOwnerCleanup is null || nonOwnerCleanup.KeepList!.All(n => !n.StartsWith("IdList_", StringComparison.Ordinal)));
        }

        [Fact]
        public void Scope_flows_from_options_onto_creates_and_cleanup()
        {
            using ModelTestContext context = TestModel.CreateContext(
                mb => mb.Entity<Order>(e =>
                {
                    e.ToTable("Orders", "gl");
                    e.HasTableType();
                }),
                sweepScope: "CustomScope");

            IReadOnlyList<MigrationOperation> ops =
                context.GetService<IMigrationsModelDiffer>().GetDifferences(null, TestModel.GetRelationalModel(context));

            Assert.Equal("CustomScope", ops.OfType<CreateTableTypeOperation>().Single().Scope);
            Assert.Equal("CustomScope", ops.OfType<CleanupTableTypesOperation>().Single().Scope);
        }
    }
}
