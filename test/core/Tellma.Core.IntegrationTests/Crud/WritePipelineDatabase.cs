// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Composition;
using Tellma.Core.Abstractions.Crud;
using Tellma.Core.Abstractions.Validation;
using Tellma.Core.IntegrationTests.Access;
using Tellma.Core.IntegrationTests.Data;
using Tellma.Core.Queryex;
using Tellma.Testing.Entities;

namespace Tellma.Core.IntegrationTests.Crud
{
    /// <summary>
    ///     The two tenant databases of <see cref="ConnectDatabase" />, served by an instance whose
    ///     widget stack runs through <see cref="WidgetService" />: a service that validates deletes and
    ///     actions through a probe a test arms, and declares an entity action. The first tenant is
    ///     seeded once with the rows every test may read but none deletes: four widgets with parts, a
    ///     small tree of nodes with links between them, two lookups, and more exports than a delete
    ///     by query may match. Each test seeds its own caller and roles, and the rows it writes.
    /// </summary>
    /// <param name="sql">The SQL Server the databases are created on.</param>
    public sealed class WritePipelineDatabase(SqlServerFixture sql) : IAsyncLifetime
    {
        /// <summary>The name every bulk export carries.</summary>
        public const string BulkName = "bulk";

        /// <summary>How many bulk exports the seed holds: one beyond a delete by query's default cap.</summary>
        public const int BulkExports = 10_001;

        /// <summary>
        ///     The seed: widgets 1 to 4 with parts, nodes 1 to 5 under two roots with links 1 to 3
        ///     (1 on N111 at N12, 2 on N2 at N111, 3 on N12 at N2), lookups 1 and 2, and the bulk exports.
        /// </summary>
        private static readonly string[] Seed =
        [
            DataQueryDatabase.Stamped(
                "fixture.Widgets",
                "[Id], [Code], [Kind], [Name], [Subject], [Price]",
                "(1, 'W1', 'Plain', N'Alpha', N's1', 10.5)",
                "(2, 'W2', 'Fancy', N'Beta', N's2', NULL)",
                "(3, 'W3', 'Plain', N'Gamma', N's3', 30)",
                "(4, 'W4', 'Fancy', N'Delta', N's4', 40)"),
            "INSERT INTO [fixture].[WidgetParts] ([Id], [WidgetId], [Ordinal], [Notes]) VALUES (11, 1, 1, N'a'), (12, 1, 2, N'b'), (31, 3, 1, N'c'), (41, 4, 1, N'd');",
            DataQueryDatabase.Stamped(
                "fixture.Nodes",
                "[Id], [Code], [Name], [Node], [ParentId]",
                "(1, 'N1', N'Root', '/1/', NULL)",
                "(2, 'N11', N'Branch', '/1/1/', 1)",
                "(3, 'N111', N'Leaf', '/1/1/1/', 2)",
                "(4, 'N2', N'Other', '/2/', NULL)",
                "(5, 'N12', N'Twig', '/1/2/', 1)"),
            "INSERT INTO [fixture].[Links] ([Id], [NodeId], [OtherNodeId]) VALUES (1, 3, 5), (2, 4, 3), (3, 5, 4);",
            DataQueryDatabase.Stamped("fixture.Lookups", "[Id], [Code], [Name], [IsActive]", "(1, 'L1', N'Lookup 1', 1)", "(2, 'L2', N'Lookup 2', 1)"),
            string.Create(
                CultureInfo.InvariantCulture,
                $"""
                WITH [Numbers] AS (SELECT TOP ({BulkExports}) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) AS [I] FROM sys.all_columns AS A CROSS JOIN sys.all_columns AS B)
                INSERT INTO [fixture].[Exports] ([Id], [Name], [CreatedAt], [CreatedById], [ModifiedAt], [ModifiedById])
                SELECT 100000 + [I], N'{BulkName}', SYSDATETIMEOFFSET(), {WellKnownIds.SystemUserId}, SYSDATETIMEOFFSET(), {WellKnownIds.SystemUserId}
                FROM [Numbers];
                """),
        ];

        /// <summary>The instance that serves the tests; null until initialised.</summary>
        private ServiceProvider? _instance;

        /// <summary>The two tenant databases and their composition.</summary>
        public ConnectDatabase Connect { get; } = new(sql);

        /// <summary>The instance whose widget stack runs through <see cref="WidgetService" />.</summary>
        public IServiceProvider Instance => _instance ?? throw new InvalidOperationException("The databases are not created yet.");

        /// <summary>The widget service's probe, inert until a test arms it.</summary>
        public WidgetServiceProbe Probe { get; } = new();

        /// <inheritdoc />
        public async ValueTask InitializeAsync()
        {
            await Connect.InitializeAsync();
            await Connect.ExecuteAsync(ConnectDatabase.TenantA, Seed);
            _instance = Connect.CreateInstance(
                configure: services => services.AddSingleton(Probe),
                compose: static tellma => tellma.AddFeature(new WidgetServiceFeature()));
        }

        /// <inheritdoc />
        public async ValueTask DisposeAsync()
        {
            if (_instance is not null)
            {
                await _instance.DisposeAsync();
            }

            await Connect.DisposeAsync();
        }

