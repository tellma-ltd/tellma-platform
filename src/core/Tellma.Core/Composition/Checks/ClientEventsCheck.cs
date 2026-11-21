// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Composition;
using Tellma.Core.Notifications;

namespace Tellma.Core.Composition.Checks
{
    /// <summary><c>core.client-events</c>: every client event name matches its grammar, and names are unique.</summary>
    /// <remarks>
    ///     Reads the composition alone. The registry keeps a repeated name once, so that this check, and
    ///     not a failing constructor, is where the mistake surfaces.
    /// </remarks>
    /// <param name="record">The realised composition.</param>
    internal sealed class ClientEventsCheck(TellmaCompositionRecord record) : IStartupCheck
    {
        /// <inheritdoc />
        public string Name => CoreCheckNames.ClientEvents;

        /// <inheritdoc />
        public bool RequiresInfrastructure => false;

        /// <inheritdoc />
        public Task<IReadOnlyList<CompositionProblem>> CheckAsync(IServiceProvider services, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(services);
            List<CompositionProblem> problems = [];

            // --- Every name's grammar: a notification type key's ---
            foreach (ContributedClientEvent contributed in record.ClientEvents)
            {
                if (!NotificationNames.IsClientEventName(contributed.Name))
                {
                    problems.Add(new CompositionProblem(
                        Name,
                        $"The client event '{contributed.Name}' of the feature '{contributed.Owner.Name}' does not have a well-formed name.",
                        $"Name the event with two or more dot-separated segments of lowercase letters, digits and hyphens, each starting with a letter, at most {NotificationNames.MaxClientEventNameLength} characters, such as 'inbox.changed'."));
                }
            }

            // --- Names are unique, compared ordinally as the registry finds them ---
            foreach (IGrouping<string, ContributedClientEvent> clash in record.ClientEvents
                .GroupBy(static contributed => contributed.Name, StringComparer.Ordinal)
                .Where(static group => group.Count() > 1))
            {
                string owners = string.Join(" and ", clash.Select(static contributed => $"'{contributed.Owner.Name}'"));
                problems.Add(new CompositionProblem(
                    Name,
                    $"The client event '{clash.Key}' is registered more than once, by the features {owners}.",
                    "Register each client event once, in the feature whose code publishes it."));
            }

            return Task.FromResult<IReadOnlyList<CompositionProblem>>(problems);
        }
    }
}
