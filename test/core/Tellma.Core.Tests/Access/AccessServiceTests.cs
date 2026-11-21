// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Api;
using Tellma.Core.Abstractions.Crud;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Errors;
using Tellma.Core.Abstractions.TableTypes;
using Tellma.Core.Access;
using Tellma.Core.Queryex;
using Tellma.Core.Tests.Crud.Query;
using Tellma.Core.Tests.Crud.Scripted;

namespace Tellma.Core.Tests.Access
{
    /// <summary>
    ///     The access endpoints over the scripted batch: the caller's own decisions from the caller's
    ///     set with no round trip, another user's behind the roles' read grant and one read of that
    ///     user under the caller's read of the users, the ceiling and the malformed pairs refused
    ///     before anything, and the holders behind the same grant over a resolver that records.
    /// </summary>
    public sealed class AccessServiceTests : IAsyncDisposable
    {
        /// <summary>The other user a check names.</summary>
        private const int Target = 7;

        /// <summary>The filter the caller's read of the users holds.</summary>
        private const string UsersFilter = "Name != 'hidden'";

        /// <summary>The holders the resolver answers, and the pairs it was asked.</summary>
        private readonly RecordingHolders _holders = new();

        /// <summary>The default composition with the recording resolver.</summary>
        private readonly QueryRig _rig;

        /// <summary>Composes the default composition with the recording resolver.</summary>
        public AccessServiceTests()
        {
            _rig = new QueryRig(static _ => { }, services => services.Replace(ServiceDescriptor.Scoped<IAccessHoldersResolver>(_ => _holders)));
        }

        /// <summary>The test's cancellation.</summary>
        private static CancellationToken Token => TestContext.Current.CancellationToken;

        /// <inheritdoc />
        public ValueTask DisposeAsync()
        {
            return _rig.DisposeAsync();
        }

        // --- The caller's own access ---

        [Theory]
        [InlineData(null)]
        [InlineData(QueryRig.UserId)]
        public async Task The_caller_s_own_check_answers_from_the_caller_s_set_in_order_with_no_round_trip_and_no_roles_grant(int? userId)
        {
            _rig.Grant((CoreResources.Job, AccessActions.Read, null));

            await using AsyncServiceScope scope = _rig.Scope();
            IReadOnlyList<AccessDecision> decisions = await CheckAsync(scope, new AccessCheckRequest(userId, [new(CoreResources.Job, AccessActions.Read), new(CoreResources.Role, AccessActions.Read), new("fx.Gone", AccessActions.Read)]));

            Assert.Equal([AccessOutcome.Unrestricted, AccessOutcome.Denied, AccessOutcome.Denied], decisions.Select(static decision => decision.Outcome));
            Assert.Equal(AccessProblemCode.UnknownResource, Assert.Single(decisions[2].Problems).Code);
            Assert.Equal(0, _rig.Script.RoundTrips);
            Assert.Equal(0, _rig.OtherLoads);
        }

        // --- Another user's access ---

        [Fact]
        public async Task Another_user_s_check_reads_the_user_under_the_caller_s_filter_then_decides_from_one_load_in_order()
        {
            _rig.Grant((CoreResources.Role, AccessActions.Read, null), (CoreResources.User, AccessActions.Read, UsersFilter));
            _rig.GrantOther(Target, (CoreResources.Job, AccessActions.Read, null), (CoreResources.Schedule, AccessActions.Read, null));
            _ = _rig.Script.OnRows(static _ => QueryRig.Page(Target));

            await using AsyncServiceScope scope = _rig.Scope();
            IReadOnlyList<AccessDecision> decisions = await CheckAsync(scope, new AccessCheckRequest(Target, [new(CoreResources.Schedule, AccessActions.Read), new(CoreResources.Role, AccessActions.Read), new(CoreResources.Job, "Fly"), new(CoreResources.Job, AccessActions.Read)]));

            // --- The target's decisions, in request order; an unknown action is denied with its problem ---
            Assert.Equal(
                [AccessOutcome.Unrestricted, AccessOutcome.Denied, AccessOutcome.Denied, AccessOutcome.Unrestricted],
                decisions.Select(static decision => decision.Outcome));
            Assert.Equal(AccessProblemCode.UnknownAction, Assert.Single(decisions[2].Problems).Code);

            // --- Two round trips: the visibility read, then the one load of the target ---
            Assert.Equal(1, _rig.Script.RoundTrips);
            Assert.Equal(1, _rig.OtherLoads);
            ScriptedBatch batch = Assert.Single(_rig.Script.Batches);
            Assert.Equal(BatchPurpose.Read, batch.Purpose);
            ScriptedStatement read = Assert.Single(batch.Of(ScriptedKind.Rows));
            Assert.Equal((CoreResources.User, "Id"), (read.Spec.Root.Name, read.Spec.Select));
            Assert.Contains(UsersFilter, QueryRig.Render(read.Spec.Filter), StringComparison.Ordinal);
            KeySetRestriction restriction = Assert.Single(read.Spec.Restrictions);
            Assert.Equal("Id", restriction.Path);
            Assert.Equal([Target], batch.TvpRows<IdList>(restriction.TableSource).Select(static row => row.Id));
        }

