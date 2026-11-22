// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Crud;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Errors;
using Tellma.Core.Abstractions.Excel;
using Tellma.Core.Abstractions.TableTypes;
using Tellma.Core.Queryex;
using Tellma.Core.Tests.Crud.Query;
using Tellma.Core.Tests.Crud.Scripted;
using Tellma.Core.Tests.Crud.Validation;

namespace Tellma.Core.Tests.Crud.ExcelRows
{
    /// <summary>
    ///     The Excel row source's completions over the scripted batch and a model with tiny ceilings:
    ///     a lookup that reaches the take ceiling read on in a wave of its own, a grandchild chunk that
    ///     reaches it completed over its owner chunk's ids, and a by-ids source beyond the ids
    ///     ceiling refused before any round trip.
    /// </summary>
    public sealed class ExcelRowSourceCompletionTests : IAsyncDisposable
    {
        /// <summary>The model's take ceiling.</summary>
        private const int MaxTake = ExcelRowsModel.MaxTake;

        /// <summary>The model over the scripted batch.</summary>
        private readonly QueryRig _rig = new(ExcelRowsModel.Compose);

        /// <summary>The test's cancellation.</summary>
        private static CancellationToken Token => TestContext.Current.CancellationToken;

        /// <inheritdoc />
        public ValueTask DisposeAsync()
        {
            return _rig.DisposeAsync();
        }

        [Fact]
        public async Task A_lookup_whose_chunk_reaches_the_ceiling_reads_on_alone_in_the_next_wave_its_rows_in_order()
        {
            _rig.Grant((ExcelRowsModel.Sheets, AccessActions.Read, null), (ExcelRowsModel.Tags, AccessActions.Read, null));
            _ = _rig.Script.OnRows(static statement => statement.Spec.Root.Name == ExcelRowsModel.Tags
                ? new RowPage(Ids(statement.Spec.Skip == 0 ? MaxTake : 2, from: statement.Spec.Skip!.Value), null, null, null)
                : new RowPage(Ids(1), null, null, null));
            ExcelQuery sheets = Lookup(ExcelRowsModel.Sheets, "Id", [1L]);
            ExcelQuery tags = Lookup(ExcelRowsModel.Tags, "Code", ["a", "b", "c", "d", "e"]);

            await using AsyncServiceScope scope = _rig.Scope();
            IReadOnlyList<IReadOnlyList<IReadOnlyList<object?>>> answers = await Sheets(scope).ExcelRowSource(new ExportSource.All()).ReadAsync([sheets, tags], Token);

            // --- Each query complete, the full one's chunks concatenated in order ---
            Assert.Equal([1], answers[0].Select(static row => (int?)row[0]));
            Assert.Equal([1, 2, 3, 4, 5], answers[1].Select(static row => (int?)row[0]));

            // --- Two round trips: both first chunks, then the full query's next chunk alone ---
            Assert.Equal(2, _rig.Script.RoundTrips);
            Assert.Equal(2, _rig.Script.Batches[0].Of(ScriptedKind.Rows).Count);
            ScriptedStatement followUp = Assert.Single(_rig.Script.Batches[1].Of(ScriptedKind.Rows));
            Assert.Equal((ExcelRowsModel.Tags, MaxTake, MaxTake), (followUp.Spec.Root.Name, followUp.Spec.Skip, followUp.Spec.Take));
            Assert.Equal(["a", "b", "c", "d", "e"], followUp.KeySet<StringList>().Select(static row => row.Id));
        }

