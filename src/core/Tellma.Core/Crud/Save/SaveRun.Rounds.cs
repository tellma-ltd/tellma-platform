// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Crud.Enlistment;
using Tellma.Core.Queryex;

namespace Tellma.Core.Crud.Save
{
    // Spec 0015 §6.1 step 5 and §9.2: round trip 1 carries, beside every load the hook and the
    // validators requested, the reservation statement when the buffer fell short, the before images
    // of the update roots under the caller's Read filter with every collection the payload carries,
    // the Save grant's count over the same ids, and the referenced rows of every reference under its
    // target's Read filter (§7.4). Each round runs on one guarded Validate batch, its decisions taken
    // for the connected user the batch is composed for.
    /// <summary>One save of one stack: the rounds, and the pipeline's own loads of the first.</summary>
    internal sealed partial class SaveRun<TEntity, TKey>
    {
        /// <summary>The update roots as stored, loaded under the caller's read filter; empty until round trip 1 ran.</summary>
        private IReadOnlyList<TEntity> _stored = [];

        /// <summary>The update roots the save grant's filter admits; null when the grant admits every row.</summary>
        private int? _saveCount;

        /// <summary>The referenced ids each target holds under the caller's read decision, once round trip 1 ran.</summary>
        private readonly Dictionary<EntityMetadata, HashSet<long>> _visible = new(ReferenceEqualityComparer.Instance);

        /// <summary>The targets read under a sensitive securable, whose new or changed references ask for the step-up a direct read asks for.</summary>
        private readonly HashSet<EntityMetadata> _sensitive = new(ReferenceEqualityComparer.Instance);

        /// <summary>Whether the checks that follow round trip 1 ran.</summary>
        private bool _settled;

        /// <summary>The rounds dispatched when the validators started; null until they did.</summary>
        private int? _validatorsStarted;

        /// <summary>
        ///     Dispatches one round: its loads on one guarded batch, then, after the first, the ordered
        ///     checks, and the checks of every group enlisted with the save that the round served.
        /// </summary>
        /// <param name="round">The round's 1-based number.</param>
        /// <param name="cancellationToken">Cancels the round.</param>
        /// <returns>A task that completes when the round's refs are filled and, after the first, the checks ran.</returns>
        private async Task DispatchAsync(int round, CancellationToken cancellationToken)
        {
            // --- The round's loads, composed for the user the runner connects; a round with none sends nothing ---
            await DispatchRoundAsync(_loader, cancellationToken).ConfigureAwait(false);

            // --- The round the validators resume in; a deferred first round is the pipeline's alone ---
            _context.Round = _deferred ? round : round + 1;
            if (round == 1)
            {
                // Before any validator continuation resumes: the ordered checks, then, when the ids
                // rode this round, the validators start with the final ids.
                await SettleAsync().ConfigureAwait(false);
                if (_deferred)
                {
                    StartValidators();
                }
            }

            // --- The enlisted groups' checks, once the round that served their loads ran ---
            if (Enlistments is EnlistmentRounds enlistments)
            {
                await enlistments.AfterRoundAsync(round).ConfigureAwait(false);
            }
        }

        /// <summary>Composes the pipeline's own loads of round trip 1 onto the round's batch.</summary>
        /// <param name="batch">The round's batch.</param>
        /// <returns>Reads the loads' results into the save once the batch ran.</returns>
        private Action ComposeFirstRound(IDataBatch batch)
        {
            ConnectedUser user = Composer;
            List<Action> readers = [];

            // --- The reservation statement, when the buffer fell short; a recomposition reserves afresh ---
            if (_deferred)
            {
                _reservation?.Dispose();
                _reservation = null;
                _reservation = _operation.Allocator.Reserve(batch, typeof(TEntity), Payload);
            }

            // --- The before images and the save grant's count over the update roots ---
            TKey[] updates = [.. _roots.Where(static row => !row.IsNew).Select(static row => ((TEntity)row.Row).Id)];
            if (updates.Length > 0)
            {
                readers.Add(ComposeTargets<TEntity, TKey>(batch, user, _map, _root, updates, AccessActions.Save, PresentCollections(), (images, count) =>
                {
                    _stored = images;
                    _saveCount = count;
                }));
            }

            // --- The referenced rows of every reference, by target ---
            foreach ((EntityMetadata target, HashSet<long> ids) in _targets)
            {
                readers.Add(ComposeTargets(batch, user, target, ids));
            }

            return () =>
            {
                foreach (Action read in readers)
                {
                    read();
                }
            };
        }

