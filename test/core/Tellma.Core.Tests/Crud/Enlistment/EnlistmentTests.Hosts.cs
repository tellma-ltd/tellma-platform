// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.Metrics.Testing;
using System.Diagnostics.Metrics;
using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Api;
using Tellma.Core.Abstractions.Caching;
using Tellma.Core.Abstractions.Crud;
using Tellma.Core.Abstractions.Errors;
using Tellma.Core.Abstractions.Tenancy;
using Tellma.Core.Abstractions.Validation;
using Tellma.Core.Calendars;
using Tellma.Core.Crud;
using Tellma.Core.Crud.Enlistment;
using Tellma.Core.Data;
using Tellma.Core.Provisioning;
using Tellma.Core.Tests.Crud.Save;
using Tellma.Core.Tests.Crud.Scripted;

namespace Tellma.Core.Tests.Crud.Enlistment
{
    /// <summary>
    ///     Enlisted writes from every host and under every judge: an entity action's method and hooks
    ///     and a delete's hook enlisting under their host's named operation; a re-entry answering each
    ///     re-run enlistment from the group the same task enlisted, or refusing as stale; a re-entry
    ///     for a moved permission set, judged again or refused; the injected open host refusing a
    ///     nested enlistment; the access guards brought by a group writing a security table; a
    ///     read-only tenant's verdict judged by the frame a write is nested in; the server-owned
    ///     members a writer supplies; the source a frame records; and a failure attributed to the one
    ///     of two groups on a target that raised it.
    /// </summary>
    public sealed partial class EnlistmentTests
    {
        // --- Action and delete hosts ---

        [Fact]
        public async Task An_entity_actions_hook_and_method_and_a_deletes_hook_enlist_under_their_hosts_named_operation_and_counted_operation()
        {
            using MetricCollector<long> enlistments = new(_rig.Services.GetRequiredService<IMeterFactory>(), CrudTelemetryNames.MeterName, CrudTelemetryNames.Enlistments);
            _storedOrders.Add(7);
            List<string> hosts = [];
            _hooks.NoteValidate = (_, context) =>
            {
                hosts.Add($"{context.Entities[0].Text} {context.Host.Operation}");
                return Task.CompletedTask;
            };

            // --- An entity action: its hook enlists first, then its method; the host is named by the action ---
            _hooks.OrderValidateAction = async (service, context) => _ = await service.EnlistSaveAsync(context.Host, [Note("hook")]);
            _hooks.Touch = async (service, context) => _ = await service.EnlistSaveAsync(context.Host, [Note("method")]);
            _ = await TouchAsync(7);
            Assert.Equal(["note hook", "note method"], Persists[^1].Of(ScriptedKind.Save).Select(static statement => Describe(statement)));
            Assert.Equal([$"hook {EnlistmentModel.Orders}:{EnlistmentModel.Touch}", $"method {EnlistmentModel.Orders}:{EnlistmentModel.Touch}"], hosts);
            Assert.Equal(["action save ok", "action save ok"], enlistments.GetMeasurementSnapshot().Select(static measurement => Tagged(measurement)));

            // --- A delete: its hook enlists a save, composed after the host's delete ---
            hosts.Clear();
            enlistments.Clear();
            _hooks.OrderValidateDelete = async (service, context) => _ = await service.EnlistSaveAsync(context.Host, [Note("deleting")]);
            _ = await DeleteOrdersAsync(7);
            ScriptedBatch persist = Persists[^1];
            Assert.True(Assert.Single(persist.Of(ScriptedKind.Delete)).Ordinal < Assert.Single(persist.Of(ScriptedKind.Save)).Ordinal);
            Assert.Equal([$"deleting {EnlistmentModel.Orders}:delete"], hosts);
            Assert.Equal(["delete save ok"], enlistments.GetMeasurementSnapshot().Select(static measurement => Tagged(measurement)));
        }

