// Copyright (c) 2026 Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Tellma.Core.EntityFrameworkCore.TableTypes;
using Tellma.Core.EntityFrameworkCore.Tests.Infrastructure;

namespace Tellma.Core.EntityFrameworkCore.Tests.Metadata
{
    /// <summary>
    ///     The metadata API (spec §6): every aspect of the generated types is queryable from the
    ///     model — names, ordered columns with store types and facets, PK, rowversion flag,
    ///     memory-optimized flag, grants. Also pins the column-ordering rule that runtime TVP
    ///     binding depends on.
    /// </summary>
    public class TableTypeModelExtensionsTests
    {
        [Fact]
        public void GetTableTypes_returns_all_types_sorted_by_schema_then_name()
        {
            using ModelTestContext context = TestModel.CreateContext(mb =>
            {
                mb.Entity<Order>(e =>
                {
                    e.ToTable("Orders", "gl");
                    e.HasTableType();
                });
                mb.Entity<Product>(e => e.ToTable("Products", "inv"));
                mb.Entity<Plain>(e =>
                {
                    e.ToTable("Plains", "gl");
                    e.HasTableType();
                });
            });

            IReadOnlyList<TableTypeDefinition> types = TestModel.GetFinalizedModel(context).GetTableTypes();

            Assert.Equal(["gl.OrdersList", "gl.PlainsList", "inv.ProductsList"], types.Select(t => $"{t.Schema}.{t.Name}"));
        }

        [Fact]
        public void GetTableType_finds_the_entity_types_definition()
        {
            using ModelTestContext context = TestModel.CreateContext(mb =>
            {
                mb.Entity<Order>(e =>
                {
                    e.ToTable("Orders", "gl");
                    e.HasTableType();
                });
                mb.Entity<Plain>(e => e.ToTable("Plains", "gl"));
            });

            IModel model = TestModel.GetFinalizedModel(context);
            IEntityType orderType = model.FindEntityType(typeof(Order))!;
            IEntityType plainType = model.FindEntityType(typeof(Plain))!;

            Assert.True(orderType.HasTableType());
            Assert.Equal("OrdersList", orderType.GetTableType()!.Name);
            Assert.False(plainType.HasTableType());
            Assert.Null(plainType.GetTableType());
        }

        [Fact]
        public void Columns_carry_store_types_facets_and_nullability()
        {
            using ModelTestContext context = TestModel.CreateContext(mb =>
                mb.Entity<AllTypes>(e =>
                {
                    e.ToTable("AllTypes", "dbo");
                    e.HasTableType();
                    e.Property(a => a.Decimal).HasPrecision(19, 4);
                    e.Property(a => a.BoundedString).HasMaxLength(100);
                    e.Property(a => a.AnsiString).HasMaxLength(50).IsUnicode(false);
                    e.Property(a => a.FixedString).HasMaxLength(10).IsFixedLength();
                    e.Property(a => a.CollatedString).HasMaxLength(20).UseCollation("Latin1_General_100_CI_AS");
                    e.Property(a => a.Binary).HasMaxLength(512);
                    e.Property(a => a.Money).HasColumnType("money");
                    e.Property(a => a.DateTime).HasPrecision(3);
                }));

            TableTypeDefinition definition = Assert.Single(TestModel.GetFinalizedModel(context).GetTableTypes());
            var columns = definition.Columns.ToDictionary(c => c.Name);

            Assert.Equal("int", columns["Id"].StoreType);
            Assert.False(columns["Id"].IsNullable);
            Assert.Equal("bigint", columns["Int64"].StoreType);
            Assert.Equal("smallint", columns["Int16"].StoreType);
            Assert.Equal("tinyint", columns["Byte"].StoreType);
            Assert.Equal("bit", columns["Bool"].StoreType);
            Assert.Equal("decimal(19,4)", columns["Decimal"].StoreType);
            Assert.Equal(19, columns["Decimal"].Precision);
            Assert.Equal(4, columns["Decimal"].Scale);
            Assert.Equal("float", columns["Double"].StoreType);
            Assert.Equal("real", columns["Float"].StoreType);
            Assert.Equal("datetime2(3)", columns["DateTime"].StoreType);
            Assert.Equal("datetimeoffset", columns["DateTimeOffset"].StoreType);
            Assert.Equal("date", columns["DateOnly"].StoreType);
            Assert.Equal("time", columns["TimeOnly"].StoreType);
            Assert.Equal("uniqueidentifier", columns["Guid"].StoreType);
            Assert.Equal("nvarchar(max)", columns["RequiredString"].StoreType);
            Assert.False(columns["RequiredString"].IsNullable);
            Assert.Equal("nvarchar(100)", columns["BoundedString"].StoreType);
            Assert.True(columns["BoundedString"].IsNullable);
            Assert.Equal(100, columns["BoundedString"].MaxLength);
            Assert.Equal("varchar(50)", columns["AnsiString"].StoreType);
            Assert.Equal("nchar(10)", columns["FixedString"].StoreType);
            Assert.Equal("nvarchar(20)", columns["CollatedString"].StoreType);
            Assert.Equal("Latin1_General_100_CI_AS", columns["CollatedString"].Collation);
            Assert.Equal("varbinary(512)", columns["Binary"].StoreType);
            Assert.Equal("money", columns["Money"].StoreType);
        }

