// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.Metrics.Testing;
using System.Diagnostics.Metrics;
using System.Globalization;
using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Blobs;
using Tellma.Core.Abstractions.Composition;
using Tellma.Core.Abstractions.Crud;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Errors;
using Tellma.Core.Abstractions.Validation;
using Tellma.Core.Data.Ids;
using Tellma.Core.IntegrationTests.Access;
using Tellma.Core.Testing.Blobs;
using Tellma.Testing.Entities;

namespace Tellma.Core.IntegrationTests.Blobs
{
    /// <summary>
    ///     Blob references written by an enlisted save against a real tenant database: a widget save
    ///     enlists a blob owner, so the owner's attach rule and effects run as a group of the widget's
    ///     persist. On a cold id buffer the enlisted attach costs the save no round trip beyond an
    ///     enlisted owner without blobs; a blob that stops being attachable before the persist refuses
    ///     the whole transaction, each attach of the group counted once.
    /// </summary>
    /// <param name="fixture">The tenant databases and the seeding helpers.</param>
    [Trait("Category", "Integration")]
    public sealed class BlobEnlistmentTests(BlobReferenceDatabase fixture) : IClassFixture<BlobReferenceDatabase>
    {
        /// <summary>The widgets' resource, the host stack.</summary>
        private const string Widgets = "fixture.Widget";

        /// <summary>The blob owners' resource, the enlisted stack.</summary>
        private const string Owners = "fixture.BlobOwner";

        /// <summary>The options of a save that answers ids only.</summary>
        private static readonly SaveOptions IdsOnly = new() { ReturnEntities = false };

        /// <summary>The test's cancellation.</summary>
        private static CancellationToken Token => TestContext.Current.CancellationToken;

        [Fact]
        public async Task On_a_cold_id_buffer_an_enlisted_save_attaches_its_blobs_at_no_round_trip_of_their_own()
        {
            BlobEnlistmentProbe probe = new();
            await using ServiceProvider instance = CreateInstance(probe);
            (int userId, string subject) = await CallerAsync();
            int file = await fixture.StageAsync(userId, BlobReferenceDatabase.FileKind);

            // --- Warm the caller's connection and permissions with one enlisted save ---
            probe.Validate = static async (service, context) => _ = await service.EnlistSaveAsync(context.Host, [new BlobOwner { Name = "warm" }]);
            _ = await CountedSaveAsync(instance, subject, "warm");

            // --- Cold buffers: an enlisted owner without blobs, then one that attaches a file ---
            probe.Validate = static async (service, context) => _ = await service.EnlistSaveAsync(context.Host, [new BlobOwner { Name = "cold plain" }]);
            int plainTrips = await ColdSaveAsync(instance, subject, "cold-plain");
            probe.Validate = async (service, context) => _ = await service.EnlistSaveAsync(context.Host, [new BlobOwner { Name = "cold attach", FileId = file }]);
            int attachTrips = await ColdSaveAsync(instance, subject, "cold-attach");

            // --- The blob rows rode the enlisted round that assigned the ids, and the attach committed ---
            Assert.Equal(plainTrips, attachTrips);
            Assert.Equal("Committed|c|-", await fixture.StateAsync(file));
            Assert.Equal(file, await fixture.Connect.ScalarAsync<int?>(ConnectDatabase.TenantA, "SELECT [FileId] FROM [fixture].[BlobOwners] WHERE [Name] = N'cold attach';"));
        }

        [Fact]
        public async Task A_blob_an_enlisted_save_can_no_longer_attach_at_persist_refuses_the_transaction_and_counts_each_attach_of_the_group_once()
        {
            BlobEnlistmentProbe probe = new();
            await using ServiceProvider instance = CreateInstance(probe);
            using MetricCollector<long> rejected = new(instance.GetRequiredService<IMeterFactory>(), BlobTelemetryNames.MeterName, BlobTelemetryNames.AttachRejected);
            (int userId, string subject) = await CallerAsync();
            int file = await fixture.StageAsync(userId, BlobReferenceDatabase.FileKind);
            int photo = await fixture.StageAsync(userId, BlobReferenceDatabase.PhotoKind);

            // --- The enlisted owner attaches both; the file expires on another connection while the
            // widget's persist is composed, after the validation round passed it ---
            probe.Validate = async (service, context) => _ = await service.EnlistSaveAsync(context.Host, [new BlobOwner { Name = "enlisted late", FileId = file, PhotoId = photo }]);
            probe.Persist = () => fixture.Connect.ExecuteAsync(
                ConnectDatabase.TenantA, Invariant($"UPDATE [core].[Blobs] SET [ExpiresAt] = DATEADD(minute, -1, SYSUTCDATETIME()) WHERE [Id] = {file};"));
            ValidationException refused;
            await using (AsyncServiceScope scope = await fixture.Connect.ScopeAsync(subject, services: instance))
            {
                refused = await Assert.ThrowsAsync<ValidationException>(() => WidgetService(scope).SaveAsync([NewWidget("enlisted-late")], IdsOnly, Token));
            }

            // --- The group translated the refusal and counted each of its attaches once ---
            Assert.Equal(
                [$"{ValidationCodes.BlobNotAttachable} persist {Owners}"],
                [.. refused.Errors.Select(static error => $"{error.Code} {error.Arguments.GetValueOrDefault("reason")} {error.Arguments.GetValueOrDefault("enlistedResource")}")]);
            Assert.Equal(
                ["fixture-file persist", "fixture-photo persist"],
                [.. rejected.GetMeasurementSnapshot()
                    .Select(static measurement => $"{measurement.Tags[BlobTelemetryNames.KindTag]} {measurement.Tags[BlobTelemetryNames.ReasonTag]}")
                    .Order(StringComparer.Ordinal)]);

            // --- Nothing of the transaction committed ---
            Assert.Equal(("Staged|-|e", "Staged|-|e"), (await fixture.StateAsync(file), await fixture.StateAsync(photo)));
            Assert.Equal(0, await fixture.Connect.ScalarAsync<int>(ConnectDatabase.TenantA, "SELECT COUNT(*) FROM [fixture].[BlobOwners] WHERE [Name] = N'enlisted late';"));
            Assert.Equal(0, await fixture.Connect.ScalarAsync<int>(ConnectDatabase.TenantA, "SELECT COUNT(*) FROM [fixture].[Widgets] WHERE [Subject] = 'enlisted-late';"));
        }

