// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Api;
using Tellma.Core.Abstractions.Crud;
using Tellma.Core.Abstractions.Errors;
using Xunit;

namespace Tellma.Core.Testing.Crud
{
    // Spec 0015 §2.3 and §2.4: the operations a stack serves, the securables they run under, and
    // the enlisted writes it accepts.
    /// <content>The operations, the securables and the enlisted writes.</content>
    public abstract partial class StackConformanceTests<TEntity, TKey>
    {
        /// <summary>Every declared operation reaches the pipeline, and every operation the stack omits is refused as an authoring error.</summary>
        /// <returns>A task that completes when the case ran.</returns>
        [Fact]
        public async Task Every_declared_operation_is_reachable_and_every_absent_one_is_refused()
        {
            StackConformanceCaller writer = await WriterAsync();

            // --- Each operation, the stack operation and capability it needs, and a call that changes
            // no row: every id is one no row holds, so a write that runs refuses it as missing. Excel
            // rows are not built yet, and their import and export are not the pipeline's ---
            (string Name, bool Served, Func<EntityService<TEntity, TKey>, Task> Call)[] operations =
            [
                ("query", Serves(StackOperations.Query), service => service.QueryAsync(new QueryRequest { Select = Metadata.Key.Name, Take = 1 }, Token)),
                ("get", Serves(StackOperations.Details), service => service.GetByIdAsync(MissingId(), DetailsRequest.None, Token)),
                ("get_by_ids", Serves(StackOperations.Details), service => service.GetByIdsAsync([MissingId()], DetailsRequest.None, Token)),
                ("all", Serves(StackOperations.Details, Cacheable), service => service.GetAllCachedAsync(DetailsRequest.None, Token)),
                ("save", Serves(StackOperations.Save), service => service.SaveAsync([NewValidEntity(NextOrdinal())], new SaveOptions { ReturnEntities = false }, Token)),
                ("delete", Serves(StackOperations.Delete), service => service.DeleteByIdsAsync([MissingId()], null, Token)),
                ("delete_by_query", Serves(StackOperations.DeleteByQuery), service => service.DeleteByQueryAsync(new DeleteByQueryRequest($"{Metadata.Key.Name} = {Text(MissingId())}", null, 0), Token)),
                ("get_by_parent_ids", Serves(StackOperations.Query, Tree), service => service.Pipeline.GetByParentIdsAsync(new ParentIdsRequest([long.CreateChecked(MissingId())], Metadata.Key.Name, null, null), Token)),
                ("delete_with_descendants", Serves(StackOperations.Delete, Tree), service => service.Pipeline.DeleteWithDescendantsAsync([MissingId()], Token)),
                ("activate", ServesActivation(), service => service.ExecuteActionAsync(ActivateAction, [MissingId()], null, new ActionOptions(), Token)),
                ("deactivate", ServesActivation(), service => service.ExecuteActionAsync(DeactivateAction, [MissingId()], null, new ActionOptions(), Token)),
            ];

            foreach ((string name, bool served, Func<EntityService<TEntity, TKey>, Task> call) in operations)
            {
                Counted<Exception?> attempt = await AttemptAsync(writer, call);
                if (served)
                {
                    Assert.False(attempt.Result is InvalidOperationException, $"{Stack.Resource} declares {name}, yet its service refused it: {attempt.Result?.Message}");
                }
                else
                {
                    Assert.True(attempt.Result is InvalidOperationException, $"{Stack.Resource} omits {name}, yet its service answered {attempt.Result?.GetType().Name ?? "a result"} rather than refusing it.");
                }
            }

            // --- The declared entity actions reach the pipeline; an undeclared one is refused ---
            foreach (EntityActionDescriptor action in Stack.Actions.OfType<EntityActionDescriptor>().Where(static action => !action.IsBuiltIn))
            {
                Counted<Exception?> attempt = await AttemptAsync(writer, service => service.ExecuteActionAsync(action.Name, [MissingId()], ActionArguments(action), new ActionOptions(), Token));
                Assert.False(attempt.Result is InvalidOperationException, $"{Stack.Resource} declares the action {action.Name}, yet its service refused it: {attempt.Result?.Message}");
            }

            Counted<Exception?> undeclared = await AttemptAsync(writer, service => service.ExecuteActionAsync("conformance-undeclared", [MissingId()], null, new ActionOptions(), Token));
            Assert.True(undeclared.Result is InvalidOperationException, $"{Stack.Resource} ran an action it does not declare.");
        }

        /// <summary>The registry holds exactly the securables the descriptor implies, and each child query surface reads through its owner.</summary>
        [Fact]
        public void The_stack_registers_its_securables_with_their_filter_roots_and_each_child_surface_reads_through_its_owner()
        {
            ISecurableRegistry securables = Harness.Services.GetRequiredService<ISecurableRegistry>();
            IStackRegistry registry = Harness.Services.GetRequiredService<IStackRegistry>();

            // --- The stack's own: per its operations and actions, under its feature, owned by no row
            // above; a routed service's action naming the resource adds its own, with no filter ---
            HashSet<(string Action, string? FilterRoot)> routed = [.. registry.Services
                .SelectMany(static service => service.Actions)
                .Select(static action => action.Securable)
                .OfType<SecurableRef>()
                .Where(securable => securable.Resource == Stack.Resource)
                .Select(static securable => (securable.Action, (string?)null))];
            HashSet<(string Action, string? FilterRoot)> expected = [.. ExpectedSecurables(Stack), .. routed];
            IReadOnlyList<SecurableDescriptor> registered = securables.ForResource(Stack.Resource);

            Assert.Equal(
                expected.Select(Line).Order(StringComparer.Ordinal),
                registered.Select(static securable => Line((securable.Action, securable.FilterRoot))).Distinct().Order(StringComparer.Ordinal));
            Assert.All(
                registered.Where(securable => !routed.Contains((securable.Action, securable.FilterRoot))),
                securable => Assert.Equal((Stack.Feature, null), (securable.Feature, securable.Owner)));

            // --- Each child query surface: Read alone, filtered on its own rows, owned through the
            // navigation to the row above, under the root stack's feature ---
            foreach (ChildQueryDescriptor child in ChildSurfaces())
            {
                SecurableDescriptor read = Assert.Single(securables.ForResource(child.Resource));
                Assert.Equal(
                    (AccessActions.Read, child.Resource, new SecurableOwner(child.OwnerResource, child.OwnerNavigation), Stack.Feature),
                    (read.Action, read.FilterRoot, read.Owner, read.Feature));
            }

            static string Line((string Action, string? FilterRoot) securable)
            {
                return $"{securable.Action} {securable.FilterRoot ?? "-"}";
            }
        }

