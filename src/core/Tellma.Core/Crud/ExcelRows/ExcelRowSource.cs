// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Runtime.CompilerServices;
using Tellma.Core.Abstractions.Api;
using Tellma.Core.Abstractions.Errors;
using Tellma.Core.Abstractions.Excel;
using Tellma.Core.Crud.Query;
using Tellma.Core.Queryex;

namespace Tellma.Core.Crud.ExcelRows
{
    // Spec 0015 §15: the read check on the first call, one read batch per page of the take ceiling
    // reconnecting the caller through the runner, the capped count on the first page, the
    // dependents riding each page at every depth, a dependent beyond the ceiling completed by
    // follow-ups over the page's owner ids before the page is yielded, and several flat queries
    // in one read batch. The stack's count cap and take ceiling bind neither the query's count cap
    // nor its take, and the skip window never binds it.
    /// <summary>
    ///     The rows the Excel codec reads from one stack, or one child query surface, under one
    ///     source, with the caller's read filter. Creating it runs nothing; its first call runs the
    ///     root's read check before any row is read.
    /// </summary>
    /// <param name="operation">The scope's row-source operation.</param>
    /// <param name="surface">The root's surface.</param>
    /// <param name="source">The rows to read.</param>
    /// <param name="searchFilter">The service's branch replacing the searchable columns', given the stripped search; null for the platform's.</param>
    internal sealed class ExcelRowSource(ExcelRowsOperation operation, QuerySurface surface, ExportSource source, Func<string, FilterTree?>? searchFilter)
        : IExcelRowSource
    {
        /// <summary>The path a by-ids source's out-of-range id is reported at.</summary>
        private const string IdsPath = "ids";

        /// <summary>The binding the first call's admission found; null before the first call.</summary>
        private RowsBinding? _binding;

        /// <summary>The source's ordering, which a query on the own root that names none takes.</summary>
        private string? SourceOrderBy => source switch
        {
            ExportSource.ByIds ids => ids.OrderBy,
            ExportSource.ByQuery query => query.OrderBy,
            _ => null,
        };

        /// <inheritdoc />
        /// <exception cref="ArgumentException">The query is not one the row source reads: another root, a dependent that is no child collection, or a blank column.</exception>
        public IAsyncEnumerable<ExcelPage> Pages(ExcelQuery query, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(query);
            var root = RowsNode.Admit(query, operation.Surfaces, surface, SourceOrderBy, paged: true);
            return StreamAsync(root, cancellationToken);
        }

        /// <inheritdoc />
        /// <exception cref="ArgumentException">A query is not one the row source reads: one with dependents, an unmapped root, or a blank column.</exception>
        public Task<IReadOnlyList<IReadOnlyList<IReadOnlyList<object?>>>> ReadAsync(IReadOnlyList<ExcelQuery> queries, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(queries);
            RowsNode[] nodes = [.. queries.Select(query => RowsNode.Admit(query ?? throw new ArgumentException("The queries hold no null.", nameof(queries)), operation.Surfaces, surface, SourceOrderBy, paged: false))];
            int values = nodes.Sum(static node => node.Query.RestrictionValues?.Count ?? 0);
            return operation.RunAsync(surface.Resource, operation.Lane(values), () => ReadAllAsync(nodes, cancellationToken), cancellationToken);
        }

        /// <summary>The pages of a root query, one read batch each, until a page falls short of its size or the take is reached.</summary>
        /// <param name="root">The admitted root query.</param>
        /// <param name="cancellationToken">Stops the stream between pages and cancels the page in flight.</param>
        /// <returns>The pages.</returns>
        private async IAsyncEnumerable<ExcelPage> StreamAsync(RowsNode root, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            string lane = operation.Lane(source is ExportSource.ByIds ids ? ids.Ids.Count : 0);
            long read = 0;
            int skip = 0;
            bool first = true;
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                // --- The page's size: the take ceiling, short of the query's take when fewer remain ---
                int maxTake = surface.Limits.MaxTake;
                int take = root.Query.Take is int total ? (int)Math.Min(maxTake, total - read) : maxTake;
                if (take <= 0)
                {
                    yield break;
                }

                // --- One page, its count on the first alone; an empty page after the first ends the stream unseen ---
                int? countCap = first ? root.Query.CountCap : null;
                int from = skip;
                ExcelPage page = await operation.RunAsync(surface.Resource, lane, () => PageAsync(root, from, take, countCap, cancellationToken), cancellationToken).ConfigureAwait(false);
                if (!first && page.Rows.Count == 0)
                {
                    yield break;
                }

                yield return page;
                if (page.Rows.Count < take)
                {
                    yield break;
                }

                read += page.Rows.Count;
                skip = checked(skip + page.Rows.Count);
                first = false;
            }
        }

