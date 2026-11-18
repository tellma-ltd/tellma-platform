// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.Metrics.Testing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using System.Diagnostics.Metrics;
using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Caching;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Errors;
using Tellma.Core.Abstractions.Tenancy;
using Tellma.Core.Access;
using Tellma.Core.Caching;
using Tellma.Core.Calendars;
using Tellma.Core.Queryex;

namespace Tellma.Core.Tests.Access
{
    /// <summary>
    ///     The evaluator over a fake connection: decisions from the caller's set, the criteria read
    ///     on every evaluation and owners decided first; a stale denial re-checked once per scope and
    ///     never by the summary; a refusal that names the pair and no grant, and the step-up bar of
    ///     a sensitive pair; another user's set loaded once per scope; and the witness and the
    ///     decision counter of every evaluation of the caller, never of another user's or the summary's.
    /// </summary>
    public sealed class AccessEvaluatorTests : IDisposable
    {
        /// <summary>The documents' resource.</summary>
        private const string Doc = "acme.Doc";

        /// <summary>The document lines' resource, owned by the documents.</summary>
        private const string Line = "acme.DocLine";

        /// <summary>The scope's caller.</summary>
        private const int Caller = 7;

        /// <summary>Another user of the tenant.</summary>
        private const int Other = 9;

        /// <summary>The instant the scope starts, and the caller's set is computed.</summary>
        private static readonly DateTimeOffset Start = new(2026, 11, 16, 9, 0, 0, TimeSpan.Zero);

        /// <summary>The tenant's permissions tag.</summary>
        private static readonly Guid PermissionsTag = new("9e40a000-0000-0000-0000-000000000001");

        /// <summary>The fixture's tenant.</summary>
        private static readonly TenantDescriptor Acme = new(1, "Acme", null, null, ["en"], TenantCategory.Live, TenantState.Active, null);

        /// <summary>
        ///     The fixture's securables: documents with a sensitive save and an action that takes no
        ///     filter, document lines owned by their document, and the documents' former name.
        /// </summary>
        private static readonly SecurableRegistry Registry = SecurableRegistryTests.Compose(("acme", static builder => builder
            .Add(Doc, "Read", Doc)
            .Add(Doc, "Save", Doc, isSensitive: true)
            .Add(Doc, "Approve", null)
            .Add(Line, "Read", Line, owner: new SecurableOwner(Doc, "Doc"))
            .Alias("acme.Document", Doc)));

        /// <summary>The fixture's schema: documents and their lines.</summary>
        private static readonly QueryexSchema Schema = BuildSchema();

        /// <summary>The meter factory and the logs.</summary>
        private readonly ServiceProvider _services = new ServiceCollection().AddMetrics().AddLogging().BuildServiceProvider();

        /// <summary>The host's clock.</summary>
        private readonly FakeTimeProvider _time = new(Start);

        /// <summary>The scope's context: user 7 of the tenant, signed in by subject.</summary>
        private readonly Holder _holder = new();

        /// <summary>The criteria providers the evaluators are created over.</summary>
        private readonly List<IAccessCriteriaProvider> _providers = [];

        /// <summary>The access options: the default fast-deny window.</summary>
        private readonly AccessOptions _options = new();

        /// <summary>Builds the sets.</summary>
        private readonly UserAccessBuilder _builder;

        /// <summary>The access instruments.</summary>
        private readonly AccessMetrics _metrics;

        /// <summary>The process's permission sets.</summary>
        private readonly PermissionsCache _cache;

        /// <summary>The process's profiles.</summary>
        private readonly ProfileCache _profiles;

        /// <summary>The process's connected users.</summary>
        private readonly ConnectCache _connections = new(Options.Create(new CacheOptions()));

        /// <summary>The fake connection, connected as the caller with an empty set.</summary>
        private readonly FakeConnection _connection;

        /// <summary>The evaluators the test created, disposed with the fixture as their scopes would be.</summary>
        private readonly List<AccessEvaluator> _evaluators = [];

        /// <summary>Creates the fixture.</summary>
        public AccessEvaluatorTests()
        {
            IMeterFactory meters = _services.GetRequiredService<IMeterFactory>();
            _metrics = new AccessMetrics(meters);
            _builder = new UserAccessBuilder(new PermissionClassifier(Registry, new QueryexEngine()), _metrics, _services.GetRequiredService<ILoggerFactory>(), _time);
            _cache = new PermissionsCache(Options.Create(new CacheOptions()), meters);
            _profiles = new ProfileCache(Options.Create(new CacheOptions()), meters);
            _connection = new FakeConnection(Connected(Set(Caller)), _time);
        }

        /// <inheritdoc />
        public void Dispose()
        {
            foreach (AccessEvaluator evaluator in _evaluators)
            {
                evaluator.Dispose();
            }

            _cache.Dispose();
            _profiles.Dispose();
            _connections.Dispose();
            _services.Dispose();
        }

        // --- The deny re-check ---

        [Theory]
        [InlineData(0)]
        [InlineData(4)]
        [InlineData(5)]
        public async Task A_denial_within_the_fast_deny_window_is_trusted_with_no_round_trip(int seconds)
        {
            _time.Advance(TimeSpan.FromSeconds(seconds));

            AccessDecision decision = await NewEvaluator().EvaluateAsync(Doc, "Read", TestContext.Current.CancellationToken);

            Assert.Equal(AccessOutcome.Denied, decision.Outcome);
            Assert.Empty(_connection.Revalidations);
        }

        [Theory]
        [InlineData(6)]
        [InlineData(3600)]
        public async Task A_denial_outside_the_fast_deny_window_is_re_checked_and_decided_again_from_what_the_round_trip_returned(int seconds)
        {
            _time.Advance(TimeSpan.FromSeconds(seconds));
            ConnectedUser granted = Connected(Set(Caller, Row(1, Doc, "Read")));
            _connection.Revalidated = _ => granted;

            AccessDecision decision = await NewEvaluator().EvaluateAsync(Doc, "Read", TestContext.Current.CancellationToken);

            Assert.Equal(AccessOutcome.Unrestricted, decision.Outcome);
            Assert.Same(_connection.Connected, Assert.Single(_connection.Revalidations));
        }

        [Fact]
        public async Task The_window_is_the_configured_one()
        {
            _options.FastDenyWindow = TimeSpan.FromMinutes(1);
            _time.Advance(TimeSpan.FromSeconds(30));

            _ = await NewEvaluator().EvaluateAsync(Doc, "Read", TestContext.Current.CancellationToken);
            Assert.Empty(_connection.Revalidations);

            _time.Advance(TimeSpan.FromSeconds(31));
            _ = await NewEvaluator().EvaluateAsync(Doc, "Read", TestContext.Current.CancellationToken);
            _ = Assert.Single(_connection.Revalidations);
        }

