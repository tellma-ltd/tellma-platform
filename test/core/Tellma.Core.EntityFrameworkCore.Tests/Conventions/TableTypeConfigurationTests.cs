// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Tellma.Core.EntityFrameworkCore.TableTypes;
using Tellma.Core.EntityFrameworkCore.Tests.Infrastructure;

namespace Tellma.Core.EntityFrameworkCore.Tests.Conventions
{
    /// <summary>
    ///     Configuration semantics: opt-in/opt-out, default naming, attribute inheritance,
    ///     fluent-beats-attribute precedence, exclusions, and the convention's validations.
    /// </summary>
    public class TableTypeConfigurationTests
    {
        [Fact]
        public void HasTableType_derives_default_name_and_schema_from_table()
        {
            using ModelTestContext context = TestModel.CreateContext(mb =>
                mb.Entity<Order>(e =>
                {
                    e.ToTable("Orders", "gl");
                    e.HasTableType();
                }));

            IModel model = TestModel.GetFinalizedModel(context);
            TableTypeDefinition definition = Assert.Single(model.GetTableTypes());

            Assert.Equal("OrdersList", definition.Name);
            Assert.Equal("gl", definition.Schema);
            Assert.Equal("Orders", definition.TableName);
            Assert.Equal("gl", definition.TableSchema);
        }

        [Fact]
        public void Tables_do_not_get_types_unless_opted_in()
        {
            using ModelTestContext context = TestModel.CreateContext(mb => mb.Entity<Plain>());

            Assert.Empty(TestModel.GetFinalizedModel(context).GetTableTypes());
        }

        [Fact]
        public void HasTableType_accepts_name_and_schema_overrides()
        {
            using ModelTestContext context = TestModel.CreateContext(mb =>
                mb.Entity<Order>(e =>
                {
                    e.ToTable("Orders", "gl");
                    e.HasTableType("OrderRows", "bulk");
                }));

            TableTypeDefinition definition = Assert.Single(TestModel.GetFinalizedModel(context).GetTableTypes());

            Assert.Equal("OrderRows", definition.Name);
            Assert.Equal("bulk", definition.Schema);
        }

        [Fact]
        public void TableType_attribute_is_inherited_by_leaf_entities()
        {
            // Only the leaf is mapped (leaf-only mapping); the opt-in and the InternalNotes
            // exclusion both come from the pack base class.
            using ModelTestContext context = TestModel.CreateContext(mb =>
                mb.Entity<Product>(e => e.ToTable("Products", "inv")));

            TableTypeDefinition definition = Assert.Single(TestModel.GetFinalizedModel(context).GetTableTypes());

            Assert.Equal("ProductsList", definition.Name);
            Assert.Equal("inv", definition.Schema);
            Assert.DoesNotContain(definition.Columns, c => c.Name == "InternalNotes");
            Assert.Contains(definition.Columns, c => c.Name == "Name");
            Assert.Contains(definition.Columns, c => c.Name == "Price");
        }

        [Fact]
        public void HasNoTableType_overrides_inherited_attribute()
        {
            using ModelTestContext context = TestModel.CreateContext(mb =>
                mb.Entity<Product>(e =>
                {
                    e.ToTable("Products", "inv");
                    e.HasNoTableType();
                }));

            Assert.Empty(TestModel.GetFinalizedModel(context).GetTableTypes());
        }

        [Fact]
        public void IncludeInTableType_overrides_inherited_attribute_exclusion()
        {
            using ModelTestContext context = TestModel.CreateContext(mb =>
                mb.Entity<Product>(e =>
                {
                    e.ToTable("Products", "inv");
                    e.Property(p => p.InternalNotes).IncludeInTableType();
                }));

            TableTypeDefinition definition = Assert.Single(TestModel.GetFinalizedModel(context).GetTableTypes());

            Assert.Contains(definition.Columns, c => c.Name == "InternalNotes");
        }

        [Fact]
        public void ExcludeFromTableType_fluent_excludes_a_column()
        {
            using ModelTestContext context = TestModel.CreateContext(mb =>
                mb.Entity<Order>(e =>
                {
                    e.ToTable("Orders", "gl");
                    e.HasTableType();
                    e.Property(o => o.Memo).ExcludeFromTableType();
                }));

            TableTypeDefinition definition = Assert.Single(TestModel.GetFinalizedModel(context).GetTableTypes());

            Assert.DoesNotContain(definition.Columns, c => c.Name == "Memo");
        }

