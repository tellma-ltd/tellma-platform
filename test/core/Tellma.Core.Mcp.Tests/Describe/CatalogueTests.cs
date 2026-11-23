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
using Tellma.Core.Abstractions.Localization;
using Tellma.Core.Abstractions.Settings;
using Tellma.Core.Mcp.Describe;
using Tellma.Core.Mcp.Tests.Hosting;
using Tellma.Core.Mcp.Tools;
using Tellma.Core.Queryex;

namespace Tellma.Core.Mcp.Tests.Describe
{
    /// <summary>
    ///     What an agent is told of the platform's own entities, over the host's real registry: entity
    ///     names matched ignoring case, a hidden stack and its child entities answered as unknown, the
    ///     columns the tenant's shape leaves out, the sensitive operations and actions and the hidden
    ///     actions never described, and whoami's matrix without sensitive or hidden grants.
    /// </summary>
    /// <param name="fixture">The host.</param>
    public sealed class CatalogueTests(McpHost fixture) : IClassFixture<McpHost>
    {
        /// <summary>The host's services.</summary>
        private IServiceProvider Services => fixture.Host.Services;

        /// <summary>The host's stack registry.</summary>
        private IStackRegistry Registry => Services.GetRequiredService<IStackRegistry>();

        [Fact]
        public void An_entity_is_found_ignoring_case_and_a_child_carries_its_root_s_exposure()
        {
            McpEntities entities = new(Registry);

            Assert.Equal(CoreResources.Role, entities.Find("CORE.ROLE")?.Name);
            Assert.NotEmpty(Registry.ChildQueries);
            Assert.All(Registry.ChildQueries, child => Assert.Equal(Registry.Find(child.OwnerResource)!.Mcp, child.Mcp));
        }

        [Fact]
        public void A_hidden_stack_and_its_child_entities_are_answered_as_unknown()
        {
            McpEntities entities = new(new Hiding(Registry, CoreResources.Role));

            Assert.Null(entities.Find(CoreResources.Role));
            ChildQueryDescriptor member = Registry.ChildQueries.First(static child => child.OwnerResource == CoreResources.Role);
            Assert.Null(entities.Find(member.Resource));
            Assert.Throws<BadRequestException>(() => entities.Require(CoreResources.Role, "entity"));
            Assert.NotNull(entities.Find(CoreResources.User));
        }

        [Theory]
        [InlineData(MultilingualShape.Primary, false, false)]
        [InlineData(MultilingualShape.PrimaryAndSecondary, true, false)]
        [InlineData(MultilingualShape.All, true, true)]
        public void A_description_names_only_the_columns_the_tenant_s_languages_fill(MultilingualShape shape, bool secondary, bool tertiary)
        {
            string text = Describe(CoreResources.Role, shape);

            Assert.Contains("\n| Name |", text, StringComparison.Ordinal);
            Assert.Equal(secondary, text.Contains("\n| Name2 |", StringComparison.Ordinal));
            Assert.Equal(tertiary, text.Contains("\n| Name3 |", StringComparison.Ordinal));
        }

        [Fact]
        public void A_description_leaves_out_sensitive_operations_sensitive_actions_and_hidden_actions()
        {
            // --- The user's save and delete are sensitive: query and get alone are offered ---
            string user = Describe(CoreResources.User, MultilingualShape.All);
            Assert.Contains("- Operations: query, get\n", user, StringComparison.Ordinal);
            ISecurableRegistry securables = Services.GetRequiredService<ISecurableRegistry>();
            foreach (EntityActionDescriptor action in Registry.Find(CoreResources.User)!.Actions.OfType<EntityActionDescriptor>())
            {
                if (securables.Find(CoreResources.User, action.Action) is { IsSensitive: true })
                {
                    Assert.DoesNotContain($"\n### {action.Name}\n", user, StringComparison.Ordinal);
                }
            }

            // --- A job's confirm is hidden from agents ---
            StackDescriptor job = Registry.Find(CoreResources.Job)!;
            Assert.Contains(job.Actions, static action => action is { Mcp: McpExposure.Hidden, Name: "confirm" });
            Assert.DoesNotContain("\n### confirm\n", Describe(CoreResources.Job, MultilingualShape.All), StringComparison.Ordinal);

            // --- The same action, no longer hidden, is described ---
            StackDescriptor shown = job with { Actions = [.. job.Actions.Select(static action => action is EntityActionDescriptor { Mcp: McpExposure.Hidden } hidden ? Shown(hidden) : action)] };
            Assert.Contains("\n### confirm\n", Describer.Describe(new McpEntity(shown.Resource, shown, null), MultilingualShape.All, Labels), StringComparison.Ordinal);
        }