        [Fact]
        public async Task The_re_check_runs_at_most_once_per_scope_however_many_evaluations_ask_for_it()
        {
            _time.Advance(TimeSpan.FromMinutes(1));

            // The round trip changes nothing, so every later denial of the scope is still stale.
            _connection.Revalidated = static user => user;
            AccessEvaluator evaluator = NewEvaluator();

            _ = await evaluator.EvaluateAsync(Doc, "Read", TestContext.Current.CancellationToken);
            _ = await evaluator.EvaluateAsync(Doc, "Save", TestContext.Current.CancellationToken);
            _ = await evaluator.EvaluateAsync([new SecurableRef(Doc, "Approve"), new SecurableRef(Line, "Read")], TestContext.Current.CancellationToken);
            _ = await Assert.ThrowsAsync<ForbiddenException>(() => evaluator.RequireAsync(Doc, "Read", TestContext.Current.CancellationToken));
            _ = await Task.WhenAll(Enumerable.Range(0, 16).Select(_ => Task.Run(() => evaluator.EvaluateAsync(Doc, "Read", TestContext.Current.CancellationToken), TestContext.Current.CancellationToken)));

            _ = Assert.Single(_connection.Revalidations);
        }

        [Fact]
        public async Task The_summary_never_re_checks_and_leaves_the_scopes_one_re_check_to_the_next_evaluation()
        {
            _time.Advance(TimeSpan.FromMinutes(1));
            AccessEvaluator evaluator = NewEvaluator();

            IReadOnlyList<AccessDecision> summary = await evaluator.EvaluateAllAsync(TestContext.Current.CancellationToken);

            Assert.Equal(Registry.All.Select(static securable => (securable.Resource, securable.Action)), summary.Select(static decision => (decision.Resource, decision.Action)));
            Assert.All(summary, static decision => Assert.Equal(AccessOutcome.Denied, decision.Outcome));
            Assert.Empty(_connection.Revalidations);

            _ = await evaluator.EvaluateAsync(Doc, "Read", TestContext.Current.CancellationToken);
            _ = Assert.Single(_connection.Revalidations);
        }

