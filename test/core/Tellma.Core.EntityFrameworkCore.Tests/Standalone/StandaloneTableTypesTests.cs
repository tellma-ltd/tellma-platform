// Copyright (c) 2026 Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Tellma.Core.EntityFrameworkCore.TableTypes;
using Tellma.Core.EntityFrameworkCore.TableTypes.Operations;
using Tellma.Core.EntityFrameworkCore.Tests.Infrastructure;

namespace Tellma.Core.EntityFrameworkCore.Tests.Standalone
{
    /// <summary>
    ///     Standalone table types (spec 0001 §5): operation-specific shapes paired with no table,
    ///     authored ad hoc through the fluent builder or derived from a plain CLR class, flowing
    ///     through the same differ, operations, SQL and metadata API as table-derived types.
    /// </summary>
    public class StandaloneTableTypesTests
    {
        [Fact]
        public void Fluent_route_derives_the_definition()
        {
            using ModelTestContext context = TestModel.CreateContext(mb =>
            {
                mb.Entity<Plain>();
                mb.HasTableType("IdStateList", schema: "dbo", type => type
                    .Column<int>("Id")
                    .Column<short>("State")
                    .HasKey("Id")
                    .HasGrants("tellma_app"));
            });

            TableTypeDefinition definition = Assert.Single(TestModel.GetFinalizedModel(context).GetTableTypes());

            Assert.Equal("IdStateList", definition.Name);
            Assert.Equal("dbo", definition.Schema);
            Assert.Null(definition.TableName);
            Assert.Equal(["Id"], definition.PrimaryKey);
            Assert.Equal(["tellma_app"], definition.Grants);
            Assert.Equal(["Id", "State"], definition.Columns.Select(c => c.Name));
            Assert.Equal(["int", "smallint"], definition.Columns.Select(c => c.StoreType));
            Assert.All(definition.Columns, c => Assert.False(c.IsNullable));
        }

        [Fact]
        public void Fluent_route_resolves_facets_and_honors_explicit_store_types()
        {
            using ModelTestContext context = TestModel.CreateContext(mb =>
            {
                mb.Entity<Plain>();
                mb.HasTableType("FacetsList", schema: "dbo", type => type
                    .Column<int>("Id")
                    .Column<string>("Code", maxLength: 50, unicode: false)
                    .Column<string?>("Comment", nullable: true, maxLength: 100)
                    .Column<decimal>("Amount", precision: 19, scale: 4)
                    .Column("Legacy", "money", nullable: true)
                    .HasKey("Id"));
            });

            TableTypeDefinition definition = Assert.Single(TestModel.GetFinalizedModel(context).GetTableTypes());
            var columns = definition.Columns.ToDictionary(c => c.Name);

            Assert.Equal("varchar(50)", columns["Code"].StoreType);
            Assert.False(columns["Code"].IsNullable);
            Assert.Equal("nvarchar(100)", columns["Comment"].StoreType);
            Assert.True(columns["Comment"].IsNullable);
            Assert.Equal("decimal(19,4)", columns["Amount"].StoreType);
            Assert.Equal("money", columns["Legacy"].StoreType);
            Assert.True(columns["Legacy"].IsNullable);
        }

