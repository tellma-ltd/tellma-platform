// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Numerics;
using Tellma.Core.Abstractions.Api;
using Tellma.Core.Abstractions.Crud;
using Tellma.Core.Abstractions.Entities;
using Tellma.Core.Abstractions.Excel;
using Tellma.Core.Abstractions.Validation;

namespace Tellma.Core.Abstractions.Tests.Crud
{
    /// <summary>
    ///     A stack's service forwards every operation to the pipeline its factory builds, and offers
    ///     the pipeline its hooks, which do nothing until a stack overrides them.
    /// </summary>
    public class EntityServiceTests
    {
        [Fact]
        public void A_service_hands_itself_to_the_factory_and_keeps_the_pipeline_it_builds()
        {
            (IEntityPipeline<Widget, int> pipeline, _) = RecordingProxy.For<IEntityPipeline<Widget, int>>();
            Factory factory = new(pipeline);

            WidgetService service = new(factory);

            Assert.Same(service, factory.Behavior);
            Assert.Same(pipeline, service.Pipeline);
        }

        [Fact]
        public void A_factory_that_builds_no_pipeline_fails_the_service()
        {
            InvalidOperationException refused = Assert.Throws<InvalidOperationException>(() => new WidgetService(new Factory(null)));

            Assert.Contains(nameof(WidgetService), refused.Message, StringComparison.Ordinal);
            _ = Assert.Throws<ArgumentNullException>(() => new WidgetService(null!));
        }

        [Fact]
        public async Task Each_operation_reaches_the_pipeline_with_its_arguments_and_token()
        {
            using CancellationTokenSource source = new();
            CancellationToken token = source.Token;
            QueryRequest query = new();
            DetailsRequest details = new();
            SaveOptions save = new();
            ActionOptions action = new();
            DeleteByQueryRequest delete = new("IsActive = false", null, 2);
            ExportSource export = new ExportSource.All();
            int[] ids = [1, 2];
            Widget[] rows = [new()];

            // Each operation is checked against the one call it makes.
            await Check(service => service.QueryAsync(query, token), "QueryAsync", query, token);
            await Check(service => service.GetByIdAsync(3, details, token), "GetByIdAsync", 3, details, token);
            await Check(service => service.GetByIdsAsync(ids, details, token), "GetByIdsAsync", ids, details, token);
            await Check(service => service.GetAllCachedAsync(details, token), "GetAllCachedAsync", details, token);
            await Check(service => service.SaveAsync(rows, save, token), "SaveAsync", rows, save, token);
            await Check(service => service.DeleteByIdsAsync(ids, null, token), "DeleteByIdsAsync", ids, null, token);
            await Check(service => service.DeleteByQueryAsync(delete, token), "DeleteByQueryAsync", delete, token);
            await Check(service => service.ExecuteActionAsync("archive", ids, 5, action, token), "ExecuteActionAsync", "archive", ids, 5, action, token);
            await Check(service => service.ExecuteActionAsync<int>("count", ids, null, action, token), "ExecuteActionAsync", "count", ids, null, action, token);
            await Check(service => Task.FromResult(service.ExcelRowSource(export)), "CreateExcelRowSource", export);
        }

        [Fact]
        public async Task An_operation_refuses_a_missing_argument_before_the_pipeline_sees_it()
        {
            (IEntityPipeline<Widget, int> pipeline, RecordingProxy recorder) = RecordingProxy.For<IEntityPipeline<Widget, int>>();
            WidgetService service = new(new Factory(pipeline));

            CancellationToken token = TestContext.Current.CancellationToken;
            _ = await Assert.ThrowsAsync<ArgumentNullException>(() => service.QueryAsync(null!, token));
            _ = await Assert.ThrowsAsync<ArgumentNullException>(() => service.SaveAsync([], null!, token));
            _ = Assert.Throws<ArgumentNullException>(() => service.ExcelRowSource(null!));
            _ = await Assert.ThrowsAsync<ArgumentException>(() => service.ExecuteActionAsync(" ", [], null, new ActionOptions(), token));
            Assert.Empty(recorder.Calls);
        }

        [Fact]
        public async Task The_hooks_do_nothing_until_a_stack_overrides_one()
        {
            (IEntityPipeline<Widget, int> pipeline, _) = RecordingProxy.For<IEntityPipeline<Widget, int>>();
            IEntityBehavior<Widget, int> plain = new WidgetService(new Factory(pipeline));
            OverridingService overriding = new(new Factory(pipeline));
            SaveContext<Widget> context = CrudTestItems.SaveContext([], []);

            await plain.PreprocessAsync(context);
            await plain.ValidateAsync(context);
            await plain.ContributeAsync(null!);
            await plain.AfterCommitAsync(null!);
            plain.ContributeDetails(null!);
            Assert.Null(plain.SearchFilter("east"));
            Assert.Null(plain.RefuseOnRecord(new Widget(), "Update"));

            // The pipeline reaches a protected override through the behaviour it was handed.
            await ((IEntityBehavior<Widget, int>)overriding).ValidateAsync(context);
            Assert.Same(context, overriding.Validated);
            Assert.Equal("Posted", ((IEntityBehavior<Widget, int>)overriding).RefuseOnRecord(new Widget(), "Update"));
        }

        [Fact]
        public void A_service_over_int_keys_is_the_common_case()
        {
            Assert.Equal(typeof(EntityService<Widget, int>), typeof(EntityService<Widget>).BaseType);
        }