        [Fact]
        public async Task A_passing_re_check_advances_the_sets_validation_so_the_next_scope_trusts_its_denial()
        {
            _time.Advance(TimeSpan.FromMinutes(1));
            DateTimeOffset rechecked = _time.GetUtcNow();

            // The default round trip passes: the same user, the set marked validated at the host's now.
            _ = await NewEvaluator().EvaluateAsync(Doc, "Read", TestContext.Current.CancellationToken);
            Assert.Equal(rechecked, _connection.Connected.Access.ValidatedAt);

            _time.Advance(TimeSpan.FromSeconds(3));
            AccessDecision decision = await NewEvaluator().EvaluateAsync(Doc, "Read", TestContext.Current.CancellationToken);

            Assert.Equal(AccessOutcome.Denied, decision.Outcome);
            _ = Assert.Single(_connection.Revalidations);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task A_re_check_that_fails_leaves_the_scopes_one_re_check_to_the_next_stale_denial(bool cancelled)
        {
            _time.Advance(TimeSpan.FromMinutes(1));

            // --- The first round trip fails; the next one completes and changes nothing, so the set stays stale ---
            int calls = 0;
            Exception failure = cancelled ? new OperationCanceledException("The round trip was cancelled.") : new TimeoutException("The round trip timed out.");
            _connection.Revalidated = user => ++calls > 1 ? user : throw failure;
            AccessEvaluator evaluator = NewEvaluator();

            Assert.Same(failure, await Assert.ThrowsAnyAsync<Exception>(() => evaluator.EvaluateAsync(Doc, "Read", TestContext.Current.CancellationToken)));
            AccessDecision retried = await evaluator.EvaluateAsync(Doc, "Read", TestContext.Current.CancellationToken);
            AccessDecision later = await evaluator.EvaluateAsync(Doc, "Save", TestContext.Current.CancellationToken);

            // --- The failed one spent nothing: the next stale denial re-checked, and the one after it did not ---
            Assert.Equal((AccessOutcome.Denied, AccessOutcome.Denied), (retried.Outcome, later.Outcome));
            Assert.Equal(2, _connection.Revalidations.Count);
        }

        [Fact]
        public async Task Only_the_denial_of_a_registered_pair_is_re_checked()
        {
            _connection.Connected = Connected(Set(Caller, Row(1, Doc, "Read")));
            _time.Advance(TimeSpan.FromMinutes(1));
            AccessEvaluator evaluator = NewEvaluator();

            Assert.Equal(AccessOutcome.Unrestricted, (await evaluator.EvaluateAsync(Doc, "Read", TestContext.Current.CancellationToken)).Outcome);
            Assert.Equal(AccessOutcome.Denied, (await evaluator.EvaluateAsync("acme.Nothing", "Read", TestContext.Current.CancellationToken)).Outcome);
            Assert.Equal(AccessOutcome.Denied, (await evaluator.EvaluateAsync(Doc, "Fly", TestContext.Current.CancellationToken)).Outcome);

            Assert.Empty(_connection.Revalidations);
        }

        [Fact]
        public async Task The_batch_decides_in_order_from_one_connection_and_at_most_one_re_check()
        {
            _time.Advance(TimeSpan.FromMinutes(1));
            ConnectedUser granted = Connected(Set(Caller, Row(1, Doc, "Read", "Amount > 1"), Row(2, Doc, "Save", "Amount > 5")));
            _connection.Revalidated = _ => granted;

            IReadOnlyList<AccessDecision> decisions = await NewEvaluator().EvaluateAsync(
                [new SecurableRef("acme.document", "read"), new SecurableRef("acme.Nothing", "Read"), new SecurableRef(Line, "Read"), new SecurableRef(Doc, "Approve"), new SecurableRef(Doc, "Save")],
                TestContext.Current.CancellationToken);

            Assert.Equal(
                [(Doc, "Read", AccessOutcome.Filtered), ("acme.Nothing", "Read", AccessOutcome.Denied), (Line, "Read", AccessOutcome.Filtered), (Doc, "Approve", AccessOutcome.Denied), (Doc, "Save", AccessOutcome.Filtered)],
                decisions.Select(static decision => (decision.Resource, decision.Action, decision.Outcome)));
            _ = Assert.Single(_connection.Revalidations);
            Assert.Equal(1, _connection.Currents);
        }

        [Fact]
        public async Task The_system_and_a_scope_without_a_caller_never_connect_and_never_re_check()
        {
            _time.Advance(TimeSpan.FromMinutes(1));

            _holder.Current = _holder.Current with { Kind = PrincipalKind.System, Subject = "system", UserId = null };
            AccessDecision system = await NewEvaluator().EvaluateAsync(Doc, "Save", TestContext.Current.CancellationToken);

            _holder.Current = _holder.Current with { Kind = PrincipalKind.Anonymous, Subject = null };
            AccessDecision anonymous = await NewEvaluator().EvaluateAsync(Doc, "Read", TestContext.Current.CancellationToken);

            Assert.Equal(AccessOutcome.Unrestricted, system.Outcome);
            _ = Assert.IsType<SystemAccessGrant>(Assert.Single(system.Grants));
            Assert.Equal(AccessOutcome.Denied, anonymous.Outcome);
            Assert.Equal(0, _connection.Currents);
            Assert.Empty(_connection.Revalidations);
        }

        // --- Decisions: criteria, owners and unknown pairs ---

        [Fact]
        public async Task A_scope_without_a_caller_is_denied_where_a_criterion_would_grant_on_its_own()
        {
            _providers.Add(new CriteriaSource(Doc, new AccessCriterion("Read", FilterTree.Leaf("OwnerId = 7"), "self")));

            _holder.Current = _holder.Current with { Kind = PrincipalKind.Anonymous, Subject = null, UserId = null };
            AccessEvaluator anonymous = NewEvaluator();
            _holder.Current = RequestContext.Tenantless(new GregorianCalendarSystem(), Start) with { Kind = PrincipalKind.User, Subject = "sub-7" };
            AccessEvaluator tenantless = NewEvaluator();

            Assert.Equal(AccessOutcome.Denied, (await anonymous.EvaluateAsync(Doc, "Read", TestContext.Current.CancellationToken)).Outcome);
            Assert.Equal(AccessOutcome.Denied, (await tenantless.EvaluateAsync(Doc, "Read", TestContext.Current.CancellationToken)).Outcome);
            Assert.Empty((await anonymous.GetAccessAsync(TestContext.Current.CancellationToken)).Grants);
            Assert.Equal(0, _connection.Currents);
        }

        [Fact]
        public async Task The_criteria_are_read_on_every_evaluation()
        {
            CriteriaSource criteria = new(Doc);
            _providers.Add(criteria);
            AccessEvaluator evaluator = NewEvaluator();

            AccessDecision before = await evaluator.EvaluateAsync(Doc, "Read", TestContext.Current.CancellationToken);
            criteria.Criteria = [new AccessCriterion("Read", FilterTree.Leaf("OwnerId = 7"), "self")];
            AccessDecision after = await evaluator.EvaluateAsync(Doc, "Read", TestContext.Current.CancellationToken);

            Assert.Equal(AccessOutcome.Denied, before.Outcome);
            Assert.Equal(AccessOutcome.Filtered, after.Outcome);
            Assert.Equal("self", Assert.IsType<BespokeAccessGrant>(Assert.Single(after.Grants)).Reason);
        }

        [Fact]
        public async Task The_criteria_of_every_provider_of_the_resource_are_read_and_no_others()
        {
            _providers.Add(new CriteriaSource("ACME.DOC", new AccessCriterion("Read", FilterTree.Leaf("OwnerId = 7"), "self")));
            _providers.Add(new CriteriaSource(Doc, new AccessCriterion("*", FilterTree.Leaf("Amount = 0"), "empty")));
            _providers.Add(new CriteriaSource(Line, new AccessCriterion("Read", FilterTree.Leaf("Id = 1"), "first")));

            AccessDecision decision = await NewEvaluator().EvaluateAsync(Doc, "Read", TestContext.Current.CancellationToken);

            Assert.Equal(AccessOutcome.Filtered, decision.Outcome);
            Assert.Equal(["empty", "self"], decision.Grants.OfType<BespokeAccessGrant>().Select(static grant => grant.Reason).Order(StringComparer.Ordinal));
        }

        [Fact]
        public async Task A_child_securable_is_decided_through_its_owners_decision_on_the_same_action()
        {
            _connection.Connected = Connected(Set(Caller, Row(1, Doc, "Read", "Amount > 1")));
            AccessDecision filtered = await NewEvaluator().EvaluateAsync(Line, "Read", TestContext.Current.CancellationToken);

            _connection.Connected = Connected(Set(Caller, Row(2, Doc, "*")));
            AccessDecision unrestricted = await NewEvaluator().EvaluateAsync(Line, "Read", TestContext.Current.CancellationToken);

            _connection.Connected = Connected(Set(Caller));
            AccessDecision denied = await NewEvaluator().EvaluateAsync(Line, "Read", TestContext.Current.CancellationToken);

            Assert.Equal(AccessOutcome.Filtered, filtered.Outcome);
            Assert.Equal([1], filtered.Grants.OfType<RoleAccessGrant>().Select(static grant => grant.PermissionId));
            Assert.Equal(AccessOutcome.Unrestricted, unrestricted.Outcome);
            Assert.Equal(AccessOutcome.Denied, denied.Outcome);
        }

        [Theory]
        [InlineData("acme.Nothing", "Read", "acme.Nothing", AccessProblemCode.UnknownResource)]
        [InlineData("acme.document", "Fly", Doc, AccessProblemCode.UnknownAction)]
        [InlineData("ACME.DOC", "*", Doc, AccessProblemCode.UnknownAction)]
        public async Task An_unknown_pair_is_denied_with_one_problem_that_names_no_stored_permission(string resource, string action, string named, AccessProblemCode code)
        {
            _connection.Connected = Connected(Set(Caller, Row(1, "*", "*")));
            AccessEvaluator evaluator = NewEvaluator();

            AccessDecision mine = await evaluator.EvaluateAsync(resource, action, TestContext.Current.CancellationToken);
            AccessDecision theirs = await evaluator.EvaluateForAsync(Other, resource, action, TestContext.Current.CancellationToken);

            foreach (AccessDecision decision in new[] { mine, theirs })
            {
                Assert.Equal((named, action, AccessOutcome.Denied), (decision.Resource, decision.Action, decision.Outcome));
                Assert.Empty(decision.Grants);
                AccessProblem problem = Assert.Single(decision.Problems);
                Assert.Equal((0, 0, named, action, code), (problem.PermissionId, problem.RoleId, problem.Resource, problem.Action, problem.Code));
            }

            Assert.Empty(_connection.Loads);
        }

        // --- RequireAsync: the refusal and the step-up bar ---

        [Fact]
        public async Task Requiring_a_denied_pair_is_refused_naming_the_registered_pair_and_no_grant()
        {
            ForbiddenException refused = await Assert.ThrowsAsync<ForbiddenException>(() => NewEvaluator().RequireAsync("acme.Document", "read", TestContext.Current.CancellationToken));

            Assert.Equal("forbidden", refused.Code);
            Assert.Equal((Doc, "Read", ForbiddenReason.NoGrant), (refused.Resource, refused.Action!, refused.Reason));
            Assert.Null(refused.Path);
        }

        [Fact]
        public async Task Requiring_an_unknown_pair_is_refused_naming_the_pair_as_asked()
        {
            ForbiddenException refused = await Assert.ThrowsAsync<ForbiddenException>(() => NewEvaluator().RequireAsync("acme.Nothing", "Read", TestContext.Current.CancellationToken));

            Assert.Equal(("acme.Nothing", "Read", ForbiddenReason.NoGrant), (refused.Resource, refused.Action!, refused.Reason));
        }

        [Fact]
        public async Task Requiring_an_allowed_pair_returns_its_decision()
        {
            _connection.Connected = Connected(Set(Caller, Row(1, Doc, "Read", "Amount > 1")));

            AccessDecision decision = await NewEvaluator().RequireAsync(Doc, "Read", TestContext.Current.CancellationToken);

            Assert.Equal(AccessOutcome.Filtered, decision.Outcome);
            Assert.NotNull(decision.Filter);
        }

        [Fact]
        public async Task A_sensitive_pair_the_caller_lacks_is_refused_before_the_step_up_bar_is_judged()
        {
            _ = await Assert.ThrowsAsync<ForbiddenException>(() => NewEvaluator().RequireAsync(Doc, "Save", TestContext.Current.CancellationToken));
        }

        [Theory]
        [InlineData(null, null, null, false)]
        [InlineData(StepUpBar.DefaultAcr, null, null, false)]
        [InlineData("urn:tellma:acr:pwd", 1, 1, false)]
        [InlineData(StepUpBar.DefaultAcr, 60, 16, false)]
        [InlineData(StepUpBar.DefaultAcr, 20, null, false)]
        [InlineData(StepUpBar.DefaultAcr, 60, 15, true)]
        [InlineData(StepUpBar.DefaultAcr, 60, 1, true)]
        [InlineData(StepUpBar.DefaultAcr, 5, null, true)]
        [InlineData("urn:tellma:acr:aal1", 1, 1, false)]
        [InlineData("urn:tellma:acr:aal3", 60, 1, true)]
        [InlineData("phrh", 60, 1, true)]
        [InlineData("phr", 60, 1, true)]
        [InlineData("urn:tellma:acr:aal3", 60, 16, false)]
        [InlineData("urn:tellma:acr:mfa", 60, 1, false)]
        public async Task A_sensitive_pair_asks_for_a_recent_sign_in_of_the_bars_class_or_a_higher_tier(string? acr, int? signedInMinutesAgo, int? reachedMinutesAgo, bool passes)
        {
            _connection.Connected = Connected(Set(Caller, Row(1, Doc, "Save")));
            DateTimeOffset now = _time.GetUtcNow();
            _holder.Current = _holder.Current with
            {
                Assurance = acr is null
                    ? null
                    : new AuthenticationAssurance(acr, signedInMinutesAgo is int signedIn ? now.AddMinutes(-signedIn) : null, reachedMinutesAgo is int reached ? now.AddMinutes(-reached) : null),
            };
            AccessEvaluator evaluator = NewEvaluator();

            if (passes)
            {
                Assert.Equal(AccessOutcome.Unrestricted, (await evaluator.RequireAsync(Doc, "Save", TestContext.Current.CancellationToken)).Outcome);
            }
            else
            {
                StepUpRequiredException refused = await Assert.ThrowsAsync<StepUpRequiredException>(() => evaluator.RequireAsync(Doc, "Save", TestContext.Current.CancellationToken));
                Assert.Equal((StepUpBar.DefaultAcr, (TimeSpan?)StepUpBar.DefaultMaxAge), (refused.Acr, refused.MaxAge));
            }
        }

        [Fact]
        public async Task The_sessions_age_is_judged_on_the_hosts_clock()
        {
            _connection.Connected = Connected(Set(Caller, Row(1, Doc, "Save")));
            _holder.Current = _holder.Current with { Assurance = new AuthenticationAssurance(StepUpBar.DefaultAcr, Start, Start) };
            AccessEvaluator evaluator = NewEvaluator();

            _time.Advance(TimeSpan.FromMinutes(16));

            _ = await Assert.ThrowsAsync<StepUpRequiredException>(() => evaluator.RequireAsync(Doc, "Save", TestContext.Current.CancellationToken));
        }

        [Fact]
        public async Task The_step_up_bar_is_the_one_registered()
        {
            _connection.Connected = Connected(Set(Caller, Row(1, Doc, "Save")));
            _holder.Current = _holder.Current with { Assurance = new AuthenticationAssurance(StepUpBar.DefaultAcr, Start, Start) };
            StepUpBar bar = new(new RequireAssuranceMetadata("urn:acme:acr:hardware", TimeSpan.FromMinutes(5)));

            StepUpRequiredException refused = await Assert.ThrowsAsync<StepUpRequiredException>(() => NewEvaluator(bar).RequireAsync(Doc, "Save", TestContext.Current.CancellationToken));

            Assert.Equal(("urn:acme:acr:hardware", (TimeSpan?)TimeSpan.FromMinutes(5)), (refused.Acr, refused.MaxAge));
        }

        [Fact]
        public async Task A_service_account_cannot_exercise_a_sensitive_pair_and_may_exercise_any_other()
        {
            _connection.Connected = Connected(Set(Caller, Row(1, Doc, "*")));
            _holder.Current = _holder.Current with { Kind = PrincipalKind.ServiceAccount, Subject = "client-7" };
            AccessEvaluator evaluator = NewEvaluator();

            _ = await Assert.ThrowsAsync<IndividualRequiredException>(() => evaluator.RequireAsync(Doc, "Save", TestContext.Current.CancellationToken));
            Assert.Equal(AccessOutcome.Unrestricted, (await evaluator.RequireAsync(Doc, "Read", TestContext.Current.CancellationToken)).Outcome);
        }

        [Fact]
        public async Task The_platforms_own_work_exercises_a_sensitive_pair_with_no_session()
        {
            _holder.Current = _holder.Current with { Kind = PrincipalKind.System, Subject = "system", UserId = null };

            AccessDecision decision = await NewEvaluator().RequireAsync(Doc, "Save", TestContext.Current.CancellationToken);

            Assert.Equal(AccessOutcome.Unrestricted, decision.Outcome);
        }

        [Fact]
        public async Task The_arguments_are_checked()
        {
            AccessEvaluator evaluator = NewEvaluator();

            _ = await Assert.ThrowsAsync<ArgumentNullException>(() => evaluator.EvaluateAsync(null!, "Read", TestContext.Current.CancellationToken));
            _ = await Assert.ThrowsAsync<ArgumentNullException>(() => evaluator.EvaluateAsync(Doc, null!, TestContext.Current.CancellationToken));
            _ = await Assert.ThrowsAsync<ArgumentException>(() => evaluator.RequireAsync(" ", "Read", TestContext.Current.CancellationToken));
            _ = await Assert.ThrowsAsync<ArgumentException>(() => evaluator.RequireAsync(Doc, "", TestContext.Current.CancellationToken));
            _ = await Assert.ThrowsAsync<ArgumentNullException>(() => evaluator.EvaluateAsync(null!, TestContext.Current.CancellationToken));
            _ = await Assert.ThrowsAsync<ArgumentException>(() => evaluator.EvaluateAsync([new SecurableRef(Doc, "Read"), null!], TestContext.Current.CancellationToken));
            _ = await Assert.ThrowsAsync<ArgumentNullException>(() => evaluator.EvaluateForAsync(Other, null!, "Read", TestContext.Current.CancellationToken));
            Assert.Empty(_connection.Revalidations);
        }

        // --- The witness and the decision counter ---

        [Fact]
        public async Task Every_evaluation_and_requirement_of_the_caller_is_witnessed_as_registered_and_counted_by_outcome()
        {
            using MetricCollector<long> counted = new(_services.GetRequiredService<IMeterFactory>(), AccessTelemetryNames.MeterName, AccessTelemetryNames.Decisions);
            _connection.Connected = Connected(Set(Caller, Row(1, Doc, "Read", "Amount > 1"), Row(2, Line, "Read")));
            AccessWitness witness = NewWitness();
            AccessEvaluator evaluator = NewEvaluator(witness: witness);

            _ = await evaluator.EvaluateAsync("acme.document", "READ", TestContext.Current.CancellationToken);
            _ = await evaluator.RequireAsync(Line, "read", TestContext.Current.CancellationToken);
            _ = await Assert.ThrowsAsync<ForbiddenException>(() => evaluator.RequireAsync(Doc, "Approve", TestContext.Current.CancellationToken));
            _ = await evaluator.EvaluateAsync([new SecurableRef("acme.Nothing", "Run"), new SecurableRef(Doc, "Save")], TestContext.Current.CancellationToken);

            Assert.Equal(
                [(Doc, "Approve"), (Doc, "Read"), (Doc, "Save"), (Line, "Read"), ("acme.Nothing", "Run")],
                witness.Evaluated
                    .Select(static pair => (pair.Resource, pair.Action))
                    .OrderBy(static pair => pair.Resource, StringComparer.Ordinal)
                    .ThenBy(static pair => pair.Action, StringComparer.Ordinal));
            Assert.True(witness.HasEvaluated("acme.Document", "read"));
            Assert.Equal(
                [AccessTelemetryNames.OutcomeFiltered, AccessTelemetryNames.OutcomeUnrestricted, AccessTelemetryNames.OutcomeDenied, AccessTelemetryNames.OutcomeDenied, AccessTelemetryNames.OutcomeDenied],
                counted.GetMeasurementSnapshot().Select(static measurement => (string?)measurement.Tags[AccessTelemetryNames.OutcomeTag]));
            Assert.All(counted.GetMeasurementSnapshot(), static measurement => Assert.Equal((1L, 1), (measurement.Value, measurement.Tags.Count)));
        }

        [Fact]
        public async Task A_re_checked_evaluation_is_counted_once_by_its_final_outcome()
        {
            using MetricCollector<long> counted = new(_services.GetRequiredService<IMeterFactory>(), AccessTelemetryNames.MeterName, AccessTelemetryNames.Decisions);
            _time.Advance(TimeSpan.FromMinutes(1));
            ConnectedUser granted = Connected(Set(Caller, Row(1, Doc, "Read")));
            _connection.Revalidated = _ => granted;

            _ = await NewEvaluator().EvaluateAsync(Doc, "Read", TestContext.Current.CancellationToken);

            Assert.Equal(AccessTelemetryNames.OutcomeUnrestricted, (string?)Assert.Single(counted.GetMeasurementSnapshot()).Tags[AccessTelemetryNames.OutcomeTag]);
        }

        [Fact]
        public async Task Another_users_decisions_and_the_summary_are_neither_witnessed_nor_counted()
        {
            using MetricCollector<long> counted = new(_services.GetRequiredService<IMeterFactory>(), AccessTelemetryNames.MeterName, AccessTelemetryNames.Decisions);
            _connection.Others[Other] = Set(Other, Row(1, Doc, "Read"));
            AccessWitness witness = NewWitness();
            AccessEvaluator evaluator = NewEvaluator(witness: witness);

            _ = await evaluator.EvaluateForAsync(Other, Doc, "Read", TestContext.Current.CancellationToken);
            _ = await evaluator.EvaluateForAsync(Caller, Doc, "Save", TestContext.Current.CancellationToken);
            _ = await evaluator.EvaluateAllAsync(TestContext.Current.CancellationToken);

            Assert.Empty(witness.Evaluated);
            Assert.False(witness.HasEvaluated(Doc, "Read"));
            Assert.Empty(counted.GetMeasurementSnapshot());
        }

        // --- EvaluateForAsync: another user's set, once per scope ---

        [Fact]
        public async Task Another_user_is_loaded_once_per_scope_however_many_decisions_and_callers_share_it()
        {
            _connection.Others[Other] = Set(Other, Row(1, Doc, "Read"), Row(2, Doc, "Save", "Amount > 1"));
            _connection.Hold = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            AccessEvaluator evaluator = NewEvaluator();

            // Every call races for the same load while it is held, and then decides from it.
            Task<AccessDecision>[] racing = [.. Enumerable.Range(0, 8).Select(index => Task.Run(
                () => evaluator.EvaluateForAsync(Other, Doc, index % 2 == 0 ? "Read" : "Save", TestContext.Current.CancellationToken),
                TestContext.Current.CancellationToken))];
            _connection.Hold.SetResult();
            AccessDecision[] decisions = await Task.WhenAll(racing);
            AccessDecision later = await evaluator.EvaluateForAsync(Other, Line, "Read", TestContext.Current.CancellationToken);

            Assert.Equal([Other], _connection.Loads);
            Assert.All(decisions.Where(static decision => decision.Action == "Read"), static decision => Assert.Equal(AccessOutcome.Unrestricted, decision.Outcome));
            Assert.All(decisions.Where(static decision => decision.Action == "Save"), static decision => Assert.Equal(AccessOutcome.Filtered, decision.Outcome));
            Assert.Equal(AccessOutcome.Unrestricted, later.Outcome);

            // The next scope loads again: nothing of another user's is cached.
            _ = await NewEvaluator().EvaluateForAsync(Other, Doc, "Read", TestContext.Current.CancellationToken);
            Assert.Equal([Other, Other], _connection.Loads);
            Assert.Equal(0, _connection.Currents);
        }

        [Fact]
        public async Task The_callers_own_id_is_decided_from_the_callers_set_with_no_load_and_no_re_check()
        {
            _connection.Connected = Connected(Set(Caller, Row(1, Doc, "Read", "Amount > 1")));
            _time.Advance(TimeSpan.FromMinutes(1));

            // A request names its caller by subject alone until the connection says who it is.
            _holder.Current = _holder.Current with { UserId = null };
            AccessEvaluator evaluator = NewEvaluator();

            AccessDecision read = await evaluator.EvaluateForAsync(Caller, Doc, "Read", TestContext.Current.CancellationToken);
            AccessDecision save = await evaluator.EvaluateForAsync(Caller, Doc, "Save", TestContext.Current.CancellationToken);

            Assert.Equal(AccessOutcome.Filtered, read.Outcome);
            Assert.Equal(AccessOutcome.Denied, save.Outcome);
            Assert.Empty(_connection.Loads);
            Assert.Empty(_connection.Revalidations);
        }

        [Theory]
        [InlineData(Other)]
        [InlineData(0)]
        [InlineData(-1)]
        public async Task An_unknown_inactive_or_system_user_is_denied_where_a_criterion_would_grant_on_its_own(int userId)
        {
            _providers.Add(new CriteriaSource(Doc, new AccessCriterion("*", FilterTree.Leaf("OwnerId = 9"), "self")));
            _connection.Others[Other] = null;

            AccessDecision decision = await NewEvaluator().EvaluateForAsync(userId, Doc, "Read", TestContext.Current.CancellationToken);

            Assert.Equal((Doc, "Read", AccessOutcome.Denied), (decision.Resource, decision.Action, decision.Outcome));
            Assert.Empty(decision.Grants);
            int[] loaded = userId == Other ? [Other] : [];
            Assert.Equal(loaded, _connection.Loads);
        }

        [Fact]
        public async Task Another_users_decision_reads_the_criteria_and_decides_owners_first()
        {
            _providers.Add(new CriteriaSource(Doc, new AccessCriterion("Read", FilterTree.Leaf("OwnerId = 9"), "self")));
            _connection.Others[Other] = Set(Other);

            AccessDecision decision = await NewEvaluator().EvaluateForAsync(Other, Line, "Read", TestContext.Current.CancellationToken);

            Assert.Equal(AccessOutcome.Filtered, decision.Outcome);
            Assert.Equal("self", Assert.IsType<BespokeAccessGrant>(Assert.Single(decision.Grants)).Reason);
        }

        [Fact]
        public async Task Outside_a_tenant_another_user_is_denied_with_no_load()
        {
            _holder.Current = RequestContext.Tenantless(new GregorianCalendarSystem(), Start);
            _connection.Others[Other] = Set(Other, Row(1, "*", "*"));

            AccessDecision decision = await NewEvaluator().EvaluateForAsync(Other, Doc, "Read", TestContext.Current.CancellationToken);

            Assert.Equal(AccessOutcome.Denied, decision.Outcome);
            Assert.Empty(_connection.Loads);
        }

        [Fact]
        public async Task A_scope_without_a_caller_is_denied_another_users_decision_with_no_load()
        {
            _connection.Others[Other] = Set(Other, Row(1, "*", "*"));

            // An anonymous principal of the tenant, and a user principal named by neither subject nor id.
            _holder.Current = _holder.Current with { Kind = PrincipalKind.Anonymous, Subject = null, UserId = null };
            AccessDecision anonymous = await NewEvaluator().EvaluateForAsync(Other, Doc, "Read", TestContext.Current.CancellationToken);
            _holder.Current = _holder.Current with { Kind = PrincipalKind.User, Subject = null, UserId = null };
            AccessDecision unnamed = await NewEvaluator().EvaluateForAsync(Other, Doc, "Read", TestContext.Current.CancellationToken);

            Assert.All([anonymous, unnamed], static decision =>
            {
                Assert.Equal((Doc, "Read", AccessOutcome.Denied), (decision.Resource, decision.Action, decision.Outcome));
                Assert.Empty(decision.Grants);
            });
            Assert.Empty(_connection.Loads);
            Assert.Equal(0, _connection.Currents);
        }

        [Fact]
        public async Task The_platforms_own_work_decides_another_user_from_their_own_set()
        {
            _holder.Current = _holder.Current with { Kind = PrincipalKind.System, Subject = "system", UserId = WellKnownIds.SystemUserId };
            _connection.Others[Other] = Set(Other, Row(1, Doc, "Read"));
            AccessEvaluator evaluator = NewEvaluator();

            Assert.Equal(AccessOutcome.Unrestricted, (await evaluator.EvaluateForAsync(Other, Doc, "Read", TestContext.Current.CancellationToken)).Outcome);
            Assert.Equal(AccessOutcome.Denied, (await evaluator.EvaluateForAsync(Other, Doc, "Save", TestContext.Current.CancellationToken)).Outcome);
            Assert.Equal([Other], _connection.Loads);
            Assert.Equal(0, _connection.Currents);
        }

        [Fact]
        public async Task A_load_that_failed_is_not_shared_and_the_next_call_loads_again()
        {
            _connection.Others[Other] = Set(Other, Row(1, Doc, "Read"));
            _connection.FailingLoads = 1;
            AccessEvaluator evaluator = NewEvaluator();

            _ = await Assert.ThrowsAsync<TimeoutException>(() => evaluator.EvaluateForAsync(Other, Doc, "Read", TestContext.Current.CancellationToken));
            AccessDecision decision = await evaluator.EvaluateForAsync(Other, Doc, "Read", TestContext.Current.CancellationToken);
            _ = await evaluator.EvaluateForAsync(Other, Doc, "Save", TestContext.Current.CancellationToken);

            Assert.Equal(AccessOutcome.Unrestricted, decision.Outcome);
            Assert.Equal([Other, Other], _connection.Loads);
        }

        [Fact]
        public async Task A_call_that_stops_waiting_leaves_the_shared_load_to_the_other_calls()
        {
            _connection.Others[Other] = Set(Other, Row(1, Doc, "Read"));
            _connection.Hold = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            AccessEvaluator evaluator = NewEvaluator();
            using CancellationTokenSource impatient = new();

            // The first call starts the load under its own token, the second shares it under another.
            Task<AccessDecision> first = evaluator.EvaluateForAsync(Other, Doc, "Read", impatient.Token);
            Task<AccessDecision> second = evaluator.EvaluateForAsync(Other, Doc, "Read", TestContext.Current.CancellationToken);
            await impatient.CancelAsync();

            _ = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);
            Assert.False(second.IsCompleted);
            _connection.Hold.SetResult();
            Assert.Equal(AccessOutcome.Unrestricted, (await second).Outcome);
            Assert.Equal([Other], _connection.Loads);
        }