        /// <summary>One page: the root's rows with its dependents' first chunks on one round trip, then each dependent completed.</summary>
        /// <param name="root">The admitted root query.</param>
        /// <param name="skip">The root rows to skip.</param>
        /// <param name="take">The page's size.</param>
        /// <param name="countCap">Where the page's capped count stops; null for no count.</param>
        /// <param name="cancellationToken">Cancels the page.</param>
        /// <returns>The page.</returns>
        private async Task<ExcelPage> PageAsync(RowsNode root, int skip, int take, int? countCap, CancellationToken cancellationToken)
        {
            RowsBinding binding = await BindingAsync(cancellationToken).ConfigureAwait(false);
            ChunkRows page = await operation.RoundTripAsync<ChunkRows>(
                binding,
                composition => composition.Compose(root, owner: null, skip, take, countCap, withDependents: true).Read,
                cancellationToken).ConfigureAwait(false);

            List<ExcelPage> dependents = [];
            for (int index = 0; index < root.Dependents.Count; index++)
            {
                dependents.Add(await CompleteAsync(binding, root.Dependents[index], page.Keys, page.Dependents?[index], cancellationToken).ConfigureAwait(false));
            }

            return new ExcelPage(page.Rows, dependents, page.Count);
        }

        /// <summary>
        ///     Completes a dependent under its owner's rows: a first chunk that reached the take
        ///     ceiling is followed by follow-up reads over the owner ids, skip advancing, each with the
        ///     dependent's own dependents riding it, restricted to that chunk's rows; each of those is
        ///     completed the same way under the chunk it rode.
        /// </summary>
        /// <remarks>
        ///     The dependent's rows keep their order across its chunks. A dependent of a dependent is
        ///     read chunk by chunk of its owner, so its rows stay grouped under their owner rows, the
        ///     groups in the order the chunks were read.
        /// </remarks>
        /// <param name="binding">The row source's binding.</param>
        /// <param name="node">The dependent.</param>
        /// <param name="owners">The owner rows' keys.</param>
        /// <param name="first">The first chunk, when it rode the owner's statement; null to read it.</param>
        /// <param name="cancellationToken">Cancels the reads.</param>
        /// <returns>The dependent's complete rows under the owners, with its own dependents.</returns>
        private async Task<ExcelPage> CompleteAsync(RowsBinding binding, RowsNode node, IReadOnlyList<long> owners, ChunkRows? first, CancellationToken cancellationToken)
        {
            // --- No owner row: no child row, at any depth ---
            PageBuilder page = new(node);
            if (owners.Count == 0)
            {
                return page.Build();
            }

            // --- The first chunk, read over the owner ids when it did not ride the owner's statement ---
            int maxTake = binding.MaxTake;
            ChunkRows chunk = first ?? await operation.RoundTripAsync<ChunkRows>(
                binding,
                composition => composition.Compose(node, owners, skip: 0, maxTake, countCap: null, withDependents: true).Read,
                cancellationToken).ConfigureAwait(false);
            while (true)
            {
                // --- The chunk's rows, and its dependents completed under them ---
                page.AddRows(chunk.Rows);
                for (int index = 0; index < node.Dependents.Count; index++)
                {
                    page.AddDependent(index, await CompleteAsync(binding, node.Dependents[index], chunk.Keys, chunk.Dependents?[index], cancellationToken).ConfigureAwait(false));
                }

                if (!chunk.Full)
                {
                    return page.Build();
                }

                // --- A follow-up: the next chunk, skip advancing, its dependents riding it ---
                int skip = page.Count;
                chunk = await operation.RoundTripAsync<ChunkRows>(
                    binding,
                    composition => composition.Compose(node, owners, skip, maxTake, countCap: null, withDependents: true).Read,
                    cancellationToken).ConfigureAwait(false);
            }
        }

        /// <summary>
        ///     The flat queries' rows: every first chunk on one round trip, then one round trip per
        ///     wave of follow-ups for the queries whose chunk reached the take ceiling.
        /// </summary>
        /// <param name="nodes">The admitted queries.</param>
        /// <param name="cancellationToken">Cancels the reads.</param>
        /// <returns>Each query's rows, parallel to the queries.</returns>
        private async Task<IReadOnlyList<IReadOnlyList<IReadOnlyList<object?>>>> ReadAllAsync(RowsNode[] nodes, CancellationToken cancellationToken)
        {
            RowsBinding binding = await BindingAsync(cancellationToken).ConfigureAwait(false);
            List<object?[]>[] rows = [.. nodes.Select(static _ => new List<object?[]>())];
            List<int> pending = [.. Enumerable.Range(0, nodes.Length)];
            while (pending.Count > 0)
            {
                // --- One chunk of each pending query, each its take ceiling or what remains of its take ---
                int[] wave = [.. pending];
                int[] takes = [.. wave.Select(index => Take(nodes[index], rows[index].Count, binding.MaxTake))];
                ChunkRows[] chunks = await operation.RoundTripAsync<ChunkRows[]>(
                    binding,
                    composition =>
                    {
                        Func<ChunkRows>[] reads = [.. wave.Select((index, at) => (Func<ChunkRows>)composition.Compose(nodes[index], owner: null, rows[index].Count, takes[at], countCap: null, withDependents: false).Read)];
                        return () => [.. reads.Select(static read => read())];
                    },
                    cancellationToken).ConfigureAwait(false);

                // --- A full chunk short of the query's take reads on in the next wave ---
                pending.Clear();
                for (int at = 0; at < wave.Length; at++)
                {
                    int index = wave[at];
                    rows[index].AddRange(chunks[at].Rows);
                    if (chunks[at].Full && Take(nodes[index], rows[index].Count, binding.MaxTake) > 0)
                    {
                        pending.Add(index);
                    }
                }
            }

            return [.. rows.Select(static list => (IReadOnlyList<IReadOnlyList<object?>>)list)];
        }

