// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using System.Text;
using Tellma.Core.Abstractions.Notifications;
using Tellma.Core.Composition;

namespace Tellma.Core.Notifications
{
    /// <summary>
    ///     The channels other than the inbox that deliver on this instance — the registered channels
    ///     paired with a sender, in registration order — and the delivery condition built from them.
    ///     A singleton, resolved once on first use.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The senders are created once, in a scope of their own, to read their keys, as the
    ///         startup gate's channel check creates them; a sender cannot need a tenant to be created.
    ///         A failure to create them is not remembered, so the next use tries again.
    ///     </para>
    ///     <para>
    ///         The condition holds one term per delivering channel, joined by <c>OR</c>: the user's
    ///         preference row for the type and channel, else the channel's default. The keys and
    ///         defaults are inlined as literals, which the channel key grammar keeps free of quotes, so
    ///         the condition is fixed text.
    ///     </para>
    /// </remarks>
    internal sealed class NotificationDeliveryChannels
    {
        /// <summary>Creates the scope the senders are created in.</summary>
        private readonly IServiceScopeFactory _scopes;

        /// <summary>The registered channels, in registration order.</summary>
        private readonly INotificationChannelRegistry _registry;

        /// <summary>Guards the one resolution.</summary>
        private readonly Lock _gate = new();

        /// <summary>The resolution, once made.</summary>
        private Resolution? _resolution;

        /// <summary>Creates the set.</summary>
        /// <param name="scopes">Creates the scope the senders are created in.</param>
        /// <param name="registry">The registered channels.</param>
        public NotificationDeliveryChannels(IServiceScopeFactory scopes, INotificationChannelRegistry registry)
        {
            ArgumentNullException.ThrowIfNull(scopes);
            ArgumentNullException.ThrowIfNull(registry);
            _scopes = scopes;
            _registry = registry;
        }

        /// <summary>The channels that deliver, in registration order; empty when no channel has a sender.</summary>
        public IReadOnlyList<NotificationChannelDescriptor> Channels => Resolve().Channels;

        /// <summary>
        ///     The delivery condition as a composite format whose one placeholder, <c>{0}</c>, is the
        ///     statement's type parameter, evaluated per recipient row <c>[r]</c>; null when no channel
        ///     has a sender, and a request then enqueues no delivery.
        /// </summary>
        public string? Condition => Resolve().Condition;

        /// <summary>Builds the condition of some delivering channels.</summary>
        /// <param name="channels">The channels, in registration order.</param>
        /// <returns>The composite format; null when there are none.</returns>
        public static string? BuildCondition(IReadOnlyList<NotificationChannelDescriptor> channels)
        {
            ArgumentNullException.ThrowIfNull(channels);
            if (channels.Count == 0)
            {
                return null;
            }

            // --- One term per channel: the preference row, else the default, inlined ---
            StringBuilder condition = new();
            for (int index = 0; index < channels.Count; index++)
            {
                NotificationChannelDescriptor channel = channels[index];
                if (!NotificationNames.IsChannelKey(channel.Key))
                {
                    throw new InvalidOperationException($"The notification channel '{channel.Key}' does not have a well-formed key, so it cannot be written into the delivery condition.");
                }

                if (index > 0)
                {
                    _ = condition.Append("\n        OR ");
                }

                // {0} is the format's one placeholder: the statement's type parameter.
                _ = condition.Append("COALESCE((SELECT [p].[Enabled] FROM ")
                    .Append(NotificationStatements.Preferences.Delimited)
                    .Append(" AS [p] WHERE [p].[UserId] = [r].[UserId] AND [p].[Type] = {0} AND [p].[Channel] = '")
                    .Append(channel.Key)
                    .Append("'), ")
                    .Append(channel.DefaultEnabled ? '1' : '0')
                    .Append(") = 1");
            }

            return condition.ToString();
        }

        /// <summary>Resolves the delivering channels once.</summary>
        /// <returns>The resolution.</returns>
        private Resolution Resolve()
        {
            Resolution? resolution = Volatile.Read(ref _resolution);
            if (resolution is not null)
            {
                return resolution;
            }

            lock (_gate)
            {
                if (_resolution is not null)
                {
                    return _resolution;
                }

                // --- The senders' keys, read from senders created in a scope of their own ---
                HashSet<string> senders = new(StringComparer.Ordinal);
                using (IServiceScope scope = _scopes.CreateScope())
                {
                    foreach (INotificationChannel sender in scope.ServiceProvider.GetServices<INotificationChannel>())
                    {
                        if (sender.Key is string key)
                        {
                            _ = senders.Add(key);
                        }
                    }
                }

                // --- The registered channels with a sender, the inbox aside: its rows are the notifier's own ---
                List<NotificationChannelDescriptor> channels = [.. _registry.All
                    .Where(channel => !string.Equals(channel.Key, CoreFeature.InboxChannel, StringComparison.Ordinal) && senders.Contains(channel.Key))];
                resolution = new Resolution(channels, BuildCondition(channels));
                Volatile.Write(ref _resolution, resolution);
                return resolution;
            }
        }

        /// <summary>The delivering channels and their condition.</summary>
        /// <param name="Channels">The channels.</param>
        /// <param name="Condition">The condition; null when there are none.</param>
        private sealed record Resolution(IReadOnlyList<NotificationChannelDescriptor> Channels, string? Condition);
    }
}
