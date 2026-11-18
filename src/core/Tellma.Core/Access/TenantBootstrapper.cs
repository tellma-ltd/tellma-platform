// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Globalization;
using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Api;
using Tellma.Core.Abstractions.Crud;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Tenancy;
using Tellma.Core.Tenancy;

namespace Tellma.Core.Access
{
    // The bootstrap is spec 0014 §11. It saves through the users' stack in the platform's own scope,
    // so the emitter, the audit stamps, the companion stamp row and the tag bumps come with it, and
    // the guards of §8.3 stand aside: the tenant may have no administrator yet.
    /// <summary>
    ///     Makes the tenant's first individual administrator through the users' stack: a new user
    ///     with no invitation evidence and a permanent administrator membership, or an existing user
    ///     of the email address given that membership when it lacks one. One per scope.
    /// </summary>
    /// <typeparam name="TUser">The distribution's user leaf.</typeparam>
    /// <param name="users">The users' stack.</param>
    /// <param name="database">The tenant's database, which the email address is looked up in.</param>
    /// <param name="accessor">The scope's request context, which must be the platform's own.</param>
    /// <param name="environment">The tenancy's environment; a subject is accepted in development only.</param>
    /// <param name="subject">The scope's held subject, which the users' stack records on the created user.</param>
    /// <param name="time">The host's clock, which tells a started membership from a future one.</param>
    internal sealed class TenantBootstrapper<TUser>(
        EntityService<TUser, int> users,
        ITenantDatabase database,
        IRequestContextAccessor accessor,
        TenancyEnvironment environment,
        BootstrapSubject subject,
        TimeProvider time) : ITenantBootstrapper
        where TUser : User, new()
    {
        /// <inheritdoc />
        public async Task<int> BootstrapAdministratorAsync(TenantBootstrapRequest request, CancellationToken cancellationToken = default)
        {
            // --- The request, the scope and the environment ---
            ArgumentNullException.ThrowIfNull(request);
            ArgumentException.ThrowIfNullOrWhiteSpace(request.Email, nameof(request));
            ArgumentException.ThrowIfNullOrWhiteSpace(request.Name, nameof(request));
            if (!accessor.Current.IsSystem)
            {
                throw new InvalidOperationException("The administrator bootstrap runs in the platform's own scope of the tenant, which the provisioning steps open; this scope belongs to a user.");
            }

            if (request.Subject is not null && !environment.IsDevelopment)
            {
                throw new InvalidOperationException("A bootstrap subject is accepted in a development environment only; a deployed administrator obtains a subject by invitation.");
            }

            // --- The email address as the users' stack stores it: trimmed and lower-cased ---
            string email = request.Email.Trim().ToLowerInvariant();
            SaveOptions options = new() { ReturnEntities = false, Source = SaveSource.System };

            // --- An existing user keeps its state, and gains the membership when it lacks one ---
            if (await FindAsync(email, cancellationToken).ConfigureAwait(false) is int existing)
            {
                EntitiesResult<TUser> loaded = await users.GetByIdAsync(existing, DetailsRequest.None, cancellationToken).ConfigureAwait(false);
                TUser user = loaded.Entities.Single();
                if (Grant(user.RoleMemberships ?? [], time.GetUtcNow()) is IReadOnlyList<RoleMembership> memberships)
                {
                    user.RoleMemberships = memberships;
                    _ = await users.SaveAsync([user], options, cancellationToken).ConfigureAwait(false);
                }

                return existing;
            }

            // --- A new user: no invitation evidence, so its state reads New; the subject rides the same save ---
            TUser created = new()
            {
                Kind = UserKind.Individual,
                Email = email,
                Name = request.Name.Trim(),
                PreferredLanguage = request.PreferredLanguage,
                RoleMemberships = [new RoleMembership { RoleId = WellKnownIds.AdministratorRoleId }],
            };

            using IDisposable? hold = request.Subject is string held ? subject.Hold(email, held) : null;
            EntitiesResult<TUser> saved = await users.SaveAsync([created], options, cancellationToken).ConfigureAwait(false);
            return checked((int)saved.Ids[0]);
        }

        /// <summary>
        ///     The memberships that give a user a started, permanent administrator membership: none
        ///     when the user holds one; otherwise the user's memberships with every administrator
        ///     membership that has not ended replaced by one permanent membership, starting where the
        ///     last ended one ended so the windows never overlap.
        /// </summary>
        /// <param name="memberships">The user's stored memberships.</param>
        /// <param name="now">The instant judged.</param>
        /// <returns>The memberships to save; null when the user holds the membership.</returns>
        internal static IReadOnlyList<RoleMembership>? Grant(IReadOnlyList<RoleMembership> memberships, DateTimeOffset now)
        {
            ArgumentNullException.ThrowIfNull(memberships);
            List<RoleMembership> administrator = [.. memberships.Where(static membership => membership.RoleId == WellKnownIds.AdministratorRoleId)];
            if (administrator.Any(membership => membership.EndsAt is null && (membership.StartsAt is not DateTimeOffset start || start <= now)))
            {
                return null;
            }

            // The ended rows stay as the record of past windows; the new row begins after the last of them.
            DateTimeOffset? after = administrator.Where(membership => membership.EndsAt is DateTimeOffset end && end <= now).Max(static membership => membership.EndsAt);
            return
            [
                .. memberships.Where(membership => membership.RoleId != WellKnownIds.AdministratorRoleId || (membership.EndsAt is DateTimeOffset end && end <= now)),
                new RoleMembership { RoleId = WellKnownIds.AdministratorRoleId, StartsAt = after },
            ];
        }

        /// <summary>The id of the user of an email address, if any.</summary>
        /// <param name="email">The normalised email address.</param>
        /// <param name="cancellationToken">Cancels the round trip.</param>
        /// <returns>The user's id; null when no user has the address.</returns>
        private async Task<int?> FindAsync(string email, CancellationToken cancellationToken)
        {
            IDataBatch read = database.CreateBatch(BatchPurpose.Read);
            BatchResult<RawResult> found = read.Sql($"SELECT U.[Id] FROM [core].[Users] AS U WHERE U.[Email] = {email};", new SqlOptions(resultSets: 1));
            _ = await read.ExecuteAsync(cancellationToken).ConfigureAwait(false);
            QueryRowSet rows = found.Value.ResultSets[0];
            return rows.RowCount switch
            {
                0 => null,
                1 => ((int[])rows.GetBuffer(0))[0],
                _ => throw new InvalidOperationException(string.Create(CultureInfo.InvariantCulture, $"{rows.RowCount} users share the email address the bootstrap names, which the users' unique email index forbids.")),
            };
        }
    }
}
