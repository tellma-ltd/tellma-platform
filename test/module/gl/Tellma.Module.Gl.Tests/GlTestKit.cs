// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Data;
using System.Numerics;
using Tellma.Core.Abstractions.Api;
using Tellma.Core.Abstractions.Crud;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Entities;
using Tellma.Core.Abstractions.Excel;
using Tellma.Core.Abstractions.Tenancy;
using Tellma.Core.Abstractions.Validation;
using Tellma.Core.Calendars;
using Tellma.Core.Queryex;

namespace Tellma.Module.Gl.Tests
{
    /// <summary>The stand-ins the suite drives the centers' rules and the sample step through, with no database.</summary>
    internal static class GlTestKit
    {
        /// <summary>The caller every context names.</summary>
        public static RequestContext Caller { get; } = RequestContext.Tenantless(new GregorianCalendarSystem(), DateTimeOffset.UnixEpoch);

        /// <summary>A center, as a payload or the store holds it.</summary>
        /// <param name="id">The id; negative for a new row.</param>
        /// <param name="type">The type.</param>
        /// <param name="parentId">The parent; null for a root.</param>
        /// <param name="subtreeCount">The stored subtree size, the center included.</param>
        /// <returns>The center.</returns>
        public static Center Center(int id, CenterType type, int? parentId = null, int subtreeCount = 1)
        {
            return new Center
            {
                Id = id,
                CenterType = type,
                ParentId = parentId,
                SubtreeCount = subtreeCount,
                Code = "C" + id,
                Name = "Center " + id,
            };
        }

        /// <summary>A copy of a center, as a payload saves it.</summary>
        /// <param name="center">The stored center.</param>
        /// <param name="change">What the payload changes.</param>
        /// <returns>The copy.</returns>
        public static Center Edited(Center center, Action<Center> change)
        {
            Center copy = new()
            {
                Id = center.Id,
                CenterType = center.CenterType,
                ParentId = center.ParentId,
                SubtreeCount = center.SubtreeCount,
                Code = center.Code,
                Name = center.Name,
            };
            change(copy);
            return copy;
        }
    }

    /// <summary>The centers' service with its validation hooks reachable from a test.</summary>
    internal sealed class TestCenterService() : CenterService<Center>(new RefusingPipelines())
    {
        /// <summary>Runs the save hook.</summary>
        /// <param name="context">The save's context.</param>
        /// <returns>A task that completes when the rules ran.</returns>
        public Task ValidateSaveAsync(SaveContext<Center> context)
        {
            return ValidateAsync(context);
        }

        /// <summary>Runs the delete hook.</summary>
        /// <param name="context">The delete's context.</param>
        /// <returns>A task that completes when the rule ran.</returns>
        public Task ValidateRemovalAsync(DeleteContext<Center, int> context)
        {
            return ValidateDeleteAsync(context);
        }
    }

    /// <summary>
    ///     A context loader over an in-memory store: loads by id answer at once from the store, every
    ///     request is recorded, and every round is counted. Any other load refuses.
    /// </summary>
    /// <param name="store">The stored centers.</param>
    internal sealed class ScriptedLoader(params Center[] store) : IContextLoader
    {
        /// <summary>The loads by id, in request order: the ids and the projection.</summary>
        public List<(IReadOnlyList<int> Ids, string? Select)> ByIdLoads { get; } = [];

        /// <summary>The rounds awaited.</summary>
        public int Rounds { get; private set; }

        /// <inheritdoc />
        public ContextRef<IReadOnlyDictionary<TK, T>> ByIds<T, TK>(IReadOnlyList<TK> ids, string? select = null)
            where TK : notnull
        {
            ArgumentNullException.ThrowIfNull(ids);
            if (typeof(T) != typeof(Center) || typeof(TK) != typeof(int))
            {
                throw new NotSupportedException($"The scripted loader answers loads of centers by int id, not {typeof(T).Name} by {typeof(TK).Name}.");
            }

            List<int> keys = [.. ids.Cast<int>()];
            ByIdLoads.Add((keys, select));
            Dictionary<TK, T> found = [];
            foreach (Center center in store.Where(center => keys.Contains(center.Id)))
            {
                found[(TK)(object)center.Id] = (T)(object)center;
            }

            return new ContextRef<IReadOnlyDictionary<TK, T>>(found);
        }

