// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.Logging;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Runtime.ExceptionServices;
using Tellma.Core.Abstractions.Email;
using Tellma.Core.Abstractions.Tenancy;
using Tellma.Core.Tenancy;

namespace Tellma.Core.Outbox
{
    // Spec 0022 §6: the outbox's delivery events, on spec 0007's tenantless webhook path. Each tenant
    // is looked up in the registry: a missing, provisioning or retired tenant has no email to update,
    // so its events are dropped with a warning; an active, read-only or suspended tenant's are applied,
    // since the update is bookkeeping and a soft lock or a suspension outlasts the provider's
    // redelivery window; a frozen tenant's database refuses writes, so its events wait for
    // redelivery, and the call fails once every other tenant's were applied, which spec 0007's
    // receivers answer 503. A tenant whose update fails does not keep the others' from being applied:
    // the failures are raised together at the end, and a redelivery skips what was applied.
    /// <summary>
    ///     Records on each outgoing email what its provider reports of its delivery: the strongest
    ///     status so far, when it happened, the address it concerned and the provider's reason.
    /// </summary>
    /// <remarks>
    ///     A row only moves to a stronger status, or to a later event of the same one, and an event
    ///     already applied is skipped, so the provider's repeats and its out-of-order delivery leave the
    ///     strongest status once. Opens and clicks are dropped: link scanners open mail, so neither
    ///     tells whether a person read it.
    /// </remarks>
    /// <param name="registry">The tenants' snapshot, which tells each tenant's state.</param>
    /// <param name="writer">Applies one tenant's events.</param>
    /// <param name="metrics">The outbox's instruments.</param>
    /// <param name="loggers">Creates the outbox's logger.</param>
    [SuppressMessage(
        "Naming",
        "CA1711:Identifiers should not have incorrect suffix",
        Justification = "This handles delivery events in the domain sense, matching the platform contract it implements; it is not a System.EventHandler delegate.")]
    internal sealed class OutgoingEmailDeliveryHandler(
        ITenantRegistry registry,
        IOutboxDeliveryWriter writer,
        OutboxMetrics metrics,
        ILoggerFactory loggers) : IEmailDeliveryEventHandler
    {
        /// <summary>The state a log names for a tenant the registry does not hold.</summary>
        private const string MissingState = "Missing";

        /// <summary>Reports the unmatched events, the skipped tenants and the stale events.</summary>
        private readonly ILogger _logger = loggers.CreateLogger(OutboxLog.Category);

        /// <inheritdoc />
        public string OwnerKey => OutgoingEmailHandler.CorrelationOwnerKey;

        /// <inheritdoc />
        /// <exception cref="TenantUnavailableException">
        ///     A tenant is frozen, so its events wait for redelivery, or the tenants' snapshot is
        ///     unavailable.
        /// </exception>
        public async Task HandleAsync(IReadOnlyList<EmailDeliveryEvent> events, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(events);

            // --- Step 1: the strongest event per row, by tenant; the unaddressed ones match nothing ---
            DeliverySelection selection = DeliveryEventSelection.Select(events);
            foreach (UnaddressedEvent unaddressed in selection.Unaddressed)
            {
                OutboxLog.OutboxDeliveryEventUnmatched(_logger, unaddressed.TenantId, unaddressed.Reference);
            }

            metrics.RecordDeliveryUpdates(OutboxMetrics.UnmatchedResult, selection.Unaddressed.Count);

            // --- Steps 2 and 3: each tenant by its state; the frozen ones and the failures last ---
            List<int> frozen = [];
            List<ExceptionDispatchInfo> failures = [];
            foreach (TenantDeliveryEvents tenant in selection.Tenants)
            {
                TenantState? state = registry.Find(tenant.TenantId)?.Descriptor.State;
                switch (state)
                {
                    case null or TenantState.Provisioning or TenantState.Retired:
                        OutboxLog.OutboxDeliveryTenantSkipped(_logger, tenant.TenantId, state?.ToString() ?? MissingState, tenant.Weighed);
                        metrics.RecordDeliveryUpdates(OutboxMetrics.TenantSkippedResult, tenant.Weighed);
                        break;

                    case TenantState.Frozen:
                        OutboxLog.OutboxDeliveryTenantFrozen(_logger, tenant.TenantId, tenant.Weighed);
                        frozen.Add(tenant.TenantId);
                        break;

                    case TenantState.Active or TenantState.ReadOnly or TenantState.Suspended:
                    default:
                        try
                        {
                            await ApplyAsync(tenant, cancellationToken).ConfigureAwait(false);
                        }
                        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
                        {
                            failures.Add(ExceptionDispatchInfo.Capture(exception));
                        }

                        break;
                }
            }

            // --- What waits for redelivery: one failure as it was, several together, else the freeze ---
            Exception? freeze = frozen.Count > 0 ? new TenantUnavailableException(frozen[0], TenantState.Frozen, TenantProblemCodes.Frozen, retryAfter: null) : null;
            if (failures.Count == 1 && freeze is null)
            {
                failures[0].Throw();
            }

            if (failures.Count > 0)
            {
                throw new AggregateException(
                    "The delivery events of some tenants could not be applied, and wait for the provider to deliver them again.",
                    [.. failures.Select(static failure => failure.SourceException), .. freeze is null ? Array.Empty<Exception>() : [freeze]]);
            }

            if (freeze is not null)
            {
                throw freeze;
            }
        }

        /// <summary>Applies one tenant's events and counts what became of each.</summary>
        /// <param name="tenant">The tenant's events.</param>
        /// <param name="cancellationToken">Cancels the update.</param>
        /// <returns>A task that completes when the events are applied and counted.</returns>
        private async Task ApplyAsync(TenantDeliveryEvents tenant, CancellationToken cancellationToken)
        {
            DeliveryUpdateResult result = await writer.ApplyAsync(tenant.TenantId, tenant.Rows, cancellationToken).ConfigureAwait(false);

            // --- Each unmatched reference, logged; the rest were repeats or no stronger ---
            HashSet<int> unmatched = [.. result.Unmatched];
            foreach (int rowId in unmatched.Order())
            {
                OutboxLog.OutboxDeliveryEventUnmatched(_logger, tenant.TenantId, rowId.ToString(CultureInfo.InvariantCulture));
            }

            int stale = Math.Max(0, tenant.Weighed - result.Applied - unmatched.Count);
            if (stale > 0)
            {
                OutboxLog.OutboxDeliveryEventsStale(_logger, tenant.TenantId, stale);
            }

            metrics.RecordDeliveryUpdates(OutboxMetrics.AppliedResult, result.Applied);
            metrics.RecordDeliveryUpdates(OutboxMetrics.UnmatchedResult, unmatched.Count);
            metrics.RecordDeliveryUpdates(OutboxMetrics.StaleResult, stale);
        }
    }
}