        [Fact]
        public void A_pipeline_hosts_enlistments_are_counted_under_delete_for_every_kind_of_delete()
        {
            Assert.Equal(
                [CrudOperationNames.Save, CrudOperationNames.Delete, CrudOperationNames.Delete, CrudOperationNames.Delete, CrudOperationNames.Action],
                new[] { CrudOperationNames.Save, CrudOperationNames.Delete, CrudOperationNames.DeleteByQuery, CrudOperationNames.DeleteWithDescendants, CrudOperationNames.Action }
                    .Select(PipelineFrame.CountedAs));
        }

        // --- A re-entry's re-run ---

        [Fact]
        public async Task A_re_run_answers_each_enlistment_from_the_group_the_same_task_enlisted_however_differently_the_tasks_interleave()
        {
            // --- The hook awaits a load before it enlists and the component does not, so the first run
            // enlists the component's note first; the re-run's loads are answered at once, so the hook
            // enlists first there ---
            List<string> wired = [];
            _hooks.OrderValidate = async (service, context) =>
            {
                _ = context.Loader.ByIds<EnOrder, int>([9]);
                await context.Loader.LoadAsync();
                EnlistedSave<EnNote> saved = await service.EnlistSaveAsync(context.Host, [Note("hook")]);
                context.Entities[0].Name = $"hook {saved.Rows[0].Id}";
                wired.Add(context.Entities[0].Name);
            };
            _hooks.OrderRules = async (rules, context) =>
            {
                EnlistedSave<EnNote> saved = await rules.EnlistSaveAsync(context.Host, [Note("rules")]);
                wired.Add($"rules {saved.Rows[0].Id}");
            };
            RecomposeOnce();

            _ = await SaveOrdersAsync(Order("A"));

            // --- Each task got its own ids on both runs, and the host row wires the hook's note ---
            Assert.Equal(["rules 201", "hook 202", "hook 202", "rules 201"], wired);
            ScriptedBatch committed = Persists[^1];
            Assert.Equal(2, Persists.Length);
            Assert.Equal(
                ["order hook 202", "note rules 201", "note hook 202"],
                committed.Of(ScriptedKind.Save).Select(static statement => statement.Statement switch
                {
                    IReadOnlyList<EnNote> notes => $"note {notes[0].Text} {notes[0].Id}",
                    _ => Describe(statement),
                }));
        }

        [Fact]
        public async Task A_re_run_of_an_actions_validators_never_answers_an_enlistment_from_a_group_the_method_enlisted()
        {
            // --- The method enlists at once, the hook after a load: the method's note takes 201 ---
            _storedOrders.Add(7);
            List<int> validated = [];
            _hooks.Touch = async (service, context) => _ = await service.EnlistSaveAsync(context.Host, [Note("method")]);
            _hooks.OrderValidateAction = async (service, context) =>
            {
                _ = context.Loader.ByIds<EnOrder, int>([9]);
                await context.Loader.LoadAsync();
                validated.Add((await service.EnlistSaveAsync(context.Host, [Note("hook")])).Rows[0].Id);
            };
            RecomposeOnce();

            _ = await TouchAsync(7);

            // --- The re-run answers the hook from its own group; the method's is replayed as it was ---
            Assert.Equal([202, 202], validated);
            Assert.Equal(2, Persists.Length);
            Assert.Equal(
                ["method 201", "hook 202"],
                Persists[^1].Of(ScriptedKind.Save).Select(static statement => $"{((IReadOnlyList<EnNote>)statement.Statement!)[0].Text} {((IReadOnlyList<EnNote>)statement.Statement!)[0].Id}"));
        }

        [Fact]
        public async Task A_re_entry_whose_first_run_enlisted_beside_a_parked_flow_of_the_same_task_is_stale_rather_than_matched_by_order()
        {
            // --- One task, two flows: the early enlistment is made while the other flow is parked ---
            _hooks.OrderValidate = async (service, context) =>
            {
                async Task LateAsync()
                {
                    _ = context.Loader.ByIds<EnOrder, int>([9]);
                    await context.Loader.LoadAsync();
                    _ = await service.EnlistSaveAsync(context.Host, [Note("late")]);
                }

                Task late = LateAsync();
                _ = await service.EnlistSaveAsync(context.Host, [Note("early")]);
                await late;
            };
            RecomposeOnce();

            _ = await Assert.ThrowsAsync<StaleContextException>(() => SaveOrdersAsync(Order("A")));

            // --- The first composition was refused by its guard, and the recomposed one never sent ---
            Assert.True(Persists[0].IsRefused);
            Assert.False(Persists[^1].IsExecuted);
        }