        [Fact]
        public void Class_route_derives_columns_key_and_name_from_the_class()
        {
            using ModelTestContext context = TestModel.CreateContext(mb =>
            {
                mb.Entity<Plain>();
                mb.HasTableType<DocumentAssignment>(buildAction: type => type.HasGrants("tellma_app"));
            });

            TableTypeDefinition definition = Assert.Single(TestModel.GetFinalizedModel(context).GetTableTypes());

            // Name/schema from [TableType]; columns in declaration order; [NotMapped],
            // [ExcludeFromTableType] and read-only properties skipped.
            Assert.Equal("DocumentAssignmentsList", definition.Name);
            Assert.Equal("wf", definition.Schema);
            Assert.Null(definition.TableName);
            Assert.Equal(["DocumentId", "AssigneeId", "Comment", "Weight"], definition.Columns.Select(c => c.Name));
            Assert.Equal(["DocumentId"], definition.PrimaryKey);
            Assert.Equal(["tellma_app"], definition.Grants);

            var columns = definition.Columns.ToDictionary(c => c.Name);
            Assert.Equal("int", columns["DocumentId"].StoreType);
            Assert.False(columns["DocumentId"].IsNullable);
            // [MaxLength] + NRT nullability honored.
            Assert.Equal("nvarchar(500)", columns["Comment"].StoreType);
            Assert.True(columns["Comment"].IsNullable);
            // [Precision] honored.
            Assert.Equal("decimal(9,2)", columns["Weight"].StoreType);
        }

        [Fact]
        public void Class_route_defaults_the_name_to_the_class_name()
        {
            using ModelTestContext context = TestModel.CreateContext(mb =>
            {
                mb.Entity<Plain>();
                mb.HasTableType<PlainShape>();
            });

            TableTypeDefinition definition = Assert.Single(TestModel.GetFinalizedModel(context).GetTableTypes());

            Assert.Equal(nameof(PlainShape), definition.Name);
            Assert.Null(definition.Schema);
            Assert.Empty(definition.PrimaryKey); // no [Key], no override
        }

        [Fact]
        public void Builder_action_can_override_the_attribute_derived_key()
        {
            using ModelTestContext context = TestModel.CreateContext(mb =>
            {
                mb.Entity<Plain>();
                mb.HasTableType<DocumentAssignment>(buildAction: type => type.HasKey("DocumentId", "AssigneeId"));
            });

            TableTypeDefinition definition = Assert.Single(TestModel.GetFinalizedModel(context).GetTableTypes());

            Assert.Equal(["DocumentId", "AssigneeId"], definition.PrimaryKey);
        }

        [Fact]
        public void Standalone_types_diff_and_generate_sql_like_any_other_type()
        {
            using ModelTestContext source = TestModel.CreateContext(mb => mb.Entity<Plain>());
            using ModelTestContext target = TestModel.CreateContext(mb =>
            {
                mb.Entity<Plain>();
                mb.HasTableType("IdStateList", schema: "dbo", type => type
                    .Column<int>("Id")
                    .Column<short>("State")
                    .HasKey("Id")
                    .HasGrants("tellma_app"));
            });

            IMigrationsModelDiffer differ = target.GetService<IMigrationsModelDiffer>();
            IReadOnlyList<MigrationOperation> operations = differ.GetDifferences(
                TestModel.GetRelationalModel(source), TestModel.GetRelationalModel(target));

            CreateTableTypeOperation create = Assert.IsType<CreateTableTypeOperation>(Assert.Single(operations));

            IMigrationsSqlGenerator generator = target.GetService<IMigrationsSqlGenerator>();
            MigrationCommand command = Assert.Single(generator.Generate([create]));

            Assert.Equal(
                """
                CREATE TYPE [dbo].[IdStateList] AS TABLE (
                    [Id] int NOT NULL,
                    [State] smallint NOT NULL,
                    PRIMARY KEY CLUSTERED ([Id])
                );
                GRANT EXECUTE ON TYPE::[dbo].[IdStateList] TO [tellma_app];

                """,
                command.CommandText.Replace("\r\n", "\n", StringComparison.Ordinal));
        }

