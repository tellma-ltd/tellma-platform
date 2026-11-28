// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Testing;
using System.Collections.Immutable;
using System.Data;
using System.Globalization;
using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Composition;
using Tellma.Core.Abstractions.Realtime;
using Tellma.Core.Abstractions.Settings;
using Tellma.Core.Abstractions.Tenancy;
using Tellma.Core.IntegrationTests.Access;
using Tellma.Core.IntegrationTests.Data;
using Tellma.Core.Tenancy;

namespace Tellma.Core.IntegrationTests.Settings
{
    /// <summary>
    ///     The two tenant databases of <see cref="ConnectDatabase" />, served by instances that offer
    ///     English and Arabic and declare the suite's setting keys, with data protection, a catalog
    ///     that records each rename (or fails it), a publisher that records each client event, and a
    ///     count of the round trips each instance's batches take.
    /// </summary>
    /// <param name="sql">The SQL Server the databases are created on.</param>
    public sealed class SettingsDatabase(SqlServerFixture sql) : IAsyncLifetime
    {
        /// <summary>Puts the first tenant's typed row back as the fixture completed it, removes every entry, and moves the settings tag.</summary>
        private const string ResetText = """
            DELETE FROM [core].[SettingEntries];
            UPDATE [core].[Settings]
               SET [Name] = N'Tenant 1', [Name2] = NULL, [Name3] = NULL, [PrimaryLanguage] = 'en', [SecondaryLanguage] = NULL, [TertiaryLanguage] = NULL,
                   [PrimaryCalendar] = 'gc', [SecondaryCalendar] = NULL, [TimeZone] = 'UTC', [Region] = NULL, [Digits] = NULL;
            UPDATE [core].[VersionTags] SET [Tag] = NEWID() WHERE [Name] = N'settings';
            """;

        /// <summary>The instance that serves the tests; null until initialised.</summary>
        private ServiceProvider? _instance;

        /// <summary>The two tenant databases and their composition.</summary>
        public ConnectDatabase Connect { get; } = new(sql);

        /// <summary>The instance that serves the tests.</summary>
        public ServiceProvider Instance => _instance ?? throw new InvalidOperationException("The databases are not created yet.");

        /// <summary>The round trips of the serving instance's batches.</summary>
        public RoundTripCounter RoundTrips { get; } = new();

        /// <summary>The catalog every instance mirrors the names into.</summary>
        public RecordingCatalog Catalog { get; } = new();

        /// <summary>Every client event the serving instance published.</summary>
        public DataBatchDatabase.RecordingPublisher Events { get; } = new();

        /// <summary>The data protection every instance shares, so one instance reads the secrets another saved.</summary>
        public IDataProtectionProvider Protection { get; } = new EphemeralDataProtectionProvider();

        /// <summary>The serving instance's log records.</summary>
        public FakeLogCollector Logs => Instance.GetFakeLogCollector();