        [Fact]
        public async Task A_re_run_that_skips_an_enlistment_of_its_first_run_is_stale()
        {
            int runs = 0;
            _hooks.OrderValidate = async (service, context) =>
            {
                if (++runs == 1)
                {
                    _ = await service.EnlistSaveAsync(context.Host, [Note("once")]);
                }
            };
            RecomposeOnce();

            _ = await Assert.ThrowsAsync<StaleContextException>(() => SaveOrdersAsync(Order("A")));

            Assert.Equal(2, runs);
            Assert.True(Persists[0].IsRefused);
            Assert.False(Persists[^1].IsExecuted);
        }

        // --- A re-entry for a moved permission set ---

        [Fact]
        public async Task An_open_frames_re_entry_decides_the_actions_securable_for_the_fresh_caller_and_refuses_one_who_lost_it()
        {
            // --- The note's hook arms a recomposition as the frame's persist composes; the fresh set
            // holds every grant but the action's Save ---
            _hooks.NoteContribute = Once<EnNoteService, PersistContext<EnNote>>(
                () => _rig.GrantOnRecompose([.. Grants.Where(static grant => grant.Action != AccessActions.Save)]));
            _hooks.Stamp = async (service, host, body, cancellationToken) =>
            {
                _ = await service.EnlistSaveAsync(host, [Note("acted")]);
                _ = await host.PersistAsync(cancellationToken);
                return body.Name!;
            };

            ForbiddenException denied = await Assert.ThrowsAsync<ForbiddenException>(() => InvokeAsync(EnlistmentModel.Stamp, new EnStamp { Name = "ab" }));

            // --- Refused as no grant of the securable, the recomposed batch never sent ---
            Assert.Equal((EnlistmentModel.Orders, AccessActions.Save, ForbiddenReason.NoGrant), (denied.Resource, denied.Action, denied.Reason));
            Assert.Equal(2, Persists.Length);
            Assert.True(Persists[0].IsRefused);
            Assert.False(Persists[^1].IsExecuted);
        }