        /// <summary>An instance with in-memory blobs whose widget stack runs through <see cref="BlobEnlistingWidgetService" />.</summary>
        /// <param name="probe">The probe the widget service runs.</param>
        /// <returns>The instance.</returns>
        private ServiceProvider CreateInstance(BlobEnlistmentProbe probe)
        {
            return fixture.Connect.CreateInstance(
                configure: services =>
                {
                    _ = services.AddInMemoryBlobs();
                    _ = services.AddSingleton(probe);
                },
                compose: static tellma => tellma.AddFeature(new BlobEnlistmentFeature()));
        }

        /// <summary>Seeds a caller who reads and saves both widgets and blob owners.</summary>
        /// <returns>The caller's id and subject.</returns>
        private Task<(int UserId, string Subject)> CallerAsync()
        {
            return fixture.SeedCallerAsync(
                ConnectDatabase.TenantA,
                (Widgets, AccessActions.Read, null),
                (Widgets, AccessActions.Save, null),
                (Owners, AccessActions.Read, null),
                (Owners, AccessActions.Save, null));
        }

        /// <summary>Saves one widget on cold id buffers, counting the round trips the save took.</summary>
        /// <param name="instance">The instance.</param>
        /// <param name="subject">The caller's subject.</param>
        /// <param name="code">The widget's code.</param>
        /// <returns>The round trips.</returns>
        private async Task<int> ColdSaveAsync(ServiceProvider instance, string subject, string code)
        {
            // Every new row's id, the widget's and the enlisted owner's, then rides a round trip.
            instance.GetRequiredService<IdAllocator>().ForgetTenant(ConnectDatabase.TenantA);
            return await CountedSaveAsync(instance, subject, code);
        }

        /// <summary>Saves one widget in a scope of its own, counting the round trips the save took.</summary>
        /// <param name="instance">The instance.</param>
        /// <param name="subject">The caller's subject.</param>
        /// <param name="code">The widget's code.</param>
        /// <returns>The round trips.</returns>
        private async Task<int> CountedSaveAsync(ServiceProvider instance, string subject, string code)
        {
            await using AsyncServiceScope scope = await fixture.Connect.ScopeAsync(subject, services: instance);
            DataAccessScope access = scope.ServiceProvider.GetRequiredService<DataAccessScope>();
            int before = access.RoundTrips;
            _ = await WidgetService(scope).SaveAsync([NewWidget(code)], IdsOnly, Token);
            return access.RoundTrips - before;
        }

        /// <summary>A new widget whose code, name and subject are one value.</summary>
        /// <param name="code">The value.</param>
        /// <returns>The widget.</returns>
        private static Widget NewWidget(string code)
        {
            return new Widget { Code = code, Kind = WidgetKind.Plain, Name = code, Subject = code };
        }

        /// <summary>A scope's widget service.</summary>
        /// <param name="scope">The scope.</param>
        /// <returns>The service.</returns>
        private static EntityService<Widget, int> WidgetService(AsyncServiceScope scope)
        {
            return scope.ServiceProvider.GetRequiredService<EntityService<Widget, int>>();
        }

        /// <summary>Formats text in the invariant culture.</summary>
        /// <param name="text">The text.</param>
        /// <returns>The formatted text.</returns>
        private static string Invariant(FormattableString text)
        {
            return text.ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>The feature that runs the fixture's widget stack through <see cref="BlobEnlistingWidgetService" />.</summary>
        private sealed class BlobEnlistmentFeature : ITellmaFeature
        {
            /// <inheritdoc />
            public string Name => "blob-enlistment";

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
                _ = contribution.Entity<Widget, BlobEnlistingWidgetService>();
            }
        }
    }

    /// <summary>What the widget service of the blob enlistment suite does, which a test arms.</summary>
    public sealed class BlobEnlistmentProbe
    {
        /// <summary>The widgets' validation hook, given the service so it can enlist; nothing when null.</summary>
        public Func<BlobEnlistingWidgetService, SaveContext<Widget>, Task>? Validate { get; set; }

        /// <summary>The widgets' persist hook, which runs while the persist is composed; nothing when null.</summary>
        public Func<Task>? Persist { get; set; }
    }

    /// <summary>The widget stack's service of the blob enlistment suite: its hooks run what the probe arms, so a test enlists blob owners.</summary>
    /// <param name="factory">The pipeline factory.</param>
    /// <param name="probe">The probe.</param>
    public sealed class BlobEnlistingWidgetService(IEntityPipelineFactory factory, BlobEnlistmentProbe probe) : EntityService<Widget>(factory), IEnlists<BlobOwner>
    {
        /// <inheritdoc />
        protected override Task ValidateAsync(SaveContext<Widget> context)
        {
            return probe.Validate?.Invoke(this, context) ?? Task.CompletedTask;
        }

        /// <inheritdoc />
        protected override Task ContributeAsync(PersistContext<Widget> context)
        {
            return probe.Persist?.Invoke() ?? Task.CompletedTask;
        }
    }
}
