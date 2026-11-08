// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Reflection;
using Tellma.Core.Abstractions.Api;
using Tellma.Core.Abstractions.Crud;

namespace Tellma.Core.Abstractions.Tests.Crud
{
    /// <summary>
    ///     The annotations that declare a stack and its operations: their targets, what a subclass
    ///     inherits, and the defaults the platform's stacks run under when nothing is declared.
    /// </summary>
    public class CrudAnnotationTests
    {
        [Fact]
        public void Stack_operations_are_flags_with_enlistment_outside_all()
        {
            Assert.Equal(
                [0, 1, 2, 4, 8, 16, 32, 64],
                new[]
                {
                    StackOperations.None, StackOperations.Query, StackOperations.Details, StackOperations.Save,
                    StackOperations.Delete, StackOperations.Import, StackOperations.Export, StackOperations.Enlist,
                }.Select(static operation => (int)operation));

            Assert.Equal(StackOperations.Query | StackOperations.Details | StackOperations.Export, StackOperations.Read);
            Assert.Equal(StackOperations.Read | StackOperations.Save | StackOperations.Delete | StackOperations.Import, StackOperations.All);
            Assert.False(StackOperations.All.HasFlag(StackOperations.Enlist));
            Assert.NotNull(typeof(StackOperations).GetCustomAttribute<FlagsAttribute>());
        }

        [Fact]
        public void A_stack_declaration_defaults_to_every_operation_and_the_platform_ceilings()
        {
            StackAttribute stack = new();

            Assert.Equal(StackOperations.All, stack.Operations);
            Assert.Equal(
                (10_000, 10_000, 100_000, 10_000, 100_000, 10_000, 10_000, 3, 3, 200, 16),
                (stack.MaxTake, stack.CountCap, stack.MaxSkipWindow, stack.MaxSaveCount, stack.MaxRowsPerSave, stack.MaxIds,
                    stack.MaxDeleteByQueryRows, stack.MaxExpandDepth, stack.MaxValidationRounds, stack.MaxSearchLength, stack.MaxExtras));
            Assert.Null(stack.DefaultOrderBy);
        }

        [Fact]
        public void The_default_select_is_declared_once_by_its_own_attribute()
        {
            // The stack declaration carries the ordering only; the default select has one home.
            Assert.Null(typeof(StackAttribute).GetProperty("DefaultSelect"));
        }

        [Theory]
        [InlineData(typeof(StackAttribute), AttributeTargets.Class, false)]
        [InlineData(typeof(EntityActionAttribute), AttributeTargets.Method, false)]
        [InlineData(typeof(ApiActionAttribute), AttributeTargets.Method, false)]
        [InlineData(typeof(DetailsExtrasAttribute), AttributeTargets.Class, true)]
        [InlineData(typeof(ApiRouteAttribute), AttributeTargets.Class, false)]
        [InlineData(typeof(EntityRefAttribute), AttributeTargets.Property, false)]
        public void Each_annotation_has_its_target_and_is_inherited(Type attribute, AttributeTargets target, bool allowMultiple)
        {
            AttributeUsageAttribute usage = attribute.GetCustomAttribute<AttributeUsageAttribute>()!;

            Assert.Equal(target, usage.ValidOn);
            Assert.Equal(allowMultiple, usage.AllowMultiple);
            Assert.True(usage.Inherited);
            Assert.True(attribute.IsSealed);
        }

        [Fact]
        public void A_leaf_inherits_its_pack_defaults_stack_declaration_until_it_declares_its_own()
        {
            Assert.Equal(StackOperations.Read, typeof(LookupLeaf).GetCustomAttribute<StackAttribute>(inherit: true)!.Operations);
            Assert.Equal(StackOperations.All | StackOperations.Enlist, typeof(EnlistedLeaf).GetCustomAttribute<StackAttribute>(inherit: true)!.Operations);
        }

        [Fact]
        public void An_entity_action_defaults_to_a_filtered_mutation_with_no_extras()
        {
            EntityActionAttribute action = new("take-over");

            Assert.Equal("take-over", action.Name);
            Assert.Null(action.Action);
            Assert.True(action.SupportsFilter);
            Assert.True(action.Mutation);
            Assert.False(action.LoadChildren || action.PostCheck || action.SingleTarget || action.AllowWhenReadOnly
                || action.Idempotent || action.Long || action.Destructive);
            Assert.Equal(McpExposure.Full, action.Mcp);
            Assert.Null(action.Description);
            Assert.Equal(0, action.ContractRevision);
        }