        [Fact]
        public void Primary_key_mirrors_the_tables_key_in_key_order()
        {
            using ModelTestContext context = TestModel.CreateContext(mb =>
                mb.Entity<Order>(e =>
                {
                    e.ToTable("Orders", "gl");
                    e.HasKey(o => new { o.CustomerId, o.Id });
                    e.HasTableType();
                }));

            TableTypeDefinition definition = Assert.Single(TestModel.GetFinalizedModel(context).GetTableTypes());

            Assert.Equal(["CustomerId", "Id"], definition.PrimaryKey);
        }

        [Fact]
        public void Columns_are_ordered_pk_first_then_leaf_columns_then_base_class_columns()
        {
            // Mirrors the table's own column order (EF's CREATE TABLE ordering): PK first, then
            // properties declared on the leaf CLR type, then base-class properties base-most
            // first — a pack adding a column in a base class lands after the leaf's columns,
            // exactly like the table. (Pinned against the physical table by the integration
            // column-order parity test.)
            using ModelTestContext context = TestModel.CreateContext(mb =>
                mb.Entity<Product>(e => e.ToTable("Products", "inv")));

            TableTypeDefinition definition = Assert.Single(TestModel.GetFinalizedModel(context).GetTableTypes());

            Assert.Equal(["Id", "Price", "Name"], definition.Columns.Select(c => c.Name));
        }

        [Fact]
        public void Explicit_HasColumnOrder_overrides_the_structural_order()
        {
            using ModelTestContext context = TestModel.CreateContext(mb =>
                mb.Entity<Order>(e =>
                {
                    e.ToTable("Orders", "gl");
                    e.HasTableType();
                    e.ExcludesRowVersionFromTableType();
                    e.Property(o => o.Total).HasColumnOrder(0);
                    e.Property(o => o.Memo).HasColumnOrder(1);
                }));

            TableTypeDefinition definition = Assert.Single(TestModel.GetFinalizedModel(context).GetTableTypes());

            Assert.Equal(["Total", "Memo", "Id", "CustomerId"], definition.Columns.Select(c => c.Name));
        }

        [Fact]
        public void Definitions_round_trip_through_canonical_json()
        {
            using ModelTestContext context = TestModel.CreateContext(mb =>
                mb.Entity<Order>(e =>
                {
                    e.ToTable("Orders", "gl");
                    e.HasTableType();
                    e.IsMemoryOptimizedTableType();
                    e.HasTableTypeGrants("tellma_app");
                }));

            TableTypeDefinition definition = Assert.Single(TestModel.GetFinalizedModel(context).GetTableTypes());
            TableTypeDefinition roundTripped =
                TableTypes.Json.TableTypeJson.DeserializeDefinition(TableTypes.Json.TableTypeJson.Serialize(definition));

            Assert.Equal(TableTypes.Json.TableTypeJson.Serialize(definition), TableTypes.Json.TableTypeJson.Serialize(roundTripped));
            Assert.Equal(definition.Columns.Select(c => c.Name), roundTripped.Columns.Select(c => c.Name));
            Assert.Equal("[gl].[OrdersList]", definition.DisplayName);
        }
    }
}
