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
    ///     A table type may live in a schema no table or sequence uses (e.g. a standalone type in its
    ///     own schema). EF's differ only ensures schemas its relational model references, so the
    ///     adapter must emit <c>EnsureSchema</c> for such a schema or the <c>CREATE TYPE</c> would
    ///     target a non-existent schema.
    /// </summary>
    public class SchemaEnsureTests
    {
        [Fact]
        public void Create_in_a_table_less_schema_emits_EnsureSchema_before_the_create()
        {
            using ModelTestContext context = TestModel.CreateContext(mb =>
            {
                mb.Entity<Plain>(); // a table in the default schema; nothing maps to "tt"
                mb.HasTableType("ThingList", schema: "tt", type => type.Column<int>("Id").HasKey("Id"));
            });

            IMigrationsModelDiffer differ = context.GetService<IMigrationsModelDiffer>();
            List<MigrationOperation> ops =
                [.. differ.GetDifferences(null, TestModel.GetRelationalModel(context))];

            int ensureIndex = ops.FindIndex(o => o is EnsureSchemaOperation { Name: "tt" });
            int createIndex = ops.FindIndex(o => o is CreateTableTypeOperation { Schema: "tt" });

            Assert.True(ensureIndex >= 0, "EnsureSchema for the table-less type schema was not emitted.");
            Assert.True(ensureIndex < createIndex, "EnsureSchema must precede the CreateTableType that uses it.");
        }

        [Fact]
        public void No_duplicate_EnsureSchema_when_a_table_already_covers_the_schema()
        {
            using ModelTestContext context = TestModel.CreateContext(mb =>
            {
                mb.Entity<Plain>(e => e.ToTable("Plains", "shared"));
                mb.HasTableType("ThingList", schema: "shared", type => type.Column<int>("Id").HasKey("Id"));
            });

            IMigrationsModelDiffer differ = context.GetService<IMigrationsModelDiffer>();
            List<MigrationOperation> ops =
                [.. differ.GetDifferences(null, TestModel.GetRelationalModel(context))];

            // EF already ensures "shared" for the Plains table; the adapter must not add a second.
            Assert.Single(ops, o => o is EnsureSchemaOperation { Name: "shared" });
        }
    }
}