        [Fact]
        public void Whoami_s_matrix_leaves_out_sensitive_grants_and_hidden_entities_and_lists_filtered_ones()
        {
            McpEntities entities = new(new Hiding(Registry, CoreResources.Job));
            AccessDecision[] decisions =
            [
                Decision(CoreResources.User, AccessActions.Read, AccessOutcome.Unrestricted),
                Decision(CoreResources.User, AccessActions.Save, AccessOutcome.Unrestricted),
                Decision(CoreResources.Role, AccessActions.Read, AccessOutcome.Filtered),
                Decision(CoreResources.Role, AccessActions.Delete, AccessOutcome.Denied),
                Decision(CoreResources.Job, AccessActions.Read, AccessOutcome.Unrestricted),
            ];

            IReadOnlyList<WhoamiEntityAccess> matrix = WhoamiTool.Matrix(decisions, entities, Services.GetRequiredService<ISecurableRegistry>());

            Assert.Equal([CoreResources.User, CoreResources.Role], matrix.Select(static entry => entry.Entity));
            Assert.Equal([AccessActions.Read], matrix[0].Operations);
            Assert.Empty(matrix[0].Filtered);
            Assert.Equal([AccessActions.Read], matrix[1].Operations);
            Assert.Equal([AccessActions.Read], matrix[1].Filtered);
        }

        /// <summary>The describer the host composed.</summary>
        private EntityDescriber Describer => Services.GetRequiredService<EntityDescriber>();

        /// <summary>The host's labels.</summary>
        private ILabelProvider Labels => Services.GetRequiredService<ILabelProvider>();

        /// <summary>Describes one of the host's stacks under a shape.</summary>
        /// <param name="resource">The stack.</param>
        /// <param name="shape">The tenant's multilingual shape.</param>
        /// <returns>The description.</returns>
        private string Describe(string resource, MultilingualShape shape)
        {
            StackDescriptor stack = Registry.Find(resource)!;
            return Describer.Describe(new McpEntity(stack.Resource, stack, null), shape, Labels);
        }

        /// <summary>An entity action exposed to agents in full.</summary>
        /// <param name="action">The action.</param>
        /// <returns>The same action, no longer hidden.</returns>
        private static EntityActionDescriptor Shown(EntityActionDescriptor action)
        {
            return new EntityActionDescriptor(
                action.Name,
                action.Description,
                action.Mutation,
                action.AllowWhenReadOnly,
                action.Idempotent,
                action.Long,
                action.Destructive,
                McpExposure.Full,
                action.ContractRevision,
                action.ResultType,
                action.Action,
                action.SupportsFilter,
                action.LoadChildren,
                action.PostCheck,
                action.SingleTarget,
                action.IsBuiltIn,
                action.ArgumentsType);
        }

        /// <summary>An evaluator's decision.</summary>
        /// <param name="resource">The resource.</param>
        /// <param name="action">The action.</param>
        /// <param name="outcome">The outcome.</param>
        /// <returns>The decision.</returns>
        private static AccessDecision Decision(string resource, string action, AccessOutcome outcome)
        {
            return new AccessDecision(resource, action, outcome, outcome == AccessOutcome.Filtered ? FilterTree.Or([FilterTree.Leaf("Id = 1")]) : null, [], []);
        }

        /// <summary>A registry whose one stack, and the child entities under it, are hidden from agents.</summary>
        /// <param name="inner">The registry it reads.</param>
        /// <param name="hidden">The stack hidden.</param>
        private sealed class Hiding(IStackRegistry inner, string hidden) : IStackRegistry
        {
            /// <inheritdoc />
            public IReadOnlyList<StackDescriptor> Stacks => [.. inner.Stacks.Select(stack => stack.Resource == hidden ? stack with { Mcp = McpExposure.Hidden } : stack)];

            /// <inheritdoc />
            public IReadOnlyList<ChildQueryDescriptor> ChildQueries => [.. inner.ChildQueries.Select(child => child.OwnerResource == hidden ? child with { Mcp = McpExposure.Hidden } : child)];

            /// <inheritdoc />
            public IReadOnlyList<ApiServiceDescriptor> Services => inner.Services;

            /// <inheritdoc />
            public IReadOnlyList<EnlistmentDescriptor> Enlistments => inner.Enlistments;

            /// <inheritdoc />
            public StackDescriptor? Find(string resource)
            {
                return Stacks.FirstOrDefault(stack => stack.Resource == resource);
            }

            /// <inheritdoc />
            public ChildQueryDescriptor? FindChildQuery(string resource)
            {
                return ChildQueries.FirstOrDefault(child => child.Resource == resource);
            }

            /// <inheritdoc />
            public StackDescriptor? OwnerOf(TableName table)
            {
                return inner.OwnerOf(table);
            }
        }
    }
}
