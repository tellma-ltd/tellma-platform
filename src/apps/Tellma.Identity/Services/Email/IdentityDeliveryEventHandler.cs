// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Diagnostics.CodeAnalysis;
using Tellma.Core.Abstractions.Email;
using Tellma.Identity.Data;
using Tellma.Identity.Data.Entities;

namespace Tellma.Identity.Services.Email
{
    /// <summary>
    ///     Records what a mail provider reports about the messages this engine sent.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Runs on the webhook request path, so it does indexed updates and nothing else.
    ///         Providers deliver events at least once and out of order, which decides the two rules
    ///         here: a repeat of the event already applied is ignored, and a report only replaces a
    ///         weaker one — a "delayed" arriving after a "bounced" is stale news, not an update.
    ///     </para>
    ///     <para>
    ///         Engagement reports are discarded on purpose rather than by omission. Both connectors
    ///         can translate them — SendGrid maps <c>open</c> and <c>click</c>, and the Azure
    ///         adapter maps engagement reports — so enabling tracking at the provider would start
    ///         delivering them here. Identity keeps no record of who opened its mail, and a click
    ///         would be a poor signal anyway: corporate link scanners follow addresses in mail, so
    ///         the click may well be a machine.
    ///     </para>
    /// </remarks>
    /// <param name="store">The identity store.</param>
    /// <param name="timeProvider">The clock.</param>
    /// <param name="logger">Diagnostics.</param>
    [SuppressMessage(
        "Naming",
        "CA1711:Identifiers should not have incorrect suffix",
        Justification = "This handles delivery events in the domain sense, matching the platform contract it implements; it is not a System.EventHandler delegate.")]
    public sealed class IdentityDeliveryEventHandler(
        TellmaIdentityDbContext store,
        TimeProvider timeProvider,
        ILogger<IdentityDeliveryEventHandler> logger) : IEmailDeliveryEventHandler
    {
        /// <inheritdoc />
        public string OwnerKey => IdentityEmailCorrelation.OwnerKey;

        /// <inheritdoc />
        public async Task HandleAsync(
            IReadOnlyList<EmailDeliveryEvent> events, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(events);

            foreach (EmailDeliveryEvent @event in events)
            {
                if (Classify(@event.Type) is not { } status)
                {
                    // Engagement, or something the platform could not classify at all. Neither
                    // says anything about delivery, so neither touches the row.
                    continue;
                }

                await ApplyAsync(@event, status, cancellationToken);
            }
        }

        /// <summary>
        ///     Maps a platform event to what it says about delivery, or null when it says nothing.
        /// </summary>
        private static EmailDeliveryStatus? Classify(EmailDeliveryEventType type)
        {
            return type switch
            {
                EmailDeliveryEventType.Delivered => EmailDeliveryStatus.Delivered,
                EmailDeliveryEventType.Deferred => EmailDeliveryStatus.Deferred,
                EmailDeliveryEventType.Bounced => EmailDeliveryStatus.Bounced,
                EmailDeliveryEventType.Dropped => EmailDeliveryStatus.Dropped,
                EmailDeliveryEventType.Failed => EmailDeliveryStatus.Failed,
                EmailDeliveryEventType.SpamReported => EmailDeliveryStatus.SpamReported,
                EmailDeliveryEventType.Other => EmailDeliveryStatus.Other,

                // Named rather than left to a discard, so that enabling engagement tracking at a
                // provider starts delivering these to a branch that already decided to drop them
                // instead of to a fallback that would look like an oversight.
                EmailDeliveryEventType.Opened or EmailDeliveryEventType.Clicked => null,
                _ => null,
            };
        }

        /// <summary>Applies one classified event to the row its correlation names.</summary>
        private async Task ApplyAsync(
            EmailDeliveryEvent @event, EmailDeliveryStatus status, CancellationToken cancellationToken)
        {
            string reference = @event.Correlation!.Reference;

            // Conditional in the statement rather than read-then-write: the webhook path can be
            // concurrent with itself, and two events for one message must not be able to interleave
            // into the weaker one winning. A repeat of the event already recorded matches nothing.
            int applied = await store.Set<SingleUseCode>()
                .Where(c => c.Id == reference)
                .Where(c => c.LastProviderEventId != @event.ProviderEventId)
                .Where(c => c.DeliveryStatus == null || c.DeliveryStatus < status)
                .ExecuteUpdateAsync(
                    setters => setters
                        .SetProperty(static c => c.DeliveryStatus, status)
                        .SetProperty(static c => c.DeliveryUpdatedUtc, timeProvider.GetUtcNow())
                        .SetProperty(static c => c.DeliveryReason, Truncate(@event.Reason))
                        .SetProperty(static c => c.LastProviderEventId, @event.ProviderEventId),
                    cancellationToken);

            if (applied == 0)
            {
                // Either a duplicate, or news older than what the row already holds, or a
                // correlation that outlived its record. Only the last is a defect, and it is not
                // worth a second query to tell them apart on this path.
                IdentityDeliveryEventLog.NotApplied(logger, reference, @event.RawType);
            }
        }

        /// <summary>Caps a provider's reason text to what the column holds.</summary>
        private static string? Truncate(string? reason)
        {
            const int Max = 512;
            return reason is null || reason.Length <= Max ? reason : reason[..Max];
        }
    }

    /// <summary>Source-generated log messages for <see cref="IdentityDeliveryEventHandler" />.</summary>
    internal static partial class IdentityDeliveryEventLog
    {
        /// <summary>An event that changed nothing.</summary>
        /// <param name="logger">The logger.</param>
        /// <param name="reference">The correlation's reference.</param>
        /// <param name="rawType">The provider's own event name.</param>
        [LoggerMessage(
            Level = LogLevel.Debug,
            Message = "Delivery event {RawType} for {Reference} changed nothing: duplicate, stale, or no such row.")]
        public static partial void NotApplied(ILogger logger, string reference, string rawType);
    }
}
