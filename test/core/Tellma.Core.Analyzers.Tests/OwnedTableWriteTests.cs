// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Analyzers.Tests.Infrastructure;

namespace Tellma.Core.Analyzers.Tests
{
    /// <summary>TELLMA0006: a mapped entity's table, and its child tables, are written only by the entity's own stack.</summary>
    public class OwnedTableWriteTests
    {
        /// <summary>A stack's entity with a child collection two levels deep, a system entity, and the usings.</summary>
        private const string Model = """
            using System.Collections.Generic;
            using System.ComponentModel.DataAnnotations.Schema;
            using System.Threading.Tasks;
            using Tellma.Core.Abstractions.Crud;
            using Tellma.Core.Abstractions.Data;
            using Tellma.Core.Abstractions.Entities;

            [Table("Centers", Schema = "gl")]
            public class Center : TopLevelEntity
            {
                [NotMapped]
                public List<CenterLine>? Lines { get; set; }
            }

            [Table("CenterLines", Schema = "gl")]
            public class CenterLine : ChildEntity
            {
                [NotMapped]
                public List<CenterLineNote>? Notes { get; set; }
            }

            [Table("CenterLineNotes", Schema = "gl")]
            public class CenterLineNote : ChildEntity;

            // A distribution's leaf keeps its default's table.
            public class AcmeCenter : Center;

            [Table("Units", Schema = "gl")]
            public class Unit : TopLevelEntity;

            public static class Writes
            {
                public static readonly SqlOptions Declared = new(writes: new HashSet<TableName> { new("gl", "Centers") });
            }

            """;

        /// <summary>A type that owns no stack writing an entity's table or a child table is refused, by entity write or raw text.</summary>
        /// <returns>A task that completes when the check has passed.</returns>
        [Fact]
        public Task A_non_owner_writing_a_stack_table_is_refused()
        {
            return AnalyzerHarness.VerifyAsync<DataBatchAnalyzer>(Model + """
                public class Importer
                {
                    public void Run(IDataBatch batch)
                    {
                        _ = {|TELLMA0006:batch.Save<Center>([])|};
                        _ = {|TELLMA0006:batch.Save<AcmeCenter>([])|};
                        _ = {|TELLMA0006:batch.Delete<Center>(null!)|};
                        _ = {|TELLMA0006:batch.Update<CenterLine>(null!)|};
                        _ = batch.Sql($"UPDATE {|TELLMA0006:[gl].[Centers]|} SET [Code] = N'x';", Writes.Declared);
                        _ = batch.Sql($"DELETE FROM {|TELLMA0006:[gl].[CenterLineNotes]|};", Writes.Declared);
                        _ = batch.Sql($"UPDATE {|TELLMA0006:[core].[Jobs]|} SET [State] = 'x';", Writes.Declared);
                        System.Func<IReadOnlyList<Center>, ConcurrencyMode, SaveHandle> save = {|TELLMA0006:batch.Save<Center>|};
                    }
                }
                """);
        }

        /// <summary>A unit's service writing the centers' table is refused: it owns its own stack only.</summary>
        /// <returns>A task that completes when the check has passed.</returns>
        [Fact]
        public Task Another_stack_writing_the_table_is_refused()
        {
            return AnalyzerHarness.VerifyAsync<DataBatchAnalyzer>(Model + """
                public class UnitService : EntityService<Unit>
                {
                    public void Run(IDataBatch batch)
                    {
                        _ = batch.Save<Unit>([]);
                        _ = {|TELLMA0006:batch.Save<Center>([])|};
                    }
                }
                """);
        }

        /// <summary>The entity's service, validators and effects write its table and its child tables, a generic leaf's included.</summary>
        /// <returns>A task that completes when the check has passed.</returns>
        [Fact]
        public Task The_owning_stack_writes_its_tables()
        {
            return AnalyzerHarness.VerifyAsync<DataBatchAnalyzer>(Model + """
                public class CenterService<TCenter> : EntityService<TCenter>
                    where TCenter : Center
                {
                    public void Run(IDataBatch batch)
                    {
                        _ = batch.Save<TCenter>([]);
                        _ = batch.Save<Center>([]);
                        _ = batch.Sql($"UPDATE [gl].[CenterLines] SET [Code] = N'x';", Writes.Declared);
                        System.Action later = () => batch.Sql($"DELETE FROM [gl].[CenterLineNotes];", Writes.Declared);
                    }
                }

                public class CenterRules : IEntityValidator<AcmeCenter>
                {
                    public Task ValidateAsync(IReadOnlyList<AcmeCenter> entities) => Task.CompletedTask;

                    public void Run(IDataBatch batch) => batch.Sql($"UPDATE [gl].[Centers] SET [Code] = N'x';", Writes.Declared);
                }

                public class CenterEffect : IPersistEffect<Center>
                {
                    public Task ContributeAsync(IReadOnlyList<Center> entities) => Task.CompletedTask;

                    public void Run(IDataBatch batch) => batch.Save<CenterLine>([]);
                }
                """);
        }

        /// <summary>A table no entity maps, a write whose entity the code does not show, and the runtime pass.</summary>
        /// <returns>A task that completes when the check has passed.</returns>
        [Fact]
        public Task Unowned_and_unknown_tables_pass()
        {
            return AnalyzerHarness.VerifyAsync<DataBatchAnalyzer>(Model + """
                public class Companion<TEntity>
                    where TEntity : Entity<int>
                {
                    public void Run(IDataBatch batch, SqlIdentifier table)
                    {
                        _ = batch.Save<TEntity>([]);
                        _ = batch.Sql($"UPDATE [dist].[Ledger] SET [Amount] = 0;", Writes.Declared);
                        _ = batch.Sql($"UPDATE {table} SET [Amount] = 0;", Writes.Declared);
                    }
                }
                """);
        }

        /// <summary>The runtime writes every table.</summary>
        /// <returns>A task that completes when the check has passed.</returns>
        [Fact]
        public Task The_runtime_writes_every_table()
        {
            return AnalyzerHarness.VerifyAsync<DataBatchAnalyzer>(Model + """
                public class Maintenance
                {
                    public void Run(IDataBatch batch)
                    {
                        _ = batch.Save<Center>([]);
                        _ = batch.Sql($"UPDATE [core].[Jobs] SET [State] = 'x';", Writes.Declared);
                    }
                }
                """, "Tellma.Core");
        }
    }
}
