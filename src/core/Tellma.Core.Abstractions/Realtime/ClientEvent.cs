// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Realtime
{
    /// <summary>
    ///     A thin, named message pushed to a tenant's open clients through the real-time hub. It
    ///     announces that something changed and carries ids and short codes only, never tenant content:
    ///     the client fetches what the event announces.
    /// </summary>
    /// <remarks>
    ///     Its name must be registered by the feature that publishes it, and its payload, serialised
    ///     with the platform's JSON options, must fit in <see cref="MaxPayloadBytes" />;
    ///     <see cref="IClientEventPublisher" /> refuses either breach with
    ///     <see cref="InvalidOperationException" />, since a thin event that grew is a design error.
    /// </remarks>
    /// <param name="Name">The event's registered name, such as <c>inbox.changed</c>.</param>
    /// <param name="UserIds">The users of the current tenant the event is for; empty for every connected user of the tenant.</param>
    /// <param name="Payload">The event's payload, or <see langword="null" /> for none.</param>
    public sealed record ClientEvent(string Name, IReadOnlyList<int> UserIds, object? Payload)
    {
        /// <summary>The most UTF-8 bytes an event's serialised payload may take.</summary>
        public const int MaxPayloadBytes = 4096;
    }
}
