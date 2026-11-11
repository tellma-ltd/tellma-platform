// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Tenancy;

namespace Tellma.Core.Tests.Infrastructure
{
    /// <summary>
    ///     A catalog the suite writes directly: its version, its rows and its migration state, with
    ///     counts of every read, an optional failure and an optional gate that holds every read until
    ///     the test opens it.
    /// </summary>
    internal sealed class FakeCatalogReader : ICatalogReader
    {
        private readonly Lock _sync = new();
        private readonly List<TenantRow> _rows = [];
        private int _probes;
        private int _reloads;
        private int _historyReads;

        /// <summary>The server every row names unless it says otherwise; allowed by the sound configuration.</summary>
        public const string Server = "sql01.database.windows.net";

        /// <summary>The catalog's version.</summary>
        public Guid Version { get; set; } = Guid.NewGuid();

        /// <summary>The catalog's migration state.</summary>
        public CatalogHistoryState History { get; set; } = CatalogHistoryState.Current;

        /// <summary>When set, every read throws it.</summary>
        public Exception? Failure { get; set; }

        /// <summary>When set, every read waits for it before answering.</summary>
        public TaskCompletionSource? Gate { get; set; }

        /// <summary>Completed when a read is waiting at the gate.</summary>
        public TaskCompletionSource AtGate { get; private set; } = NewSignal();

        /// <summary>How many probes ran.</summary>
        public int Probes => Volatile.Read(ref _probes);

        /// <summary>How many reloads ran.</summary>
        public int Reloads => Volatile.Read(ref _reloads);

        /// <summary>How many migration-state reads ran.</summary>
        public int HistoryReads => Volatile.Read(ref _historyReads);

        /// <summary>A row with the given parts and everything else sound.</summary>
        /// <param name="id">The id.</param>
        /// <param name="state">The stored state.</param>
        /// <param name="name">The first name.</param>
        /// <param name="server">The server.</param>
        /// <param name="database">The database; <c>tenant-&lt;id&gt;</c> when null.</param>
        /// <param name="profile">The profile.</param>
        /// <param name="category">The stored category.</param>
        /// <param name="version">The row version; a fresh one when null.</param>
        /// <returns>The row.</returns>
        public static TenantRow Row(
            int id,
            string state = "Active",
            string? name = null,
            string server = Server,
            string? database = null,
            string profile = "default",
            string category = "Live",
            Guid? version = null)
        {
            return new TenantRow(
                id,
                name ?? $"Tenant {id}",
                null,
                null,
                "en",
                null,
                null,
                category,
                null,
                state,
                server,
                database ?? $"tenant-{id}",
                profile,
                null,
                version ?? Guid.NewGuid());
        }

        /// <summary>Replaces the rows and moves the version, as a catalog write does.</summary>
        /// <param name="rows">The rows.</param>
        public void Write(params TenantRow[] rows)
        {
            lock (_sync)
            {
                _rows.Clear();
                _rows.AddRange(rows);
                Version = Guid.NewGuid();
            }
        }

        /// <summary>Replaces one row, giving it a fresh row version, and moves the catalog version.</summary>
        /// <param name="row">The row, matched by id.</param>
        public void Update(TenantRow row)
        {
            ArgumentNullException.ThrowIfNull(row);
            lock (_sync)
            {
                int index = _rows.FindIndex(existing => existing.Id == row.Id);
                TenantRow updated = row with { Version = Guid.NewGuid() };
                if (index < 0)
                {
                    _rows.Add(updated);
                }
                else
                {
                    _rows[index] = updated;
                }

                Version = Guid.NewGuid();
            }
        }

        /// <summary>Deletes one row and moves the catalog version.</summary>
        /// <param name="tenantId">The row's id.</param>
        public void Remove(int tenantId)
        {
            lock (_sync)
            {
                _ = _rows.RemoveAll(row => row.Id == tenantId);
                Version = Guid.NewGuid();
            }
        }

        /// <inheritdoc />
        public async Task<CatalogHistoryState> ReadHistoryAsync(CancellationToken cancellationToken)
        {
            _ = Interlocked.Increment(ref _historyReads);
            await PassAsync(cancellationToken);
            return History;
        }

        /// <inheritdoc />
        public async Task<Guid> ReadVersionAsync(CancellationToken cancellationToken)
        {
            _ = Interlocked.Increment(ref _probes);
            await PassAsync(cancellationToken);
            lock (_sync)
            {
                return Version;
            }
        }

        /// <inheritdoc />
        public async Task<CatalogReload> ReadTenantsAsync(CancellationToken cancellationToken)
        {
            _ = Interlocked.Increment(ref _reloads);
            await PassAsync(cancellationToken);
            lock (_sync)
            {
                return new CatalogReload(Version, [.. _rows]);
            }
        }

        /// <summary>A signal whose continuations run asynchronously.</summary>
        /// <returns>The signal.</returns>
        private static TaskCompletionSource NewSignal()
        {
            return new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        /// <summary>Waits at the gate when there is one, then throws the failure when there is one.</summary>
        /// <param name="cancellationToken">Cancels the wait.</param>
        /// <returns>A task that completes when the read may answer.</returns>
        private async Task PassAsync(CancellationToken cancellationToken)
        {
            if (Gate is TaskCompletionSource gate)
            {
                _ = AtGate.TrySetResult();
                await gate.Task.WaitAsync(cancellationToken);
                AtGate = NewSignal();
            }

            if (Failure is Exception failure)
            {
                throw failure;
            }
        }
    }
}
