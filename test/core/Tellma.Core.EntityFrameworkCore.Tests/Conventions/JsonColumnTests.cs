// Copyright (c) 2026 Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Tellma.Core.EntityFrameworkCore.TableTypes;
using Tellma.Core.EntityFrameworkCore.TableTypes.Json;
using Tellma.Core.EntityFrameworkCore.TableTypes.Operations;
using Tellma.Core.EntityFrameworkCore.Tests.Infrastructure;

namespace Tellma.Core.EntityFrameworkCore.Tests.Conventions
{
    /// <summary>
    ///     JSON column support (spec 0001 §2). A <c>ToJson()</c> owned navigation or complex property,
    ///     and a native <c>json</c> scalar, each map to a single column — a complete row image — so the
    ///     opt-in is allowed (unlike a flattened complex type). The column is carried in the UDTT as
    ///     <c>varchar(max)</c> with the json type's own UTF-8 collation (on-disk) or <c>nvarchar(max)</c>
    ///     (memory-optimized, where UTF-8 collations are unsupported): the wire form the bulk-save TVP
    ///     pipeline binds (the native <c>json</c> type is not a bindable <c>SqlMetaData</c> column type),
    ///     non-Latin safe, implicitly converted back to the table's column type on insert, and flagged
    ///     <see cref="TableTypeColumnDefinition.IsJson" /> so the binder serializes the object graph.
    /// </summary>
    public class JsonColumnTests
    {
        private const string JsonStoreType = "varchar(max)";
        private const string JsonCollation = "Latin1_General_100_BIN2_UTF8";

        private static TableTypeColumnDefinition Column(ModelTestContext context, string columnName)
        {
            TableTypeDefinition definition = Assert.Single(TestModel.GetFinalizedModel(context).GetTableTypes());
            return definition.Columns.Single(c => c.Name == columnName);
        }

        [Fact]
        public void Owned_to_json_reference_is_carried_as_varchar_max_utf8()
        {
            using ModelTestContext context = TestModel.CreateContext(mb => mb.Entity<DocHolder>(e =>
            {
                e.ToTable("DocHolders", "x");
                e.HasTableType();
                e.OwnsOne(h => h.Doc, b => b.ToJson());
            }));

            TableTypeColumnDefinition doc = Column(context, "Doc");
            Assert.Equal(JsonStoreType, doc.StoreType);
            Assert.Equal(JsonCollation, doc.Collation);
            Assert.True(doc.IsJson);
            Assert.True(doc.IsNullable); // an optional reference by default
        }

        [Fact]
        public void Required_owned_to_json_reference_is_not_nullable()
        {
            using ModelTestContext context = TestModel.CreateContext(mb => mb.Entity<DocHolder>(e =>
            {
                e.ToTable("DocHolders", "x");
                e.HasTableType();
                e.OwnsOne(h => h.Doc, b => b.ToJson());
                e.Navigation(h => h.Doc).IsRequired();
            }));

            Assert.False(Column(context, "Doc").IsNullable);
        }

        [Fact]
        public void Owned_to_json_collection_is_carried_as_varchar_max_utf8()
        {
            using ModelTestContext context = TestModel.CreateContext(mb => mb.Entity<LineHolder>(e =>
            {
                e.ToTable("LineHolders", "x");
                e.HasTableType();
                e.OwnsMany(h => h.Lines, b => b.ToJson());
            }));

            TableTypeColumnDefinition lines = Column(context, "Lines");
            Assert.Equal(JsonStoreType, lines.StoreType);
            Assert.Equal(JsonCollation, lines.Collation);
            Assert.True(lines.IsJson);
            Assert.True(lines.IsNullable); // a collection container column is nullable
        }

        [Fact]
        public void Scalar_native_json_column_is_normalized_to_varchar_max_utf8()
        {
            using ModelTestContext context = TestModel.CreateContext(mb => mb.Entity<Payloaded>(e =>
            {
                e.ToTable("Payloads", "x");
                e.HasTableType();
                e.Property(p => p.Payload).HasColumnType("json");
            }));

            TableTypeColumnDefinition payload = Column(context, "Payload");
            Assert.Equal(JsonStoreType, payload.StoreType);
            Assert.Equal(JsonCollation, payload.Collation);
            Assert.True(payload.IsJson);
        }

