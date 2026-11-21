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
        /// <summary>
        ///     Every declared operation answers as it should when no row it names exists, and every
        ///     operation the stack omits is refused as an authoring error.
        /// </summary>
        /// <returns>A task that completes when the case ran.</returns>
        [Fact]
        public async Task Every_declared_operation_is_reachable_and_every_absent_one_is_refused()
        {
            StackConformanceCaller writer = await WriterAsync();

            // --- Each operation, whether the stack serves it, and what it answers given an id no row
            // holds, so no call changes a row. A call answers the rows its answer holds, or null when
            // its answer holds none to count. Each takes the scope's services rather than the stack's
            // service, so an operation another service serves (an import) joins the table as one more
            // row once the platform serves it ---
            List<(string Name, bool Served, Reach Expected, Func<IServiceProvider, TKey, Task<int?>> Call)> operations =
            [
                ("query", Serves(StackOperations.Query), Reach.Answers, async (scope, _) => (await Service(scope).QueryAsync(new QueryRequest { Select = Metadata.Key.Name, Take = 1 }, Token)).Rows.RowCount),
                ("get", Serves(StackOperations.Details), Reach.NotFound, async (scope, id) => (await Service(scope).GetByIdAsync(id, DetailsRequest.None, Token)).Ids.Count),
                ("get_by_ids", Serves(StackOperations.Details), Reach.AnswersNone, async (scope, id) => (await Service(scope).GetByIdsAsync([id], DetailsRequest.None, Token)).Ids.Count),
                ("all", Serves(StackOperations.Details, Cacheable), Reach.Answers, async (scope, _) => (await Service(scope).GetAllCachedAsync(DetailsRequest.None, Token)).Ids.Count),
                ("save", Serves(StackOperations.Save), Reach.Answers, async (scope, _) => (await Service(scope).SaveAsync([NewValidEntity(NextOrdinal())], new SaveOptions { ReturnEntities = false }, Token)).Ids.Count),
                ("delete", Serves(StackOperations.Delete), Reach.NotFound, async (scope, id) => (await Service(scope).DeleteByIdsAsync([id], null, Token)).Count),
                ("delete_by_query", Serves(StackOperations.DeleteByQuery), Reach.AnswersNone, async (scope, id) => (await Service(scope).DeleteByQueryAsync(new DeleteByQueryRequest($"{Metadata.Key.Name} = {Text(id)}", null, 0), Token)).Count),
                ("get_by_parent_ids", Serves(StackOperations.Query, Tree), Reach.AnswersNone, async (scope, id) => (await Service(scope).Pipeline.GetByParentIdsAsync(ByParents([id]), Token)).RowCount),
                ("delete_with_descendants", Serves(StackOperations.Delete, Tree), Reach.NotFound, async (scope, id) => (await Service(scope).Pipeline.DeleteWithDescendantsAsync([id], Token)).Count),
                ("activate", ServesActivation(), Reach.NotFound, (scope, id) => ActAsync(scope, ActivateAction, id, null)),
                ("deactivate", ServesActivation(), Reach.NotFound, (scope, id) => ActAsync(scope, DeactivateAction, id, null)),
                ("excel_rows", Serves(StackOperations.Export), Reach.AnswersNone, (scope, id) => ExcelRowsAsync(Service(scope).ExcelRowSource(ExportByIds([id])), ExcelTree())),
            ];

            // --- The declared entity actions, each with its arguments, and the declared API actions,
            // each with its body; then an action the stack does not declare ---
            foreach (EntityActionDescriptor action in Stack.Actions.OfType<EntityActionDescriptor>().Where(static action => !action.IsBuiltIn))
            {
                operations.Add(($"the action {action.Name}", true, Reach.NotFound, (scope, id) => ActAsync(scope, action.Name, id, ActionArguments(action))));
            }

            foreach (ApiActionDescriptor action in Stack.Actions.OfType<ApiActionDescriptor>())
            {
                operations.Add(($"the API action {action.Name}", true, Reach.Answers, (scope, _) => InvokeAsync(scope, action)));
            }

            operations.Add(("an undeclared action", false, Reach.Answers, (scope, id) => ActAsync(scope, "conformance-undeclared", id, null)));

            // --- Each served operation answers as expected; each absent one is an authoring error ---
            foreach ((string name, bool served, Reach expected, Func<IServiceProvider, TKey, Task<int?>> call) in operations)
            {
                TKey missing = MissingId();
                int? rows = null;
                Counted<Exception?> attempt = await AttemptInScopeAsync(writer, async scope => rows = await call(scope, missing));
                Exception? failure = attempt.Result;
                if (!served)
                {
                    Assert.True(failure is InvalidOperationException, $"{Stack.Resource} omits {name}, yet its service answered {failure?.GetType().Name ?? "a result"} rather than refusing it.");
                    continue;
                }

                switch (expected)
                {
                    case Reach.NotFound:
                        Assert.True(
                            failure is NotFoundException refusal && refusal.Resource == Stack.Resource && refusal.Ids.SequenceEqual([Text(missing)]),
                            $"{Stack.Resource}: {name} of a missing row answered {Describe(failure)} rather than as not found, naming it.");
                        break;
                    case Reach.AnswersNone:
                        Assert.True(failure is null && rows == 0, $"{Stack.Resource}: {name} naming a missing row answered {(failure is null ? $"{rows} rows" : Describe(failure))} rather than none.");
                        break;
                    case Reach.Answers:
                        Assert.True(failure is null, $"{Stack.Resource}: {name} answered {Describe(failure)} rather than a result.");
                        break;
                    default:
                        throw new InvalidOperationException($"The outcome {expected} is not one the case judges.");
                }
            }

            // A failure as its type and message, for an assertion's message.
            static string Describe(Exception? failure)
            {
                return failure is null ? "a result" : $"{failure.GetType().Name} ({failure.Message})";
            }
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
            List<(string Resource, string Action, bool Served, Func<IServiceProvider, Task> Call)> guarded =
            [
                (Stack.Resource, AccessActions.Read, Serves(StackOperations.Query), scope => Service(scope).QueryAsync(new QueryRequest { Select = Metadata.Key.Name, Take = 1 }, Token)),
                (Stack.Resource, AccessActions.Save, Serves(StackOperations.Save), scope => Service(scope).SaveAsync([NewValidEntity(NextOrdinal())], new SaveOptions(), Token)),
                (Stack.Resource, AccessActions.Delete, Serves(StackOperations.Delete), scope => Service(scope).DeleteByIdsAsync([MissingId()], null, Token)),
                (Stack.Resource, AccessActions.Read, Serves(StackOperations.Export), scope => ExcelRowsAsync(Service(scope).ExcelRowSource(ExportByIds([MissingId()])), ExcelTree())),
            ];

            // --- Each entity action, the built-ins among them, under its own securable; each API
            // action under the securable it names, a member-only one under none ---
            foreach (EntityActionDescriptor action in Stack.Actions.OfType<EntityActionDescriptor>())
            {
                guarded.Add((Stack.Resource, action.Action, true, scope => ActAsync(scope, action.Name, MissingId(), action.IsBuiltIn ? null : ActionArguments(action))));
            }

            foreach (ApiActionDescriptor action in Stack.Actions.OfType<ApiActionDescriptor>())
            {
                if (action.Securable is SecurableRef securable)
                {
                    guarded.Add((securable.Resource, securable.Action, true, scope => InvokeAsync(scope, action)));
                }
            }

            foreach ((string resource, string action, _, Func<IServiceProvider, Task> call) in guarded.Where(static operation => operation.Served))
            {
                Counted<Exception?> attempt = await AttemptInScopeAsync(caller, call);

                Assert.True(attempt.Result is ForbiddenException, $"{Stack.Resource}: a caller without {action} was answered {attempt.Result?.GetType().Name ?? "with a result"} rather than refused.");
                var refusal = (ForbiddenException)attempt.Result!;
                Assert.Equal((resource, action, ForbiddenReason.NoGrant), (refusal.Resource, refusal.Action, refusal.Reason));
            }
        }

        /// <summary>Runs an entity action of the stack on one row.</summary>
        /// <param name="scope">The scope's services.</param>
        /// <param name="action">The action's name.</param>
        /// <param name="id">The row's id.</param>
        /// <param name="arguments">The action's arguments; null for none.</param>
        /// <returns>The rows the action answered.</returns>
        private static async Task<int?> ActAsync(IServiceProvider scope, string action, TKey id, object? arguments)
        {
            return (await Service(scope).ExecuteActionAsync(action, [id], arguments, new ActionOptions(), Token)).Ids.Count;
        }

        /// <summary>Invokes an API action of the stack with the body the closing class gives it.</summary>
        /// <param name="scope">The scope's services.</param>
        /// <param name="action">The action.</param>
        /// <returns>Null: an action's answer holds no rows to count.</returns>
        private async Task<int?> InvokeAsync(IServiceProvider scope, ApiActionDescriptor action)
        {
            _ = await scope.GetRequiredService<IApiActionInvoker>().InvokeAsync(action, ApiActionArguments(action), Token);
            return null;
        }

        /// <summary>
        ///     A stack whose deletes are judged serves no delete by query, which names no ids its
        ///     rules could see and so would delete past them.
        /// </summary>
        [Fact]
        public void A_stack_whose_deletes_are_judged_serves_no_delete_by_query()
        {
            Assert.False(
                ValidatesDeletes && Serves(StackOperations.DeleteByQuery),
                $"The stack {Stack.Resource} judges its deletes yet serves a delete by query, which names no ids those rules could see: omit DeleteByQuery from its operations.");
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

        /// <summary>What a served operation answers when no row it names exists.</summary>
        private enum Reach
        {
            /// <summary>A result, whatever rows it holds.</summary>
            Answers,

            /// <summary>A result that holds no rows.</summary>
            AnswersNone,

            /// <summary>A refusal as not found, naming the stack's resource and the id.</summary>
            NotFound,
        }

        /// <summary>A writer of the stack the registry holds no pair for.</summary>
        private sealed class UndeclaredWriter : IEnlists<TEntity>;
    }
}
