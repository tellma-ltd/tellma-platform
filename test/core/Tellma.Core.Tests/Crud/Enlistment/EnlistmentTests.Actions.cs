// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Api;
using Tellma.Core.Abstractions.Crud;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Errors;
using Tellma.Core.Abstractions.Tenancy;
using Tellma.Core.Abstractions.Validation;
using Tellma.Core.Crud.Enlistment;
using Tellma.Core.Crud.Save;
using Tellma.Core.Provisioning;
using Tellma.Core.Tests.Crud.Scripted;

namespace Tellma.Core.Tests.Crud.Enlistment
{
    /// <summary>
    ///     The API action invoker and the open frames: the registry's descriptor alone, the securable
    ///     or the member check before the method, the body's annotations, the interceptors in
    ///     registration order around the method inside its frame; a frame that persists its own
    ///     enlistments once, a self-pair from its own action, a job's frame appending its groups to
    ///     the completion batch it is handed, and a provisioning step's frame persisting its own.
    /// </summary>
    public sealed partial class EnlistmentTests
    {
        // --- The invoker ---

        [Fact]
        public async Task An_api_action_runs_inside_its_own_frame_through_every_interceptor_in_registration_order()
        {
            (WriteHostKind, string)? frame = null;
            _hooks.Stamp = (_, host, body, _) =>
            {
                frame = (host.Kind, host.Operation);
                return Task.FromResult($"stamped {body.Name}");
            };

            object? result = await InvokeAsync(EnlistmentModel.Stamp, new EnStamp { Name = "ab" });

            Assert.Equal("stamped ab", result);
            Assert.Equal((WriteHostKind.ApiAction, $"{EnlistmentModel.Orders}:{EnlistmentModel.Stamp}"), frame);
            Assert.Equal(["outer before", "inner before", EnlistmentModel.Stamp, "inner after", "outer after"], _hooks.Calls);
            Assert.Equal(0, _rig.Script.RoundTrips);
        }

        [Fact]
        public async Task A_caller_without_the_actions_securable_is_refused_before_the_method_runs()
        {
            _rig.Grant((EnlistmentModel.Orders, AccessActions.Read, null));

            ForbiddenException refused = await Assert.ThrowsAsync<ForbiddenException>(() => InvokeAsync(EnlistmentModel.Stamp, new EnStamp { Name = "ab" }));

            Assert.Equal((EnlistmentModel.Orders, AccessActions.Save), (refused.Resource, refused.Action));
            Assert.Empty(_hooks.Calls);
        }

        [Fact]
        public async Task A_member_only_action_runs_for_a_connected_member_and_is_refused_for_a_caller_who_is_none()
        {
            _rig.Grant();
            Assert.Equal(0, await InvokeAsync(EnlistmentModel.Ping, body: null));

            _hooks.Calls.Clear();
            _rig.ConnectFailure = new TenantNotFoundException(Tenant);
            _ = await Assert.ThrowsAsync<TenantNotFoundException>(() => InvokeAsync(EnlistmentModel.Ping, body: null));
            Assert.Empty(_hooks.Calls);
        }

        [Fact]
        public async Task The_body_is_checked_by_its_annotations_at_its_own_members_and_by_its_type_before_the_method_runs()
        {
            // --- Every annotation failure at once, rooted at the body's members ---
            ValidationException invalid = await Assert.ThrowsAsync<ValidationException>(() => InvokeAsync(EnlistmentModel.Stamp, new EnStamp { Name = "toolong" }));
            Assert.Equal([$"Name {ValidationCodes.MaxLength} -"], Lines(invalid));

            // --- None sent is the caller's 400; one of another type, or one to an action without a body, an authoring bug ---
            _ = await Assert.ThrowsAsync<BadRequestException>(() => InvokeAsync(EnlistmentModel.Stamp, body: null));
            _ = await Assert.ThrowsAsync<InvalidOperationException>(() => InvokeAsync(EnlistmentModel.Stamp, "ab"));
            _ = await Assert.ThrowsAsync<InvalidOperationException>(() => InvokeAsync(EnlistmentModel.Ping, new EnStamp { Name = "ab" }));
            Assert.Empty(_hooks.Calls);
        }

