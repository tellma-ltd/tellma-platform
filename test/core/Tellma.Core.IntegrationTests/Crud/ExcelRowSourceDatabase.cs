// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Globalization;
using Tellma.Core.IntegrationTests.Access;
using Tellma.Core.IntegrationTests.Data;

namespace Tellma.Core.IntegrationTests.Crud
{
    /// <summary>
    ///     The two tenant databases of <see cref="ConnectDatabase" />, served by the real connect
    ///     prologue, runner and access evaluator, with the first tenant seeded once with the rows the
    ///     Excel row source reads across pages of the widgets' take ceiling of 50: 120 widgets, every
    ///     tenth inactive; one part under every third widget, each with a note; the first widget's 60
    ///     parts, beyond the ceiling, the first three with two notes each; and two nodes. Each test
    ///     seeds its own caller and roles.
    /// </summary>
    /// <param name="sql">The SQL Server the databases are created on.</param>
    public sealed class ExcelRowSourceDatabase(SqlServerFixture sql) : IAsyncLifetime
    {
        /// <summary>The widgets seeded: ids 1 to this.</summary>
        public const int WidgetCount = 120;

        /// <summary>The parts of the first widget, beyond the take ceiling.</summary>
        public const int FirstWidgetParts = 60;

        /// <summary>The two tenant databases and their composition.</summary>
        public ConnectDatabase Connect { get; } = new(sql);

        /// <summary>Whether a seeded widget is inactive: every tenth.</summary>
        /// <param name="id">The widget's id.</param>
        /// <returns>True when it is inactive.</returns>
        public static bool IsInactive(int id)
        {
            return id % 10 == 0;
        }

        /// <summary>Whether a seeded widget has the one part every third widget has.</summary>
        /// <param name="id">The widget's id.</param>
        /// <returns>True when it has it.</returns>
        public static bool HasPart(int id)
        {
            return id % 3 == 0;
        }

        /// <summary>The id of the one part of a widget that has one.</summary>
        /// <param name="widgetId">The widget's id.</param>
        /// <returns>The part's id.</returns>
        public static int PartOf(int widgetId)
        {
            return 2000 + widgetId;
        }

        /// <inheritdoc />
        public async ValueTask InitializeAsync()
        {
            await Connect.InitializeAsync();
            await Connect.ExecuteAsync(ConnectDatabase.TenantA, Seed());
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
            (int userId, string subject) = await Connect.SeedCallerAsync("Exporter");
            int roleId = await Connect.SeedRoleAsync(permissions);
            await Connect.SeedMembershipAsync(userId, roleId);
            await Connect.BumpPermissionsAsync(userId);
            return (userId, subject);
        }

        /// <summary>Takes every role from a user, and moves the tags a save of the user's memberships moves.</summary>
        /// <param name="userId">The user.</param>
        /// <returns>A task that completes when the revocation is saved.</returns>
        public async Task RevokeAsync(int userId)
        {
            await Connect.ExecuteAsync(ConnectDatabase.TenantA, string.Create(CultureInfo.InvariantCulture, $"DELETE FROM [core].[RoleMemberships] WHERE [UserId] = {userId};"));
            await Connect.BumpPermissionsAsync(userId);
        }

        /// <summary>The seed's statements.</summary>
        /// <returns>The statements.</returns>
        private static string[] Seed()
        {
            // --- The widgets, every tenth inactive ---
            IEnumerable<int> widgets = Enumerable.Range(1, WidgetCount);
            string inactive = string.Join(", ", widgets.Where(IsInactive));
            string[] statements =
            [
                DataQueryDatabase.Stamped(
                    "fixture.Widgets",
                    "[Id], [Code], [Kind], [Name], [Subject], [Price]",
                    [.. widgets.Select(static id => string.Create(CultureInfo.InvariantCulture, $"({id}, 'X{id:000}', 'Plain', N'Widget {id}', N'x{id}', {id})"))]),
                $"UPDATE [fixture].[Widgets] SET [IsActive] = 0 WHERE [Id] IN ({inactive});",

                // --- One part under every third widget, each with one note ---
                Insert(
                    "[fixture].[WidgetParts] ([Id], [WidgetId], [Ordinal], [Notes])",
                    widgets.Where(HasPart).Select(static id => string.Create(CultureInfo.InvariantCulture, $"({PartOf(id)}, {id}, 1, N'p{id}')"))),
                Insert(
                    "[fixture].[WidgetPartNotes] ([Id], [WidgetPartId], [Text])",
                    widgets.Where(HasPart).Select(static id => string.Create(CultureInfo.InvariantCulture, $"({6000 + id}, {PartOf(id)}, N'n{id}')"))),

                // --- The first widget's parts, beyond the ceiling, the first three with two notes each ---
                Insert(
                    "[fixture].[WidgetParts] ([Id], [WidgetId], [Ordinal], [Notes])",
                    Enumerable.Range(1, FirstWidgetParts).Select(static ordinal => string.Create(CultureInfo.InvariantCulture, $"({1000 + ordinal}, 1, {ordinal}, N'q{ordinal}')"))),
                Insert(
                    "[fixture].[WidgetPartNotes] ([Id], [WidgetPartId], [Text])",
                    Enumerable.Range(1, 6).Select(static note => string.Create(CultureInfo.InvariantCulture, $"({5000 + note}, {1000 + ((note + 1) / 2)}, N'm{note}')"))),

                // --- Two root nodes, the lookups' other target ---
                DataQueryDatabase.Stamped(
                    "fixture.Nodes",
                    "[Id], [Code], [Name], [Node], [ParentId]",
                    "(1, 'N1', N'One', '/1/', NULL)",
                    "(2, 'N2', N'Two', '/2/', NULL)"),
            ];
            return statements;
        }

        /// <summary>One insert of several rows.</summary>
        /// <param name="target">The table and its columns.</param>
        /// <param name="rows">The rows' value lists.</param>
        /// <returns>The statement.</returns>
        private static string Insert(string target, IEnumerable<string> rows)
        {
            return $"INSERT INTO {target} VALUES {string.Join(", ", rows)};";
        }
    }
}