        [Fact]
        public void An_api_action_defaults_to_a_securable_mutation()
        {
            ApiActionAttribute action = new("me/save");

            Assert.Equal("me/save", action.Name);
            Assert.Null(action.Action);
            Assert.Null(action.Resource);
            Assert.True(action.Mutation);
            Assert.False(action.MemberOnly || action.AllowWhenReadOnly || action.Idempotent || action.Long || action.Destructive);
            Assert.Equal(McpExposure.Full, action.Mcp);
            Assert.Null(action.Description);
            Assert.Equal(0, action.ContractRevision);
        }

        [Fact]
        public void An_overriding_action_method_stays_the_action_it_overrides()
        {
            MethodInfo entityAction = typeof(DistributionService).GetMethod(nameof(PackService.PostAsync))!;
            MethodInfo apiAction = typeof(DistributionService).GetMethod(nameof(PackService.StatusAsync))!;

            Assert.Equal("post", entityAction.GetCustomAttribute<EntityActionAttribute>(inherit: true)!.Name);
            Assert.Equal("status", apiAction.GetCustomAttribute<ApiActionAttribute>(inherit: true)!.Name);
        }

        [Fact]
        public void A_classes_details_extras_are_its_own_and_its_base_classes()
        {
            string[] names = [.. typeof(DistributionService)
                .GetCustomAttributes<DetailsExtrasAttribute>(inherit: true)
                .SelectMany(static extras => extras.Names)
                .Order(StringComparer.Ordinal)];

            Assert.Equal(["activity", "members", "problems"], names);
            Assert.Empty(new DetailsExtrasAttribute(null!).Names);
        }

        [Fact]
        public void A_route_service_and_an_entity_reference_carry_their_names()
        {
            Assert.Equal("access", typeof(AccessService).GetCustomAttribute<ApiRouteAttribute>()!.Route);

            PropertyInfo assignee = typeof(AssignArguments).GetProperty(nameof(AssignArguments.AssigneeId))!;
            Assert.Equal("core.User", assignee.GetCustomAttribute<EntityRefAttribute>()!.Resource);
        }

        /// <summary>A lookup's pack default, read-only.</summary>
        [Stack(Operations = StackOperations.Read)]
        private class LookupDefault;

        /// <summary>A leaf that keeps the lookup's declaration.</summary>
        private sealed class LookupLeaf : LookupDefault;

        /// <summary>A leaf that declares its own operations over the inherited ones.</summary>
        [Stack(Operations = StackOperations.All | StackOperations.Enlist)]
        private sealed class EnlistedLeaf : LookupDefault;

        /// <summary>A pack's service with an entity action, an api action and details extras.</summary>
        [DetailsExtras("members", "problems")]
        private class PackService
        {
            /// <summary>An entity action a distribution may override.</summary>
            /// <returns>A completed task.</returns>
            [EntityAction("post", LoadChildren = true)]
            public virtual Task PostAsync()
            {
                return Task.CompletedTask;
            }

            /// <summary>An api action a distribution may override.</summary>
            /// <returns>A completed task.</returns>
            [ApiAction("status", Action = "Read", Mutation = false)]
            public virtual Task StatusAsync()
            {
                return Task.CompletedTask;
            }
        }

        /// <summary>A distribution's service overriding both actions and adding an extra.</summary>
        [DetailsExtras("activity")]
        private sealed class DistributionService : PackService
        {
            /// <inheritdoc />
            public override Task PostAsync()
            {
                return Task.CompletedTask;
            }

            /// <inheritdoc />
            public override Task StatusAsync()
            {
                return Task.CompletedTask;
            }
        }

        /// <summary>A route service.</summary>
        [ApiRoute("access")]
        private sealed class AccessService;

        /// <summary>An action's arguments naming another entity's row.</summary>
        /// <param name="AssigneeId">The assignee's user id.</param>
        /// <param name="Comment">An optional comment.</param>
        private sealed record AssignArguments([property: EntityRef("core.User")] int AssigneeId, string? Comment);
    }
}