        [Fact]
        public async Task A_descriptor_equal_to_the_registrys_but_not_its_own_is_refused()
        {
            ApiActionDescriptor copy = Action(EnlistmentModel.Stamp) with { };

            await using AsyncServiceScope scope = _rig.Scope();
            InvalidOperationException refused = await Assert.ThrowsAsync<InvalidOperationException>(
                () => scope.ServiceProvider.GetRequiredService<IApiActionInvoker>().InvokeAsync(copy, new EnStamp { Name = "ab" }, Token));

            Assert.Contains("not a descriptor the stack registry holds", refused.Message, StringComparison.Ordinal);
            Assert.Empty(_hooks.Calls);
        }

        // --- The open frame ---

        [Fact]
        public async Task An_api_action_persists_its_self_pair_on_its_own_frame_once_and_a_second_persist_is_refused()
        {
            _storedOrders.Add(7);
            EnOrder order = new() { Id = 7, Name = "renamed" };
            InvalidOperationException? again = null;
            EnlistmentOutcome? outcome = null;
            _hooks.Stamp = async (service, host, body, cancellationToken) =>
            {
                _ = await service.EnlistSaveAsync(host, [order]);
                outcome = await host.PersistAsync(cancellationToken);
                again = await Assert.ThrowsAsync<InvalidOperationException>(() => host.PersistAsync(cancellationToken));
                return body.Name!;
            };

            _ = await InvokeAsync(EnlistmentModel.Stamp, new EnStamp { Name = "ab" });

            // --- The image on a validation round, then one persist: the order's own row, stamped ---
            Assert.Equal(2, _rig.Script.RoundTrips);
            Assert.Equal(BatchPurpose.Validate, _rig.Script.Batches[0].Purpose);
            ScriptedStatement save = Assert.Single(Assert.Single(Persists).Of(ScriptedKind.Save));
            Assert.Equal("renamed", Assert.Single((IReadOnlyList<EnOrder>)save.Statement!).Name);
            Assert.Equal((Committed, Committed), (outcome!.Stamp, order.ModifiedAt));
            Assert.Contains("persists once", again!.Message, StringComparison.Ordinal);
            Assert.Equal([EnlistmentModel.Orders], _hooks.Committed);
        }

        [Fact]
        public async Task An_open_frame_with_nothing_enlisted_persists_on_no_round_trip_and_admits_no_enlistment_after_it_persisted()
        {
            InvalidOperationException? late = null;
            _hooks.Stamp = async (service, host, body, cancellationToken) =>
            {
                _ = await host.PersistAsync(cancellationToken);
                late = await Assert.ThrowsAsync<InvalidOperationException>(() => service.EnlistSaveAsync(host, [Note("n")]));
                return body.Name!;
            };

            _ = await InvokeAsync(EnlistmentModel.Stamp, new EnStamp { Name = "ab" });

            Assert.Equal(0, _rig.Script.RoundTrips);
            Assert.Contains("after it persisted", late!.Message, StringComparison.Ordinal);
        }

        [Fact]
        public async Task An_api_action_that_returns_with_enlisted_writes_it_never_persisted_fails_and_sends_nothing()
        {
            _hooks.Stamp = async (service, host, body, cancellationToken) =>
            {
                _ = await service.EnlistSaveAsync(host, [Note("n")]);
                return body.Name!;
            };

            InvalidOperationException refused = await Assert.ThrowsAsync<InvalidOperationException>(() => InvokeAsync(EnlistmentModel.Stamp, new EnStamp { Name = "ab" }));

            Assert.Contains("never persisted", refused.Message, StringComparison.Ordinal);
            Assert.Equal(0, _rig.Script.RoundTrips);
        }

        [Fact]
        public async Task An_api_action_may_call_its_stacks_front_door_which_runs_its_own_pipeline_frame_inside_the_open_one()
        {
            _hooks.Stamp = async (service, _, body, cancellationToken) =>
            {
                EntitiesResult<EnOrder> saved = await service.SaveAsync([Order(body.Name!)], IdsOnly(), cancellationToken);
                return $"{saved.Ids[0]}";
            };

            Assert.Equal("101", await InvokeAsync(EnlistmentModel.Stamp, new EnStamp { Name = "ab" }));
            Assert.Equal(1, _rig.Script.RoundTrips);
        }