        [Fact]
        public async Task Another_user_s_check_without_the_roles_read_grant_is_refused_before_any_round_trip()
        {
            _rig.Grant((CoreResources.User, AccessActions.Read, null));

            await using AsyncServiceScope scope = _rig.Scope();
            ForbiddenException refused = await Assert.ThrowsAsync<ForbiddenException>(
                () => CheckAsync(scope, new AccessCheckRequest(Target, [new(CoreResources.Job, AccessActions.Read)])));

            Assert.Equal((CoreResources.Role, AccessActions.Read), (refused.Resource, refused.Action));
            Assert.Equal(0, _rig.Script.RoundTrips);
            Assert.Equal(0, _rig.OtherLoads);
        }

        [Fact]
        public async Task Another_user_the_caller_may_not_read_is_not_found_and_never_loaded()
        {
            _rig.Grant((CoreResources.Role, AccessActions.Read, null), (CoreResources.User, AccessActions.Read, UsersFilter));
            _rig.GrantOther(Target, (CoreResources.Job, AccessActions.Read, null));
            _ = _rig.Script.OnRows(static _ => QueryRig.Page());

            await using AsyncServiceScope scope = _rig.Scope();
            NotFoundException missing = await Assert.ThrowsAsync<NotFoundException>(
                () => CheckAsync(scope, new AccessCheckRequest(Target, [new(CoreResources.Job, AccessActions.Read)])));

            Assert.Equal((CoreResources.User, "7"), (missing.Resource, Assert.Single(missing.Ids)));
            Assert.Equal(1, _rig.Script.RoundTrips);
            Assert.Equal(0, _rig.OtherLoads);
        }

        [Fact]
        public async Task A_user_id_no_user_can_have_is_not_found_with_no_round_trip()
        {
            _rig.Grant((CoreResources.Role, AccessActions.Read, null));

            await using AsyncServiceScope scope = _rig.Scope();
            _ = await Assert.ThrowsAsync<NotFoundException>(
                () => CheckAsync(scope, new AccessCheckRequest(-1, [new(CoreResources.Job, AccessActions.Read)])));

            Assert.Equal(0, _rig.Script.RoundTrips);
        }

        // --- Admission ---

        [Fact]
        public async Task More_securables_than_the_ceiling_are_refused_before_anything_and_the_ceiling_itself_is_answered()
        {
            _rig.Grant((CoreResources.Job, AccessActions.Read, null));
            SecurableRef[] ceiling = [.. Enumerable.Repeat(new SecurableRef(CoreResources.Job, AccessActions.Read), AccessService.MaxSecurables)];

            await using AsyncServiceScope scope = _rig.Scope();
            LimitExceededException exceeded = await Assert.ThrowsAsync<LimitExceededException>(
                () => CheckAsync(scope, new AccessCheckRequest(Target, [.. ceiling, new(CoreResources.Job, AccessActions.Read)])));
            IReadOnlyList<AccessDecision> answered = await CheckAsync(scope, new AccessCheckRequest(null, ceiling));

            Assert.Equal((LimitNames.MaxSecurables, 201, 200), (exceeded.Limit, exceeded.Actual, exceeded.Maximum));
            Assert.Equal(AccessService.MaxSecurables, answered.Count);
            Assert.Equal(0, _rig.Script.RoundTrips);
        }