        [Fact]
        public void Excluding_a_primary_key_column_throws()
        {
            using ModelTestContext context = TestModel.CreateContext(mb =>
                mb.Entity<Order>(e =>
                {
                    e.ToTable("Orders", "gl");
                    e.HasTableType();
                    e.Property(o => o.Id).ExcludeFromTableType();
                }));

            InvalidOperationException exception =
                Assert.Throws<InvalidOperationException>(() => { _ = TestModel.GetFinalizedModel(context); });
            Assert.Contains("primary key", exception.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void Rowversion_is_included_as_nullable_binary8_by_default()
        {
            using ModelTestContext context = TestModel.CreateContext(mb =>
                mb.Entity<Order>(e =>
                {
                    e.ToTable("Orders", "gl");
                    e.HasTableType();
                }));

            TableTypeDefinition definition = Assert.Single(TestModel.GetFinalizedModel(context).GetTableTypes());
            TableTypeColumnDefinition rowVersion = Assert.Single(definition.Columns, c => c.IsRowVersion);

            Assert.Equal("RowVersion", rowVersion.Name);
            Assert.Equal("binary(8)", rowVersion.StoreType);
            Assert.True(rowVersion.IsNullable);
        }

        [Fact]
        public void Rowversion_is_excludable_per_table()
        {
            using ModelTestContext context = TestModel.CreateContext(mb =>
                mb.Entity<Order>(e =>
                {
                    e.ToTable("Orders", "gl");
                    e.HasTableType();
                    e.ExcludesRowVersionFromTableType();
                }));

            TableTypeDefinition definition = Assert.Single(TestModel.GetFinalizedModel(context).GetTableTypes());

            Assert.DoesNotContain(definition.Columns, c => c.IsRowVersion);
        }

        [Fact]
        public void Computed_columns_are_always_excluded()
        {
            using ModelTestContext context = TestModel.CreateContext(mb =>
                mb.Entity<Order>(e =>
                {
                    e.ToTable("Orders", "gl");
                    e.HasTableType();
                    e.Property(o => o.Total).HasComputedColumnSql("[CustomerId] * 2", stored: true);
                }));

            TableTypeDefinition definition = Assert.Single(TestModel.GetFinalizedModel(context).GetTableTypes());

            Assert.DoesNotContain(definition.Columns, c => c.Name == "Total");
        }

        [Fact]
        public void Memory_optimized_and_grants_flow_into_the_definition()
        {
            using ModelTestContext context = TestModel.CreateContext(mb =>
                mb.Entity<Order>(e =>
                {
                    e.ToTable("Orders", "gl");
                    e.HasTableType();
                    e.IsMemoryOptimizedTableType();
                    e.HasTableTypeGrants("tellma_app", "tellma_jobs");
                }));

            TableTypeDefinition definition = Assert.Single(TestModel.GetFinalizedModel(context).GetTableTypes());

            Assert.True(definition.IsMemoryOptimized);
            Assert.Equal(["tellma_app", "tellma_jobs"], definition.Grants);
        }

        [Fact]
        public void Duplicate_type_names_throw_naming_both_contributors()
        {
            using ModelTestContext context = TestModel.CreateContext(mb =>
            {
                mb.Entity<Order>(e =>
                {
                    e.ToTable("Orders", "gl");
                    e.HasTableType("SharedList", "gl");
                });
                mb.Entity<Plain>(e =>
                {
                    e.ToTable("Plains", "gl");
                    e.HasTableType("SharedList", "gl");
                });
            });

            InvalidOperationException exception =
                Assert.Throws<InvalidOperationException>(() => { _ = TestModel.GetFinalizedModel(context); });
            Assert.Contains("SharedList", exception.Message, StringComparison.Ordinal);
            Assert.Contains("Order", exception.Message, StringComparison.Ordinal);
            Assert.Contains("Plain", exception.Message, StringComparison.Ordinal);
        }
    }
}
