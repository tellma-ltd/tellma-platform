// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Tellma.Core.EntityFrameworkCore.TableTypes;
using Tellma.Core.EntityFrameworkCore.Tests.Infrastructure;

namespace Tellma.Core.EntityFrameworkCore.Tests.Standalone
{
    /// <summary>
    ///     Shaping a standalone type's columns from the build action:
    ///     <see cref="TableTypeBuilder.HasColumnType" /> replaces a column's store type and facets, and
    ///     <see cref="TableTypeBuilder.ExcludeColumn" /> drops a column, whether the class or
    ///     <c>Column(…)</c> added it.
    /// </summary>
    public class StandaloneColumnShapingTests
    {
        [Fact]
        public void A_store_type_replaces_the_one_the_clr_type_would_resolve_to()
        {
            TableTypeDefinition definition = FinalizeSingle(mb => mb.HasTableType<StatusShape>(
                schema: "dbo",
                buildAction: type => type.HasColumnType("Status", "varchar(16)", maxLength: 16)));

            TableTypeColumnDefinition status = definition.Columns.Single(c => c.Name == "Status");
            Assert.Equal(("varchar(16)", 16, false), (status.StoreType, status.MaxLength, status.IsNullable));
        }

        [Fact]
        public void A_store_type_applies_before_the_collation_so_a_retyped_column_takes_one()
        {
            TableTypeDefinition definition = FinalizeSingle(mb => mb.HasTableType<StatusShape>(
                schema: "dbo",
                buildAction: type => type
                    .UseCollation("Status", "Latin1_General_100_BIN2")
                    .HasColumnType("Status", "varchar(16)", maxLength: 16)));

            TableTypeColumnDefinition status = definition.Columns.Single(c => c.Name == "Status");
            Assert.Equal(("varchar(16)", "Latin1_General_100_BIN2"), (status.StoreType, status.Collation));
        }

        [Fact]
        public void The_last_store_type_wins_also_over_the_classes_own()
        {
            TableTypeDefinition definition = FinalizeSingle(mb => mb.HasTableType<StatusShape>(
                schema: "dbo",
                buildAction: type => type
                    .HasColumnType("Code", "varchar(10)", maxLength: 10)
                    .HasColumnType("Code", "char(4)", maxLength: 4)));

            Assert.Equal("char(4)", definition.Columns.Single(c => c.Name == "Code").StoreType);
        }

        [Fact]
        public void An_excluded_column_leaves_the_type_after_the_other_calls_named_it()
        {
            TableTypeDefinition definition = FinalizeSingle(mb => mb.HasTableType<StatusShape>(
                schema: "dbo",
                buildAction: type => type
                    .ExcludeColumn("note")
                    .UseCollation("Note", "Latin1_General_100_BIN2")));

            Assert.Equal(["Id", "Status", "Code"], definition.Columns.Select(c => c.Name));
        }

        [Fact]
        public void A_column_added_by_Column_can_be_excluded_and_retyped()
        {
            TableTypeDefinition definition = FinalizeSingle(mb => mb.HasTableType("ProbesList", "dbo", type => type
                .Column<int>("Id")
                .Column<string>("Extra", nullable: true)
                .Column<string>("Value", maxLength: 10)
                .HasColumnType("Value", "nchar(10)", maxLength: 10)
                .ExcludeColumn("Extra")
                .HasKey("Id")));

            Assert.Equal([("Id", "int"), ("Value", "nchar(10)")], definition.Columns.Select(c => (c.Name, c.StoreType)));
        }

        [Fact]
        public void Excluding_a_key_column_fails_the_registration()
        {
            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => FinalizeSingle(mb => mb.HasTableType<StatusShape>(
                schema: "dbo",
                buildAction: type => type.ExcludeColumn("Id"))));

            Assert.Contains("cannot exclude column 'Id'", exception.Message, StringComparison.Ordinal);
        }

        [Theory]
        [InlineData(true, "to give store type 'int'", "HasColumnType")]
        [InlineData(false, "to exclude", "ExcludeColumn")]
        public void A_call_that_names_no_column_fails_the_registration(bool retype, string action, string method)
        {
            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => FinalizeSingle(mb => mb.HasTableType<StatusShape>(
                schema: "dbo",
                buildAction: type => _ = retype ? type.HasColumnType("Missing", "int") : type.ExcludeColumn("Missing"))));

            Assert.Contains($"has no column 'Missing' {action}", exception.Message, StringComparison.Ordinal);
            Assert.Contains($"{method} must name a column", exception.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void A_type_shaped_by_neither_call_keeps_its_definition()
        {
            string plain = FinalizeSingle(mb => mb.HasTableType<StatusShape>(schema: "dbo")).PhysicalName;
            string again = FinalizeSingle(mb => mb.HasTableType<StatusShape>(schema: "dbo", buildAction: static _ => { })).PhysicalName;

            Assert.Equal(plain, again);
        }

        private static TableTypeDefinition FinalizeSingle(Action<ModelBuilder> configure)
        {
            using ModelTestContext context = TestModel.CreateContext(mb =>
            {
                mb.Entity<Plain>();
                configure(mb);
            });

            return Assert.Single(TestModel.GetFinalizedModel(context).GetTableTypes());
        }

        /// <summary>A shape with a column a model stores by name, a fixed-type column and a note.</summary>
        private sealed class StatusShape
        {
            [Key]
            public int Id { get; set; }

            public int Status { get; set; }

            [Column(TypeName = "varchar(10)")]
            public string Code { get; set; } = null!;

            [MaxLength(100)]
            public string? Note { get; set; }
        }
    }
}
