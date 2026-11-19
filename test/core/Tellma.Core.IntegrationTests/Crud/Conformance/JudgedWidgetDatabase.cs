// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Diagnostics.CodeAnalysis;
using Tellma.Core.Abstractions.Api;
using Tellma.Core.Abstractions.Composition;
using Tellma.Core.Abstractions.Crud;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Validation;
using Tellma.Core.Composition;
using Tellma.Testing.Entities;

namespace Tellma.Core.IntegrationTests.Crud.Conformance
{
    /// <summary>
    ///     The databases of <see cref="ConformanceDatabase" />, served by an instance whose widget
    ///     stack runs through <see cref="JudgedWidgetService" /> and <see cref="DependentLoadsValidator" />:
    ///     a stack with a custom entity action and an API action, whose deletes and actions are judged
    ///     (so it serves no delete by query) and whose saves load across three dependent validation
    ///     rounds.
    /// </summary>
    /// <param name="sql">The SQL Server the databases are created on.</param>
    public sealed class JudgedWidgetDatabase(SqlServerFixture sql) : ConformanceDatabase(sql)
    {
        /// <inheritdoc />
        protected override void Compose(TellmaBuilder tellma)
        {
            base.Compose(tellma);
            _ = tellma.AddFeature(new JudgedWidgetFeature());
        }

        /// <summary>The feature that runs the fixture's widget stack through the judged service and the dependent-loads validator.</summary>
        private sealed class JudgedWidgetFeature : ITellmaFeature
        {
            /// <inheritdoc />
            public string Name => "judged-widgets";

            /// <inheritdoc />
            public void Declare(FeatureDeclaration declaration)
            {
                ArgumentNullException.ThrowIfNull(declaration);

                // Depending on the fixture's feature lets this registration replace its widget service.
                _ = declaration.Requires(FixtureFeature.FeatureName);
            }

            /// <inheritdoc />
            public void Contribute(FeatureContribution contribution)
            {
                ArgumentNullException.ThrowIfNull(contribution);

                // The service judges deletes, so the stack omits DeleteByQuery: a delete by query
                // names no ids its rules could see.
                _ = contribution
                    .Add(new StackContributionItem(typeof(Widget), typeof(JudgedWidgetService), StackOperations.Read | StackOperations.Save | StackOperations.Delete | StackOperations.Import))
                    .Validator<Widget, DependentLoadsValidator>();
            }
        }
    }

    /// <summary>
    ///     The judged widget stack's service: it declares the rename action and the count API action,
    ///     and overrides the delete and action validation hooks, which takes deletes and the built-in
    ///     actions off their one-round-trip path.
    /// </summary>
    /// <param name="factory">The pipeline factory.</param>
    public sealed class JudgedWidgetService(IEntityPipelineFactory factory) : EntityService<Widget>(factory)
    {
        /// <summary>The rename action's name.</summary>
        public const string Rename = "rename";

        /// <summary>The count API action's name.</summary>
        public const string Count = "count";

        /// <summary>The securable action the count API action requires.</summary>
        public const string CountAction = "Count";

        /// <summary>Renames the target widgets, once their rows loaded.</summary>
        /// <param name="context">The action's context.</param>
        /// <param name="arguments">The new name.</param>
        /// <returns>A task that completes when the rename composed.</returns>
        [EntityAction(Rename)]
        [SuppressMessage("Performance", "CA1822:Mark members as static", Justification = "An action is an instance method by contract; the registry refuses a static one.")]
        public async Task RenameAsync(ActionContext<Widget, int> context, WidgetRename arguments)
        {
            ArgumentNullException.ThrowIfNull(context);
            ArgumentNullException.ThrowIfNull(arguments);

            // --- The targets load with the first round; the update composes after it ---
            await context.Loader.LoadAsync();
            context.Update(new Dictionary<string, object?>(StringComparer.Ordinal) { [nameof(Widget.Name)] = arguments.Name });
        }

        /// <summary>Counts the widgets the caller may read.</summary>
        /// <param name="cancellationToken">The invocation's token.</param>
        /// <returns>The count.</returns>
        [ApiAction(Count, Action = CountAction, Mutation = false, Idempotent = true)]
        public async Task<int> CountAsync(CancellationToken cancellationToken)
        {
            RowPage page = await QueryAsync(new QueryRequest { Select = nameof(Widget.Id), Take = 1, IncludeCount = true }, cancellationToken);
            return page.Count ?? 0;
        }

        /// <inheritdoc />
        /// <remarks>Refuses no delete: the override alone is what takes a delete onto its validation round.</remarks>
        protected override Task ValidateDeleteAsync(DeleteContext<Widget, int> context)
        {
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        /// <remarks>Refuses no action: the override alone is what takes the built-in actions onto their validation round.</remarks>
        protected override Task ValidateActionAsync(string action, ActionContext<Widget, int> context)
        {
            return Task.CompletedTask;
        }
    }

    /// <summary>
    ///     A validator of every widget save whose loads depend on one another across three rounds,
    ///     as a rule that walks a chain of references does: it loads the anchor node, then the node
    ///     whose id follows the last one it read, twice, each read waiting for the one before.
    /// </summary>
    internal sealed class DependentLoadsValidator : IEntityValidator<Widget>
    {
        /// <summary>The rounds the validator loads in.</summary>
        public const int Rounds = 3;

        /// <inheritdoc />
        public async Task ValidateAsync(SaveContext<Widget> context)
        {
            ArgumentNullException.ThrowIfNull(context);

            // --- Each round's read names an id the round before decided ---
            int next = ConformanceDatabase.AnchorNode;
            for (int round = 0; round < Rounds; round++)
            {
                ContextRef<IReadOnlyDictionary<int, Node>> nodes = context.Loader.ByIds<Node, int>([next]);
                await context.Loader.LoadAsync();
                next = nodes.Value.Keys.DefaultIfEmpty(next).Max() + 1;
            }
        }
    }
}
