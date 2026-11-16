// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using System.Data;
using System.Text.Json;
using Tellma.Core.Abstractions.Caching;
using Tellma.Core.Abstractions.Crud;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Localization;
using Tellma.Core.Abstractions.Settings;
using Tellma.Core.Abstractions.Tenancy;
using Tellma.Core.Abstractions.Validation;
using Tellma.Core.Calendars;
using Tellma.Core.Data.Queries;
using Tellma.Core.Data.Trees;
using Tellma.Core.Queryex;
using Tellma.Core.Tests.Data.Batches;
using Tellma.Core.Tests.Data.Saves;

namespace Tellma.Core.Tests.Data.Trees
{
    /// <summary>
    ///     The C# tree check: the table's parents loaded once with the payload's laid over them, a
    ///     cycle reported on the rows that close it, a row its own parent, a depth beyond the limit
    ///     reached by a moved row's subtree, and no load for a payload of roots.
    /// </summary>
    public sealed class TreeCycleValidatorTests : IDisposable
    {
        /// <summary>The composition over the save model, whose plain tree is four levels deep at most.</summary>
        private readonly BatchHarness _harness = new(compose: SaveModel.Compose);

        [Fact]
        public async Task A_move_under_its_own_descendant_is_a_cycle_on_the_moved_row_only()
        {
            // 1 > 2 > 3, and 4 under 3; the payload moves 1 under 3 and leaves 4 where it is.
            TreeLoader loader = new((1, null), (2, 1), (3, 2), (4, 3));
            SaveContext<Region> context = Context(loader, Row(1, parent: 3), Row(4, parent: 3));

            await Validator().ValidateAsync(context);

            Assert.Equal(["[0].ParentId:Tree.Cycle"], Errors(context));
            Assert.Equal("Id,ParentId", loader.Select);
        }

        [Fact]
        public async Task Two_rows_that_swap_parents_each_close_the_cycle()
        {
            TreeLoader loader = new((1, null), (2, 1), (3, 1));
            SaveContext<Region> context = Context(loader, Row(2, parent: 3), Row(3, parent: 2));

            await Validator().ValidateAsync(context);

            Assert.Equal(["[0].ParentId:Tree.Cycle", "[1].ParentId:Tree.Cycle"], Errors(context));
        }

        [Fact]
        public async Task A_row_that_is_its_own_parent_is_a_cycle()
        {
            SaveContext<Region> context = Context(new TreeLoader((1, null)), Row(1, parent: 1));

            await Validator().ValidateAsync(context);

            Assert.Equal(["[0].ParentId:Tree.Cycle"], Errors(context));
        }

        [Fact]
        public async Task A_move_that_puts_the_subtree_deeper_than_the_limit_is_too_deep()
        {
            // 1 > 2 > 3, and 10 > 11 > 12: moving 10 under 3 nests 12 at the sixth level; 11 under 1 at the fourth is fine.
            TreeLoader loader = new((1, null), (2, 1), (3, 2), (10, null), (11, 10), (12, 11), (20, null), (21, 20));
            SaveContext<Region> context = Context(loader, Row(10, parent: 3), Row(21, parent: 2));

            await Validator().ValidateAsync(context);

            Assert.Equal(["[0].ParentId:Tree.TooDeep"], Errors(context));
        }

        [Fact]
        public async Task New_rows_chained_to_exactly_the_limit_pass()
        {
            SaveContext<Region> context = Context(new TreeLoader(), Row(1, parent: null), Row(2, parent: 1), Row(3, parent: 2), Row(4, parent: 3));

            await Validator().ValidateAsync(context);

            Assert.Empty(Errors(context));
        }

        [Fact]
        public async Task A_payload_of_roots_loads_nothing()
        {
            TreeLoader loader = new();
            SaveContext<Region> context = Context(loader, Row(1, parent: null));

            await Validator().ValidateAsync(context);

            Assert.Empty(Errors(context));
            Assert.Null(loader.Select);
        }

        public void Dispose()
        {
            _harness.Dispose();
        }

        /// <summary>A payload row.</summary>
        /// <param name="id">The id.</param>
        /// <param name="parent">The parent.</param>
        /// <returns>The row.</returns>
        private static Region Row(int id, int? parent)
        {
            return new Region { Id = id, ParentId = parent, Name = "r" };
        }

        /// <summary>The errors of a context, each as its path and code.</summary>
        /// <param name="context">The context.</param>
        /// <returns>The errors.</returns>
        private static IEnumerable<string> Errors(SaveContext<Region> context)
        {
            return context.Errors.Items.Select(static error => $"{error.Path}:{error.Code}");
        }