        /// <summary>
        ///     The binding of the first call, admitted once: the root's read check with its step-up,
        ///     then a by-ids source's ids under the ceiling and narrowed to the key type, and the
        ///     service's branch for a by-query source's search, asked once.
        /// </summary>
        /// <param name="cancellationToken">Cancels the check.</param>
        /// <returns>The binding.</returns>
        /// <exception cref="ForbiddenException">The caller holds no read grant on the root.</exception>
        /// <exception cref="LimitExceededException">A by-ids source names more ids than the root's ceiling.</exception>
        /// <exception cref="BadRequestException">An id is outside the root's key range.</exception>
        private async Task<RowsBinding> BindingAsync(CancellationToken cancellationToken)
        {
            if (_binding is RowsBinding bound)
            {
                return bound;
            }

            // --- The type-level grant first: it refuses before any row is read ---
            await operation.RequireAsync(surface.Resource, cancellationToken).ConfigureAwait(false);

            // --- The ids, under the ceiling, narrowed to the key type, each once ---
            IReadOnlyList<long>? ids = null;
            if (source is ExportSource.ByIds byIds)
            {
                if (byIds.Ids.Count > surface.Limits.MaxIds)
                {
                    throw new LimitExceededException(LimitNames.MaxIds, byIds.Ids.Count, surface.Limits.MaxIds);
                }

                ids = surface.KeyType == typeof(int)
                    ? [.. WireIds.ToKeys<int>(byIds.Ids, IdsPath).Distinct().Select(static id => (long)id)]
                    : [.. WireIds.ToKeys<long>(byIds.Ids, IdsPath).Distinct()];
            }

            // --- The service's branch reads only the text, so it is asked once, whatever the pages ---
            FilterTree? customSearch = source is ExportSource.ByQuery query && QueryShape.Strip(query.Search) is string search && searchFilter is not null
                ? searchFilter(search)
                : null;
            return _binding = new RowsBinding(surface, source, ids, customSearch);
        }

        /// <summary>The size of a flat query's next chunk: the take ceiling, short of the query's take when fewer remain.</summary>
        /// <param name="node">The query.</param>
        /// <param name="read">The rows read so far.</param>
        /// <param name="maxTake">The take ceiling.</param>
        /// <returns>The size; zero when the query's take is reached.</returns>
        private static int Take(RowsNode node, int read, int maxTake)
        {
            return node.Query.Take is int total ? Math.Max(0, Math.Min(maxTake, total - read)) : maxTake;
        }

        /// <summary>A dependent's rows gathered chunk by chunk, with its own dependents' rows under them.</summary>
        /// <param name="node">The dependent.</param>
        private sealed class PageBuilder(RowsNode node)
        {
            /// <summary>The rows gathered so far.</summary>
            private readonly List<IReadOnlyList<object?>> _rows = [];

            /// <summary>Each dependent's rows gathered so far, parallel to the node's.</summary>
            private readonly PageBuilder[] _dependents = [.. node.Dependents.Select(static dependent => new PageBuilder(dependent))];

            /// <summary>The rows gathered so far.</summary>
            public int Count => _rows.Count;

            /// <summary>Adds a chunk's rows.</summary>
            /// <param name="rows">The rows.</param>
            public void AddRows(IReadOnlyList<object?[]> rows)
            {
                _rows.AddRange(rows);
            }

            /// <summary>Adds one dependent's rows under a chunk, with theirs at every depth.</summary>
            /// <param name="index">The dependent's index.</param>
            /// <param name="page">Its rows under the chunk.</param>
            public void AddDependent(int index, ExcelPage page)
            {
                _dependents[index].Add(page);
            }

            /// <summary>The page of every row gathered.</summary>
            /// <returns>The page.</returns>
            public ExcelPage Build()
            {
                return new ExcelPage([.. _rows], [.. _dependents.Select(static dependent => dependent.Build())], Count: null);
            }

            /// <summary>Adds a page's rows and its dependents' rows at every depth.</summary>
            /// <param name="page">The page.</param>
            private void Add(ExcelPage page)
            {
                _rows.AddRange(page.Rows);
                for (int index = 0; index < _dependents.Length; index++)
                {
                    _dependents[index].Add(page.Dependents[index]);
                }
            }
        }
    }
}
