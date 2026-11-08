// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Tellma.Core.Abstractions.TableTypes;
using Tellma.Core.EntityFrameworkCore.TableTypes;
using Tellma.Core.EntityFrameworkCore.TableTypes.Json;
using Tellma.Core.EntityFrameworkCore.TableTypes.Operations;
using Tellma.Core.EntityFrameworkCore.Tests.Infrastructure;

namespace Tellma.Core.EntityFrameworkCore.Tests.Standalone
{
    /// <summary>
    ///     Column collations on standalone table types: <see cref="TableTypeBuilder.UseCollation" />
    ///     on class-derived and <c>Column(…)</c>-added columns, the character-column check at model
    ///     finalization, versioning through the differ, and the unchanged canonical JSON of a type
    ///     without a collation.
    /// </summary>
    public class StandaloneColumnCollationTests
    {
        /// <summary>The binary collation the tests give a column.</summary>
        private const string Bin2 = "Latin1_General_100_BIN2";

        /// <summary>A second collation, to tell an earlier setting from a later one.</summary>
        private const string CaseSensitive = "Latin1_General_100_CS_AS";

        // ---------------------------------------------------------------------------------------
        // What UseCollation sets
        // ---------------------------------------------------------------------------------------

        [Fact]
        public void A_class_derived_column_gets_the_collation_and_keeps_its_store_type()
        {
            TableTypeDefinition definition = FinalizeSingle(mb =>
                mb.HasTableType<SubjectShape>(schema: "dbo", buildAction: type => type.UseCollation("Subject", Bin2)));

            var columns = definition.Columns.ToDictionary(c => c.Name);

            // The collation lands on the named column only; its resolved store type, facets and
            // nullability are those the class gives it.
            Assert.Equal(Bin2, columns["Subject"].Collation);
            Assert.Equal("nvarchar(255)", columns["Subject"].StoreType);
            Assert.Equal(255, columns["Subject"].MaxLength);
            Assert.False(columns["Subject"].IsNullable);
            Assert.Null(columns["DisplayName"].Collation);
            Assert.Equal("nvarchar(100)", columns["DisplayName"].StoreType);
            Assert.Null(columns["Id"].Collation);
            Assert.Equal(["Id", "Subject", "DisplayName"], definition.Columns.Select(c => c.Name));
        }