        /// <summary>A caller who holds none of the stack's securables is refused each operation, naming the securable it lacks.</summary>
        /// <returns>A task that completes when the case ran.</returns>
        [Fact]
        public async Task A_caller_without_the_stacks_securables_is_refused_each_operation_naming_its_securable()
        {
            // --- Each standard securable the stack registers, and an operation it guards; a write
            // grant implies the read, so the caller holds no grant of the stack at all ---
            StackConformanceCaller caller = await CallerAsync(none: true);
            (string Action, bool Served, Func<EntityService<TEntity, TKey>, Task> Call)[] guarded =
            [
                (AccessActions.Read, Serves(StackOperations.Query), service => service.QueryAsync(new QueryRequest { Select = Metadata.Key.Name, Take = 1 }, Token)),
                (AccessActions.Save, Serves(StackOperations.Save), service => service.SaveAsync([NewValidEntity(NextOrdinal())], new SaveOptions(), Token)),
                (AccessActions.Delete, Serves(StackOperations.Delete), service => service.DeleteByIdsAsync([MissingId()], null, Token)),
            ];

            foreach ((string action, _, Func<EntityService<TEntity, TKey>, Task> call) in guarded.Where(static operation => operation.Served))
            {
                Counted<Exception?> attempt = await AttemptAsync(caller, call);

                Assert.True(attempt.Result is ForbiddenException, $"{Stack.Resource}: a caller without {action} was answered {attempt.Result?.GetType().Name ?? "with a result"} rather than refused.");
                var refusal = (ForbiddenException)attempt.Result!;
                Assert.Equal((Stack.Resource, action, ForbiddenReason.NoGrant), (refusal.Resource, refusal.Action, refusal.Reason));
            }
        }

        /// <summary>
        ///     A stack that serves enlisted writes takes rows from a declared writer at no round trip of
        ///     their own and refuses an undeclared one; a stack that does not is the target of no pair.
        /// </summary>
        /// <returns>A task that completes when the case ran.</returns>
        [Fact]
        public async Task A_declared_writer_enlists_at_no_round_trip_of_its_own_and_an_undeclared_one_is_refused()
        {
            IStackRegistry registry = Harness.Services.GetRequiredService<IStackRegistry>();
            if (!Serves(StackOperations.Enlist))
            {
                Assert.DoesNotContain(registry.Enlistments, enlistment => enlistment.TargetResource == Stack.Resource);
                return;
            }

            StackConformanceCaller writer = await WriterAsync();

            // --- One enlisted write first, so the buffers and the caller are warm ---
            _ = await CountedAsync(writer, scope => Enlisted(scope, NewValidEntity(NextOrdinal())));

            // --- The host write alone, then with a row enlisted: the same round trips ---
            Counted<TKey?> plain = await CountedAsync(writer, scope => Enlisted(scope, null));
            Counted<TKey?> enlisted = await CountedAsync(writer, scope => Enlisted(scope, NewValidEntity(NextOrdinal())));

            Assert.True(plain.RoundTrips == enlisted.RoundTrips, $"{Stack.Resource}: the enlisted write took {enlisted.RoundTrips - plain.RoundTrips} round trips of its own.");
            Assert.True(enlisted.Result is TKey id && await ExistsAsync(id), $"{Stack.Resource}: the enlisted row was not committed with its host.");

            // --- A writer the registry holds no pair for is refused before anything is enlisted ---
            Exception? refusal = null;
            _ = await CountedAsync(writer, async scope =>
            {
                await EnlistAsync(scope, async (host, _) => refusal = await Record(() => new UndeclaredWriter().EnlistSaveAsync(host, [NewValidEntity(NextOrdinal())])));
                return 0;
            });
            Assert.True(refusal is InvalidOperationException, $"{Stack.Resource} took rows from an undeclared writer: {refusal?.GetType().Name ?? "no refusal"}.");

            // A write enlisted through the closing class's frame, answering the enlisted row's id.
            async Task<TKey?> Enlisted(IServiceProvider scope, TEntity? row)
            {
                TKey? id = null;
                await EnlistAsync(scope, async (host, declared) =>
                {
                    if (row is not null)
                    {
                        EnlistedSave<TEntity> saved = await declared.EnlistSaveAsync(host, [row]);
                        id = Assert.Single(saved.Rows).Id;
                    }
                });
                return id;
            }

            static async Task<Exception?> Record(Func<Task> call)
            {
                try
                {
                    await call();
                    return null;
                }
#pragma warning disable CA1031 // The refusal is recorded so the host's write completes and the case judges it.
                catch (Exception exception)
#pragma warning restore CA1031
                {
                    return exception;
                }
            }
        }

        /// <summary>A writer of the stack the registry holds no pair for.</summary>
        private sealed class UndeclaredWriter : IEnlists<TEntity>;
    }
}
