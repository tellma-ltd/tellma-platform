// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.IntegrationTests.Access;
using Tellma.Core.IntegrationTests.Data;

namespace Tellma.Core.IntegrationTests.Crud
{
    /// <summary>
    ///     The two tenant databases of <see cref="ConnectDatabase" />, served by the real connect
    ///     prologue, runner and access evaluator, with the first tenant seeded once with the rows the
    ///     query pipeline reads: four widgets with parts, and a small tree of nodes with links between
    ///     them. Each test seeds its own caller and roles.
    /// </summary>
    /// <param name="sql">The SQL Server the databases are created on.</param>
    public sealed class QueryPipelineDatabase(SqlServerFixture sql) : IAsyncLifetime
    {
        /// <summary>
        ///     The seed: widgets 1 to 4 (priced 10.5, none, 30 and 40), one or two parts each but the
        ///     second's, nodes 1 to 5 under two roots, and links 1 to 3, each on one node pointing at
        ///     another: 1 on N111 at N12, 2 on N2 at N111, and 3 on N12 at N2.
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
        ];

        /// <summary>The two tenant databases and their composition.</summary>
        public ConnectDatabase Connect { get; } = new(sql);

        /// <inheritdoc />
        public async ValueTask InitializeAsync()
        {
            await Connect.InitializeAsync();
            await Connect.ExecuteAsync(ConnectDatabase.TenantA, Seed);
        }

        /// <inheritdoc />
        public ValueTask DisposeAsync()
        {
            return Connect.DisposeAsync();
        }

        /// <summary>Seeds a caller of the first tenant who is a member of one role holding the permissions.</summary>
        /// <param name="permissions">The role's permissions: a resource, an action and a filter, null for none.</param>
        /// <returns>The caller's id and subject.</returns>
        public async Task<(int UserId, string Subject)> SeedCallerAsync(params (string Resource, string Action, string? Filter)[] permissions)
        {
            (int userId, string subject) = await Connect.SeedCallerAsync("Caller");
            await GrantAsync(userId, permissions);
            return (userId, subject);
        }

        /// <summary>
        ///     Makes a user a member of a new role holding the permissions, and moves the tags a save
        ///     of the user's memberships moves.
        /// </summary>
        /// <param name="userId">The user.</param>
        /// <param name="permissions">The role's permissions: a resource, an action and a filter, null for none.</param>
        /// <returns>A task that completes when the grant is saved.</returns>
        public async Task GrantAsync(int userId, params (string Resource, string Action, string? Filter)[] permissions)
        {
            int roleId = await Connect.SeedRoleAsync(permissions);
            await Connect.SeedMembershipAsync(userId, roleId);
            await Connect.BumpPermissionsAsync(userId);
        }
    }
}