        [Fact]
        public async Task Disposing_the_evaluator_cancels_the_loads_its_scope_started()
        {
            _connection.Others[Other] = Set(Other, Row(1, Doc, "Read"));
            _connection.Hold = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            AccessEvaluator evaluator = NewEvaluator();
            Task<AccessDecision> waiting = evaluator.EvaluateForAsync(Other, Doc, "Read", TestContext.Current.CancellationToken);

            evaluator.Dispose();
            evaluator.Dispose();

            _ = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting);
            _ = await Assert.ThrowsAsync<ObjectDisposedException>(() => evaluator.EvaluateForAsync(Other, Doc, "Read", TestContext.Current.CancellationToken));
            Assert.Equal([Other], _connection.Loads);
        }

        // --- Invalidate and GetAccessAsync ---

        [Fact]
        public async Task Invalidating_a_user_drops_their_cached_set_profile_and_connections_and_the_scopes_load_of_them()
        {
            // --- The process's entries of the user, of the caller, and of the user in another tenant ---
            TenantUserCacheKey key = new(Acme.Id, Other);
            ConnectedUser other = Connected(Set(Other));
            VersionTag preferences = new(new Guid("9e40a000-0000-0000-0000-000000000002"));
            _cache.Set(key, other.Access, new VersionTag(PermissionsTag));
            _profiles.Set(key, other.Profile, preferences);
            _connections.Set(Acme.Id, "sub-9", other);
            _connections.Set(Acme.Id, "sub-9-former", other);
            _connections.Set(2, "sub-9", other);
            _connections.Set(Acme.Id, "sub-7", _connection.Connected);
            _connection.Others[Other] = Set(Other);
            AccessEvaluator evaluator = NewEvaluator();
            _ = await evaluator.EvaluateForAsync(Other, Doc, "Read", TestContext.Current.CancellationToken);

            evaluator.Invalidate(Acme.Id, Other);

            // --- The user's set, profile and every connection of the tenant are gone; nothing else is ---
            Assert.Null(_cache.Peek(key, new VersionTag(PermissionsTag)));
            Assert.Null(_profiles.Peek(key, preferences));
            Assert.Null(_connections.Find(Acme.Id, "sub-9"));
            Assert.Null(_connections.Find(Acme.Id, "sub-9-former"));
            Assert.Same(other, _connections.Find(2, "sub-9"));
            Assert.Same(_connection.Connected, _connections.Find(Acme.Id, "sub-7"));

            // --- The scope loads the user again ---
            _connection.Others[Other] = Set(Other, Row(1, Doc, "Read"));
            Assert.Equal(AccessOutcome.Unrestricted, (await evaluator.EvaluateForAsync(Other, Doc, "Read", TestContext.Current.CancellationToken)).Outcome);
            Assert.Equal([Other, Other], _connection.Loads);
        }