        [Fact]
        public async Task A_jobs_frame_appends_its_groups_to_the_completion_batch_after_the_outcomes_and_runs_their_post_commit_once_told()
        {
            await using AsyncServiceScope scope = _rig.Scope();
            EnSweepJob handler = new(_hooks, scope.ServiceProvider.GetRequiredService<IOpenWriteHost>());
            EnNote note = Note("swept");
            ScriptedBatch completion = _rig.Script.NewBatch(BatchPurpose.Persist);
            _ = completion.Sql($"SELECT 1 AS [Completed]", options: null);
            _hooks.Sweep = async (job, host) =>
            {
                _ = await job.EnlistSaveAsync(host, [note]);
                _ = await host.PersistAsync(Token);
            };

            // --- The handler runs in the job's frame; its persist appends and sends nothing ---
            var frame = OpenWriteFrame.ForJob(
                scope.ServiceProvider.GetRequiredService<SaveOperation>(), EnlistmentModel.SweepKey, typeof(EnSweepJob), runsWhenReadOnly: false, completion, Token);
            _ = await frame.RunAsync(async () =>
            {
                await handler.ExecuteAsync(null!);
                return true;
            });
            Assert.Equal(0, _rig.Script.RoundTrips);
            Assert.Equal([ScriptedKind.Sql, ScriptedKind.Save], completion.Statements.Select(static statement => statement.Kind).Where(static kind => kind is ScriptedKind.Sql or ScriptedKind.Save));
            Assert.Empty(_hooks.Committed);

            // --- The worker sends the batch, then tells the frame it committed ---
            BatchOutcome committed = await completion.ExecuteAsync(Token);
            await frame.AfterCompletionAsync(committed);
            Assert.Equal((201, Committed), (note.Id, note.ModifiedAt));
            Assert.Equal([EnlistmentModel.Notes], _hooks.Committed);
        }

        [Fact]
        public async Task A_provisioning_step_runs_in_its_own_frame_and_persists_its_enlistments_in_one_batch()
        {
            await using AsyncServiceScope scope = _rig.Scope();
            EnSeedStep step = ActivatorUtilities.CreateInstance<EnSeedStep>(scope.ServiceProvider);
            EnNote note = Note("seeded");
            (WriteHostKind, string)? frame = null;
            _hooks.Seed = async (seed, host) =>
            {
                frame = (host.Kind, host.Operation);
                _ = await seed.EnlistSaveAsync(host, [note]);
                _ = await host.PersistAsync(Token);
            };

            // --- The runner hands the step to the opener, which runs it inside the frame ---
            await _rig.Services.GetRequiredService<IProvisioningFrameOpener>()
                .RunAsync(scope.ServiceProvider, EnlistmentModel.SeedStep, typeof(EnSeedStep), () => step.RunAsync(null!), Token);

            Assert.Equal((WriteHostKind.Provisioning, $"provisioning:{EnlistmentModel.SeedStep}"), frame);
            Assert.Equal(1, _rig.Script.RoundTrips);
            Assert.Equal([typeof(EnNote)], Assert.Single(Persists).Of(ScriptedKind.Save).Select(static statement => statement.Entity));
            Assert.Equal((201, Committed), (note.Id, note.ModifiedAt));
            Assert.Equal([EnlistmentModel.Notes], _hooks.Committed);
        }

        // --- Helpers ---

        /// <summary>The registry's own descriptor of an order action.</summary>
        /// <param name="name">The action's name.</param>
        /// <returns>The descriptor.</returns>
        private ApiActionDescriptor Action(string name)
        {
            StackDescriptor orders = _rig.Services.GetRequiredService<IStackRegistry>().Find(EnlistmentModel.Orders)!;
            return orders.Actions.OfType<ApiActionDescriptor>().Single(action => action.Name == name);
        }

        /// <summary>Invokes an order action through the invoker, in a scope of its own.</summary>
        /// <param name="name">The action's name.</param>
        /// <param name="body">The body; null for none.</param>
        /// <returns>What the action answered.</returns>
        private async Task<object?> InvokeAsync(string name, object? body)
        {
            await using AsyncServiceScope scope = _rig.Scope();
            _scoped = scope.ServiceProvider;
            _hooks.Committed.Clear();
            try
            {
                return await scope.ServiceProvider.GetRequiredService<IApiActionInvoker>().InvokeAsync(Action(name), body, Token);
            }
            finally
            {
                _scoped = null;
            }
        }
    }
}
