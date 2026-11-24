// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using System.Globalization;
using Tellma.Core.Abstractions.Composition;
using Tellma.Core.Abstractions.Crud;
using Tellma.Core.Abstractions.Excel;
using Tellma.Core.Abstractions.Validation;
using Tellma.Core.IntegrationTests.Access;
using Tellma.Core.IntegrationTests.Crud;
using Tellma.Core.IntegrationTests.Data;
using Tellma.Core.Testing.Blobs;
using Tellma.Testing.Entities;

namespace Tellma.Core.IntegrationTests.Excel
{
    /// <summary>
    ///     The two tenant databases of <see cref="ConnectDatabase" /> for the synchronous import, with
    ///     the first tenant seeded once: widgets <see cref="FirstWidget" /> on, each with a code
    ///     <c>X</c> and its number, the first two, the seventh, the twelfth and the fourteenth with
    ///     parts and notes; and four root nodes. The instance keeps uploads in memory and its log in a
    ///     fake collector, caps a synchronous import at <see cref="MaxSynchronousRows" /> rows, and
    ///     carries the widget's save probe and a validator that asks to confirm a widget named
    ///     <see cref="WarnedName" />. Each test seeds its own caller and works on rows of its own.
    /// </summary>
    /// <param name="sql">The SQL Server the databases are created on.</param>
    public sealed class ExcelImportDatabase(SqlServerFixture sql) : IAsyncLifetime
    {
        /// <summary>The first seeded widget's id; the seed's ids sit far above the sequence's, so an insert never meets one.</summary>
        public const int FirstWidget = 9001;

        /// <summary>The widgets seeded.</summary>
        public const int WidgetCount = 14;

        /// <summary>The first seeded node's id.</summary>
        public const int FirstNode = 9001;

        /// <summary>The rows a synchronous import may read.</summary>
        public const int MaxSynchronousRows = 20;

        /// <summary>The name of a widget whose save the warning validator asks to confirm.</summary>
        public const string WarnedName = "Confirm me";

        /// <summary>The key of the warning.</summary>
        public const string WarningKey = "Test.ImportChecked";

        /// <summary>The instance; null until initialised.</summary>
        private ServiceProvider? _instance;

        /// <summary>The two tenant databases and their composition.</summary>
        public ConnectDatabase Connect { get; } = new(sql);

        /// <summary>The widget's save probe, inert until a test arms it for a widget's code.</summary>
        public WidgetSaveProbe Probe { get; } = new();

        /// <summary>The instance that serves the tests.</summary>
        public ServiceProvider Instance => _instance ?? throw new InvalidOperationException("The databases are not created yet.");

        /// <summary>A seeded widget's code.</summary>
        /// <param name="id">The widget's id.</param>
        /// <returns>The code.</returns>
        public static string CodeOf(int id)
        {
            return string.Create(CultureInfo.InvariantCulture, $"X{id}");
        }

        /// <summary>A seeded part's id: its widget's id, then its ordinal.</summary>
        /// <param name="widget">The widget's id.</param>
        /// <param name="ordinal">The part's ordinal.</param>
        /// <returns>The id.</returns>
        public static int PartOf(int widget, int ordinal)
        {
            return (widget * 10) + ordinal;
        }

        /// <summary>A seeded note's id: its part's id, then its number.</summary>
        /// <param name="widget">The widget's id.</param>
        /// <param name="ordinal">The part's ordinal.</param>
        /// <param name="number">The note's number within its part.</param>
        /// <returns>The id.</returns>
        public static int NoteOf(int widget, int ordinal, int number)
        {
            return (PartOf(widget, ordinal) * 10) + number;
        }

