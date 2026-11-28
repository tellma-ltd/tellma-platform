// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Tellma.Core.EntityFrameworkCore.LockEscalation;
using Tellma.Core.EntityFrameworkCore.Tests.Infrastructure;

namespace Tellma.Core.EntityFrameworkCore.Tests.LockEscalation
{
    /// <summary>
    ///     A table's declared <c>LOCK_ESCALATION</c> option: carried by the entity type, surfaced on the
    ///     table operations the differ emits, and generated as an <c>ALTER TABLE</c> of its own when the
    ///     table is created and whenever the option changes.
    /// </summary>
    public class LockEscalationTests
    {
        /// <summary>A context whose <see cref="Plain" /> table declares <paramref name="mode" />, or nothing.</summary>
        private static ModelTestContext PlainContext(LockEscalationMode? mode)
        {
            return TestModel.CreateContext(mb => mb.Entity<Plain>(e =>
            {
                e.ToTable("Plains", "core");
                if (mode is LockEscalationMode declared)
                {
                    e.HasLockEscalation(declared);
                }
            }));
        }

        /// <summary>The operations the context's differ emits between two models.</summary>
        private static IReadOnlyList<MigrationOperation> Diff(DbContext source, DbContext target)
        {
            IMigrationsModelDiffer differ = target.GetService<IMigrationsModelDiffer>();
            return differ.GetDifferences(TestModel.GetRelationalModel(source), TestModel.GetRelationalModel(target));
        }

        /// <summary>The SQL of the operations, every command's text joined, line endings normalised.</summary>
        private static List<string> Sql(DbContext context, IReadOnlyList<MigrationOperation> operations)
        {
            IMigrationsSqlGenerator generator = context.GetService<IMigrationsSqlGenerator>();
            return [.. generator.Generate(operations, TestModel.GetFinalizedModel(context)).Select(static command => command.CommandText.Replace("\r\n", "\n", StringComparison.Ordinal))];
        }

        [Theory]
        [InlineData(LockEscalationMode.Table, "TABLE")]
        [InlineData(LockEscalationMode.Auto, "AUTO")]
        [InlineData(LockEscalationMode.Disable, "DISABLE")]
        public void The_declared_option_is_read_back_as_its_keyword(LockEscalationMode mode, string keyword)
        {
            using ModelTestContext context = PlainContext(mode);

            Assert.Equal(keyword, TestModel.GetFinalizedModel(context).FindEntityType(typeof(Plain))!.GetLockEscalation());
        }

        [Fact]
        public void An_undeclared_option_reads_back_as_null()
        {
            using ModelTestContext context = PlainContext(null);

            Assert.Null(TestModel.GetFinalizedModel(context).FindEntityType(typeof(Plain))!.GetLockEscalation());
        }

        [Fact]
        public void An_undefined_mode_is_refused()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => LockEscalationExtensions.Keyword((LockEscalationMode)42));
        }

        [Fact]
        public void Creating_the_table_carries_the_option_and_sets_it_after_the_create()
        {
            using ModelTestContext empty = TestModel.CreateContext(static _ => { });
            using ModelTestContext target = PlainContext(LockEscalationMode.Disable);

            IReadOnlyList<MigrationOperation> operations = Diff(empty, target);
            CreateTableOperation create = Assert.Single(operations.OfType<CreateTableOperation>());
            Assert.Equal("DISABLE", create[LockEscalationAnnotationNames.LockEscalation]);

            // The option is a statement of its own, after the table exists.
            List<string> sql = Sql(target, [create]);
            Assert.Equal(2, sql.Count);
            Assert.StartsWith("CREATE TABLE [core].[Plains]", sql[0], StringComparison.Ordinal);
            Assert.Equal("ALTER TABLE [core].[Plains] SET (LOCK_ESCALATION = DISABLE);\n", sql[1]);
        }

        [Fact]
        public void Creating_a_table_without_the_option_sets_nothing()
        {
            using ModelTestContext empty = TestModel.CreateContext(static _ => { });
            using ModelTestContext target = PlainContext(null);

            List<string> sql = Sql(target, Diff(empty, target));

            Assert.DoesNotContain(sql, static text => text.Contains("LOCK_ESCALATION", StringComparison.Ordinal));
        }

        [Fact]
        public void Declaring_the_option_on_an_existing_table_alters_it()
        {
            using ModelTestContext source = PlainContext(null);
            using ModelTestContext target = PlainContext(LockEscalationMode.Disable);

            IReadOnlyList<MigrationOperation> operations = Diff(source, target);
            AlterTableOperation alter = Assert.Single(operations.OfType<AlterTableOperation>());
            Assert.Equal("DISABLE", alter[LockEscalationAnnotationNames.LockEscalation]);
            Assert.Null(alter.OldTable[LockEscalationAnnotationNames.LockEscalation]);

            Assert.Contains("ALTER TABLE [core].[Plains] SET (LOCK_ESCALATION = DISABLE);\n", Sql(target, operations));
        }

        [Fact]
        public void Dropping_the_declaration_restores_the_server_default()
        {
            using ModelTestContext source = PlainContext(LockEscalationMode.Disable);
            using ModelTestContext target = PlainContext(null);

            List<string> sql = Sql(target, Diff(source, target));

            Assert.Contains("ALTER TABLE [core].[Plains] SET (LOCK_ESCALATION = TABLE);\n", sql);
        }

        [Fact]
        public void An_unchanged_option_emits_nothing()
        {
            using ModelTestContext source = PlainContext(LockEscalationMode.Disable);
            using ModelTestContext target = PlainContext(LockEscalationMode.Disable);

            Assert.Empty(Diff(source, target));
        }

        [Fact]
        public void A_keyword_SQL_Server_does_not_accept_is_refused_at_generation()
        {
            using ModelTestContext context = PlainContext(null);
            CreateTableOperation create = new() { Name = "Plains", Schema = "core" };
            create.Columns.Add(new AddColumnOperation { Name = "Id", Table = "Plains", Schema = "core", ClrType = typeof(int), ColumnType = "int" });
            create[LockEscalationAnnotationNames.LockEscalation] = "NEVER; DROP TABLE x";

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => Sql(context, [create]));

            Assert.Contains("SQL Server accepts TABLE, AUTO or DISABLE", exception.Message, StringComparison.Ordinal);
        }
    }
}