        [Fact]
        public void The_platform_bulk_shapes_register_through_the_standalone_route()
        {
            // The canonical bulk shapes are plain classes in Tellma.Core.Abstractions (no EF
            // dependency, BCL DataAnnotations only) — registered like any standalone type; this
            // is what a distribution's composition does once.
            using ModelTestContext context = TestModel.CreateContext(mb =>
            {
                mb.Entity<Plain>();
                mb.HasTableType<Tellma.Core.Abstractions.TableTypes.IdList>(schema: "dbo");
                mb.HasTableType<Tellma.Core.Abstractions.TableTypes.BigIdList>(schema: "dbo");
                mb.HasTableType<Tellma.Core.Abstractions.TableTypes.GuidList>(schema: "dbo");
                mb.HasTableType<Tellma.Core.Abstractions.TableTypes.StringList>(schema: "dbo");
            });

            IReadOnlyList<TableTypeDefinition> types = TestModel.GetFinalizedModel(context).GetTableTypes();

            Assert.Equal(["BigIdList", "GuidList", "IdList", "StringList"], types.Select(t => t.Name));
            Assert.All(types, t =>
            {
                Assert.Equal("dbo", t.Schema);
                Assert.Null(t.TableName);
                Assert.Equal(["Id"], t.PrimaryKey);
                TableTypeColumnDefinition column = Assert.Single(t.Columns);
                Assert.Equal("Id", column.Name);
                Assert.False(column.IsNullable);
            });
            Assert.Equal("int", types.Single(t => t.Name == "IdList").Columns[0].StoreType);
            Assert.Equal("bigint", types.Single(t => t.Name == "BigIdList").Columns[0].StoreType);
            Assert.Equal("uniqueidentifier", types.Single(t => t.Name == "GuidList").Columns[0].StoreType);
            Assert.Equal("nvarchar(450)", types.Single(t => t.Name == "StringList").Columns[0].StoreType);
        }

        [Fact]
        public void Standalone_names_share_the_global_uniqueness_check()
        {
            using ModelTestContext context = TestModel.CreateContext(mb =>
            {
                mb.Entity<Order>(e =>
                {
                    e.ToTable("Orders", "gl");
                    e.HasTableType();
                });
                mb.HasTableType("OrdersList", schema: "gl", type => type.Column<int>("Id").HasKey("Id"));
            });

            InvalidOperationException exception =
                Assert.Throws<InvalidOperationException>(() => { _ = TestModel.GetFinalizedModel(context); });
            Assert.Contains("OrdersList", exception.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void Standalone_types_without_columns_throw()
        {
            using ModelTestContext context = TestModel.CreateContext(mb =>
            {
                mb.Entity<Plain>();
                mb.HasTableType("EmptyList", schema: "dbo", type => type.HasGrants("x"));
            });

            InvalidOperationException exception =
                Assert.Throws<InvalidOperationException>(() => { _ = TestModel.GetFinalizedModel(context); });
            Assert.Contains("no columns", exception.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void Key_columns_must_exist_among_the_columns()
        {
            using ModelTestContext context = TestModel.CreateContext(mb =>
            {
                mb.Entity<Plain>();
                mb.HasTableType("BadKeyList", schema: "dbo", type => type.Column<int>("Id").HasKey("Missing"));
            });

            InvalidOperationException exception =
                Assert.Throws<InvalidOperationException>(() => { _ = TestModel.GetFinalizedModel(context); });
            Assert.Contains("Missing", exception.Message, StringComparison.Ordinal);
        }

        /// <summary>A standalone DTO shape: bulk assignment of documents to users.</summary>
        [TableType(Name = "DocumentAssignmentsList", Schema = "wf")]
        private sealed class DocumentAssignment
        {
            [Key]
            public int DocumentId { get; set; }

            public int AssigneeId { get; set; }

            [MaxLength(500)]
            public string? Comment { get; set; }

            [Precision(9, 2)]
            public decimal Weight { get; set; }

            [NotMapped]
            public string? Ignored { get; set; }

            [ExcludeFromTableType]
            public string? AlsoIgnored { get; set; }

            public int ReadOnlyIgnored => DocumentId;
        }

        /// <summary>A shape with no attribute: name defaults to the class name, no key.</summary>
        private sealed class PlainShape
        {
            public int Value { get; set; }
        }
    }
}
