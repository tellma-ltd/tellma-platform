// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using Tellma.Core.Abstractions.Composition;
using Tellma.Core.Abstractions.Notifications;
using Tellma.Core.Notifications;

namespace Tellma.Core.Composition.Checks
{
    /// <summary>
    ///     <c>core.notification-channels</c>: every channel key matches its grammar, keys are unique,
    ///     the baseline <c>inbox</c> and <c>email</c> channels are composed, and every
    ///     <see cref="INotificationChannel" /> sender names a registered channel other than
    ///     <c>inbox</c>, at most one per channel.
    /// </summary>
    /// <remarks>
    ///     The senders are resolved in the gate's scope to read their keys, so a sender is created
    ///     without a tenant; one that cannot be is reported. A channel without a sender is sound: it
    ///     keeps its users' preferences and delivers nothing.
    /// </remarks>
    /// <param name="record">The realised composition.</param>
    internal sealed class NotificationChannelsCheck(TellmaCompositionRecord record) : IStartupCheck
    {
        /// <inheritdoc />
        public string Name => CoreCheckNames.NotificationChannels;

        /// <inheritdoc />
        public bool RequiresInfrastructure => false;

        /// <inheritdoc />
        public Task<IReadOnlyList<CompositionProblem>> CheckAsync(IServiceProvider services, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(services);
            List<CompositionProblem> problems = [];

            // --- Every key's grammar ---
            foreach (ContributedNotificationChannel contributed in record.NotificationChannels)
            {
                if (!NotificationNames.IsChannelKey(contributed.Descriptor.Key))
                {
                    problems.Add(new CompositionProblem(
                        Name,
                        $"The notification channel '{contributed.Descriptor.Key}' of the feature '{contributed.Owner.Name}' does not have a well-formed key.",
                        $"Key the channel with lowercase letters, digits and hyphens, starting with a letter, at most {NotificationNames.MaxChannelKeyLength} characters, such as 'sms'."));
                }
            }

            // --- Keys are unique, compared ordinally as the registry finds them ---
            foreach (IGrouping<string, ContributedNotificationChannel> clash in record.NotificationChannels
                .Where(static contributed => contributed.Descriptor.Key is not null)
                .GroupBy(static contributed => contributed.Descriptor.Key, StringComparer.Ordinal)
                .Where(static group => group.Count() > 1))
            {
                string owners = string.Join(" and ", clash.Select(static contributed => $"'{contributed.Owner.Name}'"));
                problems.Add(new CompositionProblem(
                    Name,
                    $"The notification channel '{clash.Key}' is registered more than once, by the features {owners}.",
                    "Register each notification channel once, in the feature that ships its sender."));
            }

            // --- The baseline channels every distribution carries ---
            HashSet<string> keys = [.. record.NotificationChannels.Select(static contributed => contributed.Descriptor.Key).OfType<string>()];
            foreach (string baseline in (string[])[CoreFeature.InboxChannel, CoreFeature.EmailChannel])
            {
                if (!keys.Contains(baseline))
                {
                    problems.Add(new CompositionProblem(
                        Name,
                        $"The baseline notification channel '{baseline}' is not registered.",
                        "Keep the core feature's contribution intact: it registers the inbox and email channels of every distribution."));
                }
            }

            // --- The senders ---
            CheckSenders(services, keys, problems);
            return Task.FromResult<IReadOnlyList<CompositionProblem>>(problems);
        }

        /// <summary>Checks that every sender names a registered channel other than the inbox, at most one per channel.</summary>
        /// <param name="services">The gate's scoped services.</param>
        /// <param name="keys">The registered channel keys.</param>
        /// <param name="problems">Receives the problems.</param>
        private void CheckSenders(IServiceProvider services, HashSet<string> keys, List<CompositionProblem> problems)
        {
            // --- Resolve the senders to read their keys ---
            List<INotificationChannel> senders;
            try
            {
                senders = [.. services.GetServices<INotificationChannel>()];
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                problems.Add(new CompositionProblem(
                    Name,
                    $"The notification channel senders registered by {SenderOwners(null)} could not be created to read their keys: {exception.GetType().Name}: {exception.Message}",
                    "Register every service a sender depends on, and keep its constructor from needing a tenant or throwing."));
                return;
            }

            // --- Each sender names a channel it may deliver ---
            List<(string Key, INotificationChannel Sender)> keyed = [];
            foreach (INotificationChannel sender in senders)
            {
                string? key = sender.Key;
                string described = $"The notification channel sender {TypeDisplay.Qualified(sender.GetType())} registered by {SenderOwners(sender.GetType())}";
                if (key is null || !keys.Contains(key))
                {
                    problems.Add(new CompositionProblem(
                        Name,
                        $"{described} delivers the channel '{key}', which is not registered.",
                        "Register the sender's channel with contribution.NotificationChannel(descriptor) in the feature that registers the sender, or give the sender a registered channel's key."));
                }
                else if (string.Equals(key, CoreFeature.InboxChannel, StringComparison.Ordinal))
                {
                    problems.Add(new CompositionProblem(
                        Name,
                        $"{described} delivers the '{CoreFeature.InboxChannel}' channel, which the notifier's own rows deliver.",
                        "Remove the sender: the inbox channel has no sender."));
                }
                else
                {
                    keyed.Add((key, sender));
                }
            }

            // --- At most one sender per channel ---
            foreach (IGrouping<string, (string Key, INotificationChannel Sender)> clash in keyed
                .GroupBy(static item => item.Key, StringComparer.Ordinal)
                .Where(static group => group.Count() > 1))
            {
                string senderNames = string.Join(" and ", clash.Select(item => $"{TypeDisplay.Qualified(item.Sender.GetType())} (registered by {SenderOwners(item.Sender.GetType())})"));
                problems.Add(new CompositionProblem(
                    Name,
                    $"The notification channel '{clash.Key}' has more than one sender: {senderNames}.",
                    "Register one sender per channel, in the feature that registers the channel."));
            }
        }

        /// <summary>The features that registered a sender, quoted and joined, from the composition's service registrations.</summary>
        /// <param name="implementation">The sender's class, or <see langword="null" /> for every sender.</param>
        /// <returns>The features, or a description that names none when only a factory registration matches.</returns>
        private string SenderOwners(Type? implementation)
        {
            // A factory registration names no class, so it is matched only when every sender is asked
            // for; a sender created by one is attributed to no feature.
            List<string> owners = [.. record.Services
                .Where(service => service.ServiceType == typeof(INotificationChannel)
                    && (implementation is null || service.ImplementationType == implementation))
                .Select(static service => $"'{service.Owner.Name}'")
                .Distinct(StringComparer.Ordinal)];

            return owners.Count switch
            {
                0 => "a registration outside the features",
                1 => $"the feature {owners[0]}",
                _ => $"the features {string.Join(" and ", owners)}",
            };
        }
    }
}
