// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Globalization;
using Tellma.Core.Abstractions.Email;
using Tellma.Core.Abstractions.TableTypes;

namespace Tellma.Core.Outbox
{
    // Spec 0022 §6 step 1: open, click and unclassified events say nothing about delivery (corporate
    // link scanners open mail), so they are dropped; the rest are grouped by the correlation's tenant,
    // and each row keeps only its strongest event: the highest rank, then the latest time, then the
    // last the provider reported. An event whose correlation names no tenant, or whose reference is
    // not an email's id, can match no row and is set apart as unmatched.
    /// <summary>Chooses, from one webhook call's events, the one event per outgoing email the delivery update applies.</summary>
    internal static class DeliveryEventSelection
    {
        /// <summary>The delivery status an event reports, or null for one that says nothing about delivery.</summary>
        /// <param name="type">The event's classification.</param>
        /// <returns>The status; null for an open, a click or an unclassified event.</returns>
        public static EmailDeliveryStatus? StatusOf(EmailDeliveryEventType type)
        {
            return type switch
            {
                EmailDeliveryEventType.Deferred => EmailDeliveryStatus.Deferred,
                EmailDeliveryEventType.Delivered => EmailDeliveryStatus.Delivered,
                EmailDeliveryEventType.Dropped => EmailDeliveryStatus.Dropped,
                EmailDeliveryEventType.Failed => EmailDeliveryStatus.Failed,
                EmailDeliveryEventType.Bounced => EmailDeliveryStatus.Bounced,
                EmailDeliveryEventType.SpamReported => EmailDeliveryStatus.SpamReported,
                EmailDeliveryEventType.Opened or EmailDeliveryEventType.Clicked or EmailDeliveryEventType.Other => null,
                _ => null,
            };
        }

        /// <summary>Selects the strongest event of each outgoing email, grouped by tenant.</summary>
        /// <param name="events">The events, in the order the provider reported them.</param>
        /// <returns>The selection.</returns>
        public static DeliverySelection Select(IReadOnlyList<EmailDeliveryEvent> events)
        {
            ArgumentNullException.ThrowIfNull(events);

            // --- Each classified event, by tenant and row, the strongest kept ---
            SortedDictionary<int, (Dictionary<int, Candidate> Rows, int Weighed)> tenants = [];
            List<UnaddressedEvent> unaddressed = [];
            int dropped = 0;
            for (int position = 0; position < events.Count; position++)
            {
                EmailDeliveryEvent @event = events[position];
                if (StatusOf(@event.Type) is not EmailDeliveryStatus status)
                {
                    dropped++;
                    continue;
                }

                // An event that names no tenant, or no email id, matches no row.
                EmailCorrelation? correlation = @event.Correlation;
                if (correlation?.TenantId is not int tenantId || !TryParseId(correlation.Reference, out int rowId))
                {
                    unaddressed.Add(new UnaddressedEvent(correlation?.TenantId, correlation?.Reference ?? string.Empty));
                    continue;
                }

                if (!tenants.TryGetValue(tenantId, out (Dictionary<int, Candidate> Rows, int Weighed) tenant))
                {
                    tenant = ([], 0);
                }

                Candidate candidate = new(@event, status, position);
                tenant.Rows[rowId] = tenant.Rows.TryGetValue(rowId, out Candidate? kept) && !Stronger(candidate, kept) ? kept : candidate;
                tenants[tenantId] = (tenant.Rows, tenant.Weighed + 1);
            }

            // --- Each tenant's rows, in row order, shaped for the update's table-valued parameter ---
            List<TenantDeliveryEvents> selected = [];
            foreach ((int tenantId, (Dictionary<int, Candidate> rows, int weighed)) in tenants)
            {
                List<EmailDeliveryEventList> list = new(rows.Count);
                foreach ((int rowId, Candidate candidate) in rows.OrderBy(static pair => pair.Key))
                {
                    list.Add(Row(list.Count, rowId, candidate));
                }

                selected.Add(new TenantDeliveryEvents(tenantId, list, weighed));
            }

            return new DeliverySelection(selected, unaddressed, dropped);
        }

