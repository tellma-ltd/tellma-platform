// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using System.ComponentModel.DataAnnotations;
using Tellma.Core.Abstractions.Composition;
using Tellma.Core.Abstractions.Crud;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Validation;
using Tellma.Core.IntegrationTests.Access;
using Tellma.Core.IntegrationTests.Data;
using Tellma.Testing.Entities;

namespace Tellma.Core.IntegrationTests.Crud
{
    /// <summary>
    ///     The two tenant databases of <see cref="ConnectDatabase" />, served by an instance whose
    ///     widget stack runs through <see cref="EnlistingWidgetService" />, which enlists shipments,
    ///     and whose shipment stack runs through <see cref="EnlistedShipmentService" />, which enlists
    ///     its own shipments from its API action. The first tenant's database carries two references
    ///     by id the fixture's schema does not declare, one each way between widgets and shipments,
    ///     and is seeded with three stored shipments.
    /// </summary>
    /// <param name="sql">The SQL Server the databases are created on.</param>
    public sealed class EnlistmentDatabase(SqlServerFixture sql) : IAsyncLifetime
    {
        /// <summary>The stored shipment, which enlisted updates change.</summary>
        public const long StoredShipment = 9001;

        /// <summary>The stored shipment the restamp action changes.</summary>
        public const long RestampedShipment = 9002;

        /// <summary>The stored shipment a caller without the restamp action's securable asks it to change.</summary>
        public const long ForbiddenShipment = 9003;

        /// <summary>A widget whose code starts with this references the shipment whose id follows it.</summary>
        public const string WidgetReferencePrefix = "ref:";

        /// <summary>A shipment whose payload starts with this references the widget whose id follows it.</summary>
        public const string ShipmentReferencePrefix = "of:";

        /// <summary>A job id no job carries, which a shipment's job reference breaks on.</summary>
        public const int MissingJob = 999_999;

        // The fixture's schema holds no reference between widgets and shipments, and the serialized
        // fixture model is not this suite's to change. Each reference stands as a check constraint
        // over a function that looks the other table up by the id the row names: SQL Server checks
        // it row by row as the statement writes, as it checks a foreign key, so a statement that
        // writes a referencing row before its referenced row, or names another row's id, fails as a
        // foreign key would.
        /// <summary>
        ///     The two references and the stored shipments, seeded ahead of every allocation so the
        ///     sequence heals past them before it hands out an id.
        /// </summary>
        private static readonly string[] Seed =
        [
            """
            CREATE FUNCTION [fixture].[HasShipment] (@id bigint) RETURNS bit AS
            BEGIN
                RETURN CASE WHEN EXISTS (SELECT 1 FROM [fixture].[Shipments] WHERE [Id] = @id) THEN 1 ELSE 0 END;
            END;
            """,
            """
            CREATE FUNCTION [fixture].[HasWidget] (@id int) RETURNS bit AS
            BEGIN
                RETURN CASE WHEN EXISTS (SELECT 1 FROM [fixture].[Widgets] WHERE [Id] = @id) THEN 1 ELSE 0 END;
            END;
            """,
            FormattableString.Invariant($"""
            ALTER TABLE [fixture].[Widgets] ADD CONSTRAINT [CK_Widgets_ReferencedShipment]
                CHECK ([Code] IS NULL OR [Code] NOT LIKE '{WidgetReferencePrefix}%'
                    OR [fixture].[HasShipment](TRY_CAST(SUBSTRING([Code], {WidgetReferencePrefix.Length + 1}, 20) AS bigint)) = 1);
            """),
            FormattableString.Invariant($"""
            ALTER TABLE [fixture].[Shipments] ADD CONSTRAINT [CK_Shipments_ReferencedWidget]
                CHECK ([Payload] IS NULL OR [Payload] NOT LIKE '{ShipmentReferencePrefix}%'
                    OR [fixture].[HasWidget](TRY_CAST(SUBSTRING([Payload], {ShipmentReferencePrefix.Length + 1}, 20) AS int)) = 1);
            """),
            DataQueryDatabase.Stamped(
                "[fixture].[Shipments]",
                "[Id], [State], [Payload]",
                FormattableString.Invariant($"({StoredShipment}, 'Pending', N'stored')"),
                FormattableString.Invariant($"({RestampedShipment}, 'Pending', N'stored')"),
                FormattableString.Invariant($"({ForbiddenShipment}, 'Pending', N'stored')")),
        ];