        [Fact]
        public void The_collation_reaches_the_create_type_statement()
        {
            using ModelTestContext source = TestModel.CreateContext(mb => mb.Entity<Plain>());
            using ModelTestContext target = TestModel.CreateContext(mb =>
            {
                mb.Entity<Plain>();
                mb.HasTableType<SubjectShape>(schema: "dbo", buildAction: type => type.UseCollation("Subject", Bin2));
            });

            CreateTableTypeOperation create = Assert.Single(Differences(source, target).OfType<CreateTableTypeOperation>());
            IMigrationsSqlGenerator generator = target.GetService<IMigrationsSqlGenerator>();
            string sql = Assert.Single(generator.Generate([create])).CommandText;

            Assert.Contains($"[Subject] nvarchar(255) COLLATE {Bin2} NOT NULL,", sql, StringComparison.Ordinal);
            Assert.Contains("[DisplayName] nvarchar(100) NULL", sql, StringComparison.Ordinal);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void A_column_added_by_Column_gets_the_collation_whether_the_call_comes_before_or_after(bool callFirst)
        {
            TableTypeDefinition definition = FinalizeSingle(mb => mb.HasTableType("SubjectsList", "dbo", type =>
            {
                if (callFirst)
                {
                    type.UseCollation("Subject", Bin2);
                }

                type.Column<int>("Id").Column<string>("Subject", maxLength: 255).HasKey("Id");

                if (!callFirst)
                {
                    type.UseCollation("Subject", Bin2);
                }
            }));

            TableTypeColumnDefinition subject = definition.Columns.Single(c => c.Name == "Subject");
            Assert.Equal(Bin2, subject.Collation);
            Assert.Equal("nvarchar(255)", subject.StoreType);
        }

        [Fact]
        public void A_column_added_to_a_class_derived_type_by_Column_gets_the_collation()
        {
            TableTypeDefinition definition = FinalizeSingle(mb => mb.HasTableType<SubjectShape>(
                schema: "dbo",
                buildAction: type => type
                    .UseCollation("Issuer", Bin2)
                    .Column("Issuer", "varchar(100)", maxLength: 100)));

            TableTypeColumnDefinition issuer = definition.Columns[^1];
            Assert.Equal("Issuer", issuer.Name);
            Assert.Equal(Bin2, issuer.Collation);
            Assert.Equal("varchar(100)", issuer.StoreType);
        }

        [Fact]
        public void The_last_UseCollation_call_wins()
        {
            TableTypeDefinition definition = FinalizeSingle(mb => mb.HasTableType<SubjectShape>(
                schema: "dbo",
                buildAction: type => type.UseCollation("Subject", CaseSensitive).UseCollation("Subject", Bin2)));

            Assert.Equal(Bin2, definition.Columns.Single(c => c.Name == "Subject").Collation);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void UseCollation_wins_over_the_collation_argument_of_Column(bool callFirst)
        {
            TableTypeDefinition definition = FinalizeSingle(mb => mb.HasTableType("SubjectsList", "dbo", type =>
            {
                if (callFirst)
                {
                    type.UseCollation("Subject", Bin2);
                }

                type.Column<string>("Subject", maxLength: 255, collation: CaseSensitive);

                if (!callFirst)
                {
                    type.UseCollation("Subject", Bin2);
                }
            }));

            Assert.Equal(Bin2, Assert.Single(definition.Columns).Collation);
        }

        [Fact]
        public void Column_names_match_case_insensitively_and_keep_their_declared_spelling()
        {
            TableTypeDefinition definition = FinalizeSingle(mb =>
                mb.HasTableType<SubjectShape>(schema: "dbo", buildAction: type => type.UseCollation("SUBJECT", Bin2)));

            TableTypeColumnDefinition subject = definition.Columns.Single(c => c.Collation is not null);
            Assert.Equal("Subject", subject.Name);
            Assert.Equal(Bin2, subject.Collation);
        }

        // ---------------------------------------------------------------------------------------
        // Registration failures
        // ---------------------------------------------------------------------------------------

        [Fact]
        public void An_unknown_column_fails_the_registration_naming_the_type_and_the_column()
        {
            // The registration call itself throws, before any model is finalized.
            ModelBuilder modelBuilder = new();

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
                modelBuilder.HasTableType<SubjectShape>(schema: "dbo", buildAction: type => type.UseCollation("Subjects", Bin2)));

            Assert.Contains("[dbo].[SubjectsList]", exception.Message, StringComparison.Ordinal);
            Assert.Contains("'Subjects'", exception.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void An_unknown_column_fails_an_ad_hoc_registration_too()
        {
            ModelBuilder modelBuilder = new();

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
                modelBuilder.HasTableType("CodesList", schema: null, type => type
                    .Column<string>("Code", maxLength: 10)
                    .UseCollation("Name", Bin2)));

            Assert.Contains("[CodesList]", exception.Message, StringComparison.Ordinal);
            Assert.Contains("'Name'", exception.Message, StringComparison.Ordinal);
        }

        [Theory]
        [InlineData(null, Bin2)]
        [InlineData("", Bin2)]
        [InlineData("Subject", null)]
        [InlineData("Subject", "")]
        [InlineData("Subject", "  ")]
        public void UseCollation_refuses_a_missing_column_or_collation(string? column, string? collation)
        {
            TableTypeBuilder builder = new();

            Assert.ThrowsAny<ArgumentException>(() => builder.UseCollation(column!, collation!));
        }

        // ---------------------------------------------------------------------------------------
        // The character-column check at model finalization
        // ---------------------------------------------------------------------------------------

        [Theory]
        [InlineData("char(3)")]
        [InlineData("varchar(100)")]
        [InlineData("VARCHAR(MAX)")]
        [InlineData("nchar(10)")]
        [InlineData("nvarchar(450)")]
        [InlineData("nvarchar(max)")]
        [InlineData("nvarchar")]
        public void Character_columns_take_a_collation(string storeType)
        {
            TableTypeDefinition definition = FinalizeSingle(mb => mb.HasTableType("CodesList", "dbo", type => type
                .Column("Code", storeType)
                .UseCollation("Code", Bin2)));

            TableTypeColumnDefinition code = Assert.Single(definition.Columns);
            Assert.Equal(storeType, code.StoreType);
            Assert.Equal(Bin2, code.Collation);
            Assert.False(code.IsJson);
        }

        [Fact]
        public void An_int_column_with_a_collation_fails_finalization()
        {
            AssertFinalizationFails(
                mb => mb.HasTableType("NumbersList", "dbo", type => type.Column<int>("Number", collation: Bin2)),
                "[dbo].[NumbersList]",
                "Number",
                "int");
        }

        [Fact]
        public void A_datetimeoffset_column_with_a_collation_fails_finalization()
        {
            AssertFinalizationFails(
                mb => mb.HasTableType("StampsList", "dbo", type => type
                    .Column<DateTimeOffset>("At")
                    .UseCollation("At", Bin2)),
                "[dbo].[StampsList]",
                "At",
                "datetimeoffset");
        }

        [Fact]
        public void A_json_column_with_a_collation_fails_finalization_instead_of_taking_the_wire_collation()
        {
            AssertFinalizationFails(
                mb => mb.HasTableType("DocumentsList", "dbo", type => type
                    .Column("Payload", "json", nullable: true)
                    .UseCollation("Payload", Bin2)),
                "[dbo].[DocumentsList]",
                "Payload",
                "json");
        }

        [Fact]
        public void A_class_derived_json_column_with_a_collation_fails_finalization()
        {
            AssertFinalizationFails(
                mb => mb.HasTableType<JsonShape>(buildAction: type => type.UseCollation("Payload", Bin2)),
                "[JsonShape]",
                "Payload",
                "json");
        }

        [Fact]
        public void A_class_derived_int_column_with_a_collation_fails_finalization()
        {
            AssertFinalizationFails(
                mb => mb.HasTableType<SubjectShape>(schema: "dbo", buildAction: type => type.UseCollation("Id", Bin2)),
                "[dbo].[SubjectsList]",
                "Id",
                "int");
        }

        [Fact]
        public void A_json_column_without_a_collation_still_takes_the_wire_form()
        {
            TableTypeDefinition definition = FinalizeSingle(mb =>
                mb.HasTableType("DocumentsList", "dbo", type => type.Column("Payload", "json", nullable: true)));

            TableTypeColumnDefinition payload = Assert.Single(definition.Columns);
            Assert.Equal("varchar(max)", payload.StoreType);
            Assert.Equal("Latin1_General_100_BIN2_UTF8", payload.Collation);
            Assert.True(payload.IsJson);
        }

        // ---------------------------------------------------------------------------------------
        // Versioning
        // ---------------------------------------------------------------------------------------

        [Fact]
        public void Adding_a_collation_emits_a_create_and_the_cleanup_and_no_drop()
        {
            using ModelTestContext source = TestModel.CreateContext(mb =>
            {
                mb.Entity<Plain>();
                mb.HasTableType<SubjectShape>(schema: "dbo");
            });
            using ModelTestContext target = TestModel.CreateContext(mb =>
            {
                mb.Entity<Plain>();
                mb.HasTableType<SubjectShape>(schema: "dbo", buildAction: type => type.UseCollation("Subject", Bin2));
            });

            string before = Assert.Single(TestModel.GetFinalizedModel(source).GetTableTypes()).PhysicalName;
            string after = Assert.Single(TestModel.GetFinalizedModel(target).GetTableTypes()).PhysicalName;
            Assert.NotEqual(before, after);

            // A new version created beside the old one; the trailing sweep keeps only the new one
            // and retires the old (spec 0001 §3). Nothing is dropped in place.
            IReadOnlyList<MigrationOperation> operations = Differences(source, target);
            Assert.DoesNotContain(operations, o => o is DropTableTypeOperation);
            CreateTableTypeOperation create = Assert.Single(operations.OfType<CreateTableTypeOperation>());
            Assert.Equal(after, create.PhysicalName);
            Assert.Equal(Bin2, create.Columns.Single(c => c.Name == "Subject").Collation);
            CleanupTableTypesOperation cleanup = Assert.IsType<CleanupTableTypesOperation>(operations[^1]);
            Assert.Equal([after], Assert.IsType<string[]>(cleanup.KeepList));
        }

        [Fact]
        public void Changing_or_removing_a_collation_also_creates_a_new_version()
        {
            string none = PhysicalName(type => { });
            string bin2 = PhysicalName(type => type.UseCollation("Subject", Bin2));
            string caseSensitive = PhysicalName(type => type.UseCollation("Subject", CaseSensitive));

            Assert.Equal(3, new[] { none, bin2, caseSensitive }.Distinct(StringComparer.Ordinal).Count());
        }

        [Fact]
        public void A_type_without_a_collation_keeps_its_canonical_json_and_hash()
        {
            // The canonical JSON of a collation-free type carries no collation member at all, and
            // the bytes, and so the version, are those this type had before collations existed:
            // the physical names are the ones the committed migrations of the library's sample
            // host deployed.
            IReadOnlyList<TableTypeDefinition> types = FinalizeAll(mb =>
            {
                mb.HasTableType<StringList>(schema: "dbo", buildAction: type => type.HasGrants("public"));
                mb.HasTableType<DocumentStateShape>(buildAction: type => type.HasGrants("public"));
            });

            TableTypeDefinition stringList = types.Single(t => t.Name == "StringList");
            Assert.Equal(
                /*lang=json,strict*/"""{"name":"StringList","schema":"dbo","isMemoryOptimized":false,"grants":["public"],"primaryKey":["Id"],"columns":[{"name":"Id","storeType":"nvarchar(450)","isNullable":false,"maxLength":450}]}""",
                TableTypeJson.Serialize(stringList));
            Assert.Equal("StringList_7e92fa5c", stringList.PhysicalName);

            TableTypeDefinition documentStates = types.Single(t => t.Name == "DocumentStatesList");
            Assert.Equal(
                /*lang=json,strict*/"""{"name":"DocumentStatesList","isMemoryOptimized":false,"grants":["public"],"primaryKey":["Id"],"columns":[{"name":"Id","storeType":"int","isNullable":false},{"name":"State","storeType":"smallint","isNullable":false}]}""",
                TableTypeJson.Serialize(documentStates));
            Assert.Equal("DocumentStatesList_93a0c43e", documentStates.PhysicalName);
        }

        [Fact]
        public void The_registration_annotation_of_a_type_without_a_collation_carries_no_collation()
        {
            // The raw registration input is unchanged too: UseCollation adds no member to it, and
            // a collation reaches it only on the column it names.
            ModelBuilder plain = new();
            plain.HasTableType<SubjectShape>(schema: "dbo");
            ModelBuilder collated = new();
            collated.HasTableType<SubjectShape>(schema: "dbo", buildAction: type => type.UseCollation("Subject", Bin2));

            string plainJson = StandaloneAnnotation(plain);
            StandaloneTableTypeConfiguration plainConfiguration = TableTypeJson.DeserializeStandalone(plainJson);
            StandaloneTableTypeConfiguration collatedConfiguration =
                TableTypeJson.DeserializeStandalone(StandaloneAnnotation(collated));

            Assert.DoesNotContain("collation", plainJson, StringComparison.OrdinalIgnoreCase);
            Assert.Equal([null, Bin2, null], collatedConfiguration.Columns.Select(c => c.Collation));
            Assert.Equal(plainConfiguration.Columns, collatedConfiguration.Columns.Select(c => c with { Collation = null }));
        }

        // ---------------------------------------------------------------------------------------
        // Helpers and shapes
        // ---------------------------------------------------------------------------------------

        /// <summary>Finalizes a model holding the given registrations and returns its table types.</summary>
        private static IReadOnlyList<TableTypeDefinition> FinalizeAll(Action<ModelBuilder> configure)
        {
            using ModelTestContext context = TestModel.CreateContext(mb =>
            {
                mb.Entity<Plain>();
                configure(mb);
            });
            return TestModel.GetFinalizedModel(context).GetTableTypes();
        }

        /// <summary>Finalizes a model holding one standalone registration and returns its definition.</summary>
        private static TableTypeDefinition FinalizeSingle(Action<ModelBuilder> configure)
        {
            return Assert.Single(FinalizeAll(configure));
        }

        /// <summary>The physical name of <see cref="SubjectShape" /> registered with the given build action.</summary>
        private static string PhysicalName(Action<TableTypeBuilder> buildAction)
        {
            return FinalizeSingle(mb => mb.HasTableType<SubjectShape>(schema: "dbo", buildAction: buildAction)).PhysicalName;
        }

        /// <summary>Asserts that finalization fails, naming the type, the column and the store type.</summary>
        private static void AssertFinalizationFails(Action<ModelBuilder> configure, string type, string column, string storeType)
        {
            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => FinalizeAll(configure));

            Assert.Contains(type, exception.Message, StringComparison.Ordinal);
            Assert.Contains($"'{column}'", exception.Message, StringComparison.Ordinal);
            Assert.Contains($"'{storeType}'", exception.Message, StringComparison.Ordinal);
            Assert.Contains("char, varchar, nchar and nvarchar", exception.Message, StringComparison.Ordinal);
        }

        /// <summary>The differ's operations from one model to another.</summary>
        private static IReadOnlyList<MigrationOperation> Differences(ModelTestContext source, ModelTestContext target)
        {
            IMigrationsModelDiffer differ = target.GetService<IMigrationsModelDiffer>();
            return differ.GetDifferences(TestModel.GetRelationalModel(source), TestModel.GetRelationalModel(target));
        }

        /// <summary>The single standalone registration annotation a model builder holds.</summary>
        private static string StandaloneAnnotation(ModelBuilder modelBuilder)
        {
            return (string)modelBuilder.Model.GetAnnotations()
                .Single(a => a.Name.StartsWith(TableTypeAnnotationNames.StandalonePrefix, StringComparison.Ordinal))
                .Value!;
        }

        /// <summary>A class-derived shape whose string key meets a binary-collated column.</summary>
        [TableType(Name = "SubjectsList")]
        private sealed class SubjectShape
        {
            [Key]
            public int Id { get; set; }

            [MaxLength(255)]
            public string Subject { get; set; } = null!;

            [MaxLength(100)]
            public string? DisplayName { get; set; }
        }

        /// <summary>A class-derived shape with a native json column.</summary>
        private sealed class JsonShape
        {
            [Key]
            public int Id { get; set; }

            [Column(TypeName = "json")]
            public string? Payload { get; set; }
        }

        /// <summary>The same shape as the sample host's bulk-state type, registered without a collation.</summary>
        [TableType(Name = "DocumentStatesList")]
        private sealed class DocumentStateShape
        {
            [Key]
            public int Id { get; set; }

            public short State { get; set; }
        }
    }
}