        [Fact]
        public async Task A_re_entry_for_a_moved_permission_set_is_stale_when_it_would_replay_a_group_a_validator_judged()
        {
            // --- A pipeline host's validator enlists a note, which the note's hook validates: stale,
            // naming the permissions, the recomposed batch never sent ---
            _hooks.OrderValidate = async (service, context) => _ = await service.EnlistSaveAsync(context.Host, [Note("judged")]);
            RecomposeOnce(permissions: true);
            StaleContextException stale = await Assert.ThrowsAsync<StaleContextException>(() => SaveOrdersAsync(Order("A")));
            Assert.Equal(VersionTagNames.Permissions, Assert.Single(stale.Dependencies).Name);
            Assert.True(Persists[^2].IsRefused);
            Assert.False(Persists[^1].IsExecuted);

            // --- The same set on the re-entry, as a moved settings tag recomposes: the group replays and commits ---
            RecomposeOnce();
            _ = await SaveOrdersAsync(Order("A"));
            Assert.True(Persists[^2].IsRefused);
            Assert.True(Persists[^1].IsExecuted);

            // --- An entity action's method and an API action's frame alike ---
            _hooks.OrderValidate = null;
            _storedOrders.Add(7);
            _hooks.Touch = async (service, context) => _ = await service.EnlistSaveAsync(context.Host, [Note("method")]);
            RecomposeOnce(permissions: true);
            _ = await Assert.ThrowsAsync<StaleContextException>(() => TouchAsync(7));
            Assert.False(Persists[^1].IsExecuted);

            _hooks.NoteContribute = Once<EnNoteService, PersistContext<EnNote>>(() => _rig.GrantOnRecompose(Grants));
            _hooks.Stamp = async (service, host, body, cancellationToken) =>
            {
                _ = await service.EnlistSaveAsync(host, [Note("acted")]);
                _ = await host.PersistAsync(cancellationToken);
                return body.Name!;
            };
            _ = await Assert.ThrowsAsync<StaleContextException>(() => InvokeAsync(EnlistmentModel.Stamp, new EnStamp { Name = "ab" }));
            Assert.False(Persists[^1].IsExecuted);

            // --- A group no validator judged (a note's tag: the tags have none) replays under the moved set ---
            (string Resource, string Action, string? Filter)[] notes = [.. Grants, (EnlistmentModel.Notes, AccessActions.Save, null)];
            _rig.Grant(notes);
            _hooks.NoteContribute = Once<EnNoteService, PersistContext<EnNote>>(() => _rig.GrantOnRecompose(notes));
            _hooks.NoteValidate = async (service, context) => _ = await service.EnlistSaveAsync(context.Host, [new EnTag { Label = "untested" }]);
            await using (AsyncServiceScope scope = _rig.Scope())
            {
                _ = await scope.ServiceProvider.GetRequiredService<EnNoteService>().SaveAsync([Note("host")], IdsOnly(), Token);
            }

            Assert.True(Persists[^2].IsRefused);
            Assert.Equal(["note host", "tag untested"], Persists[^1].Of(ScriptedKind.Save).Select(static statement => Describe(statement)));
            Assert.True(Persists[^1].IsExecuted);
        }

        // --- The injected open host ---

        [Fact]
        public async Task The_injected_open_host_refuses_an_enlistment_from_inside_a_nested_save_or_an_enlisted_writes_participant()
        {
            IOpenWriteHost? injected = null;
            List<string> refused = [];

            // --- A hook of the front-door save the method calls, and the hook of the note it enlists,
            // each enlisting through the method's injected host ---
            _hooks.OrderValidate = async (service, _) => refused.Add(
                (await Assert.ThrowsAsync<InvalidOperationException>(() => service.EnlistSaveAsync(injected!, [Note("outer")]))).Message);
            _hooks.NoteValidate = async (service, _) => refused.Add(
                (await Assert.ThrowsAsync<InvalidOperationException>(() => service.EnlistSaveAsync(injected!, [new EnTag { Label = "outer" }]))).Message);
            _hooks.Stamp = async (service, host, body, cancellationToken) =>
            {
                injected = host;
                _ = await service.SaveAsync([Order("nested")], IdsOnly(), cancellationToken);

                // --- The method itself, once the nested save returned: admitted ---
                _ = await service.EnlistSaveAsync(host, [Note("method")]);
                _ = await host.PersistAsync(cancellationToken);
                return body.Name!;
            };

            _ = await InvokeAsync(EnlistmentModel.Stamp, new EnStamp { Name = "ab" });

            // --- Both refused, naming the context's Host; each persist carries only its own write ---
            Assert.Equal(2, refused.Count);
            Assert.All(refused, static message => Assert.Contains("enlist with their context's Host", message, StringComparison.Ordinal));
            Assert.Equal(["order nested"], Persists[0].Of(ScriptedKind.Save).Select(static statement => Describe(statement)));
            Assert.Equal(["note method"], Persists[^1].Of(ScriptedKind.Save).Select(static statement => Describe(statement)));
        }

        // --- The access guards ---