        /// <summary>The instance that serves the tests; null until initialised.</summary>
        private ServiceProvider? _instance;

        /// <summary>The two tenant databases and their composition.</summary>
        public ConnectDatabase Connect { get; } = new(sql);

        /// <summary>The instance whose widget and shipment stacks run through the suite's services.</summary>
        public IServiceProvider Instance => _instance ?? throw new InvalidOperationException("The databases are not created yet.");

        /// <summary>The services' probe, inert until a test arms it.</summary>
        public EnlistmentProbe Probe { get; } = new();

        /// <inheritdoc />
        public async ValueTask InitializeAsync()
        {
            await Connect.InitializeAsync();
            await Connect.ExecuteAsync(ConnectDatabase.TenantA, Seed);
            _instance = Connect.CreateInstance(
                configure: services => services.AddSingleton(Probe),
                compose: static tellma => tellma.AddFeature(new EnlistmentFeature()));
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

        /// <summary>The feature that runs the fixture's widget and shipment stacks through the suite's services.</summary>
        private sealed class EnlistmentFeature : ITellmaFeature
        {
            /// <inheritdoc />
            public string Name => "enlistment";

            /// <inheritdoc />
            public void Declare(FeatureDeclaration declaration)
            {
                ArgumentNullException.ThrowIfNull(declaration);

                // Depending on the fixture's feature lets these registrations replace its services.
                _ = declaration.Requires(FixtureFeature.FeatureName);
            }

            /// <inheritdoc />
            public void Contribute(FeatureContribution contribution)
            {
                ArgumentNullException.ThrowIfNull(contribution);
                _ = contribution
                    .Entity<Widget, EnlistingWidgetService>()
                    .Entity<Shipment, EnlistedShipmentService>();
            }
        }
    }

    /// <summary>What the suite's services do, which a test arms, and what they saw: shared by every scope of the instance.</summary>
    public sealed class EnlistmentProbe
    {
        /// <summary>The widgets' validation hook, given the service so it can enlist; nothing when null.</summary>
        public Func<EnlistingWidgetService, SaveContext<Widget>, Task>? ValidateWidgets { get; set; }

        /// <summary>The widgets' persist hook; nothing when null.</summary>
        public Action<PersistContext<Widget>>? ContributeWidgets { get; set; }

        /// <summary>Each stage of the shipment stack that ran, as <c>stage payload</c>, in the order it ran.</summary>
        public List<string> Shipments { get; } = [];

        /// <summary>Disarms the probe.</summary>
        public void Reset()
        {
            ValidateWidgets = null;
            ContributeWidgets = null;
            lock (Shipments)
            {
                Shipments.Clear();
            }
        }

        /// <summary>Records a stage of the shipment stack.</summary>
        /// <param name="stage">The stage.</param>
        /// <param name="payload">The shipment's payload.</param>
        public void Saw(string stage, string? payload)
        {
            lock (Shipments)
            {
                Shipments.Add($"{stage} {payload}");
            }
        }
    }

    /// <summary>The body of the shipment's restamp action.</summary>
    public sealed record ShipmentRestamp
    {
        /// <summary>The shipment to change.</summary>
        [Required]
        public long? Id { get; init; }

        /// <summary>The new payload.</summary>
        [Required]
        [MaxLength(20)]
        public string? Payload { get; init; }
    }

    /// <summary>
    ///     The widget stack's service: its hooks run what the probe arms, so a test enlists shipments
    ///     from its validation and appends to its persist.
    /// </summary>
    /// <param name="factory">The pipeline factory.</param>
    /// <param name="probe">The probe.</param>
    public sealed class EnlistingWidgetService(IEntityPipelineFactory factory, EnlistmentProbe probe) : EntityService<Widget>(factory), IEnlists<Shipment>
    {
        /// <inheritdoc />
        protected override Task ValidateAsync(SaveContext<Widget> context)
        {
            return probe.ValidateWidgets?.Invoke(this, context) ?? Task.CompletedTask;
        }

