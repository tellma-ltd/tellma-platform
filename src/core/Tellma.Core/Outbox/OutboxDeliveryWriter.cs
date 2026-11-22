// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.TableTypes;
using Tellma.Core.Data.Batches;

namespace Tellma.Core.Outbox
{
    /// <summary>Applies one tenant's delivery events to its outgoing emails.</summary>
    internal interface IOutboxDeliveryWriter
    {
        /// <summary>Applies the events in one round trip on the tenant's database, outside any tenant scope.</summary>
        /// <param name="tenantId">The tenant.</param>
        /// <param name="events">The events, at most one per outgoing email; at least one.</param>
        /// <param name="cancellationToken">Cancels opening the database and the round trip.</param>
        /// <returns>The rows updated and the ids of the events' rows that do not exist.</returns>
        Task<DeliveryUpdateResult> ApplyAsync(int tenantId, IReadOnlyList<EmailDeliveryEventList> events, CancellationToken cancellationToken);
    }

    // Spec 0022 §6 step 3: the tenant's database is opened through ITenantDatabaseFactory with the
    // caller's own scope, so the update runs with no tenant scope and no principal, and the handle is
    // disposed afterwards. The update is one Maintenance statement in TransactionMode.None (spec 0022
    // §1.4), idempotent, so the executor may run it again after an ambiguous failure.
    /// <summary>Applies delivery events through a handle on the tenant's database, opened and disposed per call.</summary>
    /// <param name="databases">Opens a tenant's database outside any tenant scope.</param>
    /// <param name="services">The scope the handle's batches resolve their contributors and counters from.</param>
    internal sealed class OutboxDeliveryWriter(ITenantDatabaseFactory databases, IServiceProvider services) : IOutboxDeliveryWriter
    {
        /// <inheritdoc />
        public async Task<DeliveryUpdateResult> ApplyAsync(int tenantId, IReadOnlyList<EmailDeliveryEventList> events, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(events);

            ITenantDatabaseHandle handle = await databases.OpenAsync(tenantId, services, cancellationToken).ConfigureAwait(false);
            await using (handle.ConfigureAwait(false))
            {
                var batch = (DataBatch)handle.CreateBatch(BatchPurpose.Maintenance);
                batch.TransactionMode = TransactionMode.None;
                BatchResult<DeliveryUpdateResult> result = OutboxStatements.DeliveryUpdate(batch, events);
                _ = await batch.ExecuteAsync(cancellationToken).ConfigureAwait(false);
                return result.Value;
            }
        }
    }
}
