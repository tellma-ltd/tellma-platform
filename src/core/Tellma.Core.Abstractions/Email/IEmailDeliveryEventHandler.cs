// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Diagnostics.CodeAnalysis;

namespace Tellma.Core.Abstractions.Email
{
    /// <summary>
    ///     Consumes delivery events for correlations this owner minted, updating the owner's own
    ///     records (outbox rows, invitation records).
    /// </summary>
    /// <remarks>
    ///     Handlers receive only events whose owner key matches; an event whose
    ///     <see cref="EmailCorrelation.Reference" /> resolves to no row is an anomaly worth logging,
    ///     not ignoring. Handlers must deduplicate on <see cref="EmailDeliveryEvent.ProviderEventId" />
    ///     and must be cheap — indexed updates only; anything heavier is enqueued as background work
    ///     by the handler itself, because the dispatcher runs on the webhook request path.
    /// </remarks>
    [SuppressMessage(
        "Naming",
        "CA1711:Identifiers should not have incorrect suffix",
        Justification = "This is an event handler in the domain sense — it consumes delivery events pushed by a provider — not a System.EventHandler delegate. Any other name would obscure what it does.")]
    public interface IEmailDeliveryEventHandler
    {
        /// <summary>
        ///     The owner-key segment this handler owns; matches <see cref="EmailCorrelation.OwnerKey" />
        ///     and is unique across the composition, validated at startup.
        /// </summary>
        string OwnerKey { get; }

        /// <summary>Handles a batch of events belonging to this owner.</summary>
        /// <param name="events">The events, in provider-reported order.</param>
        /// <param name="cancellationToken">Abandons the work.</param>
        /// <returns>A task that completes when the owner's records reflect the batch.</returns>
        Task HandleAsync(
            IReadOnlyList<EmailDeliveryEvent> events,
            CancellationToken cancellationToken);
    }
}
