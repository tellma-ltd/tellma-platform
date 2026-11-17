// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using System.Globalization;
using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Composition;
using Tellma.Core.Abstractions.Crud;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.IntegrationTests.Access;
using Tellma.Core.IntegrationTests.Data;
using Tellma.Core.Queryex;
using Tellma.Testing.Entities;

namespace Tellma.Core.IntegrationTests.Crud
{
    /// <summary>
    ///     The two tenant databases of <see cref="ConnectDatabase" />, served by an instance that adds
    ///     a details contributor to the widget, with the first tenant seeded once with the rows the
    ///     details reads cover: four widgets with parts, a small tree of nodes with links between
    ///     them, a root with more children than a level may return, and two lookups. Each test seeds
    ///     its own caller and roles.
    /// </summary>
    /// <param name="sql">The SQL Server the databases are created on.</param>
    public sealed class DetailsPipelineDatabase(SqlServerFixture sql) : IAsyncLifetime
    {
        /// <summary>The id of the root whose children outnumber a level's ceiling.</summary>
        public const int CrowdedRoot = 100;

        /// <summary>How many children the crowded root has: one beyond the default ceiling.</summary>
        public const int CrowdedChildren = 10_001;

        /// <summary>
        ///     The seed: widgets 1 to 4 (priced 10.5, none, 30 and 40), one or two parts each but the
        ///     second's, nodes 1 to 5 under two roots with links 1 to 3 (1 on N111 at N12, 2 on N2 at
        ///     N111, 3 on N12 at N2), the crowded root and its children, and lookups 1 and 2.
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
            "INSERT INTO [fixture].[WidgetPartNotes] ([Id], [WidgetPartId], [Text]) VALUES (111, 11, N'first'), (112, 11, N'second');",
            DataQueryDatabase.Stamped(
                "fixture.Nodes",
                "[Id], [Code], [Name], [Node], [ParentId]",
                "(1, 'N1', N'Root', '/1/', NULL)",
                "(2, 'N11', N'Branch', '/1/1/', 1)",
                "(3, 'N111', N'Leaf', '/1/1/1/', 2)",
                "(4, 'N2', N'Other', '/2/', NULL)",
                "(5, 'N12', N'Twig', '/1/2/', 1)"),
            "INSERT INTO [fixture].[Links] ([Id], [NodeId], [OtherNodeId]) VALUES (1, 3, 5), (2, 4, 3), (3, 5, 4);",
            DataQueryDatabase.Stamped("fixture.Nodes", "[Id], [Code], [Name], [Node], [ParentId]", $"({CrowdedRoot}, 'CROWD', N'Crowded', '/3/', NULL)"),
            string.Create(
                CultureInfo.InvariantCulture,
                $"""
                WITH [Numbers] AS (SELECT TOP ({CrowdedChildren}) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) AS [I] FROM sys.all_columns AS A CROSS JOIN sys.all_columns AS B)
                INSERT INTO [fixture].[Nodes] ([Id], [Code], [Name], [Node], [ParentId], [CreatedAt], [CreatedById], [ModifiedAt], [ModifiedById])
                SELECT 1000 + [I], CONCAT('C', [I]), N'Crowd', CAST(CONCAT('/3/', [I], '/') AS hierarchyid), {CrowdedRoot}, SYSDATETIMEOFFSET(), {WellKnownIds.SystemUserId}, SYSDATETIMEOFFSET(), {WellKnownIds.SystemUserId}
                FROM [Numbers];
                """),
            DataQueryDatabase.Stamped(
                "fixture.Lookups",
                "[Id], [Code], [Name], [IsActive]",
                "(1, 'L1', N'Lookup 1', 1)",
                "(2, 'L2', N'Lookup 2', 1)"),
        ];

        /// <summary>The instance that serves the tests; null until initialised.</summary>
        private ServiceProvider? _instance;

        /// <summary>The two tenant databases and their composition.</summary>
        public ConnectDatabase Connect { get; } = new(sql);

        /// <summary>The instance with the widget's details contributor.</summary>
        public IServiceProvider Instance => _instance ?? throw new InvalidOperationException("The databases are not created yet.");

