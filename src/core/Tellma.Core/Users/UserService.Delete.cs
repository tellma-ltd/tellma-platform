// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Collections.Frozen;
using System.Globalization;
using System.Text;
using Tellma.Core.Abstractions.Crud;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Access;
using Tellma.Core.Data.Batches;
using Tellma.Core.Notifications;

namespace Tellma.Core.Users
{
    // A user's preferences, stamps and notification preferences leave with the user through their
    // keys' ON DELETE CASCADE. The delete plan declares only the model's siblings, so the delete's
    // persist appends a statement that declares the three writes: run after the delete, it matches
    // nothing the cascades left, and makes the batch's change tracking and version tags see the
    // tables they wrote. It runs on the platform's own channel, which no stack's authority gates.
    public partial class UserService<TUser>
    {
        /// <summary>The user preferences' table.</summary>
        private static readonly TableName UserPreferences = new("core", "UserPreferences");

        /// <summary>The tables a user delete's keys write: those that cascade from the users.</summary>
        private static readonly FrozenSet<TableName> DeleteCascades = new[] { UserPreferences, UserStampConfiguration.Table, NotificationStatements.Preferences }.ToFrozenSet();

        /// <summary>Appends, to a delete's persist, the declaration of what the deleted users' keys cascaded to.</summary>
        /// <param name="context">The operation's persist context.</param>
        /// <returns>A task that completes when the statement is appended.</returns>
        protected override async Task ContributeAsync(PersistContext<TUser> context)
        {
            ArgumentNullException.ThrowIfNull(context);
            await base.ContributeAsync(context).ConfigureAwait(false);
            if (context is DeletePersistContext<TUser> deletion)
            {
                SqlIdentifier deleted = deletion.AffectedIds;
                Append(deletion.Batch, data => DeclareUserDelete(data, deleted));
            }
        }

        /// <summary>
        ///     Appends the declaration of what a user delete's keys wrote: the deleted users'
        ///     preferences, stamps and notification preferences, naming the deleted users as the users
        ///     those writes affect.
        /// </summary>
        /// <param name="batch">The delete's persist batch.</param>
        /// <param name="deletedIds">The table of the deleted users' ids, as the delete declared it.</param>
        internal static void DeclareUserDelete(DataBatch batch, SqlIdentifier deletedIds)
        {
            ArgumentNullException.ThrowIfNull(batch);
            ArgumentNullException.ThrowIfNull(deletedIds);

            StatementDraft draft = batch.Begin();
            try
            {
                StringBuilder text = new();
                _ = text.Append(CultureInfo.InvariantCulture, $"DELETE [p] FROM {UserPreferences.Delimited} AS [p] INNER JOIN {deletedIds.Name} AS [k] ON [k].[Id] = [p].[UserId];\n")
                    .Append(CultureInfo.InvariantCulture, $"DELETE [s] FROM {UserStampConfiguration.Table.Delimited} AS [s] INNER JOIN {deletedIds.Name} AS [k] ON [k].[Id] = [s].[UserId];\n")
                    .Append(CultureInfo.InvariantCulture, $"DELETE [n] FROM {NotificationStatements.Preferences.Delimited} AS [n] INNER JOIN {deletedIds.Name} AS [k] ON [k].[Id] = [n].[UserId];");
                batch.Append(draft, text.ToString(), DeleteCascades, idempotent: true);
            }
            catch
            {
                batch.Discard(draft);
                throw;
            }

            // The tables' rows are the deleted users' own, so the users they name are the deleted ids:
            // the bump epilogue's user-level rules over these tables see them, and their stamps are gone.
            batch.AddUserIdSource(new UserIdSource(DeleteCascades, Column: null, deletedIds));
        }

        /// <summary>Appends a statement the platform composes itself to a persist batch.</summary>
        /// <param name="batch">The persist's batch.</param>
        /// <param name="append">Appends the statement to a tenant database's batch.</param>
        /// <exception cref="ArgumentException">The batch is not one a tenant database created.</exception>
        private static void Append(IDataBatch batch, Action<DataBatch> append)
        {
            // A recorded append is replayed onto each composition of the persist batch.
            if (batch is RecordingBatch recording)
            {
                recording.Record(inner => Append(inner, append));
                return;
            }

            append(batch as DataBatch
                ?? throw new ArgumentException("The batch is not one a tenant database created, so the users' statements cannot ride it.", nameof(batch)));
        }
    }
}