        /// <inheritdoc />
        public async ValueTask InitializeAsync()
        {
            await Connect.InitializeAsync();
            _instance = CreateInstance(RoundTrips);
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

        /// <summary>Another instance over the same databases, with its own caches; the caller disposes it.</summary>
        /// <param name="roundTrips">Counts its batches' round trips; none when null.</param>
        /// <returns>The instance.</returns>
        public ServiceProvider CreateInstance(RoundTripCounter? roundTrips = null)
        {
            return Connect.CreateInstance(
                configure: services =>
                {
                    _ = services.AddFakeLogging();
                    _ = services.Replace(ServiceDescriptor.Singleton(Protection));
                    _ = services.Replace(ServiceDescriptor.Singleton<ITenantCatalog>(Catalog));
                    _ = services.Replace(ServiceDescriptor.Scoped<IClientEventPublisher>(_ => Events));
                    if (roundTrips is not null)
                    {
                        ServiceDescriptor original = services.Last(static descriptor => descriptor.ServiceType == typeof(ITenantConnectionProvider));
                        Func<IServiceProvider, object> factory = original.ImplementationFactory
                            ?? throw new InvalidOperationException("The fixture's connection provider is registered through a factory.");
                        _ = services.Replace(ServiceDescriptor.Scoped<ITenantConnectionProvider>(provider => new CountingConnections((ITenantConnectionProvider)factory(provider), roundTrips)));
                    }
                },
                compose: static tellma => tellma.Languages(["en", "ar"]).AddFeature(new SettingsKeysFeature()));
        }

        /// <summary>Puts the first tenant's settings back, with no entry, and forgets the recorded renames and events.</summary>
        /// <returns>A task that completes when the settings are reset.</returns>
        public async Task ResetAsync()
        {
            await Connect.ExecuteAsync(ConnectDatabase.TenantA, ResetText);
            Catalog.Reset();
            Events.Clear();
        }

        /// <summary>A request scope of the first tenant on an instance, for a caller whose session stepped up a moment ago.</summary>
        /// <param name="subject">The caller's subject.</param>
        /// <param name="services">The instance; the serving one when null.</param>
        /// <returns>The scope.</returns>
        public Task<AsyncServiceScope> ScopeAsync(string subject, IServiceProvider? services = null)
        {
            DateTimeOffset now = DateTimeOffset.UtcNow;
            return Connect.ScopeAsync(subject, services: services ?? Instance, assurance: new AuthenticationAssurance(AssuranceTiers.Aal2, now, now));
        }

        /// <summary>Seeds a caller of the first tenant who is a member of one role holding the permissions.</summary>
        /// <param name="name">The caller's name.</param>
        /// <param name="permissions">The role's permissions: a resource and an action each.</param>
        /// <returns>The caller's id and subject.</returns>
        public async Task<(int UserId, string Subject)> SeedCallerAsync(string name, params (string Resource, string Action)[] permissions)
        {
            (int userId, string subject) = await Connect.SeedCallerAsync(name);
            if (permissions.Length > 0)
            {
                int roleId = await Connect.SeedRoleAsync([.. permissions.Select(static permission => (permission.Resource, permission.Action, (string?)null))]);
                await Connect.SeedMembershipAsync(userId, roleId);
            }

            await Connect.BumpPermissionsAsync(userId);
            return (userId, subject);
        }

        /// <summary>The feature that declares the suite's setting keys.</summary>
        private sealed class SettingsKeysFeature : ITellmaFeature
        {
            /// <inheritdoc />
            public string Name => "settings-suite";

            /// <inheritdoc />
            public void Declare(FeatureDeclaration declaration)
            {
                ArgumentNullException.ThrowIfNull(declaration);
            }

            /// <inheritdoc />
            public void Contribute(FeatureContribution contribution)
            {
                ArgumentNullException.ThrowIfNull(contribution);
                _ = contribution.SettingKeys(typeof(SettingsSuiteKeys));
            }
        }

        /// <summary>The suite's connections, each adding its server round trips to a counter when it closes.</summary>
        /// <param name="inner">The fixture's provider.</param>
        /// <param name="counter">The counter.</param>
        private sealed class CountingConnections(ITenantConnectionProvider inner, RoundTripCounter counter) : ITenantConnectionProvider
        {
            /// <inheritdoc />
            public TenantInfo Tenant => inner.Tenant;

            /// <inheritdoc />
            public async Task<SqlConnection> OpenAsync(CancellationToken cancellationToken = default)
            {
                SqlConnection connection = await inner.OpenAsync(cancellationToken).ConfigureAwait(false);
                connection.StatisticsEnabled = true;
                connection.StateChange += (_, change) =>
                {
                    if (change.CurrentState == ConnectionState.Closed)
                    {
                        counter.Add(Convert.ToInt64(connection.RetrieveStatistics()["ServerRoundtrips"], CultureInfo.InvariantCulture));
                    }
                };

                return connection;
            }
        }
    }

