// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Diagnostics.Metrics;
using Tellma.Core.Abstractions.Access;

namespace Tellma.Core.Users
{
    // The instrument is spec 0018 §3.3 step 4's and §8.2's invites row.
    /// <summary>
    ///     The users' stack's instruments on the core meter: one count per user an invite was asked
    ///     for, by outcome. No instrument carries a tenant or a user.
    /// </summary>
    internal sealed class UsersMetrics
    {
        /// <summary>The users the invite action was asked for, by outcome.</summary>
        private readonly Counter<long> _invites;

        /// <summary>Creates the instruments.</summary>
        /// <param name="meterFactory">The host's meter factory, which owns the meter's lifetime.</param>
        public UsersMetrics(IMeterFactory meterFactory)
        {
            ArgumentNullException.ThrowIfNull(meterFactory);

            Meter meter = meterFactory.Create(UsersTelemetryNames.MeterName);
            _invites = meter.CreateCounter<long>(
                UsersTelemetryNames.Invites,
                "{user}",
                "Users the invite action was asked to invite, by outcome.");
        }

        /// <summary>Counts users of one invite outcome.</summary>
        /// <param name="outcome">The outcome's tag value, one of the invite outcomes of <see cref="UsersTelemetryNames" />.</param>
        /// <param name="count">How many users ended so; nothing is recorded for none.</param>
        public void RecordInvites(string outcome, int count)
        {
            if (count > 0)
            {
                _invites.Add(count, new KeyValuePair<string, object?>(UsersTelemetryNames.OutcomeTag, outcome));
            }
        }
    }
}
