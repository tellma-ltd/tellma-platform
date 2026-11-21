// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Errors;
using Tellma.Core.Abstractions.Identity;
using Tellma.Core.Abstractions.TableTypes;
using Tellma.Core.Abstractions.Validation;
using Tellma.Core.Data;
using Tellma.Core.Users;

namespace Tellma.Core.Tests.Users
{
    /// <summary>
    ///     The issue-credentials action: one client created between the round trips, the account
    ///     joined on its first credentials and linked to the client by a guarded statement, the client
    ///     hinted and the previous one deleted once the action committed, the new one deleted when the
    ///     persist fails unless a read-back finds the link committed, and the secret answered once and
    ///     never logged.
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
            (ActionContext<User, int> context, RecordingBatchProxy recorder) = Context(robot);

            Task<CredentialsResult> running = _kit.Service.RunIssueCredentialsAsync(context);
            Assert.False(running.IsCompleted);
            Assert.True(context.CompletePersisted());
            CredentialsResult result = await running;

            (string displayName, IdentityServiceAccount account) = Assert.Single(_kit.Identity.Created);
            Assert.Equal("Robot", displayName);
            Assert.Equal(new CredentialsResult(3, account.ClientId, account.ClientSecret), result);
            KeyValuePair<string, object?> unchanged = Assert.Single(Assert.Single(context.Updates));
            Assert.Equal((nameof(User.LastInviteError), null), (unchanged.Key, unchanged.Value));
            FormattableString swap = Assert.Single(recorder.Statements).Sql;
            Assert.Contains("[Subject] IS NULL", swap.Format, StringComparison.Ordinal);
            Assert.Equal(account.ClientId, swap.GetArgument(0));
            Assert.Equal(UsersKit.Now, swap.GetArgument(1));
            Assert.Equal([3], ((IEnumerable<IdList>)Assert.Single(recorder.Tables)).Select(static row => row.Id));
            Assert.Equal([(UsersKit.TenantId, account.ClientId, true)], _kit.Memberships.Hints());
            Assert.Empty(_kit.Identity.Deleted);
        }

        [Fact]
        public async Task A_rotation_keeps_the_join_and_deletes_the_previous_client_after_the_commit()
        {
            DateTimeOffset joined = UsersKit.Now.AddDays(-30);
            User robot = UsersKit.ServiceAccount(3, "svc-old");
            robot.JoinedAt = joined;
            (ActionContext<User, int> context, RecordingBatchProxy recorder) = Context(robot);

            Task<CredentialsResult> running = _kit.Service.RunIssueCredentialsAsync(context);

            // Nothing is deleted before the commit, so a failure in between leaves working credentials.
            Assert.Empty(_kit.Identity.Deleted);
            Assert.True(context.CompletePersisted());
            CredentialsResult result = await running;

            FormattableString swap = Assert.Single(recorder.Statements).Sql;
            Assert.Contains("[JoinedAt] = COALESCE([JoinedAt], {1})", swap.Format, StringComparison.Ordinal);
            Assert.Equal("svc-old", swap.GetArgument(3));
            Assert.Equal(["svc-old"], _kit.Identity.Deleted);
            Assert.NotEqual("svc-old", result.ClientId);
        }

        [Fact]
        public void The_link_is_guarded_by_the_loaded_client_and_the_account_being_active()
        {
            (IDataBatch batch, RecordingBatchProxy recorder) = RecordingBatchProxy.Create();

            _ = UserService<User>.ComposeClientSwap(batch, 3, "svc-old", "svc-new", UsersKit.Now);

            (FormattableString sql, SqlOptions? options, _) = Assert.Single(recorder.Statements);
            Assert.Contains("[IsActive] = 1 AND [Subject] = {3}", sql.Format, StringComparison.Ordinal);
            Assert.Contains("N'" + AccessCodes.Inactive + "'", sql.Format, StringComparison.Ordinal);
            Assert.Contains("N'" + AccessCodes.CredentialsConcurrent + "'", sql.Format, StringComparison.Ordinal);
            object?[] arguments = ["svc-new", UsersKit.Now, 3, "svc-old", 3];
            Assert.Equal(arguments, sql.GetArguments());
            Assert.NotNull(options);
            Assert.Equal(0, options.ResultSets);
            Assert.NotNull(options.UserIds);
        }

        [Fact]
        public async Task A_previous_client_whose_delete_fails_is_logged_and_the_new_credentials_still_answer()
        {
            User robot = UsersKit.ServiceAccount(3, "svc-old");
            _kit.Identity.DeleteFailure = new HttpRequestException("The identity server is down.");
            (ActionContext<User, int> context, _) = Context(robot);

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
            (ActionContext<User, int> context, _) = Context(robot);
            TimeoutException ended = new("The persist failed.");

            Task<CredentialsResult> running = _kit.Service.RunIssueCredentialsAsync(context);
            Assert.True(context.FaultPersisted(ended));

            Assert.Same(ended, await Assert.ThrowsAsync<TimeoutException>(() => running));
            string created = Assert.Single(_kit.Identity.Created).Account.ClientId;
            Assert.Equal([created], _kit.Identity.Deleted);
            Assert.Empty(_kit.Memberships.Recorded);
            Assert.Empty(_kit.Runner.Runs);
        }

        [Fact]
        public async Task A_persist_whose_commit_is_unknown_and_whose_row_names_another_client_deletes_the_new_one()
        {
            User robot = UsersKit.ServiceAccount(3, "svc-old");
            (ActionContext<User, int> context, _) = Context(robot);
            DependencyUnavailableException ended = Unknown(CommitVerdict.Unknown);

            Task<CredentialsResult> running = _kit.Service.RunIssueCredentialsAsync(context);
            Assert.True(context.FaultPersisted(ended));

            Assert.Same(ended, await Assert.ThrowsAnyAsync<Exception>(() => running));
            string created = Assert.Single(_kit.Identity.Created).Account.ClientId;
            (BatchPurpose purpose, FormattableString read) = Assert.Single(_kit.Runner.Runs);
            Assert.Equal(BatchPurpose.Read, purpose);
            object?[] arguments = [3, created];
            Assert.Equal(arguments, read.GetArguments());
            Assert.Equal([created], _kit.Identity.Deleted);
            Assert.Empty(_kit.Memberships.Recorded);
            Assert.DoesNotContain(_kit.Logs.Entries, static entry => entry.EventId.Name == "ServiceAccountClientKept");
        }

        [Fact]
        public async Task A_persist_whose_commit_is_unknown_and_whose_row_names_the_new_client_keeps_it_and_deletes_the_previous_one()
        {
            User robot = UsersKit.ServiceAccount(3, "svc-old");
            (ActionContext<User, int> context, _) = Context(robot);
            DependencyUnavailableException ended = Unknown(CommitVerdict.Unknown);
            _kit.Runner.Rows = [3];

            Task<CredentialsResult> running = _kit.Service.RunIssueCredentialsAsync(context);
            Assert.True(context.FaultPersisted(ended));

            // The commit happened, but the caller is still answered the failure: the secret reaches nobody.
            Assert.Same(ended, await Assert.ThrowsAnyAsync<Exception>(() => running));
            string created = Assert.Single(_kit.Identity.Created).Account.ClientId;
            Assert.Equal(["svc-old"], _kit.Identity.Deleted);
            Assert.Equal([(UsersKit.TenantId, created, true)], _kit.Memberships.Hints());
        }

        [Fact]
        public async Task A_persist_whose_commit_is_unknown_and_whose_read_back_fails_keeps_the_new_client_and_logs_it()
        {
            User robot = UsersKit.ServiceAccount(3, "svc-old");
            (ActionContext<User, int> context, _) = Context(robot);
            DependencyUnavailableException ended = Unknown(CommitVerdict.Unknown);
            _kit.Runner.Failure = new TimeoutException("The database is unreachable.");

            Task<CredentialsResult> running = _kit.Service.RunIssueCredentialsAsync(context);
            Assert.True(context.FaultPersisted(ended));

            Assert.Same(ended, await Assert.ThrowsAnyAsync<Exception>(() => running));
            _ = Assert.Single(_kit.Identity.Created);
            Assert.Empty(_kit.Identity.Deleted);
            Assert.Empty(_kit.Memberships.Recorded);
            Assert.Single(_kit.Logs.Entries, static entry => entry.EventId.Name == "ServiceAccountClientKept");
        }

        [Fact]
        public async Task A_persist_the_probe_proved_rolled_back_deletes_the_new_client_without_a_read_back()
        {
            User robot = UsersKit.ServiceAccount(3, "svc-old");
            (ActionContext<User, int> context, _) = Context(robot);
            DependencyUnavailableException ended = Unknown(CommitVerdict.NotCommitted);

            Task<CredentialsResult> running = _kit.Service.RunIssueCredentialsAsync(context);
            Assert.True(context.FaultPersisted(ended));

            Assert.Same(ended, await Assert.ThrowsAnyAsync<Exception>(() => running));
            string created = Assert.Single(_kit.Identity.Created).Account.ClientId;
            Assert.Equal([created], _kit.Identity.Deleted);
            Assert.Empty(_kit.Runner.Runs);
        }

        [Fact]
        public async Task A_target_the_validation_refused_creates_no_client()
        {
            User robot = UsersKit.ServiceAccount(3, null);
            (ActionContext<User, int> context, RecordingBatchProxy recorder) = Context(robot);
            context.Errors.Add(ValidationPath.Root, AccessCodes.NotService);
            InvalidOperationException ended = new("The action's validation failed.");

            Task<CredentialsResult> running = _kit.Service.RunIssueCredentialsAsync(context);
            Assert.True(context.FaultPersisted(ended));

            Assert.Same(ended, await Assert.ThrowsAsync<InvalidOperationException>(() => running));
            Assert.Empty(_kit.Identity.Created);
            Assert.Empty(context.Updates);
            Assert.Empty(recorder.Statements);
        }

        [Fact]
        public async Task The_secret_reaches_no_log_and_the_result_renders_it_redacted()
        {
            User robot = UsersKit.ServiceAccount(3, "svc-old");
            _kit.Identity.DeleteFailure = new HttpRequestException("The identity server is down.");
            (ActionContext<User, int> context, _) = Context(robot);

            Task<CredentialsResult> running = _kit.Service.RunIssueCredentialsAsync(context);
            Assert.True(context.CompletePersisted());
            CredentialsResult result = await running;

            Assert.NotEmpty(_kit.Logs.Entries);
            Assert.Empty(_kit.Logs.Mentioning(result.ClientSecret));
            Assert.DoesNotContain(result.ClientSecret, result.ToString(), StringComparison.Ordinal);
        }

        /// <summary>The action's context over one loaded account, with a batch that records the link.</summary>
        /// <param name="account">The account.</param>
        /// <returns>The context and its batch's recorder.</returns>
        private static (ActionContext<User, int> Context, RecordingBatchProxy Recorder) Context(User account)
        {
            (IDataBatch batch, RecordingBatchProxy recorder) = RecordingBatchProxy.Create();
            return (UsersKit.Action(batch, sandbox: false, [account.Id], account), recorder);
        }

        /// <summary>A persist failure after a lost connection, as the pipeline raises it.</summary>
        /// <param name="verdict">What the probe found.</param>
        /// <returns>The failure.</returns>
        private static DependencyUnavailableException Unknown(CommitVerdict verdict)
        {
            return DataAccessTranslation.Unavailable(new DataAccessAmbiguousException(new TimeoutException("The connection was lost."), verdict));
        }
    }
}
