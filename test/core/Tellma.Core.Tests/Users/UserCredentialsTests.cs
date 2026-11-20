// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Identity;
using Tellma.Core.Abstractions.Validation;
using Tellma.Core.Data;
using Tellma.Core.Tests.Access;

namespace Tellma.Core.Tests.Users
{
    /// <summary>
    ///     The issue-credentials action: one client created between the round trips, the account
    ///     updated to name it and joined on its first credentials, the client hinted and the previous
    ///     one deleted once the action committed, the new one deleted when it never commits, and the
    ///     secret answered once and never logged.
    /// </summary>
    public sealed class UserCredentialsTests : IDisposable
    {
        /// <summary>The service over a scripted identity server.</summary>
        private readonly UsersKit _kit = new();

        /// <inheritdoc />
        public void Dispose()
        {
            _kit.Dispose();
        }

        [Fact]
        public async Task First_credentials_name_the_new_client_join_the_account_and_hint_its_membership()
        {
            User robot = UsersKit.ServiceAccount(3, null);
            ActionContext<User, int> context = Context(robot);

            Task<CredentialsResult> running = _kit.Service.RunIssueCredentialsAsync(context);
            Assert.False(running.IsCompleted);
            Assert.True(context.CompletePersisted());
            CredentialsResult result = await running;

            (string displayName, IdentityServiceAccount account) = Assert.Single(_kit.Identity.Created);
            Assert.Equal("Robot", displayName);
            Assert.Equal(new CredentialsResult(3, account.ClientId, account.ClientSecret), result);
            IReadOnlyDictionary<string, object?> update = Assert.Single(context.Updates);
            Assert.Equal(account.ClientId, update[nameof(User.Subject)]);
            Assert.Equal(UsersKit.Now, update[nameof(User.JoinedAt)]);
            Assert.Equal([(UsersKit.TenantId, account.ClientId, true)], _kit.Memberships.Hints());
            Assert.Empty(_kit.Identity.Deleted);
        }

        [Fact]
        public async Task A_rotation_keeps_the_join_and_deletes_the_previous_client_after_the_commit()
        {
            User robot = UsersKit.ServiceAccount(3, "svc-old");
            robot.JoinedAt = UsersKit.Now.AddDays(-30);
            ActionContext<User, int> context = Context(robot);

            Task<CredentialsResult> running = _kit.Service.RunIssueCredentialsAsync(context);

            // Nothing is deleted before the commit, so a failure in between leaves working credentials.
            Assert.Empty(_kit.Identity.Deleted);
            Assert.True(context.CompletePersisted());
            CredentialsResult result = await running;

            Assert.False(Assert.Single(context.Updates).ContainsKey(nameof(User.JoinedAt)));
            Assert.Equal(["svc-old"], _kit.Identity.Deleted);
            Assert.NotEqual("svc-old", result.ClientId);
        }

        [Fact]
        public async Task A_previous_client_whose_delete_fails_is_logged_and_the_new_credentials_still_answer()
        {
            User robot = UsersKit.ServiceAccount(3, "svc-old");
            _kit.Identity.DeleteFailure = new HttpRequestException("The identity server is down.");
            ActionContext<User, int> context = Context(robot);

            Task<CredentialsResult> running = _kit.Service.RunIssueCredentialsAsync(context);
            Assert.True(context.CompletePersisted());
            CredentialsResult result = await running;

            Assert.Equal(3, result.Id);
            Assert.Single(_kit.Logs.Entries, static entry => entry.EventId.Name == "ServiceAccountDeleteFailed");
        }

        [Fact]
        public async Task A_persist_that_never_commits_deletes_the_new_client_and_keeps_the_previous_one()
        {
            User robot = UsersKit.ServiceAccount(3, "svc-old");
            ActionContext<User, int> context = Context(robot);
            TimeoutException ended = new("The persist failed.");

            Task<CredentialsResult> running = _kit.Service.RunIssueCredentialsAsync(context);
            Assert.True(context.FaultPersisted(ended));

            Assert.Same(ended, await Assert.ThrowsAsync<TimeoutException>(() => running));
            string created = Assert.Single(_kit.Identity.Created).Account.ClientId;
            Assert.Equal([created], _kit.Identity.Deleted);
            Assert.Empty(_kit.Memberships.Recorded);
        }