        [Fact]
        public async Task A_group_writing_a_security_table_brings_the_access_guards_to_every_hosts_persist_after_its_writes()
        {
            // --- An order alone writes no security table: no guard ---
            _ = await SaveOrdersAsync(Order("A"));
            Assert.Empty(_guards.Calls);

            // --- A save's validator enlists a user: the lock and the invariants, after every statement ---
            _hooks.OrderValidate = async (service, context) => _ = await service.EnlistSaveAsync(context.Host, [Person("Ada")]);
            _ = await SaveOrdersAsync(Order("A"));
            Assert.Equal([PsGuards.Lock, PsGuards.Invariants], _guards.Calls);
            ScriptedBatch persist = Persists[^1];
            int[] order =
            [
                Assert.Single(persist.Of(ScriptedKind.Save), static statement => statement.Entity == typeof(EnOrder)).Ordinal,
                Assert.Single(persist.Of(ScriptedKind.Save), static statement => statement.Entity == typeof(User)).Ordinal,
                SqlOrdinal(persist, PsGuards.Lock),
                SqlOrdinal(persist, PsGuards.Invariants),
            ];
            Assert.Equal([.. order.Order()], order);

            // --- An entity action's method, then a delete's hook, each enlisting a user ---
            _storedOrders.Add(7);
            _guards.Calls.Clear();
            _hooks.Touch = async (service, context) => _ = await service.EnlistSaveAsync(context.Host, [Person("Cy")]);
            _ = await TouchAsync(7);
            GuardsFollowEveryWrite(Persists[^1]);

            _guards.Calls.Clear();
            _hooks.OrderValidateDelete = async (service, context) => _ = await service.EnlistSaveAsync(context.Host, [Person("Di")]);
            _ = await DeleteOrdersAsync(7);
            GuardsFollowEveryWrite(Persists[^1]);
            _ = Assert.Single(Persists[^1].Of(ScriptedKind.Delete));

            // --- An API action's enlisted user, persisted on the action's own frame ---
            _guards.Calls.Clear();
            _hooks.Stamp = async (service, host, body, cancellationToken) =>
            {
                _ = await service.EnlistSaveAsync(host, [Person("Bo")]);
                _ = await host.PersistAsync(cancellationToken);
                return body.Name!;
            };
            _ = await InvokeAsync(EnlistmentModel.Stamp, new EnStamp { Name = "ab" });
            Assert.Equal([PsGuards.Lock, PsGuards.Invariants], _guards.Calls);
            persist = Persists[^1];
            Assert.True(Assert.Single(persist.Of(ScriptedKind.Save)).Ordinal < SqlOrdinal(persist, PsGuards.Lock));
        }

        // --- A read-only tenant ---

        [Fact]
        public async Task On_a_read_only_tenant_a_write_nested_in_an_api_action_or_a_provisioning_step_is_judged_by_that_frame()
        {
            _storedOrders.Add(7);
            _rig.TenantState = TenantState.ReadOnly;

            // --- Outside every frame: a save is refused, and an action that allows it is admitted ---
            TenantUnavailableException plain = await Assert.ThrowsAsync<TenantUnavailableException>(() => SaveOrdersAsync(Order("A")));
            Assert.Equal(TenantProblemCodes.ReadOnly, plain.Code);
            _ = await TouchAsync(7);
            int sent = Persists.Length;

            // --- In an API action that is no mutation: the front-door save is admitted and sent ---
            _hooks.Ping = async (service, _) => (int)(await service.SaveAsync([Order("P")], IdsOnly(), Token)).Ids[0];
            _ = await InvokeAsync(EnlistmentModel.Ping, body: null);
            Assert.Equal(sent + 1, Persists.Length);

            // --- In a mutating API action without AllowWhenReadOnly: refused, the nested action's own flag notwithstanding ---
            _hooks.Stamp = async (service, _, _, cancellationToken) =>
            {
                _ = await service.ExecuteActionAsync(EnlistmentModel.Touch, [7], arguments: null, new ActionOptions(), cancellationToken);
                return EnlistmentModel.Touch;
            };
            TenantUnavailableException nested = await Assert.ThrowsAsync<TenantUnavailableException>(() => InvokeAsync(EnlistmentModel.Stamp, new EnStamp { Name = "ab" }));
            Assert.Equal(TenantProblemCodes.ReadOnly, nested.Code);
            Assert.Equal(sent + 1, Persists.Length);

            // --- In a provisioning step: the front-door save is admitted and sent ---
            await using AsyncServiceScope scope = _rig.Scope();
            EnSeedStep step = ActivatorUtilities.CreateInstance<EnSeedStep>(scope.ServiceProvider);
            _hooks.Seed = async (_, _) => _ = await scope.ServiceProvider.GetRequiredService<EnOrderService>().SaveAsync([Order("seeded")], IdsOnly(), Token);
            await _rig.Services.GetRequiredService<IProvisioningFrameOpener>()
                .RunAsync(scope.ServiceProvider, EnlistmentModel.SeedStep, typeof(EnSeedStep), () => step.RunAsync(null!), Token);
            Assert.Equal(sent + 2, Persists.Length);
            Assert.Equal("order seeded", Describe(Assert.Single(Persists[^1].Of(ScriptedKind.Save))));
        }

