// Copyright (c) 2026 Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Tellma.Core.EntityFrameworkCore.TableTypes;
using Tellma.Core.EntityFrameworkCore.TableTypes.Operations;
using Tellma.Core.EntityFrameworkCore.Tests.Infrastructure;

namespace Tellma.Core.EntityFrameworkCore.Tests.Diffing
{
    /// <summary>
    ///     Model-pair differ tests: the exact operations emitted for each change class, through the
    ///     real <see cref="IMigrationsModelDiffer" /> resolved from the context (i.e. including the
    ///     service replacement installed by <c>UseTableTypes()</c>).
    /// </summary>
    /// <remarks>
    ///     The differ emits <b>creates only</b> plus one trailing
    ///     <see cref="CleanupTableTypesOperation" /> (spec 0001 §3): a definitional change yields a
    ///     new content-addressed version created alongside the old, and the cleanup carries the
    ///     complete keep-list of current physical names. It never emits a
    ///     <see cref="DropTableTypeOperation" />.
    /// </remarks>
    public class TableTypeDifferTests
    {
        /// <summary>Runs the full differ between the models of two contexts.</summary>
        private static IReadOnlyList<MigrationOperation> Diff(DbContext source, DbContext target)
        {
            IMigrationsModelDiffer differ = target.GetService<IMigrationsModelDiffer>();
            return differ.GetDifferences(TestModel.GetRelationalModel(source), TestModel.GetRelationalModel(target));
        }

        private static IReadOnlyList<CreateTableTypeOperation> Creates(IReadOnlyList<MigrationOperation> ops)
        {
            return [.. ops.OfType<CreateTableTypeOperation>()];
        }

        private static CleanupTableTypesOperation? Cleanup(IReadOnlyList<MigrationOperation> ops)
        {
            return ops.OfType<CleanupTableTypesOperation>().SingleOrDefault();
        }

        private static ModelTestContext OrdersContext(bool withType, Action<ModelBuilder>? extra = null)
        {
            return TestModel.CreateContext(mb =>
            {
                mb.Entity<Order>(e =>
                {
                    e.ToTable("Orders", "gl");
                    e.ExcludesRowVersionFromTableType();
                    if (withType)
                    {
                        e.HasTableType();
                    }
                });
                extra?.Invoke(mb);
            });
        }

        [Fact]
        public void Opt_in_emits_a_create_after_base_operations_and_a_trailing_cleanup()
        {
            using ModelTestContext source = OrdersContext(withType: false);
            using ModelTestContext target = OrdersContext(withType: true);

            IReadOnlyList<MigrationOperation> operations = Diff(source, target);

            Assert.DoesNotContain(operations, o => o is DropTableTypeOperation);
            CreateTableTypeOperation create = Assert.Single(Creates(operations));
            Assert.Equal("OrdersList", create.Name);
            Assert.Equal("gl", create.Schema);
            Assert.Equal("TestScope", create.Scope);
            Assert.StartsWith("OrdersList_", create.PhysicalName);
            Assert.Equal(["Id"], create.PrimaryKey);
            Assert.Equal(["Id", "CustomerId", "Memo", "Total"], create.Columns.Select(c => c.Name));

            // The cleanup is the last operation and keeps exactly the current version.
            CleanupTableTypesOperation cleanup = Assert.IsType<CleanupTableTypesOperation>(operations[^1]);
            Assert.Equal("TestScope", cleanup.Scope);
            Assert.Equal([create.PhysicalName], cleanup.KeepList!);
            Assert.Equal(CleanupTableTypesOperation.DefaultGracePeriodHours, cleanup.GracePeriodHours);
        }

        [Fact]
        public void Opt_out_emits_no_drop_just_a_cleanup_dropping_the_name_from_the_keep_list()
        {
            using ModelTestContext source = OrdersContext(withType: true);
            using ModelTestContext target = OrdersContext(withType: false);

            IReadOnlyList<MigrationOperation> operations = Diff(source, target);

            Assert.DoesNotContain(operations, o => o is DropTableTypeOperation);
            Assert.Empty(Creates(operations));
            CleanupTableTypesOperation cleanup = Assert.IsType<CleanupTableTypesOperation>(Assert.Single(operations));
            Assert.Empty(cleanup.KeepList!);
        }