        /// <summary>Whether an event beats the one kept for its row: a higher rank, then a later time, then a later report.</summary>
        /// <param name="candidate">The event.</param>
        /// <param name="kept">The event kept so far.</param>
        /// <returns>True when the event replaces the kept one.</returns>
        private static bool Stronger(Candidate candidate, Candidate kept)
        {
            if (candidate.Status != kept.Status)
            {
                return candidate.Status > kept.Status;
            }

            // A missing time is earlier than any time; between equal times the later report wins.
            DateTimeOffset at = candidate.Event.Timestamp ?? DateTimeOffset.MinValue;
            DateTimeOffset keptAt = kept.Event.Timestamp ?? DateTimeOffset.MinValue;
            return at != keptAt ? at > keptAt : candidate.Position > kept.Position;
        }

        /// <summary>Reads an email's id from a correlation's reference: a positive integer in invariant digits.</summary>
        /// <param name="reference">The reference.</param>
        /// <param name="id">The id; 0 when the reference is not one.</param>
        /// <returns>True when the reference is an id.</returns>
        private static bool TryParseId(string reference, out int id)
        {
            return int.TryParse(reference, NumberStyles.None, CultureInfo.InvariantCulture, out id) && id > 0;
        }

        /// <summary>Shapes the event kept for a row, each text cut to its column's width.</summary>
        /// <param name="ordinal">The row's position in the parameter.</param>
        /// <param name="rowId">The outgoing email's id.</param>
        /// <param name="candidate">The event.</param>
        /// <returns>The parameter's row.</returns>
        private static EmailDeliveryEventList Row(int ordinal, int rowId, Candidate candidate)
        {
            EmailDeliveryEvent @event = candidate.Event;
            return new EmailDeliveryEventList
            {
                Ordinal = ordinal,
                OutgoingEmailId = rowId,
                Status = candidate.Status,
                Rank = (byte)candidate.Status,
                Recipient = Cut(@event.Recipient, OutgoingEmail.DeliveryRecipientLength),
                Reason = Cut(@event.Reason, OutgoingEmail.DeliveryReasonLength),
                Timestamp = @event.Timestamp,
                ProviderEventId = Cut(@event.ProviderEventId, OutgoingEmail.ProviderEventIdLength)!,
            };
        }

        /// <summary>Cuts a provider's text to a column's width, so a long value never fails the update.</summary>
        /// <param name="text">The text; null for none.</param>
        /// <param name="length">The column's width.</param>
        /// <returns>The text, at most that long.</returns>
        private static string? Cut(string? text, int length)
        {
            return text is null || text.Length <= length ? text : text[..length];
        }

        /// <summary>An event kept for its row so far.</summary>
        /// <param name="Event">The event.</param>
        /// <param name="Status">The status it reports.</param>
        /// <param name="Position">Its position in the call.</param>
        private sealed record Candidate(EmailDeliveryEvent Event, EmailDeliveryStatus Status, int Position);
    }

    /// <summary>The events one webhook call's delivery update applies, by tenant, and those it cannot.</summary>
    /// <param name="Tenants">Each tenant's strongest event per outgoing email, in tenant order.</param>
    /// <param name="Unaddressed">The classified events whose correlation names no tenant or no email id.</param>
    /// <param name="Dropped">The open, click and unclassified events.</param>
    internal sealed record DeliverySelection(IReadOnlyList<TenantDeliveryEvents> Tenants, IReadOnlyList<UnaddressedEvent> Unaddressed, int Dropped);

    /// <summary>One tenant's events.</summary>
    /// <param name="TenantId">The tenant.</param>
    /// <param name="Rows">The strongest event of each outgoing email, in id order, numbered from 0.</param>
    /// <param name="Weighed">The tenant's classified events, those a stronger one of the same email replaced included.</param>
    internal sealed record TenantDeliveryEvents(int TenantId, IReadOnlyList<EmailDeliveryEventList> Rows, int Weighed);

    /// <summary>A classified event that names no outgoing email.</summary>
    /// <param name="TenantId">The tenant its correlation names; null for none.</param>
    /// <param name="Reference">Its correlation's reference; empty when it has no correlation.</param>
    internal sealed record UnaddressedEvent(int? TenantId, string Reference);
}
