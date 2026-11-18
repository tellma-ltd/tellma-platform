// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Collections.Frozen;
using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Crud;
using Tellma.Core.Abstractions.Data;

namespace Tellma.Core.Access
{
    // The companion is spec 0014 §8.2. @tm_tag is the batch's bump value, bound for the whole
    // command by the epilogue's bump statement: a user save always writes core.Users, whose
    // Preferences rule names the saved users, so the bump is never absent from a batch carrying it.
    /// <summary>
    ///     Gives every user a save inserts its <c>core.UserStamps</c> row, in the save's transaction:
    ///     both user tags set to the batch's bump value, no activity and an empty inbox. A save that
    ///     only updates, a delete and an action append nothing.
    /// </summary>
    /// <typeparam name="TUser">The stack's user leaf.</typeparam>
    internal sealed class UserStampsCompanion<TUser> : IPersistEffect<TUser>
        where TUser : User
    {
        /// <summary>The table the statement writes.</summary>
        private static readonly FrozenSet<TableName> Writes = new[] { UserStampConfiguration.Table }.ToFrozenSet();

        /// <inheritdoc />
        public Task ContributeAsync(PersistContext<TUser> context)
        {
            ArgumentNullException.ThrowIfNull(context);
            if (context is not SavePersistContext<TUser> save || !Inserts(save))
            {
                return Task.CompletedTask;
            }

            // --- One row per inserted user; a row already there (a re-run after a lost connection) is kept ---
            SqlIdentifier inserted = save.NewIds;
            _ = save.Batch.Sql(
                $"""
                INSERT [core].[UserStamps] ([UserId], [LastActiveAt], [PermissionsTag], [PreferencesTag], [InboxSeenAt], [InboxVersion], [AssignmentsSeenAt])
                SELECT N.[Id], NULL, @tm_tag, @tm_tag, NULL, 0, NULL
                FROM {inserted} AS N
                WHERE NOT EXISTS (SELECT 1 FROM [core].[UserStamps] AS S WHERE S.[UserId] = N.[Id]);
                """,
                new SqlOptions(Writes, idempotent: true));
            return Task.CompletedTask;
        }

        /// <summary>Whether the save inserts a user: a payload row without a before image.</summary>
        /// <param name="save">The save's persist context.</param>
        /// <returns>True when it does.</returns>
        private static bool Inserts(SavePersistContext<TUser> save)
        {
            for (int index = 0; index < save.Entities.Count; index++)
            {
                if (save.Before(index) is null)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