        [Fact]
        public void No_change_emits_nothing()
        {
            using ModelTestContext source = OrdersContext(withType: true);
            using ModelTestContext target = OrdersContext(withType: true);

            Assert.Empty(Diff(source, target));
        }

        [Fact]
        public void Initial_create_from_empty_database_emits_table_then_type_then_cleanup()
        {
            using ModelTestContext target = OrdersContext(withType: true);

            IMigrationsModelDiffer differ = target.GetService<IMigrationsModelDiffer>();
            IReadOnlyList<MigrationOperation> operations =
                differ.GetDifferences(null, TestModel.GetRelationalModel(target));

            // The cleanup sweep is the very last operation; the create precedes it but follows the table.
            Assert.IsType<CleanupTableTypesOperation>(operations[^1]);
            Assert.Contains(operations, o => o is CreateTableOperation);
            List<MigrationOperation> list = [.. operations];
            Assert.True(
                list.FindIndex(o => o is CreateTableOperation) < list.FindIndex(o => o is CreateTableTypeOperation),
                "CreateTableType must come after CreateTable.");
            Assert.True(
                list.FindIndex(o => o is CreateTableTypeOperation) < list.FindIndex(o => o is CleanupTableTypesOperation),
                "CleanupTableTypes must be the trailing operation.");
        }

        [Fact]
        public void Column_change_creates_a_new_version_without_a_drop()
        {
            using ModelTestContext source = OrdersContext(withType: true);

            using ModelTestContext addedColumn = TestModel.CreateContext(mb =>
                mb.Entity<Order>(e =>
                {
                    e.ToTable("Orders", "gl");
                    e.ExcludesRowVersionFromTableType();
                    e.HasTableType();
                    e.Property(o => o.Memo).ExcludeFromTableType();
                }));

            IReadOnlyList<MigrationOperation> operations = Diff(source, addedColumn);

            Assert.DoesNotContain(operations, o => o is DropTableTypeOperation);
            CreateTableTypeOperation create = Assert.Single(Creates(operations));
            Assert.DoesNotContain(create.Columns, c => c.Name == "Memo");
            Assert.Equal([create.PhysicalName], Cleanup(operations)!.KeepList!);

            // Retype: a facet change flows into the definition and so into a new physical name.
            using ModelTestContext retyped = TestModel.CreateContext(mb =>
                mb.Entity<Order>(e =>
                {
                    e.ToTable("Orders", "gl");
                    e.ExcludesRowVersionFromTableType();
                    e.HasTableType();
                    e.Property(o => o.Memo).HasMaxLength(500);
                }));

            operations = Diff(source, retyped);

            Assert.DoesNotContain(operations, o => o is DropTableTypeOperation);
            CreateTableTypeOperation retypedCreate = Assert.Single(Creates(operations));
            Assert.Equal("nvarchar(500)", retypedCreate.Columns.Single(c => c.Name == "Memo").StoreType);
            Assert.Contains(operations, o => o is AlterColumnOperation);
            // The new physical name differs from the source version's (content-addressed).
            CreateTableTypeOperation sourceVersion = Assert.Single(Creates(Diff(OrdersContext(withType: false), source)));
            Assert.NotEqual(sourceVersion.PhysicalName, retypedCreate.PhysicalName);
        }

        [Fact]
        public void Pure_reorder_creates_a_new_version()
        {
            using ModelTestContext source = OrdersContext(withType: true);
            using ModelTestContext reordered = TestModel.CreateContext(mb =>
                mb.Entity<Order>(e =>
                {
                    e.ToTable("Orders", "gl");
                    e.ExcludesRowVersionFromTableType();
                    e.HasTableType();
                    e.Property(o => o.Memo).HasColumnOrder(0);
                }));

            IReadOnlyList<MigrationOperation> operations = Diff(source, reordered);

            Assert.DoesNotContain(operations, o => o is DropTableTypeOperation);
            CreateTableTypeOperation create = Assert.Single(Creates(operations));
            Assert.Equal("Memo", create.Columns[0].Name);
        }

