// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using System.Globalization;
using Tellma.Core.Abstractions.Composition;
using Tellma.Core.Abstractions.Crud;
using Tellma.Core.Abstractions.Validation;
using Tellma.Core.Composition;
using Tellma.Core.IntegrationTests.Access;
using Tellma.Core.IntegrationTests.Data;
using Tellma.Core.Tenancy;
using Tellma.Core.Testing.Crud;
using Tellma.Testing.Entities;

namespace Tellma.Core.IntegrationTests.Crud.Conformance
{
    /// <summary>
    ///     The two tenant databases of <see cref="ConnectDatabase" />, served by an instance of the
    ///     fixture's stacks on their default services but for the export stack, whose service enlists
    ///     shipments from its validation, raises a confirmation and fails after its commit when a test
    ///     arms it. The first tenant holds one stored node, which every node's link may name. Each
    ///     scope's connections count their server round trips, so the conformance suite holds the
    ///     platform's count to the driver's.
    /// </summary>
    /// <param name="sql">The SQL Server the databases are created on.</param>
    public class ConformanceDatabase(SqlServerFixture sql) : IAsyncLifetime, IStackConformanceHarness
    {
        /// <summary>The stored node every node's link may name.</summary>
        public const int AnchorNode = 1;

        /// <summary>The confirmation the export service raises when armed.</summary>
        public const string ExportConfirmation = "Conformance.ExportChecked";

        /// <summary>The instance that serves the suite; null until initialised.</summary>
        private ServiceProvider? _instance;

        /// <summary>The two tenant databases and their composition.</summary>
        public ConnectDatabase Connect { get; } = new(sql);

        /// <summary>The export service's probe, inert until a test arms it.</summary>
        public ConformanceProbe Probe { get; } = new();

        /// <inheritdoc />
        public IServiceProvider Services => _instance ?? throw new InvalidOperationException("The databases are not created yet.");

        /// <inheritdoc />
        public async ValueTask InitializeAsync()
        {
            await Connect.InitializeAsync();
            await Connect.ExecuteAsync(
                ConnectDatabase.TenantA,
                DataQueryDatabase.Stamped("fixture.Nodes", "[Id], [Code], [Name], [Node], [ParentId]", string.Create(CultureInfo.InvariantCulture, $"({AnchorNode}, 'N-anchor', N'Anchor', '/{AnchorNode}/', NULL)")));
            _instance = Connect.CreateInstance(
                configure: services =>
                {
                    _ = services.AddSingleton(Probe);

                    // --- The scope's connections, opened through the fixture's provider and counted ---
                    Func<IServiceProvider, object> open = services.Last(static descriptor => descriptor.ServiceType == typeof(ITenantConnectionProvider)).ImplementationFactory
                        ?? throw new InvalidOperationException("The fixture composes its tenant connection provider from a factory.");
                    _ = services.AddScoped(scope => new RoundTripCountingProvider((ITenantConnectionProvider)open(scope)));
                    _ = services.Replace(ServiceDescriptor.Scoped<ITenantConnectionProvider>(static scope => scope.GetRequiredService<RoundTripCountingProvider>()));
                    Configure(services);
                },
                compose: Compose);
        }

        /// <inheritdoc />
        public async ValueTask DisposeAsync()
        {
            if (_instance is not null)
            {
                await _instance.DisposeAsync();
            }

            await Connect.DisposeAsync();
            GC.SuppressFinalize(this);
        }

        /// <inheritdoc />
        public async Task<StackConformanceCaller> SeedCallerAsync(IReadOnlyList<StackGrant> grants, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(grants);
            (int userId, string subject) = await Connect.SeedCallerAsync("Conformance");
            int roleId = await Connect.SeedRoleAsync([.. grants.Select(static grant => (grant.Resource, grant.Action, grant.Filter))]);
            await Connect.SeedMembershipAsync(userId, roleId);
            await Connect.BumpPermissionsAsync(userId);
            return new StackConformanceCaller(userId, subject);
        }

        /// <inheritdoc />
        public Task<AsyncServiceScope> ScopeAsync(StackConformanceCaller caller, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(caller);
            return Connect.ScopeAsync(caller.Subject, services: Services);
        }

        /// <inheritdoc />
        public long? ServerRoundTrips(IServiceProvider scope)
        {
            ArgumentNullException.ThrowIfNull(scope);
            return scope.GetRequiredService<RoundTripCountingProvider>().RoundTrips;
        }