        [Fact]
        public void Standalone_native_json_column_is_normalized_to_varchar_max_utf8()
        {
            using ModelTestContext context = TestModel.CreateContext(mb =>
            {
                mb.Entity<Plain>();
                mb.HasTableType("PayloadList", schema: "dbo", type => type
                    .Column<int>("Id")
                    .Column("Payload", "json", nullable: true)
                    .HasKey("Id"));
            });

            TableTypeColumnDefinition payload = Column(context, "Payload");
            Assert.Equal(JsonStoreType, payload.StoreType);
            Assert.Equal(JsonCollation, payload.Collation);
            Assert.True(payload.IsJson);
            Assert.True(payload.IsNullable);
        }

        [Fact]
        public void Memory_optimized_json_column_is_nvarchar_max_without_a_utf8_collation()
        {
            // A UTF-8 collation is rejected on memory-optimized table types (SQL Server error 12356),
            // so a JSON column there is carried as nvarchar(max) (UTF-16, still non-Latin safe).
            using ModelTestContext context = TestModel.CreateContext(mb => mb.Entity<DocHolder>(e =>
            {
                e.ToTable("DocHolders", "x");
                e.HasTableType();
                e.IsMemoryOptimizedTableType();
                e.OwnsOne(h => h.Doc, b => b.ToJson());
            }));

            TableTypeColumnDefinition doc = Column(context, "Doc");
            Assert.Equal("nvarchar(max)", doc.StoreType);
            Assert.Null(doc.Collation);
            Assert.True(doc.IsJson);
        }

        [Fact]
        public void Generated_sql_json_store_type_switches_by_target()
        {
            Assert.Contains(
                "varchar(max) COLLATE Latin1_General_100_BIN2_UTF8", GenerateCreateSql(memoryOptimized: false), StringComparison.Ordinal);

            string memoryOptimized = GenerateCreateSql(memoryOptimized: true);
            Assert.Contains("nvarchar(max)", memoryOptimized, StringComparison.Ordinal);
            Assert.DoesNotContain("Latin1_General_100_BIN2_UTF8", memoryOptimized, StringComparison.Ordinal);
        }

        [Fact]
        public void Json_flag_is_part_of_canonical_json()
        {
            TableTypeColumnDefinition plain = new() { Name = "Data", StoreType = "varchar(max)", IsNullable = true };
            TableTypeColumnDefinition asJson = plain with { IsJson = true };

            TableTypeDefinition withPlain = new() { Name = "Thing", Columns = [plain] };
            TableTypeDefinition withJson = new() { Name = "Thing", Columns = [asJson] };

            // Always serialized (like IsNullable/IsRowVersion), so the flag is in the canonical form.
            Assert.Contains("\"isJson\":false", TableTypeJson.Serialize(withPlain), StringComparison.Ordinal);
            Assert.Contains("\"isJson\":true", TableTypeJson.Serialize(withJson), StringComparison.Ordinal);
            // Toggling JSON-ness is a definitional change → a new physical version.
            Assert.NotEqual(withPlain.PhysicalName, withJson.PhysicalName);
        }

        private static string GenerateCreateSql(bool memoryOptimized)
        {
            using ModelTestContext context = TestModel.CreateContext(mb => mb.Entity<DocHolder>(e =>
            {
                e.ToTable("DocHolders", "x");
                e.HasTableType();
                if (memoryOptimized)
                {
                    e.IsMemoryOptimizedTableType();
                }

                e.OwnsOne(h => h.Doc, b => b.ToJson());
            }));

            IMigrationsModelDiffer differ = context.GetService<IMigrationsModelDiffer>();
            CreateTableTypeOperation create = differ
                .GetDifferences(null, TestModel.GetRelationalModel(context))
                .OfType<CreateTableTypeOperation>()
                .Single();
            IMigrationsSqlGenerator generator = context.GetService<IMigrationsSqlGenerator>();
            return Assert.Single(generator.Generate([create])).CommandText;
        }

        private sealed class DocHolder
        {
            public int Id { get; set; }

            public Doc? Doc { get; set; }
        }

        private sealed class Doc
        {
            public string Title { get; set; } = string.Empty;

            public int Version { get; set; }
        }

        private sealed class LineHolder
        {
            public int Id { get; set; }

            public List<Line> Lines { get; set; } = [];
        }

        private sealed class Line
        {
            public string Sku { get; set; } = string.Empty;

            public decimal Amount { get; set; }
        }

        private sealed class Payloaded
        {
            public int Id { get; set; }

            public string Payload { get; set; } = string.Empty;
        }
    }
}