        [Fact]
        public void Config_changes_create_new_versions_without_drops()
        {
            using ModelTestContext source = OrdersContext(withType: true);

            using ModelTestContext granted = TestModel.CreateContext(mb =>
                mb.Entity<Order>(e =>
                {
                    e.ToTable("Orders", "gl");
                    e.ExcludesRowVersionFromTableType();
                    e.HasTableType();
                    e.HasTableTypeGrants("tellma_app");
                }));

            IReadOnlyList<MigrationOperation> operations = Diff(source, granted);
            Assert.DoesNotContain(operations, o => o is DropTableTypeOperation);
            Assert.Equal(["tellma_app"], Assert.Single(Creates(operations)).Grants);

            using ModelTestContext memoryOptimized = TestModel.CreateContext(mb =>
                mb.Entity<Order>(e =>
                {
                    e.ToTable("Orders", "gl");
                    e.ExcludesRowVersionFromTableType();
                    e.HasTableType();
                    e.IsMemoryOptimizedTableType();
                }));

            operations = Diff(source, memoryOptimized);
            Assert.DoesNotContain(operations, o => o is DropTableTypeOperation);
            Assert.True(Assert.Single(Creates(operations)).IsMemoryOptimized);

            // Rename: a new version under the new name; the old name is simply not in the keep-list.
            using ModelTestContext renamed = TestModel.CreateContext(mb =>
                mb.Entity<Order>(e =>
                {
                    e.ToTable("Orders", "gl");
                    e.ExcludesRowVersionFromTableType();
                    e.HasTableType("OrderRows");
                }));

            operations = Diff(source, renamed);
            Assert.DoesNotContain(operations, o => o is DropTableTypeOperation);
            CreateTableTypeOperation renamedCreate = Assert.Single(Creates(operations));
            Assert.Equal("OrderRows", renamedCreate.Name);
            Assert.Equal([renamedCreate.PhysicalName], Cleanup(operations)!.KeepList!);
        }

        [Fact]
        public void Multiple_types_diff_deterministically_sorted_by_schema_then_name()
        {
            using ModelTestContext source = TestModel.CreateContext(mb => mb.Entity<Order>(e => e.ToTable("Orders", "gl")));
            using ModelTestContext target = TestModel.CreateContext(mb =>
            {
                mb.Entity<Order>(e =>
                {
                    e.ToTable("Orders", "gl");
                    e.ExcludesRowVersionFromTableType();
                    e.HasTableType();
                });
                mb.Entity<Plain>(e =>
                {
                    e.ToTable("Plains", "aa");
                    e.HasTableType();
                });
                mb.Entity<Product>(e => e.ToTable("Products", "gl"));
            });

            List<CreateTableTypeOperation> creates = [.. Creates(Diff(source, target))];

            Assert.Equal(["aa.PlainsList", "gl.OrdersList", "gl.ProductsList"], creates.Select(c => $"{c.Schema}.{c.Name}"));
        }

        [Fact]
        public void HasDifferences_detects_type_only_changes()
        {
            using ModelTestContext source = OrdersContext(withType: false);
            using ModelTestContext target = OrdersContext(withType: true);

            Assert.True(TableTypeDiffer.HasDifferences(
                TestModel.GetFinalizedModel(source), TestModel.GetFinalizedModel(target)));
            Assert.False(TableTypeDiffer.HasDifferences(
                TestModel.GetFinalizedModel(target), TestModel.GetFinalizedModel(target)));

            IMigrationsModelDiffer differ = target.GetService<IMigrationsModelDiffer>();
            Assert.True(differ.HasDifferences(TestModel.GetRelationalModel(source), TestModel.GetRelationalModel(target)));
        }
    }
}