        [Fact]
        public async Task A_grandchild_chunk_that_reaches_the_ceiling_is_completed_over_its_owner_chunks_ids()
        {
            _rig.Grant((ExcelRowsModel.Sheets, AccessActions.Read, null));
            _ = _rig.Script.OnRows(static statement => statement.Spec.Root.Name switch
            {
                ExcelRowsModel.Sheets => new RowPage(Ids(2), null, null, null),
                ExcelRowsModel.Lines => new RowPage(Ids(2, from: 10), null, null, null),
                _ => new RowPage(Ids(statement.Spec.Skip == 0 ? MaxTake : 1, from: 100 + statement.Spec.Skip!.Value), null, null, null),
            });
            ExcelQuery notes = new(ExcelRowsModel.Notes, ["Id"], "LineId, Id", null, null, null, null, null);
            ExcelQuery lines = new(ExcelRowsModel.Lines, ["Id"], "SheetId, Id", null, null, null, null, [notes]);

            await using AsyncServiceScope scope = _rig.Scope();
            ExcelPage page = Assert.Single(await PagesAsync(
                Sheets(scope).ExcelRowSource(new ExportSource.All()),
                new ExcelQuery(ExcelRowsModel.Sheets, ["Id"], null, null, null, null, null, [lines])));

            // --- The lines in one chunk, short of the ceiling; their notes complete under them ---
            ExcelPage dependent = Assert.Single(page.Dependents);
            Assert.Equal([11, 12], dependent.Rows.Select(static row => (int?)row[0]));
            Assert.Equal([101, 102, 103, 104], Assert.Single(dependent.Dependents).Rows.Select(static row => (int?)row[0]));

            // --- Two round trips: the page with both collections riding it, then the notes' next
            // chunk alone, over a bound list of the line chunk's ids ---
            Assert.Equal(2, _rig.Script.RoundTrips);
            Assert.Equal(3, _rig.Script.Batches[0].Of(ScriptedKind.Rows).Count);
            ScriptedStatement followUp = Assert.Single(_rig.Script.Batches[1].Of(ScriptedKind.Rows));
            Assert.Equal((ExcelRowsModel.Notes, MaxTake, MaxTake), (followUp.Spec.Root.Name, followUp.Spec.Skip, followUp.Spec.Take));
            Assert.Equal("LineId", Assert.Single(followUp.Spec.Restrictions).Path);
            Assert.Equal([11, 12], followUp.KeySet<IdList>().Select(static row => row.Id));
        }

        [Fact]
        public async Task A_by_ids_source_beyond_the_ids_ceiling_is_refused_before_any_round_trip()
        {
            _rig.Grant((ExcelRowsModel.Sheets, AccessActions.Read, null));
            long[] ids = [.. Enumerable.Range(1, ExcelRowsModel.MaxIds + 1).Select(static id => (long)id)];

            await using AsyncServiceScope scope = _rig.Scope();
            LimitExceededException refused = await Assert.ThrowsAsync<LimitExceededException>(
                () => PagesAsync(Sheets(scope).ExcelRowSource(new ExportSource.ByIds(ids, null)), new ExcelQuery(ExcelRowsModel.Sheets, ["Id"], null, null, null, null, null, null)));

            Assert.Equal((LimitNames.MaxIds, ExcelRowsModel.MaxIds + 1, ExcelRowsModel.MaxIds), (refused.Limit, refused.Actual, refused.Maximum));
            Assert.Empty(_rig.Script.Batches);
        }

        /// <summary>A flat lookup of a target's rows by one column's values.</summary>
        /// <param name="entity">The target.</param>
        /// <param name="column">The column the values match.</param>
        /// <param name="values">The values.</param>
        /// <returns>The query.</returns>
        private static ExcelQuery Lookup(string entity, string column, IReadOnlyList<object> values)
        {
            return new ExcelQuery(entity, ["Id"], null, new KeySetRestriction(column, "@lookup"), values, null, null, null);
        }

        /// <summary>A row set of consecutive ids.</summary>
        /// <param name="count">The rows.</param>
        /// <param name="from">The ids before the first.</param>
        /// <returns>The rows.</returns>
        private static QueryRowSet Ids(int count, int from = 0)
        {
            return ValidationRig.IntRows(["Id"], [.. Enumerable.Range(from + 1, count).Select(static id => new int?[] { id })]);
        }

        /// <summary>The sheet stack's service in a scope.</summary>
        /// <param name="scope">The scope.</param>
        /// <returns>The service.</returns>
        private static EntityService<XrSheet, int> Sheets(AsyncServiceScope scope)
        {
            return scope.ServiceProvider.GetRequiredService<EntityService<XrSheet, int>>();
        }

        /// <summary>Every page of a root query.</summary>
        /// <param name="rows">The row source.</param>
        /// <param name="query">The root query.</param>
        /// <returns>The pages.</returns>
        private static async Task<List<ExcelPage>> PagesAsync(IExcelRowSource rows, ExcelQuery query)
        {
            List<ExcelPage> pages = [];
            await foreach (ExcelPage page in rows.Pages(query, Token))
            {
                pages.Add(page);
            }

            return pages;
        }
    }
}