        // Spec 0015 §7.4: a reference loads under its target's Read decision, a denied target making
        // every reference to it not found. A child entity without a read securable of its own (one
        // with no query surface) is read under its owner's decision, seen through its navigation to
        // the owner's row, up to the first owner that has one, as a read of the owner's details
        // would reach it. Only an entity no grant can reach (no stack serves it, or its stack
        // serves no operation) is checked for existence alone. The decision comes from the set the
        // batch is composed under, which the batch's prologue validates: a moved permissions tag
        // recomposes the round from the fresh set, so a stale denial never stands (spec 0014 §6.4).
        /// <summary>Composes the referenced rows of one target, under the caller's read decision on it.</summary>
        /// <param name="batch">The round's batch.</param>
        /// <param name="user">The connected user.</param>
        /// <param name="target">The target entity.</param>
        /// <param name="ids">The referenced ids.</param>
        /// <returns>Reads the ids the caller sees, and whether the read is sensitive, into the save.</returns>
        private Action ComposeTargets(IDataBatch batch, ConnectedUser user, EntityMetadata target, HashSet<long> ids)
        {
            // --- The decision: nothing to load for a denied target ---
            TargetRead read = ReadOf(user, target);
            if (read.IsDenied)
            {
                return () =>
                {
                    _visible[target] = [];
                    _ = _sensitive.Remove(target);
                };
            }

            // --- The ids the target holds under the decision ---
            EntityDescriptor descriptor = _map.Descriptor(target);
            string key = descriptor.Key.Name;
            SqlIdentifier table = Type.GetTypeCode(Nullable.GetUnderlyingType(target.Key.ClrType) ?? target.Key.ClrType) == TypeCode.Int32
                ? KeyTables.Bind(batch, [.. ids.Select(static id => checked((int)id))])
                : KeyTables.Bind(batch, [.. ids]);
            BatchResult<RowPage> rows = batch.Rows(
                new QuerySpec { Root = descriptor, Select = key, Filter = read.Decision.Filter, Restrictions = [new KeySetRestriction(key, table.Name)] },
                arguments: null,
                options: null);
            bool sensitive = read.IsSensitive;
            return () =>
            {
                _visible[target] = Keys(rows.Value.Rows);
                _ = sensitive ? _sensitive.Add(target) : _sensitive.Remove(target);
            };
        }

        /// <summary>Every collection the payload carries on an update root, at any depth, dotted, parents first.</summary>
        /// <returns>The collections' paths.</returns>
        private List<string> PresentCollections()
        {
            List<string> paths = [];
            foreach (SaveRow root in _roots)
            {
                if (!root.IsNew)
                {
                    Present(root, prefix: string.Empty, paths);
                }
            }

            return paths;
        }

        /// <summary>Adds the collections a row carries, then those its children carry.</summary>
        /// <param name="owner">The row.</param>
        /// <param name="prefix">The dotted path to the row's collections.</param>
        /// <param name="paths">Receives the paths.</param>
        private static void Present(SaveRow owner, string prefix, List<string> paths)
        {
            foreach (ChildCollectionMetadata collection in owner.Entity.Children)
            {
                // A null collection is untouched, so neither it nor what it holds loads.
                if (collection.Getter(owner.Row) is null)
                {
                    continue;
                }

                string path = prefix + collection.Property;
                if (!paths.Contains(path, StringComparer.Ordinal))
                {
                    paths.Add(path);
                }

                foreach (SaveRow child in owner.Children)
                {
                    if (ReferenceEquals(child.Collection, collection))
                    {
                        Present(child, path + ".", paths);
                    }
                }
            }
        }

        /// <summary>The keys of a loaded key column.</summary>
        /// <param name="rows">The rows, the key first.</param>
        /// <returns>The keys.</returns>
        /// <exception cref="InvalidOperationException">The column holds neither int nor long keys.</exception>
        private static HashSet<long> Keys(QueryRowSet rows)
        {
            HashSet<long> keys = new(rows.RowCount);
            Array buffer = rows.GetBuffer(0);
            for (int row = 0; row < rows.RowCount; row++)
            {
                _ = keys.Add(buffer switch
                {
                    int[] narrow => narrow[row],
                    long[] wide => wide[row],
                    _ => throw new InvalidOperationException($"A key column loads as int or long values, not {buffer.GetType().Name}."),
                });
            }

            return keys;
        }
    }
}