        /// <inheritdoc />
        public Task LoadAsync()
        {
            Rounds++;
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public ContextRef<IReadOnlyDictionary<TK, T>> ByKey<T, TK>(string key, IReadOnlyList<TK> values, string? select = null)
            where TK : notnull
        {
            throw Refused();
        }

        /// <inheritdoc />
        public ContextRef<IReadOnlyDictionary<TK, IReadOnlyList<TChild>>> ByParentIds<TChild, TK>(IReadOnlyList<TK> parentIds, string? select = null)
            where TK : notnull
        {
            throw Refused();
        }

        /// <inheritdoc />
        public ContextRef<IReadOnlyList<T>> Query<T>(FilterTree filter, QueryArguments? arguments = null, string? select = null)
        {
            throw Refused();
        }

        /// <inheritdoc />
        public ContextRef<IReadOnlyList<T>> Visible<T>(FilterTree filter, QueryArguments? arguments = null, string? select = null)
        {
            throw Refused();
        }

        /// <inheritdoc />
        public ContextRef<QueryRowSet> Rows(QuerySpec spec, QueryArguments? arguments = null)
        {
            throw Refused();
        }

        /// <inheritdoc />
        public ContextRef<bool> Exists(string root, FilterTree filter, QueryArguments? arguments = null)
        {
            throw Refused();
        }

        /// <inheritdoc />
        public ContextRef<int> Count(string root, FilterTree filter, int cap, QueryArguments? arguments = null)
        {
            throw Refused();
        }

        /// <inheritdoc />
        public ContextRef<IReadOnlyDictionary<TK, IReadOnlyList<TK>>> Ancestors<T, TK>(IReadOnlyList<TK> ids)
            where TK : notnull
        {
            throw Refused();
        }

        /// <inheritdoc />
        public ContextRef<RawResult> Sql(FormattableString sql, SqlOptions? options = null)
        {
            throw Refused();
        }

        /// <inheritdoc />
        public void Prime<T, TK>(T entity)
            where TK : notnull
        {
            throw Refused();
        }

        /// <summary>The refusal of a load the centers' rules never make.</summary>
        /// <returns>The exception.</returns>
        private static NotSupportedException Refused()
        {
            return new NotSupportedException("The centers' rules load parents by id alone.");
        }
    }

    /// <summary>A pipeline factory whose pipelines refuse every operation, for a service whose hooks alone a test calls.</summary>
    internal sealed class RefusingPipelines : IEntityPipelineFactory
    {
        /// <inheritdoc />
        public IEntityPipeline<TEntity, TKey> Create<TEntity, TKey>(IEntityBehavior<TEntity, TKey> behavior)
            where TEntity : Entity<TKey>
            where TKey : struct, IBinaryInteger<TKey>
        {
            return new RecordingPipeline<TEntity, TKey>(stored: null);
        }
    }

    /// <summary>A pipeline factory over one recording pipeline of centers.</summary>
    /// <param name="pipeline">The pipeline every center service binds to.</param>
    internal sealed class RecordingPipelines(RecordingPipeline<Center, int> pipeline) : IEntityPipelineFactory
    {
        /// <inheritdoc />
        public IEntityPipeline<TEntity, TKey> Create<TEntity, TKey>(IEntityBehavior<TEntity, TKey> behavior)
            where TEntity : Entity<TKey>
            where TKey : struct, IBinaryInteger<TKey>
        {
            return pipeline as IEntityPipeline<TEntity, TKey>
                ?? throw new NotSupportedException($"The recording pipeline serves centers, not {typeof(TEntity).Name}.");
        }
    }

    /// <summary>
    ///     A pipeline that records the queries and saves it is asked for, answering a query with the
    ///     stored rows of the codes it names, in the projection <c>Id, Code</c>. Every other
    ///     operation refuses.
    /// </summary>
    /// <typeparam name="TEntity">The entity.</typeparam>
    /// <typeparam name="TKey">The key.</typeparam>
    /// <param name="stored">The stored ids by code; null for a pipeline that refuses queries too.</param>
    internal sealed class RecordingPipeline<TEntity, TKey>(IReadOnlyDictionary<string, int>? stored) : IEntityPipeline<TEntity, TKey>
    {
        /// <summary>The queries, in call order.</summary>
        public List<QueryRequest> Queries { get; } = [];

        /// <summary>The saves, in call order: the payload and the options.</summary>
        public List<(IReadOnlyList<TEntity> Entities, SaveOptions Options)> Saves { get; } = [];

