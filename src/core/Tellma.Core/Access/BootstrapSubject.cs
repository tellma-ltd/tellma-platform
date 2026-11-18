// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Collections.Frozen;
using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Crud;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.TableTypes;
using Tellma.Core.Data.Batches;

namespace Tellma.Core.Access
{
    // Spec 0014 §11, development in-proc mode: the subject statement rides the bootstrap's own save,
    // so the user and its subject commit together. It is a persist effect of the users' stack because
    // core.Users is the stack's table, which only the stack's own participants write.
    /// <summary>
    ///     The subject the scope's bootstrap records on the administrator it creates, between the
    ///     bootstrapper setting it and its save returning. One per scope; empty at all other times.
    /// </summary>
    internal sealed class BootstrapSubject
    {
        /// <summary>The normalised email address of the administrator the save creates; null when no bootstrap is saving.</summary>
        public string? Email { get; private set; }

        /// <summary>The subject to record; null when no bootstrap is saving.</summary>
        public string? Subject { get; private set; }

        /// <summary>Holds a subject for the duration of one save.</summary>
        /// <param name="email">The normalised email address of the user the save creates.</param>
        /// <param name="subject">The subject.</param>
        /// <returns>The hold; disposing it clears the subject.</returns>
        /// <exception cref="InvalidOperationException">A subject is held already.</exception>
        public IDisposable Hold(string email, string subject)
        {
            ArgumentNullException.ThrowIfNull(email);
            ArgumentNullException.ThrowIfNull(subject);
            if (Subject is not null)
            {
                throw new InvalidOperationException("A bootstrap of this scope is saving already; bootstraps of one scope run one at a time.");
            }

            Email = email;
            Subject = subject;
            return new Release(this);
        }

        /// <summary>Clears the subject when the save returns.</summary>
        /// <param name="owner">The holder.</param>
        private sealed class Release(BootstrapSubject owner) : IDisposable
        {
            /// <inheritdoc />
            public void Dispose()
            {
                owner.Email = null;
                owner.Subject = null;
            }
        }
    }

    /// <summary>
    ///     Records the held bootstrap subject on the user the save creates under the held email, as an
    ///     invitation records one: the subject, the invitation's instant and an invited status. A save
    ///     made while no bootstrap holds a subject appends nothing.
    /// </summary>
    /// <typeparam name="TUser">The stack's user leaf.</typeparam>
    /// <param name="pending">The scope's held subject.</param>
    internal sealed class BootstrapSubjectEffect<TUser>(BootstrapSubject pending) : IPersistEffect<TUser>
        where TUser : User
    {
        /// <summary>The table the statement writes.</summary>
        private static readonly FrozenSet<TableName> Writes = new[] { PersistChecks.Users }.ToFrozenSet();

        /// <inheritdoc />
        public Task ContributeAsync(PersistContext<TUser> context)
        {
            ArgumentNullException.ThrowIfNull(context);
            if (pending.Subject is not string subject || pending.Email is not string email || context is not SavePersistContext<TUser> save)
            {
                return Task.CompletedTask;
            }

            // --- The created row under the held email, by its final id; an existing user keeps its evidence ---
            for (int index = 0; index < save.Entities.Count; index++)
            {
                TUser user = save.Entities[index];
                if (save.Before(index) is not null || !string.Equals(user.Email, email, StringComparison.Ordinal))
                {
                    continue;
                }

                int id = user.Id;
                SqlIdentifier ids = save.Batch.Tvp([new IdList { Id = id }]);
                _ = save.Batch.Sql(
                    $"UPDATE [core].[Users] SET [Subject] = {subject}, [InvitedAt] = SYSUTCDATETIME(), [InviteStatus] = 'Invited' WHERE [Id] = {id};",
                    new SqlOptions(Writes, userIds: ids));
            }

            return Task.CompletedTask;
        }
    }
}