        [Fact]
        public async Task Activation_is_the_built_in_action_of_that_name_with_default_options()
        {
            (IEntityPipeline<Widget, int> pipeline, RecordingProxy recorder) = RecordingProxy.For<IEntityPipeline<Widget, int>>();
            WidgetService service = new(new Factory(pipeline));
            ActionOptions options = new(ReturnEntities: true);

            _ = await service.ActivateAsync([4], cancellationToken: TestContext.Current.CancellationToken);
            _ = await service.DeactivateAsync([4], options, TestContext.Current.CancellationToken);

            Assert.Equal(["activate", "deactivate"], recorder.Calls.Select(static call => (string)call.Arguments[0]!));
            Assert.All(recorder.Calls, static call => Assert.Null(call.Arguments[2]));
            Assert.Equal(new ActionOptions(), recorder.Calls[0].Arguments[3]);
            Assert.Same(options, recorder.Calls[1].Arguments[3]);
        }

        [Fact]
        public async Task The_tree_operations_reach_the_pipeline()
        {
            (IEntityPipeline<Node, int> pipeline, RecordingProxy recorder) = RecordingProxy.For<IEntityPipeline<Node, int>>();
            NodeService service = new(new Factory(pipeline));
            ParentIdsRequest request = new([1], null, null, null);
            int[] ids = [2];

            _ = await service.GetByParentIdsAsync(request, TestContext.Current.CancellationToken);
            _ = await service.DeleteWithDescendantsAsync(ids, TestContext.Current.CancellationToken);

            Assert.Equal(["GetByParentIdsAsync", "DeleteWithDescendantsAsync"], recorder.Calls.Select(static call => call.Method.Name));
            Assert.Same(request, recorder.Calls[0].Arguments[0]);
            Assert.Same(ids, recorder.Calls[1].Arguments[0]);
            _ = await Assert.ThrowsAsync<ArgumentNullException>(
                () => EntityServiceExtensions.GetByParentIdsAsync<Node, int>(null!, request, TestContext.Current.CancellationToken));
        }

        [Fact]
        public async Task A_component_implements_only_what_it_needs()
        {
            SaveOnlyValidator validator = new();
            ContributeOnlyEffect effect = new();

            await ((IEntityValidator<Widget>)validator).ValidateDeleteAsync(null!);
            await ((IEntityValidator<Widget>)validator).ValidateActionAsync("archive", null!);
            await ((IPersistEffect<Widget>)effect).AfterCommitAsync(null!);
        }

        /// <summary>Calls one operation of a fresh service and checks the one pipeline call it made.</summary>
        /// <param name="operation">The operation.</param>
        /// <param name="member">The pipeline member it must call.</param>
        /// <param name="expected">The arguments it must pass, compared by identity for references.</param>
        /// <returns>A task that completes when checked.</returns>
        private static async Task Check(Func<WidgetService, Task> operation, string member, params object?[] expected)
        {
            (IEntityPipeline<Widget, int> pipeline, RecordingProxy recorder) = RecordingProxy.For<IEntityPipeline<Widget, int>>();
            await operation(new WidgetService(new Factory(pipeline)));

            RecordedCall call = recorder.Single(member);
            Assert.Equal(expected.Length, call.Arguments.Count);
            for (int position = 0; position < expected.Length; position++)
            {
                if (expected[position] is { } value && !value.GetType().IsValueType && value is not string)
                {
                    Assert.Same(value, call.Arguments[position]);
                }
                else
                {
                    Assert.Equal(expected[position], call.Arguments[position]);
                }
            }
        }

        /// <summary>A factory that answers with a given pipeline and remembers the behaviour it was handed.</summary>
        /// <param name="pipeline">The pipeline to answer with; null for none.</param>
        private sealed class Factory(object? pipeline) : IEntityPipelineFactory
        {
            /// <summary>The behaviour the factory was handed.</summary>
            public object? Behavior { get; private set; }

            /// <inheritdoc />
            public IEntityPipeline<TEntity, TKey> Create<TEntity, TKey>(IEntityBehavior<TEntity, TKey> behavior)
                where TEntity : Entity<TKey>
                where TKey : struct, IBinaryInteger<TKey>
            {
                Behavior = behavior;
                return (IEntityPipeline<TEntity, TKey>)pipeline!;
            }
        }

        /// <summary>A stack that overrides nothing.</summary>
        /// <param name="factory">The pipeline factory.</param>
        private sealed class WidgetService(IEntityPipelineFactory factory) : EntityService<Widget>(factory);

        /// <summary>A tree stack that overrides nothing.</summary>
        /// <param name="factory">The pipeline factory.</param>
        private sealed class NodeService(IEntityPipelineFactory factory) : EntityService<Node>(factory);

        /// <summary>A stack that overrides a hook of each shape.</summary>
        /// <param name="factory">The pipeline factory.</param>
        private sealed class OverridingService(IEntityPipelineFactory factory) : EntityService<Widget>(factory)
        {
            /// <summary>The context the validation hook ran with.</summary>
            public SaveContext<Widget>? Validated { get; private set; }

            /// <inheritdoc />
            protected override Task ValidateAsync(SaveContext<Widget> context)
            {
                Validated = context;
                return Task.CompletedTask;
            }

            /// <inheritdoc />
            protected override string? RefuseOnRecord(Widget row, string ability)
            {
                return "Posted";
            }
        }

        /// <summary>A validator of saves only.</summary>
        private sealed class SaveOnlyValidator : IEntityValidator<Widget>
        {
            /// <inheritdoc />
            public Task ValidateAsync(SaveContext<Widget> context)
            {
                return Task.CompletedTask;
            }
        }

        /// <summary>An effect that only contributes to the persist batch.</summary>
        private sealed class ContributeOnlyEffect : IPersistEffect<Widget>
        {
            /// <inheritdoc />
            public Task ContributeAsync(PersistContext<Widget> context)
            {
                return Task.CompletedTask;
            }
        }
    }
}