        /// <summary>Every call, in order: the operation's name.</summary>
        public List<string> Calls { get; } = [];

        /// <inheritdoc />
        public StackDescriptor Descriptor => throw Refused();

        /// <inheritdoc />
        public Task<RowPage> QueryAsync(QueryRequest request, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(request);
            if (stored is null)
            {
                throw Refused();
            }

            Queries.Add(request);
            Calls.Add("query");

            // --- The stored rows whose codes the arguments name, as the projection Id, Code reads them ---
            HashSet<string> codes = new(request.Arguments!.Values.Select(static value => value.GetString()!), StringComparer.OrdinalIgnoreCase);
            using DataTable table = new();
            table.Locale = System.Globalization.CultureInfo.InvariantCulture;
            _ = table.Columns.Add("Id", typeof(int));
            _ = table.Columns.Add("Code", typeof(string));
            foreach ((string code, int id) in stored.Where(pair => codes.Contains(pair.Key)))
            {
                _ = table.Rows.Add(id, code);
            }

            QueryRowSet.Builder builder = new(
                [new QueryColumn("Id", QueryexType.QxNumeric, null, null, false, null, false), new QueryColumn("Code", QueryexType.QxString, null, null, false, null, false)],
                [typeof(int), typeof(string)],
                table.Rows.Count);
            using (DataTableReader reader = table.CreateDataReader())
            {
                while (reader.Read())
                {
                    builder.AppendRow(reader);
                }
            }

            return Task.FromResult(new RowPage(builder.Build(), null, null, null));
        }

        /// <inheritdoc />
        public Task<EntitiesResult<TEntity>> SaveAsync(IReadOnlyList<TEntity> entities, SaveOptions options, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(entities);
            if (stored is null)
            {
                throw Refused();
            }

            Saves.Add((entities, options));
            Calls.Add("save");
            return Task.FromResult(new EntitiesResult<TEntity> { Ids = [], Entities = [] });
        }

        /// <inheritdoc />
        public Task<EntitiesResult<TEntity>> GetByIdAsync(TKey id, DetailsRequest request, CancellationToken cancellationToken)
        {
            throw Refused();
        }

        /// <inheritdoc />
        public Task<EntitiesResult<TEntity>> GetByIdsAsync(IReadOnlyList<TKey> ids, DetailsRequest request, CancellationToken cancellationToken)
        {
            throw Refused();
        }

        /// <inheritdoc />
        public Task<EntitiesResult<TEntity>> GetAllCachedAsync(DetailsRequest request, CancellationToken cancellationToken)
        {
            throw Refused();
        }

        /// <inheritdoc />
        public Task<AffectedResult> DeleteByIdsAsync(IReadOnlyList<TKey> ids, IReadOnlyList<DateTimeOffset?>? expectedStamps, CancellationToken cancellationToken)
        {
            throw Refused();
        }

        /// <inheritdoc />
        public Task<AffectedResult> DeleteByQueryAsync(DeleteByQueryRequest request, CancellationToken cancellationToken)
        {
            throw Refused();
        }

        /// <inheritdoc />
        public Task<EntitiesResult<TEntity>> ExecuteActionAsync(string action, IReadOnlyList<TKey> ids, object? arguments, ActionOptions options, CancellationToken cancellationToken)
        {
            throw Refused();
        }

        /// <inheritdoc />
        public Task<TResult> ExecuteActionAsync<TResult>(string action, IReadOnlyList<TKey> ids, object? arguments, ActionOptions options, CancellationToken cancellationToken)
        {
            throw Refused();
        }

        /// <inheritdoc />
        public Task<QueryRowSet> GetByParentIdsAsync(ParentIdsRequest request, CancellationToken cancellationToken)
        {
            throw Refused();
        }

        /// <inheritdoc />
        public Task<AffectedResult> DeleteWithDescendantsAsync(IReadOnlyList<TKey> ids, CancellationToken cancellationToken)
        {
            throw Refused();
        }

        /// <inheritdoc />
        public IExcelRowSource CreateExcelRowSource(ExportSource source)
        {
            throw Refused();
        }

        /// <summary>The refusal of an operation the test does not script.</summary>
        /// <returns>The exception.</returns>
        private static NotSupportedException Refused()
        {
            return new NotSupportedException("The test does not script this operation.");
        }
    }
}
