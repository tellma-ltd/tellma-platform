// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Webhooks;

namespace Tellma.Core.Abstractions.Email
{
    /// <summary>
    ///     Routes delivery events from connector adapters to the handler owning each event's
    ///     underlying state. Adapters call this from their webhook receivers after signature
    ///     verification and translation; they never invoke handlers directly.
    /// </summary>
    /// <remarks>
    ///     The implementation routes by <see cref="EmailCorrelation.OwnerKey" /> and meters events
    ///     with no or unrecognized correlation. Dispatch may be synchronous — handler work is bounded
    ///     by contract — so a handler failure surfaces as
    ///     <see cref="WebhookOutcome.TransientFailure" /> on the webhook result and the provider's
    ///     redelivery becomes the retry.
    /// </remarks>
    public interface IEmailDeliveryEventDispatcher
    {
        /// <summary>Routes a batch of events to their owning handlers.</summary>
        /// <param name="transport">The configuration name of the transport whose receiver produced
        ///     these events ("sendgrid", "acs-email"). Carried because a receiver stays active while
        ///     a deployment migrates away from its transport, so the events being drained are not
        ///     necessarily the active transport's — telemetry that assumed otherwise would mislabel
        ///     exactly the case worth watching.</param>
        /// <param name="events">The translated events, in provider-reported order.</param>
        /// <param name="cancellationToken">Abandons the dispatch.</param>
        /// <returns>A task that completes when every owning handler has run.</returns>
        Task DispatchAsync(
            string transport,
            IReadOnlyList<EmailDeliveryEvent> events,
            CancellationToken cancellationToken);
    }
}