        [Fact]
        public async Task A_missing_body_or_a_securable_without_its_pair_is_a_bad_request()
        {
            _rig.Grant((CoreResources.Job, AccessActions.Read, null));

            await using AsyncServiceScope scope = _rig.Scope();
            _ = await Assert.ThrowsAsync<BadRequestException>(() => CheckAsync(scope, null!));
            BadRequestException missing = await Assert.ThrowsAsync<BadRequestException>(
                () => CheckAsync(scope, new AccessCheckRequest(null, [new(CoreResources.Job, AccessActions.Read), null!])));
            _ = await Assert.ThrowsAsync<BadRequestException>(
                () => CheckAsync(scope, new AccessCheckRequest(null, [new(null!, AccessActions.Read)])));
            _ = await Assert.ThrowsAsync<BadRequestException>(() => HoldersAsync(scope, null!));

            Assert.Equal(1, missing.Arguments["index"]);
            Assert.Equal(0, _rig.Script.RoundTrips);
        }

        // --- The holders ---

        [Fact]
        public async Task The_holders_answer_through_the_resolver_behind_the_roles_read_grant()
        {
            _rig.Grant((CoreResources.Role, AccessActions.Read, null));

            await using AsyncServiceScope scope = _rig.Scope();
            AccessHolders holders = await HoldersAsync(scope, new AccessHoldersRequest(CoreResources.Job, AccessActions.Read));

            Assert.Same(_holders.Answer, holders);
            Assert.Equal([(CoreResources.Job, AccessActions.Read)], _holders.Asked);
        }

        [Fact]
        public async Task The_holders_without_the_roles_read_grant_are_refused_and_never_resolved()
        {
            _rig.Grant((CoreResources.User, AccessActions.Read, null));

            await using AsyncServiceScope scope = _rig.Scope();
            ForbiddenException refused = await Assert.ThrowsAsync<ForbiddenException>(
                () => HoldersAsync(scope, new AccessHoldersRequest(CoreResources.Job, AccessActions.Read)));

            Assert.Equal((CoreResources.Role, AccessActions.Read), (refused.Resource, refused.Action));
            Assert.Empty(_holders.Asked);
        }

        /// <summary>Invokes the check in a scope, through the action invoker.</summary>
        /// <param name="scope">The scope.</param>
        /// <param name="request">The request.</param>
        /// <returns>The decisions.</returns>
        private static async Task<IReadOnlyList<AccessDecision>> CheckAsync(AsyncServiceScope scope, AccessCheckRequest request)
        {
            return Assert.IsAssignableFrom<IReadOnlyList<AccessDecision>>(await InvokeAsync(scope, "check", request));
        }

        /// <summary>Invokes the holders in a scope, through the action invoker.</summary>
        /// <param name="scope">The scope.</param>
        /// <param name="request">The request.</param>
        /// <returns>The holders.</returns>
        private static async Task<AccessHolders> HoldersAsync(AsyncServiceScope scope, AccessHoldersRequest request)
        {
            return Assert.IsType<AccessHolders>(await InvokeAsync(scope, "holders", request));
        }

        /// <summary>Invokes one of the access endpoints in a scope, through the action invoker.</summary>
        /// <param name="scope">The scope.</param>
        /// <param name="name">The action's name.</param>
        /// <param name="request">The request.</param>
        /// <returns>The answer.</returns>
        private static async Task<object?> InvokeAsync(AsyncServiceScope scope, string name, object? request)
        {
            ApiActionDescriptor action = scope.ServiceProvider.GetRequiredService<IStackRegistry>().Services
                .Single(static service => service.Route == "access").Actions
                .Single(candidate => candidate.Name == name);
            return await scope.ServiceProvider.GetRequiredService<IApiActionInvoker>().InvokeAsync(action, request, Token);
        }

        /// <summary>A holders resolver that answers one fixed result and records each pair it is asked.</summary>
        private sealed class RecordingHolders : IAccessHoldersResolver
        {
            /// <summary>The answer.</summary>
            public AccessHolders Answer { get; } = new(
                CoreResources.Job, AccessActions.Read, AccessOutcome.Denied, [], new Dictionary<int, int>(), [], 0, false, [], [], new RelatedEntities());

            /// <summary>The pairs asked, in order.</summary>
            public List<(string Resource, string Action)> Asked { get; } = [];

            /// <inheritdoc />
            public Task<AccessHolders> ResolveAsync(string resource, string action, CancellationToken cancellationToken = default)
            {
                Asked.Add((resource, action));
                return Task.FromResult(Answer);
            }
        }
    }
}
