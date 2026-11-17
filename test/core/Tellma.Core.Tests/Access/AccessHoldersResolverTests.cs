// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using System.Globalization;
using System.Text.Json;
using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Caching;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Localization;
using Tellma.Core.Abstractions.Settings;
using Tellma.Core.Abstractions.Tenancy;
using Tellma.Core.Access;
using Tellma.Core.Calendars;
using Tellma.Core.Data.Batches;
using Tellma.Core.Queryex;
using Tellma.Core.Tests.Data.Batches;

namespace Tellma.Core.Tests.Access
{
    /// <summary>
    ///     The holders' and the administrators' reads as they compose, with no database: each names
    ///     the current memberships exactly as the membership filter does, an unknown pair answers
    ///     with no round trip, and a users' filter beyond the compile ceilings is an error logged
    ///     with the roles behind it rather than a silent denial.
    /// </summary>
    public sealed class AccessHoldersResolverTests : IAsyncDisposable
    {
        /// <summary>The instant the scope runs at.</summary>
        private static readonly DateTimeOffset Now = new(2026, 11, 16, 9, 0, 0, TimeSpan.Zero);

        /// <summary>The membership predicate the reads must carry, over the memberships' alias.</summary>
        private static readonly string Current = AccessFilters.CurrentMembershipSql("M");

        /// <summary>The batches, over the default composition with a connection that refuses.</summary>
        private readonly BatchHarness _harness = new();

        /// <summary>The logs the resolver writes.</summary>
        private readonly ServiceProvider _logs = new ServiceCollection().AddLogging().AddFakeLogging().BuildServiceProvider();

        /// <summary>The scope the evaluator is resolved from.</summary>
        private readonly AsyncServiceScope _scope;

        /// <summary>Creates the scope.</summary>
        public AccessHoldersResolverTests()
        {
            _scope = _harness.Services.CreateAsyncScope();
        }

        /// <inheritdoc />
        public async ValueTask DisposeAsync()
        {
            await _scope.DisposeAsync();
            await _logs.DisposeAsync();
            _harness.Dispose();
        }

        [Fact]
        public async Task The_holders_read_names_current_memberships_as_the_membership_filter_does()
        {
            RecordingRunner runner = new(_harness, Connected(UserAccess.System));

            _ = await Assert.ThrowsAsync<RecordingRunner.SealedException>(() => Resolver(runner).ResolveAsync(CoreResources.User, AccessActions.Read, TestContext.Current.CancellationToken));

            // The candidates and their memberships are both current memberships.
            Assert.Equal(2, Occurrences(runner.Text!, Current));
            Assert.Contains("[core].[RoleMemberships] AS M", runner.Text, StringComparison.Ordinal);
        }

        [Fact]
        public async Task The_administrators_read_names_current_memberships_as_the_membership_filter_does()
        {
            RecordingRunner runner = new(_harness, Connected(UserAccess.System));

            _ = await Assert.ThrowsAsync<RecordingRunner.SealedException>(() => new AdministratorDirectory(runner).GetAdministratorIdsAsync(TestContext.Current.CancellationToken));

            Assert.Equal(1, Occurrences(runner.Text!, Current));
            Assert.Contains("[core].[RoleMemberships] AS M", runner.Text, StringComparison.Ordinal);
        }

        [Theory]
        [InlineData("core.Nothing", "Read", "core.Nothing", AccessProblemCode.UnknownResource)]
        [InlineData("core.user", "Fly", CoreResources.User, AccessProblemCode.UnknownAction)]
        public async Task An_unknown_pair_answers_no_holder_and_its_problem_with_no_round_trip(string resource, string action, string named, AccessProblemCode code)
        {
            RecordingRunner runner = new(_harness, Connected(UserAccess.System));

            AccessHolders holders = await Resolver(runner).ResolveAsync(resource, action, TestContext.Current.CancellationToken);

            Assert.Equal((named, action, AccessOutcome.Denied, 0, false), (holders.Resource, holders.Action, holders.Everyone, holders.Total, holders.Capped));
            Assert.Empty(holders.Grants);
            Assert.Empty(holders.Holders);
            Assert.Empty(holders.MembersByRole);
            Assert.Empty(holders.Criteria);
            AccessProblem problem = Assert.Single(holders.Problems);
            Assert.Equal((0, 0, code), (problem.PermissionId, problem.RoleId, problem.Code));
            Assert.Null(runner.Text);
        }