        // --- The open frame's gate ---

        [Fact]
        public async Task An_open_frames_persist_is_refused_while_an_operation_it_runs_through_a_service_still_validates()
        {
            TaskCompletionSource validating = new(TaskCreationOptions.RunContinuationsAsynchronously);
            TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
            InvalidOperationException? early = null;
            _hooks.OrderValidate = async (_, _) =>
            {
                validating.SetResult();
                await release.Task;
            };
            _hooks.Stamp = async (service, host, body, cancellationToken) =>
            {
                // --- The nested save validates while the method asks to persist the frame ---
                Task<EntitiesResult<EnOrder>> save = service.SaveAsync([Order("nested")], IdsOnly(), cancellationToken);
                await validating.Task;
                early = await Assert.ThrowsAsync<InvalidOperationException>(() => host.PersistAsync(cancellationToken));
                release.SetResult();
                _ = await save;

                // --- Once it returned, the frame persists ---
                _ = await host.PersistAsync(cancellationToken);
                return body.Name!;
            };

            _ = await InvokeAsync(EnlistmentModel.Stamp, new EnStamp { Name = "ab" });

            Assert.Contains("is still validating", early!.Message, StringComparison.Ordinal);
            Assert.Equal(typeof(EnOrder), Assert.Single(Assert.Single(Persists).Of(ScriptedKind.Save)).Entity);
        }

        // --- Server-owned members ---

        [Fact]
        public async Task An_enlisted_save_reapplies_the_server_owned_members_its_writer_names_and_refuses_an_audit_column_or_the_active_flag()
        {
            // --- A new row and an update: unnamed, the member is the platform's; named, the writer's ---
            _storedNotes.Add(5);
            _storedNotes.Add(6);
            EnlistSaveOptions named = new() { ServerOwned = new HashSet<string>(StringComparer.Ordinal) { nameof(EnNote.Origin) } };
            _hooks.OrderValidate = async (service, context) =>
            {
                _ = await service.EnlistSaveAsync(context.Host, [new EnNote { Text = "new", Origin = "writer" }, new EnNote { Id = 5, Text = "update", Origin = "writer" }]);
                _ = await service.EnlistSaveAsync(context.Host, [new EnNote { Text = "new", Origin = "writer" }, new EnNote { Id = 6, Text = "update", Origin = "writer" }], named);
            };
            _ = await SaveOrdersAsync(Order("A"));
            Assert.Equal(
                ["- stored", "writer writer"],
                Persists[^1].Of(ScriptedKind.Save)
                    .Where(static statement => statement.Entity == typeof(EnNote))
                    .Select(static statement => string.Join(' ', ((IReadOnlyList<EnNote>)statement.Statement!).Select(static note => note.Origin ?? "-"))));

            // --- An audit column or the active flag: an authoring bug, refused at the enlistment ---
            foreach (string platformOwned in new[] { nameof(EnNote.CreatedAt), nameof(EnNote.ModifiedById), "IsActive" })
            {
                _hooks.OrderValidate = async (service, context) => _ = await service.EnlistSaveAsync(
                    context.Host, [Note("n")], new EnlistSaveOptions { ServerOwned = new HashSet<string>(StringComparer.Ordinal) { platformOwned } });
                InvalidOperationException refused = await Assert.ThrowsAsync<InvalidOperationException>(() => SaveOrdersAsync(Order("A")));
                Assert.Contains("The platform alone writes the audit columns and the active flag", refused.Message, StringComparison.Ordinal);
            }
        }