        /// <summary>Runs SQL on the first tenant's database.</summary>
        /// <param name="statements">The statements, each run on its own.</param>
        /// <returns>A task that completes when they ran.</returns>
        public Task ExecuteAsync(params string[] statements)
        {
            return Connect.ExecuteAsync(ConnectDatabase.TenantA, statements);
        }

        /// <summary>Adds the instance's services beside the fixture's, after them; nothing by default.</summary>
        /// <param name="services">The instance's services.</param>
        protected virtual void Configure(IServiceCollection services)
        {
        }

        /// <summary>Adds the instance's features beside the fixture's.</summary>
        /// <param name="tellma">The instance's composition.</param>
        protected virtual void Compose(TellmaBuilder tellma)
        {
            ArgumentNullException.ThrowIfNull(tellma);
            _ = tellma.AddFeature(new ConformanceFeature());
        }

        /// <summary>The feature that runs the fixture's export stack through <see cref="EnlistingExportService" />.</summary>
        private sealed class ConformanceFeature : ITellmaFeature
        {
            /// <inheritdoc />
            public string Name => "conformance";

            /// <inheritdoc />
            public void Declare(FeatureDeclaration declaration)
            {
                ArgumentNullException.ThrowIfNull(declaration);

                // Depending on the fixture's feature lets this registration replace its service.
                _ = declaration.Requires(FixtureFeature.FeatureName);
            }

            /// <inheritdoc />
            public void Contribute(FeatureContribution contribution)
            {
                ArgumentNullException.ThrowIfNull(contribution);
                _ = contribution
                    .Entity<Export, EnlistingExportService>()
                    .Confirmation(ExportConfirmation);
            }
        }

        /// <summary>A scope's connections, each counting the round trips the driver sent on it.</summary>
        /// <param name="inner">The fixture's provider.</param>
        private sealed class RoundTripCountingProvider(ITenantConnectionProvider inner) : ITenantConnectionProvider
        {
            /// <summary>The connections the scope opened.</summary>
            private readonly List<SqlConnection> _connections = [];

            /// <summary>The round trips the driver counted on every connection the scope opened.</summary>
            public long RoundTrips
            {
                get
                {
                    lock (_connections)
                    {
                        return _connections.Sum(static connection => Convert.ToInt64(connection.RetrieveStatistics()["ServerRoundtrips"], CultureInfo.InvariantCulture));
                    }
                }
            }

            /// <inheritdoc />
            public TenantInfo Tenant => inner.Tenant;

            /// <inheritdoc />
            public async Task<SqlConnection> OpenAsync(CancellationToken cancellationToken = default)
            {
                SqlConnection connection = await inner.OpenAsync(cancellationToken);
                connection.StatisticsEnabled = true;
                lock (_connections)
                {
                    _connections.Add(connection);
                }

                return connection;
            }
        }
    }

    /// <summary>What the export service does when a test arms it: shared by every scope of the instance.</summary>
    public sealed class ConformanceProbe
    {
        /// <summary>The exports' validation hook, given the service so it can enlist shipments; nothing when null.</summary>
        public Func<EnlistingExportService, SaveContext<Export>, Task>? ValidateExports { get; set; }

        /// <summary>The exports' post-commit hook, which may throw; nothing when null.</summary>
        public Action? AfterCommit { get; set; }

        /// <summary>Disarms the probe.</summary>
        public void Reset()
        {
            ValidateExports = null;
            AfterCommit = null;
        }
    }

    /// <summary>The export stack's service: its validation runs what the probe arms, so a test enlists shipments from an export's save.</summary>
    /// <param name="factory">The pipeline factory.</param>
    /// <param name="probe">The probe.</param>
    public sealed class EnlistingExportService(IEntityPipelineFactory factory, ConformanceProbe probe) : EntityService<Export>(factory), IEnlists<Shipment>
    {
        /// <inheritdoc />
        protected override Task ValidateAsync(SaveContext<Export> context)
        {
            return probe.ValidateExports?.Invoke(this, context) ?? Task.CompletedTask;
        }

        /// <inheritdoc />
        protected override Task AfterCommitAsync(PersistOutcome<Export> outcome)
        {
            probe.AfterCommit?.Invoke();
            return Task.CompletedTask;
        }
    }
}
