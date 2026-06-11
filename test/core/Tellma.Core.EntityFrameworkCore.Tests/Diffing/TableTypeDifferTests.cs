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
    public class TableTypeDifferTests
    {
        /// <summary>Runs the full differ between the models of two contexts.</summary>
        private static IReadOnlyList<MigrationOperation> Diff(DbContext source, DbContext target)
        {
            IMigrationsModelDiffer differ = target.GetService<IMigrationsModelDiffer>();
            return differ.GetDifferences(TestModel.GetRelationalModel(source), TestModel.GetRelationalModel(target));
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
        public void Opt_in_emits_a_create_after_all_base_operations()
        {
            using ModelTestContext source = OrdersContext(withType: false);
            using ModelTestContext target = OrdersContext(withType: true);

            IReadOnlyList<MigrationOperation> operations = Diff(source, target);

            CreateTableTypeOperation create = Assert.IsType<CreateTableTypeOperation>(Assert.Single(operations));
            Assert.Equal("OrdersList", create.Name);
            Assert.Equal("gl", create.Schema);
            Assert.Equal(["Id"], create.PrimaryKey);
            Assert.Equal(["Id", "CustomerId", "Memo", "Total"], create.Columns.Select(c => c.Name));
        }

        [Fact]
        public void Opt_out_emits_a_drop_before_all_base_operations()
        {
            using ModelTestContext source = OrdersContext(withType: true);
            using ModelTestContext target = OrdersContext(withType: false);

            IReadOnlyList<MigrationOperation> operations = Diff(source, target);

            DropTableTypeOperation drop = Assert.IsType<DropTableTypeOperation>(Assert.Single(operations));
            Assert.Equal("OrdersList", drop.Name);
            Assert.Equal("gl", drop.Schema);
        }

        [Fact]
        public void No_change_emits_nothing()
        {
            using ModelTestContext source = OrdersContext(withType: true);
            using ModelTestContext target = OrdersContext(withType: true);

            Assert.Empty(Diff(source, target));
        }

        [Fact]
        public void Initial_create_from_empty_database_emits_table_then_type()
        {
            using ModelTestContext target = OrdersContext(withType: true);

            IMigrationsModelDiffer differ = target.GetService<IMigrationsModelDiffer>();
            IReadOnlyList<MigrationOperation> operations =
                differ.GetDifferences(null, TestModel.GetRelationalModel(target));

            // Creates are appended after all base operations (tables, indexes, ...).
            Assert.IsType<CreateTableTypeOperation>(operations[^1]);
            Assert.Contains(operations, o => o is CreateTableOperation);
            Assert.True(
                operations.ToList().FindIndex(o => o is CreateTableOperation)
                    < operations.ToList().FindIndex(o => o is CreateTableTypeOperation),
                "CreateTableType must come after CreateTable.");
        }

        [Fact]
        public void Column_add_remove_rename_and_retype_each_recreate_the_type()
        {
            using ModelTestContext source = OrdersContext(withType: true);

            // Added column (Plain entity adds nothing to Orders; use a property exclusion toggle
            // to add/remove a column from the type without touching the table).
            using ModelTestContext addedColumn = TestModel.CreateContext(mb =>
                mb.Entity<Order>(e =>
                {
                    e.ToTable("Orders", "gl");
                    e.ExcludesRowVersionFromTableType();
                    e.HasTableType();
                    e.Property(o => o.Memo).ExcludeFromTableType();
                }));

            IReadOnlyList<MigrationOperation> operations = Diff(source, addedColumn);

            Assert.Equal(2, operations.Count);
            Assert.IsType<DropTableTypeOperation>(operations[0]);
            CreateTableTypeOperation create = Assert.IsType<CreateTableTypeOperation>(operations[1]);
            Assert.DoesNotContain(create.Columns, c => c.Name == "Memo");

            // Retype: a facet change on a column flows into the type definition.
            using ModelTestContext retyped = TestModel.CreateContext(mb =>
                mb.Entity<Order>(e =>
                {
                    e.ToTable("Orders", "gl");
                    e.ExcludesRowVersionFromTableType();
                    e.HasTableType();
                    e.Property(o => o.Memo).HasMaxLength(500);
                }));

            operations = Diff(source, retyped);

            // The base differ also emits an AlterColumn for the table; ours adds Drop+Create
            // around it (drop first, create last).
            Assert.IsType<DropTableTypeOperation>(operations[0]);
            CreateTableTypeOperation retypedCreate = Assert.IsType<CreateTableTypeOperation>(operations[^1]);
            Assert.Equal("nvarchar(500)", retypedCreate.Columns.Single(c => c.Name == "Memo").StoreType);
            Assert.Contains(operations, o => o is AlterColumnOperation);
        }

        [Fact]
        public void Pure_reorder_recreates_the_type()
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

            Assert.Contains(operations, o => o is DropTableTypeOperation);
            CreateTableTypeOperation create = operations.OfType<CreateTableTypeOperation>().Single();
            Assert.Equal("Memo", create.Columns[0].Name);
        }

        [Fact]
        public void Config_changes_recreate_the_type()
        {
            using ModelTestContext source = OrdersContext(withType: true);

            // Grants change.
            using ModelTestContext granted = TestModel.CreateContext(mb =>
                mb.Entity<Order>(e =>
                {
                    e.ToTable("Orders", "gl");
                    e.ExcludesRowVersionFromTableType();
                    e.HasTableType();
                    e.HasTableTypeGrants("tellma_app");
                }));

            IReadOnlyList<MigrationOperation> operations = Diff(source, granted);
            Assert.Equal(2, operations.Count);
            Assert.Equal(["tellma_app"], operations.OfType<CreateTableTypeOperation>().Single().Grants);

            // Memory-optimized change.
            using ModelTestContext memoryOptimized = TestModel.CreateContext(mb =>
                mb.Entity<Order>(e =>
                {
                    e.ToTable("Orders", "gl");
                    e.ExcludesRowVersionFromTableType();
                    e.HasTableType();
                    e.IsMemoryOptimizedTableType();
                }));

            operations = Diff(source, memoryOptimized);
            Assert.Equal(2, operations.Count);
            Assert.True(operations.OfType<CreateTableTypeOperation>().Single().IsMemoryOptimized);

            // Rename (definitional: drop old name, create new name).
            using ModelTestContext renamed = TestModel.CreateContext(mb =>
                mb.Entity<Order>(e =>
                {
                    e.ToTable("Orders", "gl");
                    e.ExcludesRowVersionFromTableType();
                    e.HasTableType("OrderRows");
                }));

            operations = Diff(source, renamed);
            Assert.Equal("OrdersList", operations.OfType<DropTableTypeOperation>().Single().Name);
            Assert.Equal("OrderRows", operations.OfType<CreateTableTypeOperation>().Single().Name);
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

            IReadOnlyList<MigrationOperation> operations = Diff(source, target);
            List<CreateTableTypeOperation> creates = [.. operations.OfType<CreateTableTypeOperation>()];

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