        // --- The source ---

        [Fact]
        public async Task A_frames_writes_are_recorded_as_the_platforms_a_service_accounts_an_agents_or_the_webs()
        {
            // --- The caller's context decides an API action's and a pipeline's source ---
            RequestContext web = RequestContext.Tenantless(new GregorianCalendarSystem(), Stamp) with { Kind = PrincipalKind.User };
            Assert.Equal(
                [SaveSource.System, SaveSource.PublicApi, SaveSource.Agent, SaveSource.Web],
                new[] { web with { Kind = PrincipalKind.System }, web with { Kind = PrincipalKind.ServiceAccount }, web with { Client = "mcp" }, web }
                    .Select(OpenWriteFrame.SourceOf));

            // --- An enlisted save runs under its frame's: the web's from an API action, the platform's from a provisioning step ---
            List<SaveSource> sources = [];
            _hooks.NoteValidate = (_, context) =>
            {
                sources.Add(context.Options.Source);
                return Task.CompletedTask;
            };
            _hooks.Stamp = async (service, host, body, cancellationToken) =>
            {
                _ = await service.EnlistSaveAsync(host, [Note("acted")]);
                _ = await host.PersistAsync(cancellationToken);
                return body.Name!;
            };
            _ = await InvokeAsync(EnlistmentModel.Stamp, new EnStamp { Name = "ab" });
            await using AsyncServiceScope scope = _rig.Scope();
            EnSeedStep step = ActivatorUtilities.CreateInstance<EnSeedStep>(scope.ServiceProvider);
            _hooks.Seed = async (seed, host) =>
            {
                _ = await seed.EnlistSaveAsync(host, [Note("seeded")]);
                _ = await host.PersistAsync(Token);
            };
            await _rig.Services.GetRequiredService<IProvisioningFrameOpener>()
                .RunAsync(scope.ServiceProvider, EnlistmentModel.SeedStep, typeof(EnSeedStep), () => step.RunAsync(null!), Token);
            Assert.Equal([SaveSource.Web, SaveSource.System], sources);
        }

        // --- Attribution ---

        [Fact]
        public async Task A_persist_failure_raised_by_one_of_two_groups_on_one_target_is_translated_and_rerooted_by_that_group()
        {
            _hooks.OrderValidate = async (service, context) =>
            {
                _ = await service.EnlistSaveAsync(context.Host, [Note("first")], new EnlistSaveOptions { MapPath = Under(ValidationPath.Root.Property("First")) });
                _ = await service.EnlistSaveAsync(context.Host, [Note("second")], new EnlistSaveOptions { MapPath = Under(ValidationPath.Root.Property("Second")) });
            };

            // --- Each statement's ordinal names its own group ---
            FailPersistAt(typeof(EnNote), static ordinal => new TreeDepthExceededException(typeof(EnNote), ordinal), occurrence: 1);
            ValidationException second = await Assert.ThrowsAsync<ValidationException>(() => SaveOrdersAsync(Order("A")));
            Assert.Equal([$"Second {ValidationCodes.TreeTooDeep} {EnlistmentModel.Notes}"], Lines(second));

            FailPersistAt(typeof(EnNote), static ordinal => new TreeDepthExceededException(typeof(EnNote), ordinal), occurrence: 0);
            ValidationException first = await Assert.ThrowsAsync<ValidationException>(() => SaveOrdersAsync(Order("A")));
            Assert.Equal([$"First {ValidationCodes.TreeTooDeep} {EnlistmentModel.Notes}"], Lines(first));
        }

        // --- Helpers ---

        /// <summary>A new individual user, who has an email.</summary>
        /// <param name="name">The name.</param>
        /// <returns>The user.</returns>
        private static User Person(string name)
        {
            return new User { Name = name, Email = $"{name.ToLowerInvariant()}@example.com" };
        }

