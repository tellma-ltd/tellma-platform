// Copyright (c) 2026 Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Tellma.Core.EntityFrameworkCore.TableTypes;
using Tellma.Core.EntityFrameworkCore.TableTypes.Operations;
using Tellma.Core.EntityFrameworkCore.Tests.Infrastructure;

namespace Tellma.Core.EntityFrameworkCore.Tests.BuiltIn
{
    /// <summary>
    ///     The built-in primitive table types (spec 0001 §5): hand-defined single-column types outside
    ///     the 0-or-1-per-table rule, flowing through the same annotations, differ, operations and
    ///     SQL as table-derived types.
    /// </summary>
    public class BuiltInTableTypesTests
    {
        [Fact]
        public void All_built_in_types_expand_into_definitions()
        {
            using ModelTestContext context = TestModel.CreateContext(mb =>
            {
                mb.Entity<Plain>();
                mb.HasBuiltInTableTypes(BuiltInTableTypes.All, schema: "dbo", "tellma_app");
            });

            IReadOnlyList<TableTypeDefinition> types = TestModel.GetFinalizedModel(context).GetTableTypes();

            Assert.Equal(["BigIdList", "GuidList", "IdList", "StringList"], types.Select(t => t.Name));
            Assert.All(types, t =>
            {
                Assert.Equal("dbo", t.Schema);
                Assert.Null(t.TableName);
                Assert.Equal(["Id"], t.PrimaryKey);
                Assert.Equal(["tellma_app"], t.Grants);
                TableTypeColumnDefinition column = Assert.Single(t.Columns);
                Assert.Equal("Id", column.Name);
                Assert.False(column.IsNullable);
            });
            Assert.Equal("int", types.Single(t => t.Name == "IdList").Columns[0].StoreType);
            Assert.Equal("bigint", types.Single(t => t.Name == "BigIdList").Columns[0].StoreType);
            Assert.Equal("uniqueidentifier", types.Single(t => t.Name == "GuidList").Columns[0].StoreType);
            Assert.Equal("nvarchar(450)", types.Single(t => t.Name == "StringList").Columns[0].StoreType);
            Assert.Equal(450, types.Single(t => t.Name == "StringList").Columns[0].MaxLength);
        }

        [Fact]
        public void Flags_select_a_subset()
        {
            using ModelTestContext context = TestModel.CreateContext(mb =>
            {
                mb.Entity<Plain>();
                mb.HasBuiltInTableTypes(BuiltInTableTypes.IdList | BuiltInTableTypes.StringList);
            });

            IReadOnlyList<TableTypeDefinition> types = TestModel.GetFinalizedModel(context).GetTableTypes();

            Assert.Equal(["IdList", "StringList"], types.Select(t => t.Name));
        }

        [Fact]
        public void Built_in_types_diff_and_generate_sql_like_any_other_type()
        {
            using ModelTestContext source = TestModel.CreateContext(mb => mb.Entity<Plain>());
            using ModelTestContext target = TestModel.CreateContext(mb =>
            {
                mb.Entity<Plain>();
                mb.HasBuiltInTableTypes(BuiltInTableTypes.IdList, schema: "bulk", "tellma_app");
            });

            IMigrationsModelDiffer differ = target.GetService<IMigrationsModelDiffer>();
            IReadOnlyList<MigrationOperation> operations = differ.GetDifferences(
                TestModel.GetRelationalModel(source), TestModel.GetRelationalModel(target));

            CreateTableTypeOperation create = Assert.IsType<CreateTableTypeOperation>(Assert.Single(operations));

            IMigrationsSqlGenerator generator = target.GetService<IMigrationsSqlGenerator>();
            MigrationCommand command = Assert.Single(generator.Generate([create]));

            Assert.Equal(
                """
                CREATE TYPE [bulk].[IdList] AS TABLE (
                    [Id] int NOT NULL,
                    PRIMARY KEY CLUSTERED ([Id])
                );
                GRANT EXECUTE ON TYPE::[bulk].[IdList] TO [tellma_app];

                """,
                command.CommandText.Replace("\r\n", "\n", StringComparison.Ordinal));
        }

        [Fact]
        public void Changing_built_in_config_recreates_the_types()
        {
            using ModelTestContext source = TestModel.CreateContext(mb =>
            {
                mb.Entity<Plain>();
                mb.HasBuiltInTableTypes(BuiltInTableTypes.IdList);
            });
            using ModelTestContext target = TestModel.CreateContext(mb =>
            {
                mb.Entity<Plain>();
                mb.HasBuiltInTableTypes(BuiltInTableTypes.IdList, schema: "dbo", "tellma_app");
            });

            IMigrationsModelDiffer differ = target.GetService<IMigrationsModelDiffer>();
            IReadOnlyList<MigrationOperation> operations = differ.GetDifferences(
                TestModel.GetRelationalModel(source), TestModel.GetRelationalModel(target));

            Assert.Equal(2, operations.Count);
            Assert.IsType<DropTableTypeOperation>(operations[0]);
            Assert.Equal(["tellma_app"], Assert.IsType<CreateTableTypeOperation>(operations[1]).Grants);
        }
    }
}