        [Fact]
        public async Task Invalidating_a_user_of_another_tenant_keeps_the_scopes_load()
        {
            _cache.Set(new TenantUserCacheKey(2, Other), Set(Other), new VersionTag(PermissionsTag));
            _connection.Others[Other] = Set(Other);
            AccessEvaluator evaluator = NewEvaluator();
            _ = await evaluator.EvaluateForAsync(Other, Doc, "Read", TestContext.Current.CancellationToken);

            evaluator.Invalidate(2, Other);
            _ = await evaluator.EvaluateForAsync(Other, Doc, "Read", TestContext.Current.CancellationToken);

            Assert.Null(_cache.Peek(new TenantUserCacheKey(2, Other), new VersionTag(PermissionsTag)));
            Assert.Equal([Other], _connection.Loads);
        }

        [Fact]
        public async Task The_callers_set_is_the_connected_users_the_systems_or_an_empty_one()
        {
            UserAccess connected = await NewEvaluator().GetAccessAsync(TestContext.Current.CancellationToken);

            _holder.Current = _holder.Current with { Kind = PrincipalKind.System, Subject = "system", UserId = null };
            UserAccess system = await NewEvaluator().GetAccessAsync(TestContext.Current.CancellationToken);

            _holder.Current = RequestContext.Tenantless(new GregorianCalendarSystem(), Start);
            UserAccess nobody = await NewEvaluator().GetAccessAsync(TestContext.Current.CancellationToken);

            Assert.Same(_connection.Connected.Access, connected);
            Assert.Same(UserAccess.System, system);
            Assert.Equal((0, false), (nobody.UserId, nobody.IsSystem));
            Assert.Empty(nobody.Grants);
            Assert.Equal(1, _connection.Currents);
        }