        /// <summary>The ordinal of the one raw statement of a batch with a text.</summary>
        /// <param name="batch">The batch.</param>
        /// <param name="text">The statement's text, as the guards append it.</param>
        /// <returns>The ordinal.</returns>
        private static int SqlOrdinal(ScriptedBatch batch, string text)
        {
            // The users' own statements sit among the raw ones too, their first argument an identifier or none.
            return Assert.Single(
                batch.Of(ScriptedKind.Sql),
                statement => statement.Text.ArgumentCount > 0 && statement.Text.GetArgument(0) is string first && first == text).Ordinal;
        }

        /// <summary>
        ///     Makes the order's persist recompose once, so the operation re-enters: for the caller's own
        ///     set, as a moved settings tag recomposes, or for a moved permission set with the same grants.
        /// </summary>
        /// <param name="permissions">Whether the permission set moved.</param>
        private void RecomposeOnce(bool permissions = false)
        {
            _hooks.OrderContribute = Once<EnOrderService, PersistContext<EnOrder>>(() =>
            {
                if (permissions)
                {
                    _rig.GrantOnRecompose(Grants);
                }
                else
                {
                    _rig.RecomposeOnSettings();
                }
            });
        }

        /// <summary>A persist hook that arms something on its first call only, the first composition of the batch.</summary>
        /// <typeparam name="TService">The hook's service.</typeparam>
        /// <typeparam name="TContext">The hook's persist context.</typeparam>
        /// <param name="arm">What the first call arms.</param>
        /// <returns>The hook.</returns>
        private static Func<TService, TContext, Task> Once<TService, TContext>(Action arm)
        {
            int composed = 0;
            return (_, _) =>
            {
                if (++composed == 1)
                {
                    arm();
                }

                return Task.CompletedTask;
            };
        }

        /// <summary>Asserts the guards joined a persist once each, the lock then the invariants, after its enlisted user and its every other write.</summary>
        /// <param name="persist">The persist batch.</param>
        private void GuardsFollowEveryWrite(ScriptedBatch persist)
        {
            Assert.Equal([PsGuards.Lock, PsGuards.Invariants], _guards.Calls);
            int locked = SqlOrdinal(persist, PsGuards.Lock);
            Assert.True(locked < SqlOrdinal(persist, PsGuards.Invariants));
            Assert.Contains(persist.Of(ScriptedKind.Save), static statement => statement.Entity == typeof(User));
            Assert.All([.. persist.Of(ScriptedKind.Save), .. persist.Of(ScriptedKind.Delete)], statement => Assert.True(statement.Ordinal < locked));
        }

        /// <summary>Runs the order's entity action in a scope of its own, answering ids only.</summary>
        /// <param name="ids">The target ids.</param>
        /// <returns>The action's result.</returns>
        private async Task<EntitiesResult<EnOrder>> TouchAsync(params int[] ids)
        {
            await using AsyncServiceScope scope = _rig.Scope();
            _scoped = scope.ServiceProvider;
            _hooks.Committed.Clear();
            try
            {
                return await scope.ServiceProvider.GetRequiredService<EnOrderService>().ExecuteActionAsync(EnlistmentModel.Touch, ids, arguments: null, new ActionOptions(), Token);
            }
            finally
            {
                _scoped = null;
            }
        }

        /// <summary>Deletes orders by their ids in a scope of their own.</summary>
        /// <param name="ids">The ids.</param>
        /// <returns>The delete's result.</returns>
        private async Task<AffectedResult> DeleteOrdersAsync(params int[] ids)
        {
            await using AsyncServiceScope scope = _rig.Scope();
            _scoped = scope.ServiceProvider;
            _hooks.Committed.Clear();
            try
            {
                return await scope.ServiceProvider.GetRequiredService<EnOrderService>().DeleteByIdsAsync(ids, cancellationToken: Token);
            }
            finally
            {
                _scoped = null;
            }
        }
    }
}
