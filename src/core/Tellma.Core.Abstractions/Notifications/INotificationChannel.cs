// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Composition;
using Tellma.Core.Abstractions.Crud;

namespace Tellma.Core.Abstractions.Notifications
{
    /// <summary>
    ///     The sender of a notification channel other than <c>inbox</c>: how a notification reaches its
    ///     recipients through the channel's transport. A scoped service, registered by the feature that
    ///     registered the channel; at most one per channel.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The startup gate refuses a sender whose <see cref="Key" /> names no registered channel,
    ///         names <c>inbox</c>, or names a channel another sender already serves. A registered
    ///         channel with no sender keeps its users' preferences and delivers nothing.
    ///     </para>
    ///     <para>
    ///         Channels have no duplicate detection: the inbox's duplicate rules govern inbox rows only,
    ///         so a channel delivers every raise its recipients enabled it for.
    ///     </para>
    /// </remarks>
    [CollectionService]
    public interface INotificationChannel
    {
        /// <summary>The key of the registered channel this sender serves.</summary>
        string Key { get; }

        /// <summary>
        ///     Delivers one slice of one request to the recipients who enabled the channel for its type.
        ///     Called by the delivery job, outside the transaction that raised the notification.
        /// </summary>
        /// <remarks>
        ///     A channel that writes rows enlists them with <paramref name="host" />, so they commit with
        ///     the delivery job's completion and a re-run of the job finds nothing written. A channel that
        ///     calls an external API sends from here, so a crash between the send and the completion
        ///     re-runs the job and can send twice; a channel that needs fail-closed delivery writes its
        ///     own outbox row instead. An exception fails the job's attempt and never reaches the
        ///     operation that raised the notification.
        /// </remarks>
        /// <param name="host">The delivery job's frame, which a channel's enlisted rows commit with.</param>
        /// <param name="request">
        ///     The request, rebuilt for this slice: its recipients are the slice's, its severity is
        ///     resolved, its arguments are the stored JSON as a <c>JsonElement</c> (or null), and it has
        ///     no dedup key.
        /// </param>
        /// <param name="recipients">The slice's active recipients who enabled the channel for the type, in order, each with the context their text renders in.</param>
        /// <param name="cancellationToken">Cancels the delivery when the job stops.</param>
        /// <returns>A task that completes once the delivery is sent or enlisted.</returns>
        Task DeliverAsync(
            IOpenWriteHost host,
            NotificationRequest request,
            IReadOnlyList<NotificationRecipient> recipients,
            CancellationToken cancellationToken = default);

        /// <summary>
        ///     Sends one test message to the caller through the channel's transport, outside any batch,
        ///     to the address the caller's profile holds for the channel.
        /// </summary>
        /// <param name="cancellationToken">Cancels the send.</param>
        /// <returns>
        ///     <see cref="TestNotificationResult.Sent" /> after one send,
        ///     <see cref="TestNotificationResult.NoContactAddress" /> when the profile holds no address
        ///     for the channel, and <see cref="TestNotificationResult.NotConfigured" /> when the
        ///     transport is not configured on this instance.
        /// </returns>
        /// <exception cref="Errors.DependencyUnavailableException">The transport failed.</exception>
        Task<TestNotificationResult> SendTestAsync(CancellationToken cancellationToken = default);
    }
}
