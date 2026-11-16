// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using System.Data;
using System.Data.Common;
using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Settings;
using Tellma.Core.Crud.Validation;
using Tellma.Core.Data.Queries;
using Tellma.Core.Queryex;
using Tellma.Core.Tests.Crud.Scripted;
using Tellma.Core.Tests.Data.Batches;

namespace Tellma.Core.Tests.Crud.Validation
{
    /// <summary>
    ///     The validation model composed with no database: its metadata, its query schema, real
    ///     batches that stop before the send, and context loaders whose rounds a scripted batch
    ///     answers.
    /// </summary>
    internal sealed class ValidationRig : IDisposable
    {
        /// <summary>The composed root.</summary>
        public IServiceProvider Services => Harness.Services;

        /// <summary>The entities' metadata.</summary>
        public IEntityMetadataProvider Metadata => Services.GetRequiredService<IEntityMetadataProvider>();

        /// <summary>The query schema in the one-language shape, and its map to the metadata.</summary>
        public QueryexSchemaMap Map => Services.GetRequiredService<QueryexSchemaProvider>().Map(MultilingualShape.Primary);

        /// <summary>The composed model, for real batches.</summary>
        public BatchHarness Harness { get; } = new(compose: ValidationModel.Compose);

        /// <summary>The metadata of an entity class.</summary>
        /// <typeparam name="T">The class.</typeparam>
        /// <returns>The metadata.</returns>
        public EntityMetadata Of<T>()
        {
            return Metadata.Get(typeof(T));
        }

        /// <summary>A scheduler and a loader whose every round runs on a new batch of the script.</summary>
        /// <param name="script">The script answering the rounds.</param>
        /// <param name="maxRounds">The round ceiling.</param>
        /// <param name="readDecisions">The read decisions of visible loads; none when null.</param>
        /// <param name="metrics">The instruments; none when null.</param>
        /// <param name="cancellationToken">The operation's cancellation; the test's when null.</param>
        /// <returns>The scheduler and the loader.</returns>
        public (ValidationScheduler Scheduler, ContextLoader Loader) Loader(
            BatchScript script, int maxRounds = 5, Func<Type, AccessDecision>? readDecisions = null, CrudMetrics? metrics = null, CancellationToken? cancellationToken = null)
        {
            // The dispatch closes over the loader, created once the scheduler it parks on exists.
            ContextLoader? loader = null;
            ValidationScheduler scheduler = new(maxRounds, "save", (_, token) => loader!.DispatchAsync(script.NewBatch(), token), metrics, cancellationToken ?? TestContext.Current.CancellationToken);
            loader = new ContextLoader(Metadata, Map, scheduler, readDecisions);
            return (scheduler, loader);
        }

        /// <inheritdoc />
        public void Dispose()
        {
            Harness.Dispose();
        }

        /// <summary>A row set of integer columns, read as the batch's reader reads one; a null cell is SQL <c>NULL</c>.</summary>
        /// <param name="columns">The columns' names.</param>
        /// <param name="rows">The rows.</param>
        /// <returns>The row set.</returns>
        public static QueryRowSet IntRows(string[] columns, params int?[][] rows)
        {
            using DataTable table = new();
            foreach (string column in columns)
            {
                _ = table.Columns.Add(column, typeof(int));
            }

            foreach (int?[] row in rows)
            {
                _ = table.Rows.Add([.. row.Select(static value => value is int number ? (object)number : DBNull.Value)]);
            }

            using DbDataReader reader = table.CreateDataReader();
            QueryRowSet.Builder builder = new(
                [.. columns.Select(static name => new QueryColumn(name, QueryexType.QxNumeric, null, null, Nullable: true, [name], GroupingKey: false))],
                [.. columns.Select(static _ => typeof(int))],
                rows.Length);
            while (reader.Read())
            {
                builder.AppendRow(reader);
            }

            return builder.Build();
        }
    }
}