        // --- Helpers ---

        /// <summary>A stored permission of role 10.</summary>
        /// <param name="permissionId">The permission's id.</param>
        /// <param name="resource">The resource as stored.</param>
        /// <param name="action">The action as stored.</param>
        /// <param name="filter">The filter; null for an unrestricted permission.</param>
        /// <returns>The row.</returns>
        private static PermissionRow Row(int permissionId, string resource, string action, string? filter = null)
        {
            return new PermissionRow(permissionId, 10, "Clerks", false, resource, action, filter, filter is null ? null : QueryexLanguage.Version, null, null);
        }

        /// <summary>A connected individual over a set.</summary>
        /// <param name="access">The set.</param>
        /// <returns>The connected user.</returns>
        private static ConnectedUser Connected(UserAccess access)
        {
            return new ConnectedUser(
                access.UserId,
                UserKind.Individual,
                UserState.Joined,
                new UserProfile("Ada", null, null, null, null, null, null, null, null, null, null, null, null, null, Start),
                Guid.Empty,
                access,
                new Dictionary<string, Guid>(StringComparer.Ordinal) { [VersionTagNames.Permissions] = PermissionsTag },
                null);
        }

        /// <summary>Builds the fixture's schema.</summary>
        /// <returns>The schema.</returns>
        private static QueryexSchema BuildSchema()
        {
            QueryexSchemaBuilder builder = new();
            _ = builder.Entity(Doc, "[acme].[Docs]")
                .Key("Id", QueryexType.QxNumeric, "Id")
                .Property("Amount", QueryexType.QxNumeric, "Amount", isNotNull: true)
                .Property("OwnerId", QueryexType.QxNumeric, "OwnerId", isNotNull: true);
            _ = builder.Entity(Line, "[acme].[DocLines]")
                .Key("Id", QueryexType.QxNumeric, "Id")
                .Property("DocId", QueryexType.QxNumeric, "DocId", isNotNull: true)
                .Navigation("Doc", Doc, "DocId");
            return builder.Build();
        }