        /// <inheritdoc />
        public async ValueTask InitializeAsync()
        {
            await Connect.InitializeAsync();
            await Connect.ExecuteAsync(ConnectDatabase.TenantA, Seed);
            _instance = Connect.CreateInstance(compose: static tellma => tellma.AddFeature(new WidgetDetailsFeature()));
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

        /// <summary>The feature that attaches <see cref="WidgetDetails" /> to the fixture's widget.</summary>
        private sealed class WidgetDetailsFeature : ITellmaFeature
        {
            /// <inheritdoc />
            public string Name => "widget-details";

            /// <inheritdoc />
            public void Declare(FeatureDeclaration declaration)
            {
            }

            /// <inheritdoc />
            public void Contribute(FeatureContribution contribution)
            {
                ArgumentNullException.ThrowIfNull(contribution);
                _ = contribution.DetailsContributor<Widget, WidgetDetails>();
            }
        }
    }

    /// <summary>
    ///     The widget's details contributor: a computed extra built from one grouped read of the
    ///     parts, and a query extra whose expression divides by zero.
    /// </summary>
    /// <param name="database">The tenant's database, whose schema roots the reads.</param>
    [DetailsExtras(PartCount, Ratio)]
    public sealed class WidgetDetails(ITenantDatabase database) : IDetailsContributor<Widget>
    {
        /// <summary>The visible widgets' part counts, by id.</summary>
        public const string PartCount = "part-count";

        /// <summary>Each visible widget's price over nothing: an arithmetic failure.</summary>
        public const string Ratio = "ratio";

        /// <inheritdoc />
        public void Contribute(DetailsPlan<Widget> plan, IReadOnlySet<string> requested)
        {
            ArgumentNullException.ThrowIfNull(plan);
            ArgumentNullException.ThrowIfNull(requested);

            // --- The part counts: one grouped read the computed extra reads after the batch ---
            if (requested.Contains(PartCount))
            {
                BatchResult<RowPage> counts = plan.AddRead(
                    new QuerySpec
                    {
                        Root = Entity("fixture.WidgetPart"),
                        Select = "WidgetId, count()",
                        Aggregate = true,
                        Restrictions = [new KeySetRestriction("WidgetId", plan.IdsSource.Name)],
                    },
                    arguments: null);
                plan.AddComputedExtra(PartCount, rows => rows.ToDictionary(
                    static row => row.Id.ToString(CultureInfo.InvariantCulture),
                    row => CountOf(counts.Value.Rows, row.Id)));
            }

            // --- The ratio: the visible widgets' prices divided by zero ---
            if (requested.Contains(Ratio))
            {
                plan.AddExtra(
                    Ratio,
                    new QuerySpec { Root = Entity("fixture.Widget"), Select = "Id, Price / (Id - Id)", Restrictions = [new KeySetRestriction("Id", plan.IdsSource.Name)] },
                    arguments: null);
            }
        }

        /// <summary>A widget's part count in the grouped read; zero when it has none.</summary>
        /// <param name="rows">The grouped read's rows: the widget's id, then its count.</param>
        /// <param name="id">The widget's id.</param>
        /// <returns>The count.</returns>
        private static int CountOf(QueryRowSet rows, int id)
        {
            Array ids = rows.GetBuffer(0);
            Array counts = rows.GetBuffer(1);
            for (int row = 0; row < rows.RowCount; row++)
            {
                if (Convert.ToInt32(ids.GetValue(row), CultureInfo.InvariantCulture) == id)
                {
                    return Convert.ToInt32(counts.GetValue(row), CultureInfo.InvariantCulture);
                }
            }

            return 0;
        }

        /// <summary>A schema entity of the tenant's schema.</summary>
        /// <param name="name">The entity's name.</param>
        /// <returns>The entity.</returns>
        private EntityDescriptor Entity(string name)
        {
            return database.Schema.FindEntity(name) ?? throw new InvalidOperationException($"The tenant's schema has no entity {name}.");
        }
    }
}