    /// <summary>The suite's setting keys: two categories besides the general one, a secret, and two reference keys.</summary>
    internal static class SettingsSuiteKeys
    {
        /// <summary>A validated server width in the <c>fx</c> category.</summary>
        public static readonly SettingKey<int> Width = new("fx.posting.width", 6, validate: static width => width is >= 1 and <= 12 ? null : "Fx.WidthOutOfRange");

        /// <summary>A client flag in the <c>fx</c> category.</summary>
        public static readonly SettingKey<bool> AutoNumber = new("fx.posting.autoNumber", true, visibility: SettingVisibility.Client);

        /// <summary>A role the <c>fx</c> category points at.</summary>
        public static readonly SettingKey<int?> Owner = new("fx.owner", null, references: typeof(Role));

        /// <summary>Roles the <c>fx</c> category points at.</summary>
        public static readonly SettingKey<ImmutableArray<int>> Reviewers = new("fx.reviewers", [], references: typeof(Role));

        /// <summary>A secret of the <c>fx</c> category.</summary>
        public static readonly SettingKey<string?> ApiKey = new("fx.erp.apiKey", null, visibility: SettingVisibility.Secret);

        /// <summary>A server value of the <c>ops</c> category.</summary>
        public static readonly SettingKey<int> Shelf = new("ops.shelf", 3);

        /// <summary>A client value of the general category, which the typed row's securable guards.</summary>
        public static readonly SettingKey<string?> Welcome = new("general.welcome", null, visibility: SettingVisibility.Client);
    }

    /// <summary>A count of server round trips, shared by every connection of an instance.</summary>
    public sealed class RoundTripCounter
    {
        /// <summary>The count since the last reset.</summary>
        private long _count;

        /// <summary>The round trips since the last reset.</summary>
        public long Count => Interlocked.Read(ref _count);

        /// <summary>Starts counting afresh.</summary>
        public void Reset()
        {
            _ = Interlocked.Exchange(ref _count, 0);
        }

        /// <summary>Adds a closed connection's round trips.</summary>
        /// <param name="trips">The round trips.</param>
        public void Add(long trips)
        {
            _ = Interlocked.Add(ref _count, trips);
        }
    }

    /// <summary>A tenant catalog that records each rename, or fails it while armed, and supports nothing else.</summary>
    public sealed class RecordingCatalog : ITenantCatalog
    {
        /// <summary>The renames, in order.</summary>
        private readonly List<(int TenantId, TenantDisplayNames Names)> _renames = [];

        /// <summary>Whether a rename fails.</summary>
        public bool Fails { get; set; }

        /// <summary>The renames so far, in order.</summary>
        public IReadOnlyList<(int TenantId, TenantDisplayNames Names)> Renames
        {
            get
            {
                lock (_renames)
                {
                    return [.. _renames];
                }
            }
        }

        /// <summary>Forgets the renames and stops failing.</summary>
        public void Reset()
        {
            lock (_renames)
            {
                _renames.Clear();
            }

            Fails = false;
        }

        /// <inheritdoc />
        public Task RenameAsync(int tenantId, TenantDisplayNames names, CancellationToken cancellationToken = default)
        {
            if (Fails)
            {
                throw new InvalidOperationException("The catalog is unreachable.");
            }

            lock (_renames)
            {
                _renames.Add((tenantId, names));
            }

            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public Task<TenantDescriptor> RegisterAsync(TenantRegistration registration, string actor, int? requestedId = null, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException("The settings suite's catalog only renames.");
        }

        /// <inheritdoc />
        public Task<TenantDescriptor> SetStateAsync(int tenantId, TenantState state, string? reason, string actor, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException("The settings suite's catalog only renames.");
        }

        /// <inheritdoc />
        public Task RelocateAsync(int tenantId, TenantLocation location, string actor, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException("The settings suite's catalog only renames.");
        }

        /// <inheritdoc />
        public Task<IReadOnlyList<TenantInfo>> ListAsync(CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException("The settings suite's catalog only renames.");
        }
    }
}
