// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Text.Json;
using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Crud;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Jobs;
using Tellma.Core.Abstractions.Localization;
using Tellma.Core.Abstractions.Notifications;
using Tellma.Core.Abstractions.Settings;
using Tellma.Core.Abstractions.Tenancy;
using Tellma.Core.Data.Batches;

namespace Tellma.Core.Notifications
{
    // The handler is spec 0021 §4.5: load every slice in one read round trip, resolve each
    // channel's recipients in registration order, deliver on the job frame's write host, and persist
    // once, so every channel's enlisted rows commit with the completion. Items are left unmarked: an
    // exception from a channel or from the persist ends the invocation, and spec 0020 §3.4 decides
    // every item's outcome from it.
    /// <summary>
    ///     Delivers the slices of notification requests through every registered channel other than the
    ///     inbox, to the recipients who have the channel on for the type.
    /// </summary>
    /// <param name="runner">Runs the load behind the connect prologue.</param>
    /// <param name="channels">The registered channels that have a sender, in registration order.</param>
    /// <param name="senders">The scope's channel senders.</param>
    /// <param name="negotiator">Negotiates each recipient's localization.</param>
    /// <param name="accessor">The job scope's request context, which holds the tenant's settings.</param>
    /// <param name="settings">The tenant's settings, when the scope's context holds none.</param>
    /// <param name="host">The job frame's write host, which the channels enlist their rows with.</param>
    [JobHandler(NotificationStatements.DeliveryHandlerKey, BatchSize = 4, MaxAttempts = 12, InboxOnlyFailureNotices = true, RunsWhenReadOnly = true)]
    internal sealed class NotificationDeliveryHandler(
        IGuardedBatchRunner runner,
        NotificationDeliveryChannels channels,
        IEnumerable<INotificationChannel> senders,
        ILocalizationNegotiator negotiator,
        IRequestContextAccessor accessor,
        ITenantSettingsCache settings,
        IOpenWriteHost host) : IJobHandler
    {
        /// <inheritdoc />
        public async Task ExecuteAsync(JobBatch<Job> batch)
        {
            ArgumentNullException.ThrowIfNull(batch);

            // --- The arguments of every slice, read before anything runs ---
            NotificationDeliveryArguments[] slices = [.. batch.Items.Select(static item => item.Arguments<NotificationDeliveryArguments>())];

            // --- Load: one read round trip, one statement per slice ---
            var loads = new BatchResult<DeliverySlice>[slices.Length];
            DeliverySlice[] loaded = await runner.RunAsync(
                BatchPurpose.Read,
                (_, data) =>
                {
                    var internalBatch = (DataBatch)data;
                    for (int index = 0; index < slices.Length; index++)
                    {
                        loads[index] = NotificationDeliveryStatements.Load(internalBatch, slices[index].RecipientIds, slices[index].Type);
                    }

                    return ValueTask.CompletedTask;
                },
                (_, _) => loads.Select(static load => load.Value).ToArray(),
                CancellationToken.None).ConfigureAwait(false);

            // --- The tenant's settings, which every recipient's negotiation falls back on ---
            TenantSettings tenant = accessor.Current.TenantSettings
                ?? (await settings.GetAsync().ConfigureAwait(false)).Value
                ?? throw new InvalidOperationException("The tenant settings cache served no settings.");

            // --- Resolve and deliver: each slice, each channel with a sender in registration order ---
            var byKey = senders.ToDictionary(static sender => sender.Key, StringComparer.Ordinal);
            for (int index = 0; index < slices.Length; index++)
            {
                NotificationDeliveryArguments slice = slices[index];
                JobItem<Job> item = batch.Items[index];
                NotificationRequest request = Request(slice);
                foreach ((NotificationChannelDescriptor channel, IReadOnlyList<NotificationRecipient> recipients) in Resolve(slice, loaded[index], channels.Channels, tenant))
                {
                    if (recipients.Count > 0 && byKey.TryGetValue(channel.Key, out INotificationChannel? sender))
                    {
                        await sender.DeliverAsync(host, request, recipients, item.CancellationToken).ConfigureAwait(false);
                    }
                }
            }

            // --- Persist: once, so every channel's enlisted rows commit with the completion ---
            _ = await host.PersistAsync(CancellationToken.None).ConfigureAwait(false);
        }

        /// <summary>
        ///     Lists, for each channel in order, the slice's active recipients in the slice's order whose
        ///     preference row for the channel, else the channel's default, is on.
        /// </summary>
        /// <param name="slice">The slice's arguments.</param>
        /// <param name="loaded">What the load read of the slice.</param>
        /// <param name="channels">The channels with a sender, in registration order.</param>
        /// <param name="tenant">The tenant's settings.</param>
        /// <returns>Each channel with its recipients, possibly none, in registration order.</returns>
        private IEnumerable<(NotificationChannelDescriptor Channel, IReadOnlyList<NotificationRecipient> Recipients)> Resolve(
            NotificationDeliveryArguments slice, DeliverySlice loaded, IReadOnlyList<NotificationChannelDescriptor> channels, TenantSettings tenant)
        {
            // --- The active users in the slice's order, each negotiated once ---
            var users = loaded.Users.ToDictionary(static user => user.Id);
            List<NotificationRecipient> ordered = [];
            HashSet<int> listed = [];
            foreach (int id in slice.RecipientIds)
            {
                if (listed.Add(id) && users.TryGetValue(id, out DeliveryUser? user))
                {
                    ordered.Add(Recipient(user, tenant));
                }
            }

            // --- The stored preferences by user and channel ---
            var stored = loaded.Preferences
                .ToDictionary(static row => (row.UserId, row.Channel), static row => row.Enabled);

            foreach (NotificationChannelDescriptor channel in channels)
            {
                NotificationRecipient[] enabled = [.. ordered.Where(recipient => stored.TryGetValue((recipient.UserId, channel.Key), out bool on) ? on : channel.DefaultEnabled)];
                yield return (channel, enabled);
            }
        }

        /// <summary>
        ///     A recipient with their localization: their preferred language, calendar, time zone and
        ///     region over the tenant's, and no request headers, as for a background scope's user.
        /// </summary>
        /// <param name="user">The user as the load read them.</param>
        /// <param name="tenant">The tenant's settings.</param>
        /// <returns>The recipient.</returns>
        private NotificationRecipient Recipient(DeliveryUser user, TenantSettings tenant)
        {
            LocalizationContext localization = negotiator.Negotiate(new NegotiationInput(
                AcceptLanguage: null,
                CalendarHeader: null,
                TimeZoneHeader: null,
                user.PreferredLanguage,
                user.PreferredCalendar,
                user.PreferredTimeZone,
                user.PreferredRegion,
                MessageLanguage: null,
                MessageCalendar: null,
                tenant));
            return new NotificationRecipient(user.Id, user.Name, user.Email, user.ContactEmail, user.ContactMobile, localization);
        }

        /// <summary>
        ///     The request a channel receives, rebuilt from the slice: the slice's recipients, the
        ///     resolved severity, the stored arguments as JSON, and no dedup key.
        /// </summary>
        /// <param name="slice">The slice's arguments.</param>
        /// <returns>The request.</returns>
        private static NotificationRequest Request(NotificationDeliveryArguments slice)
        {
            object? arguments = null;
            if (slice.ArgumentsJson is string json)
            {
                using var document = JsonDocument.Parse(json);
                arguments = document.RootElement.Clone();
            }

            return new NotificationRequest(
                slice.Type,
                slice.RecipientIds,
                arguments,
                slice.TargetResource,
                slice.TargetId,
                slice.ActorUserId,
                DedupKey: null,
                Severity: slice.Severity);
        }
    }
}