        /// <summary>A user's set in the tenant, computed at the host's now.</summary>
        /// <param name="userId">The user.</param>
        /// <param name="rows">The rows.</param>
        /// <returns>The set.</returns>
        private UserAccess Set(int userId, params PermissionRow[] rows)
        {
            return _builder.Build(Acme.Id, userId, PermissionsTag, Guid.Empty, rows, Schema);
        }

        /// <summary>A scope's witness.</summary>
        /// <returns>The witness.</returns>
        private AccessWitness NewWitness()
        {
            return new AccessWitness(_holder, Registry, _metrics, _services.GetRequiredService<ILoggerFactory>());
        }

        /// <summary>A scope's evaluator over the fixture.</summary>
        /// <param name="stepUp">The step-up bar; the default one when null.</param>
        /// <param name="witness">The scope's witness; a fresh one when null.</param>
        /// <returns>The evaluator.</returns>
        private AccessEvaluator NewEvaluator(StepUpBar? stepUp = null, AccessWitness? witness = null)
        {
            AccessEvaluator evaluator = new(
                _holder,
                _connection,
                Registry,
                [.. _providers],
                witness ?? NewWitness(),
                _cache,
                _profiles,
                _connections,
                stepUp ?? StepUpBar.Default,
                _metrics,
                Options.Create(_options),
                _time);
            _evaluators.Add(evaluator);
            return evaluator;
        }

