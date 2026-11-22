// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Collections.Frozen;
using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Crud;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.TableTypes;
using Tellma.Core.Abstractions.Tenancy;
using Tellma.Core.Data.Batches;
using Tellma.Core.Tenancy;
using Tellma.Core.Users;

namespace Tellma.Core.Access
{
    // Spec 0014 §11 and spec 0018 §6.1, development in-proc mode: the subject statements ride the
    // save that creates the users, so each user and its subject commit together. They are a persist
    // effect of the users' stack because core.Users is the stack's table, which only the stack's own
    // participants write. The administrator bootstrap holds one subject; development sample data
    // holds its principals' subjects through the public seam.
    /// <summary>
    ///     The subjects the scope's saves record on the users they create, between a hold and its
    ///     release: the administrator bootstrap's, or the development principals' a provisioning step
    ///     binds. One per scope; empty at all other times.
    /// </summary>
    /// <param name="environment">The tenancy's environment; subjects are held in development only.</param>
    /// <param name="accessor">The scope's request context, which must be the platform's own.</param>
    internal sealed class DevelopmentSubjects(TenancyEnvironment environment, IRequestContextAccessor accessor) : IDevelopmentSubjects
    {
        /// <summary>The held subjects by normalised email address; null when nothing is held.</summary>
        public FrozenDictionary<string, string>? Held { get; private set; }

        /// <inheritdoc />
        public IDisposable Hold(IReadOnlyDictionary<string, string> subjects)
        {
            // --- The subjects, normalised as the users' stack stores the addresses ---
            ArgumentNullException.ThrowIfNull(subjects);
            Dictionary<string, string> normalised = new(StringComparer.Ordinal);
            foreach ((string email, string subject) in subjects)
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(email, nameof(subjects));
                ArgumentException.ThrowIfNullOrWhiteSpace(subject, nameof(subjects));
                if (!normalised.TryAdd(UserRules.Email(email)!, subject))
                {
                    throw new ArgumentException($"The email address '{email}' is held twice; each address binds one subject.", nameof(subjects));
                }
            }

            // --- The environment and the scope: a subject is never bound without an invitation elsewhere ---
            if (!environment.IsDevelopment)
            {
                throw new InvalidOperationException("Subjects are bound without an invitation in a development environment only; a deployed user obtains a subject by invitation.");
            }

            if (!accessor.Current.IsSystem)
            {
                throw new InvalidOperationException("Subjects are bound in the platform's own scope of the tenant, which the provisioning steps open; this scope belongs to a user.");
            }

            if (Held is not null)
            {
                throw new InvalidOperationException("Subjects are held in this scope already; the holds of one scope run one at a time.");
            }

            Held = normalised.ToFrozenDictionary(StringComparer.Ordinal);
            return new Release(this);
        }

        /// <summary>Clears the subjects when the hold is disposed.</summary>
        /// <param name="owner">The holder.</param>
        private sealed class Release(DevelopmentSubjects owner) : IDisposable
        {
            /// <inheritdoc />
            public void Dispose()
            {
                owner.Held = null;
            }
        }
    }

    /// <summary>
    ///     Records the held subjects on the users the save creates under the held email addresses, as
    ///     an invitation records one: the subject, the invitation's instant and an invited status. A
    ///     save made while nothing is held appends nothing, and an existing user keeps its evidence.
    /// </summary>
    /// <typeparam name="TUser">The stack's user leaf.</typeparam>
    /// <param name="pending">The scope's held subjects.</param>
    internal sealed class DevelopmentSubjectsEffect<TUser>(DevelopmentSubjects pending) : IPersistEffect<TUser>
        where TUser : User
    {
        /// <summary>The table the statements write.</summary>
        private static readonly FrozenSet<TableName> Writes = new[] { PersistChecks.Users }.ToFrozenSet();

        /// <inheritdoc />
        public Task ContributeAsync(PersistContext<TUser> context)
        {
            ArgumentNullException.ThrowIfNull(context);
            if (pending.Held is not FrozenDictionary<string, string> held || context is not SavePersistContext<TUser> save)
            {
                return Task.CompletedTask;
            }

            // --- Each created row under a held address, by its final id; the stack stored the address normalised ---
            for (int index = 0; index < save.Entities.Count; index++)
            {
                TUser user = save.Entities[index];
                if (save.Before(index) is not null || user.Email is not string email || !held.TryGetValue(email, out string? subject))
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