        /// <summary>A request scope of the first tenant on the instance, for a caller.</summary>
        /// <param name="subject">The caller's subject.</param>
        /// <returns>The scope.</returns>
        public Task<AsyncServiceScope> ScopeAsync(string subject)
        {
            return Connect.ScopeAsync(subject, services: Instance);
        }

        /// <summary>Seeds a caller of the first tenant who is a member of one role holding the permissions.</summary>
        /// <param name="permissions">The role's permissions: a resource, an action and a filter, null for none.</param>
        /// <returns>The caller's id and subject.</returns>
        public async Task<(int UserId, string Subject)> SeedCallerAsync(params (string Resource, string Action, string? Filter)[] permissions)
        {
            (int userId, string subject) = await Connect.SeedCallerAsync("Caller");
            int roleId = await Connect.SeedRoleAsync(permissions);
            await Connect.SeedMembershipAsync(userId, roleId);
            await Connect.BumpPermissionsAsync(userId);
            return (userId, subject);
        }

        /// <summary>The feature that runs the fixture's widget stack through <see cref="WidgetService" />.</summary>
        private sealed class WidgetServiceFeature : ITellmaFeature
        {
            /// <inheritdoc />
            public string Name => "widget-service";

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
                _ = contribution.Add(new StackContributionItem(typeof(Widget), typeof(WidgetService), StackOperations.Read | StackOperations.Save | StackOperations.Delete | StackOperations.Import));
            }
        }
    }

    /// <summary>What the widget service's hooks and its action do, which a test arms: shared by every scope of the instance.</summary>
    public sealed class WidgetServiceProbe
    {
        /// <summary>The delete validation hook; nothing when null.</summary>
        public Func<DeleteContext<Widget, int>, Task>? ValidateDelete { get; set; }

        /// <summary>The action validation hook, given the action's name; nothing when null.</summary>
        public Func<string, ActionContext<Widget, int>, Task>? ValidateAction { get; set; }

        /// <summary>What the rename action composes onto its batch after its update; nothing when null.</summary>
        public Action<ActionContext<Widget, int>>? Compose { get; set; }

        /// <summary>The condition the rename's update repeats when the persist runs; null for an update of every target.</summary>
        public FilterTree? Condition { get; set; }

        /// <summary>The code a target failing <see cref="Condition" /> refuses the rename with; null to skip such a target.</summary>
        public string? RefusalCode { get; set; }

        /// <summary>The names of the targets the rename action saw, by the order it saw them; empty until it runs.</summary>
        public List<string> Renamed { get; } = [];

        /// <summary>Disarms the probe.</summary>
        public void Reset()
        {
            ValidateDelete = null;
            ValidateAction = null;
            Compose = null;
            Condition = null;
            RefusalCode = null;
            Renamed.Clear();
        }
    }

    /// <summary>The arguments of the widget's rename action.</summary>
    public sealed record WidgetRename
    {
        /// <summary>The new name.</summary>
        [Required]
        [MaxLength(20)]
        public string? Name { get; init; }
    }

    /// <summary>
    ///     The widget stack's service: its delete and action validation hooks run what the probe
    ///     arms, and it declares the rename action, which renames the target widgets, under the
    ///     condition the probe arms, and composes what the probe arms.
    /// </summary>
    /// <param name="factory">The pipeline factory.</param>
    /// <param name="probe">The probe.</param>
    public sealed class WidgetService(IEntityPipelineFactory factory, WidgetServiceProbe probe) : EntityService<Widget>(factory)
    {
        /// <summary>Renames the target widgets, once it has seen their names as loaded.</summary>
        /// <param name="context">The action's context.</param>
        /// <param name="arguments">The new name.</param>
        /// <returns>A task that completes when the rename composed.</returns>
        [EntityAction("rename")]
        public async Task RenameAsync(ActionContext<Widget, int> context, WidgetRename arguments)
        {
            ArgumentNullException.ThrowIfNull(context);
            ArgumentNullException.ThrowIfNull(arguments);

            // --- The targets load with the first round; the update composes after it, under the
            // probe's condition, skipping or refusing a target that fails it ---
            await context.Loader.LoadAsync();
            probe.Renamed.AddRange(context.Entities.Select(static widget => widget.Name));
            Dictionary<string, object?> assignments = new(StringComparer.Ordinal) { [nameof(Widget.Name)] = arguments.Name };
            if (probe.Condition is not FilterTree condition)
            {
                context.Update(assignments);
            }
            else if (probe.RefusalCode is string code)
            {
                context.Update(assignments, condition, code);
            }
            else
            {
                context.Update(assignments, condition);
            }

            probe.Compose?.Invoke(context);
        }

        /// <inheritdoc />
        protected override Task ValidateDeleteAsync(DeleteContext<Widget, int> context)
        {
            return probe.ValidateDelete?.Invoke(context) ?? Task.CompletedTask;
        }

        /// <inheritdoc />
        protected override Task ValidateActionAsync(string action, ActionContext<Widget, int> context)
        {
            return probe.ValidateAction?.Invoke(action, context) ?? Task.CompletedTask;
        }
    }
}