        [Fact]
        public async Task A_users_filter_beyond_the_compile_ceilings_is_an_error_logged_with_the_roles_behind_it()
        {
            // Forty grants of reading users, each a filter the access ceilings admit, whose union the
            // pipeline's do not.
            QueryexSchema schema = _harness.Services.GetRequiredService<IQueryexSchemaProvider>().GetSchema(MultilingualShape.Primary);
            PermissionRow[] rows =
            [
                .. Enumerable.Range(1, 40).Select(static index => new PermissionRow(
                    index,
                    10 + (index % 2),
                    "Readers",
                    false,
                    CoreResources.User,
                    AccessActions.Read,
                    $"Id in ({string.Join(", ", Enumerable.Range(index * 1000, 120).Select(static id => id.ToString(CultureInfo.InvariantCulture)))})",
                    QueryexLanguage.Version,
                    null,
                    null)),
            ];
            UserAccess access = _harness.Services.GetRequiredService<UserAccessBuilder>().Build(1, 42, Guid.Empty, Guid.Empty, rows, schema);
            Assert.Equal(40, access.Grants.Count);
            RecordingRunner runner = new(_harness, Connected(access));

            InvalidOperationException refused = await Assert.ThrowsAsync<InvalidOperationException>(() => Resolver(runner).ResolveAsync(CoreResources.Role, AccessActions.Read, TestContext.Current.CancellationToken));

            _ = Assert.IsType<ArgumentException>(refused.InnerException);
            FakeLogRecord logged = Assert.Single(_logs.GetFakeLogCollector().GetSnapshot(), static record => record.Id.Id == AccessEvents.FilterTooComplex);
            Assert.Equal(LogLevel.Error, logged.Level);
            Assert.Same(refused.InnerException, logged.Exception);
            Assert.Equal(
                ("1", "42", CoreResources.User, AccessActions.Read, "10, 11"),
                (logged.GetStructuredStateValue("TenantId"), logged.GetStructuredStateValue("UserId"), logged.GetStructuredStateValue("Resource"), logged.GetStructuredStateValue("Action"), logged.GetStructuredStateValue("RoleIds")));
        }

        /// <summary>How many times a text occurs in another.</summary>
        /// <param name="text">The text searched.</param>
        /// <param name="value">The text counted.</param>
        /// <returns>The count.</returns>
        private static int Occurrences(string text, string value)
        {
            int count = 0;
            for (int at = text.IndexOf(value, StringComparison.Ordinal); at >= 0; at = text.IndexOf(value, at + value.Length, StringComparison.Ordinal))
            {
                count++;
            }

            return count;
        }

        /// <summary>The scope's caller, connected with a set.</summary>
        /// <param name="access">The set.</param>
        /// <returns>The connected user.</returns>
        private static ConnectedUser Connected(UserAccess access)
        {
            return new ConnectedUser(
                42,
                UserKind.Individual,
                UserState.Joined,
                new UserProfile("Ada", null, null, null, null, null, null, null, null, null, null, null, null, null, Now),
                Guid.Empty,
                access,
                new Dictionary<string, Guid>(StringComparer.Ordinal),
                null);
        }

        /// <summary>A resolver over the composed services, the fixture's context and a runner.</summary>
        /// <param name="runner">The runner.</param>
        /// <returns>The resolver.</returns>
        private AccessHoldersResolver<User, Role> Resolver(IGuardedBatchRunner runner)
        {
            IServiceProvider services = _harness.Services;
            return new AccessHoldersResolver<User, Role>(
                new Holder(),
                runner,
                services.GetRequiredService<ISecurableRegistry>(),
                _scope.ServiceProvider.GetRequiredService<AccessEvaluator>(),
                services.GetRequiredService<HoldersComposer>(),
                services.GetRequiredService<IQueryexSchemaProvider>(),
                services.GetRequiredService<IEntityMetadataProvider>(),
                services.GetRequiredService<IOptions<AccessOptions>>(),
                new FakeTimeProvider(Now),
                _logs.GetRequiredService<ILoggerFactory>());
        }

        /// <summary>A guarded runner that composes one batch for the caller, records its text up to the send, and stops.</summary>
        /// <param name="harness">The batches.</param>
        /// <param name="user">The caller the batch is composed for.</param>
        private sealed class RecordingRunner(BatchHarness harness, ConnectedUser user) : IGuardedBatchRunner
        {
            /// <summary>The text of the batch composed; null before one is.</summary>
            public string? Text { get; private set; }

            /// <inheritdoc />
            public async Task<TResult> RunAsync<TResult>(
                BatchPurpose purpose,
                Func<ConnectedUser, IDataBatch, ValueTask> compose,
                Func<ConnectedUser, BatchOutcome, TResult> read,
                CancellationToken cancellationToken = default)
            {
                DataBatch batch = harness.Batch(purpose);
                await compose(user, batch);
                Text = (await BatchHarness.SealAsync(batch)).Text;
                throw new SealedException();
            }

            /// <summary>Stops the run once the batch's text is recorded: no database answers it.</summary>
            internal sealed class SealedException : Exception;
        }

        /// <summary>The scope's context: user 42 of tenant 1, with its settings.</summary>
        private sealed class Holder : IRequestContextAccessor
        {
            /// <inheritdoc />
            public RequestContext Current { get; } = RequestContext.Tenantless(new GregorianCalendarSystem(), Now) with
            {
                Tenant = new TenantDescriptor(1, "Acme", null, null, ["en"], TenantCategory.Live, TenantState.Active, null),
                Kind = PrincipalKind.User,
                Subject = "sub-42",
                UserId = 42,
                TenantSettings = new TenantSettings
                {
                    TenantId = 1,
                    Tag = new VersionTag(Guid.Empty),
                    Names = ["Acme", null, null],
                    Languages = [new LanguageInfo("en", "English", "E", false)],
                    Shape = MultilingualShape.Primary,
                    Calendars = [],
                    TimeZone = TimeZoneInfo.Utc,
                    SqlServerTimeZoneName = "UTC",
                    ModifiedAt = Now,
                    Entries = new Dictionary<string, JsonElement>(),
                },
            };
        }
    }
}
