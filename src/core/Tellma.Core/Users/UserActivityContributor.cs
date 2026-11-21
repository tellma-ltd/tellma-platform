// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Crud;
using Tellma.Core.Abstractions.Data;

namespace Tellma.Core.Users
{
    // Spec 0018 §3.1: the activity extra feeds the user page's "Last active" line. Its statement joins
    // the plan's IdsSource (spec 0015 §5.3), the ids the caller's read admitted, so it reads nothing
    // of a user the caller cannot see. The connect prologue stamps LastActiveAt at most once per
    // activity interval, so the value is that coarse.
    /// <summary>
    ///     Produces the users' <c>activity</c> details extra: when each user read was last active,
    ///     one row per user with a stamp row, in no particular order.
    /// </summary>
    /// <typeparam name="TUser">The stack's user leaf.</typeparam>
    [DetailsExtras(ActivityExtra)]
    public sealed class UserActivityContributor<TUser> : IDetailsContributor<TUser>
        where TUser : User
    {
        /// <summary>The name of the extra.</summary>
        public const string ActivityExtra = "activity";

        /// <summary>Adds the extra when the read includes it: one statement over the visible ids, writing nothing.</summary>
        /// <param name="plan">The details plan.</param>
        /// <param name="requested">The extras the read includes that this contributor declares.</param>
        public void Contribute(DetailsPlan<TUser> plan, IReadOnlySet<string> requested)
        {
            ArgumentNullException.ThrowIfNull(plan);
            ArgumentNullException.ThrowIfNull(requested);
            if (requested.Contains(ActivityExtra))
            {
                plan.AddExtra(
                    ActivityExtra,
                    $"SELECT [s].[UserId], [s].[LastActiveAt] FROM [core].[UserStamps] AS [s] INNER JOIN {plan.IdsSource} AS [k] ON [k].[Id] = [s].[UserId];",
                    new SqlOptions(resultSets: 1));
            }
        }
    }
}