        /// <inheritdoc />
        public async ValueTask InitializeAsync()
        {
            await Connect.InitializeAsync();
            await Connect.ExecuteAsync(ConnectDatabase.TenantA, Seed());
            _instance = Connect.CreateInstance(
                configure: services =>
                {
                    _ = services.AddInMemoryBlobs();
                    _ = services.AddFakeLogging();
                    _ = services.AddSingleton(Probe);
                    _ = services.Configure<ExcelOptions>(static options => options.MaxSynchronousImportRows = MaxSynchronousRows);
                },
                compose: static tellma => tellma.AddFeature(new ImportProbeFeature()));
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

        /// <summary>Seeds a caller of a tenant who is a member of one role holding the permissions.</summary>
        /// <param name="tenantId">The tenant.</param>
        /// <param name="permissions">The role's permissions: a resource, an action and a filter, null for none.</param>
        /// <returns>The caller's id and subject.</returns>
        public async Task<(int UserId, string Subject)> SeedCallerAsync(int tenantId, params (string Resource, string Action, string? Filter)[] permissions)
        {
            int userId = Connect.NextUserId();
            string subject = string.Create(CultureInfo.InvariantCulture, $"sub-{userId}");
            await Connect.SeedUserAsync(tenantId, userId, subject, "Importer");
            int roleId = await Connect.SeedRoleAsync(tenantId, permissions);
            await Connect.SeedMembershipAsync(userId, roleId, tenantId: tenantId);
            if (tenantId == ConnectDatabase.TenantA)
            {
                await Connect.BumpPermissionsAsync(userId);
            }

            return (userId, subject);
        }

        /// <summary>The seed's statements.</summary>
        /// <returns>The statements.</returns>
        private static string[] Seed()
        {
            IEnumerable<int> widgets = Enumerable.Range(FirstWidget, WidgetCount);
            int first = FirstWidget;
            int second = FirstWidget + 1;
            int seventh = FirstWidget + 6;
            int twelfth = FirstWidget + 11;
            int fourteenth = FirstWidget + 13;
            return
            [
                // --- The widgets ---
                DataQueryDatabase.Stamped(
                    "fixture.Widgets",
                    "[Id], [Code], [Kind], [Name], [Subject], [Price]",
                    [.. widgets.Select(static id => string.Create(CultureInfo.InvariantCulture, $"({id}, '{CodeOf(id)}', 'Plain', N'Widget {id}', N's{id}', {id - FirstWidget + 1})"))]),

                // --- Two parts of the first widget, each with two notes; one of the second and of the seventh, each with one;
                // two of the twelfth and of the fourteenth, each with one ---
                string.Create(CultureInfo.InvariantCulture, $"""
                    INSERT INTO [fixture].[WidgetParts] ([Id], [WidgetId], [Ordinal], [Notes]) VALUES
                        ({PartOf(first, 1)}, {first}, 1, N'p1'), ({PartOf(first, 2)}, {first}, 2, N'p2'), ({PartOf(second, 1)}, {second}, 1, N'q1'), ({PartOf(seventh, 1)}, {seventh}, 1, N'r1'),
                        ({PartOf(twelfth, 1)}, {twelfth}, 1, N't1'), ({PartOf(twelfth, 2)}, {twelfth}, 2, N't2'), ({PartOf(fourteenth, 1)}, {fourteenth}, 1, N'f1'), ({PartOf(fourteenth, 2)}, {fourteenth}, 2, N'f2');
                    INSERT INTO [fixture].[WidgetPartNotes] ([Id], [WidgetPartId], [Text]) VALUES
                        ({NoteOf(first, 1, 1)}, {PartOf(first, 1)}, N'n1'), ({NoteOf(first, 1, 2)}, {PartOf(first, 1)}, N'n2'), ({NoteOf(first, 2, 1)}, {PartOf(first, 2)}, N'n3'), ({NoteOf(first, 2, 2)}, {PartOf(first, 2)}, N'n4'),
                        ({NoteOf(second, 1, 1)}, {PartOf(second, 1)}, N'm1'), ({NoteOf(seventh, 1, 1)}, {PartOf(seventh, 1)}, N'o1'),
                        ({NoteOf(twelfth, 1, 1)}, {PartOf(twelfth, 1)}, N'u1'), ({NoteOf(twelfth, 2, 1)}, {PartOf(twelfth, 2)}, N'u2'),
                        ({NoteOf(fourteenth, 1, 1)}, {PartOf(fourteenth, 1)}, N'g1'), ({NoteOf(fourteenth, 2, 1)}, {PartOf(fourteenth, 2)}, N'g2');
                    """),

                // --- Four root nodes ---
                DataQueryDatabase.Stamped(
                    "fixture.Nodes",
                    "[Id], [Code], [Name], [Node], [ParentId]",
                    string.Create(CultureInfo.InvariantCulture, $"({FirstNode}, 'N1', N'One', '/{FirstNode}/', NULL)"),
                    string.Create(CultureInfo.InvariantCulture, $"({FirstNode + 1}, 'N2', N'Two', '/{FirstNode + 1}/', NULL)"),
                    string.Create(CultureInfo.InvariantCulture, $"({FirstNode + 2}, 'N3', N'Three', '/{FirstNode + 2}/', NULL)"),
                    string.Create(CultureInfo.InvariantCulture, $"({FirstNode + 3}, 'N4', N'Four', '/{FirstNode + 3}/', NULL)")),
            ];
        }

        /// <summary>The feature that attaches the widget's save probe and the warning validator, with its confirmation key.</summary>
        private sealed class ImportProbeFeature : ITellmaFeature
        {
            /// <inheritdoc />
            public string Name => "import-probe";

            /// <inheritdoc />
            public void Declare(FeatureDeclaration declaration)
            {
            }

            /// <inheritdoc />
            public void Contribute(FeatureContribution contribution)
            {
                ArgumentNullException.ThrowIfNull(contribution);
                _ = contribution
                    .Confirmation(WarningKey)
                    .Validator<Widget, WidgetSaveProbeValidator>()
                    .Validator<Widget, ImportWarningValidator>();
            }
        }

        /// <summary>A widgets' validator that asks the caller to confirm the save of every widget named <see cref="WarnedName" />.</summary>
        private sealed class ImportWarningValidator : IEntityValidator<Widget>
        {
            /// <inheritdoc />
            public Task ValidateAsync(SaveContext<Widget> context)
            {
                ArgumentNullException.ThrowIfNull(context);
                long[] warned = [.. context.Entities.Where(static widget => widget.Name == WarnedName).Select(static widget => (long)widget.Id)];
                if (warned.Length > 0)
                {
                    context.Host.Confirmations.Require(WarningKey, warned, new Dictionary<string, object?>(StringComparer.Ordinal) { ["count"] = warned.Length });
                }

                return Task.CompletedTask;
            }
        }
    }
}