        /// <summary>A criteria provider of one resource whose criteria may change between evaluations.</summary>
        /// <param name="resource">The resource.</param>
        /// <param name="criteria">The initial criteria.</param>
        private sealed class CriteriaSource(string resource, params AccessCriterion[] criteria) : IAccessCriteriaProvider
        {
            /// <inheritdoc />
            public string Resource { get; } = resource;

            /// <summary>The criteria the next read returns.</summary>
            public IReadOnlyList<AccessCriterion> Criteria { get; set; } = criteria;
        }

        /// <summary>The scope's context: user 7 of the tenant by default.</summary>
        private sealed class Holder : IRequestContextAccessor
        {
            /// <inheritdoc />
            public RequestContext Current { get; set; } = RequestContext.Tenantless(new GregorianCalendarSystem(), Start) with
            {
                Tenant = Acme,
                Kind = PrincipalKind.User,
                Subject = "sub-7",
                UserId = Caller,
            };
        }

        /// <summary>
        ///     The scope's connection: connected as the caller, a re-check that by default passes,
        ///     and other users' sets by id. Composing batches is not the evaluator's concern.
        /// </summary>
        /// <param name="connected">The user the scope connects as.</param>
        /// <param name="time">The host's clock, which a passing re-check marks the set validated at.</param>
        private sealed class FakeConnection(ConnectedUser connected, TimeProvider time) : IScopeConnection
        {
            /// <summary>The calls to <see cref="CurrentAsync" /> so far.</summary>
            private int _currents;

            /// <summary>The user the scope connects as.</summary>
            public ConnectedUser Connected { get; set; } = connected;

            /// <summary>What a re-check returns for the user; null for a passing prologue, which marks the set validated now.</summary>
            public Func<ConnectedUser, ConnectedUser>? Revalidated { get; set; }

            /// <summary>The users re-checked, in order.</summary>
            public List<ConnectedUser> Revalidations { get; } = [];

            /// <summary>Other users' sets by id; a null set for an unknown, inactive or system user.</summary>
            public Dictionary<int, UserAccess?> Others { get; } = [];

            /// <summary>The users loaded, in order.</summary>
            public List<int> Loads { get; } = [];

            /// <summary>How many of the next loads fail.</summary>
            public int FailingLoads { get; set; }

            /// <summary>Holds every load until it completes; null to complete them at once.</summary>
            public TaskCompletionSource? Hold { get; set; }

            /// <summary>The calls to <see cref="CurrentAsync" />, counted safely across concurrent calls.</summary>
            public int Currents => Volatile.Read(ref _currents);

            /// <inheritdoc />
            public RequestContextInputs Inputs => RequestContextInputs.Empty;

            /// <inheritdoc />
            public Task<ConnectedUser> CurrentAsync(CancellationToken cancellationToken)
            {
                _ = Interlocked.Increment(ref _currents);
                return Task.FromResult(Connected);
            }

            /// <inheritdoc />
            public Task<ConnectedUser> RevalidateAsync(ConnectedUser user, CancellationToken cancellationToken)
            {
                lock (Revalidations)
                {
                    Revalidations.Add(user);
                }

                if (Revalidated is { } revalidated)
                {
                    return Task.FromResult(revalidated(user));
                }

                user.Access.MarkValidated(time.GetUtcNow());
                return Task.FromResult(user);
            }

            /// <inheritdoc />
            public async Task<UserAccess?> LoadDetachedAsync(int userId, CancellationToken cancellationToken)
            {
                lock (Loads)
                {
                    Loads.Add(userId);
                }

                if (Hold is { } hold)
                {
                    await hold.Task.WaitAsync(cancellationToken);
                }

                if (FailingLoads > 0)
                {
                    FailingLoads--;
                    throw new TimeoutException("The load timed out.");
                }

                return Others.TryGetValue(userId, out UserAccess? access) ? access : throw new InvalidOperationException($"The fixture has no user {userId}.");
            }

            /// <inheritdoc />
            public ConnectPremises PremisesFor(ConnectedUser user)
            {
                throw new NotSupportedException("The evaluator composes no batch.");
            }

            /// <inheritdoc />
            public BatchResult<ConnectResult> Contribute(IDataBatch batch, ConnectPremises premises)
            {
                throw new NotSupportedException("The evaluator composes no batch.");
            }

            /// <inheritdoc />
            public ConnectResult? ReadOf(BatchResult<ConnectResult> prologue)
            {
                throw new NotSupportedException("The evaluator reads no prologue.");
            }

            /// <inheritdoc />
            public ConnectedUser Apply(ConnectResult result, ConnectedUser basis)
            {
                throw new NotSupportedException("The evaluator applies no prologue.");
            }

            /// <inheritdoc />
            public Task<ConnectedUser> ReconnectColdAsync(CancellationToken cancellationToken)
            {
                throw new NotSupportedException("The evaluator never re-connects cold.");
            }

            /// <inheritdoc />
            public void Drop(ConnectedUser user)
            {
                throw new NotSupportedException("The evaluator drops no user.");
            }
        }
    }
}
