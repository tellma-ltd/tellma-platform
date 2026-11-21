// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Crud;
using Tellma.Core.Caching;

namespace Tellma.Core.Tests.Users
{
    /// <summary>
    ///     The post-commit effects of deactivating and activating users: the session listeners told of
    ///     each deactivated linked user, the membership hints recorded in one call, and a deactivated
    ///     service account's client deleted; every failure logged, none thrown.
    /// </summary>
    public sealed class UserEffectsTests : IDisposable
    {
        /// <summary>The service over a scripted identity server and recording collaborators.</summary>
        private readonly UsersKit _kit = new();

        /// <inheritdoc />
        public void Dispose()
        {
            _kit.Dispose();
        }

        [Fact]
        public async Task A_deactivation_revokes_access_hints_inactive_and_deletes_service_clients()
        {
            User ada = UsersKit.Individual(3, "Ada");
            ada.Subject = "sub-ada";
            User robot = UsersKit.ServiceAccount(4, "svc-1");
            User unlinked = UsersKit.Individual(5, "Bob");

            await _kit.Hooks.AfterCommitAsync(Outcome("deactivate", ada, robot, unlinked));

            Assert.Equal([(UsersKit.TenantId, "sub-ada"), (UsersKit.TenantId, "svc-1")], _kit.Listener.Revoked);
            Assert.Equal([(UsersKit.TenantId, "sub-ada", false), (UsersKit.TenantId, "svc-1", false)], _kit.Memberships.Hints());
            Assert.Equal(["svc-1"], _kit.Identity.Deleted);
            Assert.Equal(2, _kit.Logs.Entries.Count(static entry => entry.EventId.Name == "UserAccessRevoked"));
        }

        [Fact]
        public async Task An_activation_only_hints_active()
        {
            User ada = UsersKit.Individual(3, "Ada");
            ada.Subject = "sub-ada";

            await _kit.Hooks.AfterCommitAsync(Outcome("activate", ada));

            Assert.Empty(_kit.Listener.Revoked);
            Assert.Equal([(UsersKit.TenantId, "sub-ada", true)], _kit.Memberships.Hints());
            Assert.Empty(_kit.Identity.Deleted);
        }

        [Fact]
        public async Task Other_actions_and_unlinked_users_have_no_effects()
        {
            User ada = UsersKit.Individual(3, "Ada");
            ada.Subject = "sub-ada";

            await _kit.Hooks.AfterCommitAsync(Outcome("invite", ada));
            await _kit.Hooks.AfterCommitAsync(Outcome("deactivate", UsersKit.Individual(5, "Bob")));

            Assert.Empty(_kit.Listener.Revoked);
            Assert.Empty(_kit.Memberships.Recorded);
            Assert.Empty(_kit.Identity.Deleted);
        }

        [Fact]
        public async Task Every_failure_is_logged_and_none_is_thrown()
        {
            User robot = UsersKit.ServiceAccount(4, "svc-1");
            _kit.Listener.Failure = new InvalidOperationException("The listener failed.");
            _kit.Memberships.Failure = new HttpRequestException("The directory is down.");
            _kit.Identity.DeleteFailure = new HttpRequestException("The identity server is down.");

            await _kit.Hooks.AfterCommitAsync(Outcome("deactivate", robot));

            Assert.Single(_kit.Logs.Entries, static entry => entry.EventId.Name == "UserAccessRevocationFailed");
            Assert.Single(_kit.Logs.Entries, static entry => entry.EventId.Name == "UserMembershipHintFailed");
            Assert.Single(_kit.Logs.Entries, static entry => entry.EventId.Name == "ServiceAccountDeleteFailed");
            Assert.Equal(["svc-1"], _kit.Identity.Deleted);
        }

        /// <summary>The committed outcome of a built-in action over loaded targets.</summary>
        /// <param name="action">The action's name.</param>
        /// <param name="targets">The targets, as loaded before the update.</param>
        /// <returns>The outcome.</returns>
        private static ActionPersistOutcome<User, int> Outcome(string action, params User[] targets)
        {
            return new ActionPersistOutcome<User, int>
            {
                Action = action,
                Ids = [.. targets.Select(static user => user.Id)],
                Entities = targets,
                Stamp = UsersKit.Now,
                Context = UsersKit.Context(),
                VersionTags = VersionTagSnapshots.Empty(UsersKit.TenantId),
            };
        }
    }
}