        /// <inheritdoc />
        protected override Task ContributeAsync(PersistContext<Widget> context)
        {
            probe.ContributeWidgets?.Invoke(context);
            return Task.CompletedTask;
        }
    }

    /// <summary>
    ///     The shipment stack's service: it trims each payload, refuses a payload of <c>refused</c>,
    ///     and records every stage it ran in the probe. It declares the restamp action, which loads
    ///     one shipment, changes its payload and enlists it with the action's own frame under
    ///     <see cref="ConcurrencyMode.Check" />, as a user's own profile save enlists the user's row.
    /// </summary>
    /// <param name="factory">The pipeline factory.</param>
    /// <param name="probe">The probe.</param>
    /// <param name="host">The open frame of the action that runs.</param>
    public sealed class EnlistedShipmentService(IEntityPipelineFactory factory, EnlistmentProbe probe, IOpenWriteHost host)
        : EntityService<Shipment, long>(factory), IEnlists<Shipment>
    {
        /// <summary>The restamp action's name.</summary>
        public const string Restamp = "restamp";

        /// <summary>The payload the validator refuses.</summary>
        public const string Refused = "refused";

        /// <summary>The code of the validator's refusal.</summary>
        public const string RefusedCode = "Fixture.Shipments.Refused";

        /// <summary>Changes one shipment's payload: its row as loaded, enlisted with the action's own frame and persisted once.</summary>
        /// <param name="body">The shipment and its new payload.</param>
        /// <param name="cancellationToken">The invocation's token.</param>
        /// <returns>The shipment as committed.</returns>
        [ApiAction(Restamp, Action = "Save")]
        public async Task<Shipment> RestampAsync(ShipmentRestamp body, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(body);

            // --- The row as stored, its stamp the one Check compares ---
            Shipment shipment = (await GetByIdAsync(body.Id!.Value, new DetailsRequest(), cancellationToken)).Entities.Single();
            shipment.Payload = body.Payload;

            // --- The self-pair, admitted from the action's own frame alone, then one persist ---
            _ = await this.EnlistSaveAsync(host, [shipment], new EnlistSaveOptions { Concurrency = ConcurrencyMode.Check });
            _ = await host.PersistAsync(cancellationToken);
            return shipment;
        }

        /// <inheritdoc />
        protected override Task PreprocessAsync(SaveContext<Shipment> context)
        {
            ArgumentNullException.ThrowIfNull(context);
            foreach (Shipment shipment in context.Entities)
            {
                shipment.Payload = shipment.Payload?.Trim();
                probe.Saw("preprocess", shipment.Payload);
            }

            return Task.CompletedTask;
        }

        /// <inheritdoc />
        protected override Task ValidateAsync(SaveContext<Shipment> context)
        {
            ArgumentNullException.ThrowIfNull(context);
            for (int i = 0; i < context.Entities.Count; i++)
            {
                probe.Saw("validate", context.Entities[i].Payload);
                if (context.Entities[i].Payload == Refused)
                {
                    context.Errors.Add(i, static shipment => shipment.Payload, RefusedCode);
                }
            }

            return Task.CompletedTask;
        }

        /// <inheritdoc />
        protected override Task ContributeAsync(PersistContext<Shipment> context)
        {
            ArgumentNullException.ThrowIfNull(context);
            foreach (Shipment shipment in context.Entities)
            {
                probe.Saw("contribute", shipment.Payload);
            }

            return Task.CompletedTask;
        }

        /// <inheritdoc />
        protected override Task AfterCommitAsync(PersistOutcome<Shipment> outcome)
        {
            ArgumentNullException.ThrowIfNull(outcome);
            foreach (Shipment shipment in outcome.Entities)
            {
                probe.Saw("committed", shipment.Payload);
            }

            return Task.CompletedTask;
        }
    }
}