        /// <summary>A save context of a primary-shaped tenant over the payload.</summary>
        /// <param name="loader">The loader.</param>
        /// <param name="rows">The payload.</param>
        /// <returns>The context.</returns>
        private static SaveContext<Region> Context(TreeLoader loader, params Region[] rows)
        {
            DateTimeOffset now = new(2026, 11, 14, 9, 0, 0, TimeSpan.Zero);
            return new SaveContext<Region>
            {
                Entities = rows,
                Options = new SaveOptions(),
                Context = RequestContext.Tenantless(new GregorianCalendarSystem(), now) with
                {
                    TenantSettings = new TenantSettings
                    {
                        TenantId = 1,
                        Tag = VersionTag.New(),
                        Names = ["Acme", null, null],
                        Languages = [new LanguageInfo("en", "English", "E", false)],
                        Shape = MultilingualShape.Primary,
                        Calendars = [],
                        TimeZone = TimeZoneInfo.Utc,
                        SqlServerTimeZoneName = "UTC",
                        ModifiedAt = now,
                        Entries = new Dictionary<string, JsonElement>(),
                    },
                },
                Loader = loader,
                Errors = new ValidationErrors<Region>(),
                Host = new TestHost(),
            };
        }

        /// <summary>The validator over the composition's metadata and schemas.</summary>
        /// <returns>The validator.</returns>
        private TreeCycleValidator<Region> Validator()
        {
            return new TreeCycleValidator<Region>(
                _harness.Services.GetRequiredService<IEntityMetadataProvider>(), _harness.Services.GetRequiredService<QueryexSchemaProvider>());
        }

        /// <summary>A loader that serves the table's (Id, ParentId) rows and records the selection it was asked for.</summary>
        /// <param name="rows">The table's rows.</param>
        private sealed class TreeLoader(params (int Id, int? ParentId)[] rows) : IContextLoader
        {
            /// <summary>The selection of the one rows query, or null when none was asked.</summary>
            public string? Select { get; private set; }

            /// <inheritdoc />
            public ContextRef<QueryRowSet> Rows(QuerySpec spec, QueryArguments? arguments = null)
            {
                Select = spec.Select;
                using DataTable table = new();
                _ = table.Columns.Add("Id", typeof(int));
                _ = table.Columns.Add("ParentId", typeof(int));
                foreach ((int id, int? parentId) in rows)
                {
                    _ = table.Rows.Add(id, parentId is int parent ? parent : DBNull.Value);
                }

                QueryRowSet.Builder builder = new(
                    [new QueryColumn("Id", QueryexType.QxNumeric, null, null, false, null, false), new QueryColumn("ParentId", QueryexType.QxNumeric, null, null, true, null, false)],
                    [typeof(int), typeof(int)],
                    rows.Length);
                using DataTableReader reader = table.CreateDataReader();
                while (reader.Read())
                {
                    builder.AppendRow(reader);
                }

                return new ContextRef<QueryRowSet>(builder.Build());
            }

            /// <inheritdoc />
            public Task LoadAsync()
            {
                return Task.CompletedTask;
            }

            /// <inheritdoc />
            public ContextRef<IReadOnlyDictionary<TK, T>> ByIds<T, TK>(IReadOnlyList<TK> ids, string? select = null)
                where TK : notnull
            {
                throw new NotSupportedException();
            }

            /// <inheritdoc />
            public ContextRef<IReadOnlyDictionary<TK, T>> ByKey<T, TK>(string key, IReadOnlyList<TK> values, string? select = null)
                where TK : notnull
            {
                throw new NotSupportedException();
            }

            /// <inheritdoc />
            public ContextRef<IReadOnlyDictionary<TK, IReadOnlyList<TChild>>> ByParentIds<TChild, TK>(IReadOnlyList<TK> parentIds, string? select = null)
                where TK : notnull
            {
                throw new NotSupportedException();
            }

            /// <inheritdoc />
            public ContextRef<IReadOnlyList<T>> Query<T>(FilterTree filter, QueryArguments? arguments = null, string? select = null)
            {
                throw new NotSupportedException();
            }

            /// <inheritdoc />
            public ContextRef<IReadOnlyList<T>> Visible<T>(FilterTree filter, QueryArguments? arguments = null, string? select = null)
            {
                throw new NotSupportedException();
            }

            /// <inheritdoc />
            public ContextRef<bool> Exists(string root, FilterTree filter, QueryArguments? arguments = null)
            {
                throw new NotSupportedException();
            }

            /// <inheritdoc />
            public ContextRef<int> Count(string root, FilterTree filter, int cap, QueryArguments? arguments = null)
            {
                throw new NotSupportedException();
            }

            /// <inheritdoc />
            public ContextRef<IReadOnlyDictionary<TK, IReadOnlyList<TK>>> Ancestors<T, TK>(IReadOnlyList<TK> ids)
                where TK : notnull
            {
                throw new NotSupportedException();
            }

            /// <inheritdoc />
            public ContextRef<RawResult> Sql(FormattableString sql, SqlOptions? options = null)
            {
                throw new NotSupportedException();
            }

            /// <inheritdoc />
            public void Prime<T, TK>(T entity)
                where TK : notnull
            {
                throw new NotSupportedException();
            }
        }

        /// <summary>A write host the check never asks anything.</summary>
        private sealed class TestHost : IWriteHost
        {
            /// <inheritdoc />
            public WriteHostKind Kind => WriteHostKind.Pipeline;

            /// <inheritdoc />
            public string Operation => "test.save";

            /// <inheritdoc />
            public IConfirmations Confirmations => throw new NotSupportedException();
        }
    }
}