        [Fact]
        public async Task A_persist_whose_commit_is_unknown_keeps_the_new_client_and_logs_it()
        {
            User robot = UsersKit.ServiceAccount(3, "svc-old");
            ActionContext<User, int> context = Context(robot);
            Exception ended = DataAccessTranslation.Unavailable(new DataAccessAmbiguousException(new TimeoutException("The connection was lost."), CommitVerdict.Unknown));

            Task<CredentialsResult> running = _kit.Service.RunIssueCredentialsAsync(context);
            Assert.True(context.FaultPersisted(ended));

            Assert.Same(ended, await Assert.ThrowsAnyAsync<Exception>(() => running));
            _ = Assert.Single(_kit.Identity.Created);
            Assert.Empty(_kit.Identity.Deleted);
            Assert.Empty(_kit.Memberships.Recorded);
            Assert.Single(_kit.Logs.Entries, static entry => entry.EventId.Name == "ServiceAccountClientKept");
        }

        [Fact]
        public async Task A_persist_the_probe_proved_rolled_back_deletes_the_new_client()
        {
            User robot = UsersKit.ServiceAccount(3, "svc-old");
            ActionContext<User, int> context = Context(robot);
            Exception ended = DataAccessTranslation.Unavailable(new DataAccessAmbiguousException(new TimeoutException("The connection was lost."), CommitVerdict.NotCommitted));

            Task<CredentialsResult> running = _kit.Service.RunIssueCredentialsAsync(context);
            Assert.True(context.FaultPersisted(ended));

            Assert.Same(ended, await Assert.ThrowsAnyAsync<Exception>(() => running));
            string created = Assert.Single(_kit.Identity.Created).Account.ClientId;
            Assert.Equal([created], _kit.Identity.Deleted);
            Assert.DoesNotContain(_kit.Logs.Entries, static entry => entry.EventId.Name == "ServiceAccountClientKept");
        }

        [Fact]
        public async Task A_target_the_validation_refused_creates_no_client()
        {
            User robot = UsersKit.ServiceAccount(3, null);
            ActionContext<User, int> context = Context(robot);
            context.Errors.Add(ValidationPath.Root, AccessCodes.NotService);
            InvalidOperationException ended = new("The action's validation failed.");

            Task<CredentialsResult> running = _kit.Service.RunIssueCredentialsAsync(context);
            Assert.True(context.FaultPersisted(ended));

            Assert.Same(ended, await Assert.ThrowsAsync<InvalidOperationException>(() => running));
            Assert.Empty(_kit.Identity.Created);
            Assert.Empty(context.Updates);
        }

        [Fact]
        public async Task The_secret_reaches_no_log_and_the_result_renders_it_redacted()
        {
            User robot = UsersKit.ServiceAccount(3, "svc-old");
            _kit.Identity.DeleteFailure = new HttpRequestException("The identity server is down.");
            ActionContext<User, int> context = Context(robot);

            Task<CredentialsResult> running = _kit.Service.RunIssueCredentialsAsync(context);
            Assert.True(context.CompletePersisted());
            CredentialsResult result = await running;

            Assert.NotEmpty(_kit.Logs.Entries);
            Assert.Empty(_kit.Logs.Mentioning(result.ClientSecret));
            Assert.DoesNotContain(result.ClientSecret, result.ToString(), StringComparison.Ordinal);
        }

        /// <summary>The action's context over one loaded account.</summary>
        /// <param name="account">The account.</param>
        /// <returns>The context.</returns>
        private static ActionContext<User, int> Context(User account)
        {
            return UsersKit.Action(Refusing.Of<IDataBatch>(), sandbox: false, [account.Id], account);
        }
    }
}
