// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using System.Diagnostics.Metrics;
using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Api;
using Tellma.Core.Abstractions.Crud;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Entities;
using Tellma.Core.Abstractions.Errors;
using Tellma.Core.Crud.Validation;
using Tellma.Core.Testing.Crud;
using Tellma.Testing.Entities;
using static System.FormattableString;

namespace Tellma.Core.IntegrationTests.Crud.Conformance
{
    // Spec 0015 §16.1: every instrument of the pipeline, driven through the real stack on a tenant
    // database, each measurement held to its instrument's closed tag set.
    /// <summary>
    ///     The pipeline's instruments, each driven by the operation that records it: every one is
    ///     observed, every measurement carries exactly its instrument's tags, and every closed tag
    ///     takes a value of its set. The instance's large-batch threshold is a few rows, so the large
    ///     lane is driven too.
    /// </summary>
    /// <param name="fixture">The tenant databases.</param>
    [Trait("Category", "Integration")]
    public sealed class CrudInstrumentTests(InstrumentDatabase fixture) : IClassFixture<InstrumentDatabase>
    {
        /// <summary>The parcels' resource.</summary>
        private const string Parcels = "fixture.Parcel";

        /// <summary>The nodes' resource.</summary>
        private const string Nodes = "fixture.Node";

        /// <summary>The shipments' resource.</summary>
        private const string Shipments = "fixture.Shipment";

        /// <summary>The widgets' resource.</summary>
        private const string Widgets = "fixture.Widget";

        /// <summary>The test's cancellation.</summary>
        private static CancellationToken Token => TestContext.Current.CancellationToken;

        [Fact]
        public async Task Every_pipeline_instrument_records_within_its_closed_tag_set_through_the_operations_that_drive_it()
        {
            fixture.Probe.Reset();
            (int userId, string subject) = await SeedCallerAsync();
            using CrudInstrumentCollector collector = new(fixture.Services.GetRequiredService<IMeterFactory>());

            // --- Saves: a create from each of two sources, its rounds and its entities ---
            int created = await CreateParcelAsync(subject, "instrumented");
            _ = await InScopeAsync(subject, scope => Service<Parcel>(scope).SaveAsync([new Parcel { Name = "imported" }], new SaveOptions { Source = SaveSource.Import }, Token));

            // --- Reads: a filter of a shape not seen before is discovered; a missing row is not found ---
            _ = await InScopeAsync(subject, scope => Service<Parcel>(scope).QueryAsync(new QueryRequest { Select = "Id,Name", Filters = ["Name = 'instrumented'"] }, Token));
            _ = await RefusedAsync<NotFoundException>(subject, scope => Service<Parcel>(scope).GetByIdAsync(2_000_000_000, DetailsRequest.None, Token));

            // --- A stale stamp, then the same payload under the override ---
            Parcel stale = await ParcelAsync(subject, created);
            Parcel fresh = await ParcelAsync(subject, created);
            fresh.Name = "moved";
            _ = await InScopeAsync(subject, scope => Service<Parcel>(scope).SaveAsync([fresh], new SaveOptions(), Token));
            stale.Name = "stale";
            _ = await RefusedAsync<ConcurrencyException>(subject, scope => Service<Parcel>(scope).SaveAsync([stale], new SaveOptions(), Token));
            _ = await InScopeAsync(subject, scope => Service<Parcel>(scope).SaveAsync([stale], new SaveOptions { Concurrency = ConcurrencyMode.Override }, Token));

            // --- A row deleted on another connection before the persist: a missing conflict ---
            int doomed = await CreateParcelAsync(subject, "doomed");
            Parcel vanishing = await ParcelAsync(subject, doomed);
            vanishing.Name = "vanished";
            SaveOptions deleting = new() { OnPersist = Meanwhile(Invariant($"DELETE FROM [fixture].[Parcels] WHERE [Id] = {doomed};")) };
            _ = await RefusedAsync<NotFoundException>(subject, scope => Service<Parcel>(scope).SaveAsync([vanishing], deleting, Token));

            // --- A move another connection closes into a cycle before the persist: a cycle conflict ---
            Node a = await CreateNodeAsync(subject, "N-instrument-a");
            Node b = await CreateNodeAsync(subject, "N-instrument-b");
            Node moving = Assert.Single((await InScopeAsync(subject, scope => Service<Node>(scope).GetByIdAsync(a.Id, DetailsRequest.None, Token))).Entities);
            moving.ParentId = b.Id;
            SaveOptions cycling = new() { ReturnEntities = false, OnPersist = Meanwhile(Invariant($"UPDATE [fixture].[Nodes] SET [ParentId] = {a.Id}, [Node] = '/{a.Id}/{b.Id}/' WHERE [Id] = {b.Id};")) };
            _ = await RefusedAsync<ValidationException>(subject, scope => Service<Node>(scope).SaveAsync([moving], cycling, Token));

            // --- Permissions moved between the validation round and the persist: a re-run ---
            SaveOptions bumping = new() { ReturnEntities = false, OnPersist = Meanwhile(() => fixture.Connect.BumpPermissionsAsync(userId)) };
            _ = await InScopeAsync(subject, scope => Service<Parcel>(scope).SaveAsync([new Parcel { Name = "rerun" }], bumping, Token));

            // --- A warning the caller did not confirm, then one it did ---
            fixture.Probe.ValidateParcels = static (_, context) =>
            {
                context.Host.Confirmations.Require(ConformanceDatabase.ParcelConfirmation);
                return Task.CompletedTask;
            };
            _ = await RefusedAsync<ConfirmationRequiredException>(subject, scope => Service<Parcel>(scope).SaveAsync([new Parcel { Name = "unconfirmed" }], new SaveOptions(), Token));
            _ = await InScopeAsync(subject, scope =>
            {
                scope.GetRequiredService<Confirmations>().Seed([ConformanceDatabase.ParcelConfirmation]);
                return Service<Parcel>(scope).SaveAsync([new Parcel { Name = "confirmed" }], new SaveOptions(), Token);
            });

            // --- A shipment enlisted with a parcel's save ---
            fixture.Probe.ValidateParcels = static async (service, context) => _ = await service.EnlistSaveAsync(context.Host, [new Shipment { Payload = "enlisted" }]);
            _ = await InScopeAsync(subject, scope => Service<Parcel>(scope).SaveAsync([new Parcel { Name = "enlisting" }], new SaveOptions(), Token));

            // --- A post-commit hook that fails, which never changes the answer ---
            fixture.Probe.Reset();
            fixture.Probe.AfterCommit = static () => throw new InvalidOperationException("The instrumented post-commit hook fails.");
            _ = await InScopeAsync(subject, scope => Service<Parcel>(scope).SaveAsync([new Parcel { Name = "effect" }], new SaveOptions(), Token));
            fixture.Probe.Reset();

            // --- Every instrument observed, each measurement within its shape ---
            collector.AssertClosed();
            Assert.All(CrudInstrumentCollector.Instruments, instrument => Assert.NotEmpty(collector.Snapshot(instrument)));

            // --- Each tag's values as the operations above drove them ---
            Assert.Superset(
                new HashSet<string> { "save ok", "save conflict", "save not_found", "save validation", "save confirmation", "query ok", "get ok", "get not_found" },
                Values(collector, CrudTelemetryNames.Operations, CrudTelemetryNames.OperationTag, CrudTelemetryNames.OutcomeTag));
            Assert.Superset(new HashSet<string> { "save web small", "save import small" }, Values(collector, CrudTelemetryNames.OperationDuration, CrudTelemetryNames.OperationTag, CrudTelemetryNames.SourceTag, CrudTelemetryNames.LaneTag));
            Assert.Superset(new HashSet<string> { "save ok" }, Values(collector, CrudTelemetryNames.ValidationRounds, CrudTelemetryNames.OperationTag, CrudTelemetryNames.OutcomeTag));
            Assert.Superset(new HashSet<string> { "web small", "import small" }, Values(collector, CrudTelemetryNames.SaveEntities, CrudTelemetryNames.SourceTag, CrudTelemetryNames.LaneTag));
            Assert.Superset(new HashSet<string> { "stamp", "missing", "cycle" }, Values(collector, CrudTelemetryNames.ConcurrencyConflicts, CrudTelemetryNames.KindTag));
            Assert.Superset(new HashSet<string> { "permissions" }, Values(collector, CrudTelemetryNames.StaleContextReruns, CrudTelemetryNames.TagTag));
            Assert.Superset(new HashSet<string> { nameof(EnlistingParcelService) }, Values(collector, CrudTelemetryNames.EffectFailures, CrudTelemetryNames.EffectTag));
            Assert.Superset(new HashSet<string> { "save save ok" }, Values(collector, CrudTelemetryNames.Enlistments, CrudTelemetryNames.OperationTag, CrudTelemetryNames.KindTag, CrudTelemetryNames.OutcomeTag));
            Assert.Superset(
                new HashSet<string> { $"{ConformanceDatabase.ParcelConfirmation} required", $"{ConformanceDatabase.ParcelConfirmation} confirmed" },
                Values(collector, CrudTelemetryNames.Confirmations, CrudTelemetryNames.KeyTag, CrudTelemetryNames.OutcomeTag));

            // --- The lanes: a save counts its rows at every depth, for its duration and its entities
            // alike, and only more rows than the threshold are large. A widget with a part is two rows,
            // with a note under the part three, though each is one root; a read by ids counts its ids ---
            Assert.Equal(["save small"], await LanesAsync(collector, "save", () => InScopeAsync(subject, scope => Service<Widget>(scope).SaveAsync([NewWidget(1, notes: false)], new SaveOptions { ReturnEntities = false }, Token))));
            Assert.Equal(["web small"], Sorted(Values(collector, CrudTelemetryNames.SaveEntities, CrudTelemetryNames.SourceTag, CrudTelemetryNames.LaneTag)));
            Assert.Equal(["save large"], await LanesAsync(collector, "save", () => InScopeAsync(subject, scope => Service<Widget>(scope).SaveAsync([NewWidget(2, notes: true)], new SaveOptions { ReturnEntities = false }, Token))));
            Assert.Equal(["web large"], Sorted(Values(collector, CrudTelemetryNames.SaveEntities, CrudTelemetryNames.SourceTag, CrudTelemetryNames.LaneTag)));
            Assert.Equal(["get_by_ids small"], await LanesAsync(collector, "get_by_ids", () => InScopeAsync(subject, scope => Service<Widget>(scope).GetByIdsAsync(MissingIds(InstrumentDatabase.LargeBatchThreshold), DetailsRequest.None, Token))));
            Assert.Equal(["get_by_ids large"], await LanesAsync(collector, "get_by_ids", () => InScopeAsync(subject, scope => Service<Widget>(scope).GetByIdsAsync(MissingIds(InstrumentDatabase.LargeBatchThreshold + 1), DetailsRequest.None, Token))));
            collector.AssertClosed();
        }

        /// <summary>A set of values in ordinal order.</summary>
        /// <param name="values">The values.</param>
        /// <returns>The values, sorted.</returns>
        private static List<string> Sorted(HashSet<string> values)
        {
            return [.. values.Order(StringComparer.Ordinal)];
        }

        /// <summary>The lanes an operation's durations carried on one call, the collector cleared before it.</summary>
        /// <typeparam name="T">What the call answers.</typeparam>
        /// <param name="collector">The collector.</param>
        /// <param name="operation">The operation whose durations are read.</param>
        /// <param name="call">The call.</param>
        /// <returns>Each duration's operation and lane, distinct, in ordinal order.</returns>
        private static async Task<List<string>> LanesAsync<T>(CrudInstrumentCollector collector, string operation, Func<Task<T>> call)
        {
            collector.Clear();
            _ = await call();
            return [.. collector.Snapshot(CrudTelemetryNames.OperationDuration)
                .Where(measurement => measurement.Tag(CrudTelemetryNames.OperationTag) == operation)
                .Select(static measurement => $"{measurement.Tag(CrudTelemetryNames.OperationTag)} {measurement.Tag(CrudTelemetryNames.LaneTag)}")
                .Distinct()
                .Order(StringComparer.Ordinal)];
        }

        /// <summary>A new widget with one part, and one note under the part when asked.</summary>
        /// <param name="ordinal">A number no other widget of the test was created with.</param>
        /// <param name="notes">Whether the part holds a note.</param>
        /// <returns>The widget.</returns>
        private static Widget NewWidget(int ordinal, bool notes)
        {
            return new Widget
            {
                Code = Invariant($"WI-{ordinal}"),
                Kind = WidgetKind.Plain,
                Name = Invariant($"Instrumented {ordinal}"),
                Subject = Invariant($"wi-{ordinal}"),
                Parts = [new WidgetPart { Ordinal = 1, PartNotes = notes ? [new WidgetPartNote { Text = "Noted" }] : null }],
            };
        }

        /// <summary>Ids no widget holds.</summary>
        /// <param name="count">How many.</param>
        /// <returns>The ids.</returns>
        private static int[] MissingIds(int count)
        {
            return [.. Enumerable.Range(2_000_000_000 - count, count)];
        }

        /// <summary>The values an instrument's measurements carried on some of their tags, each measurement's joined by spaces.</summary>
        /// <param name="collector">The collector.</param>
        /// <param name="instrument">The instrument.</param>
        /// <param name="tags">The tags.</param>
        /// <returns>The distinct values.</returns>
        private static HashSet<string> Values(CrudInstrumentCollector collector, string instrument, params string[] tags)
        {
            return [.. collector.Snapshot(instrument).Select(measurement => string.Join(' ', tags.Select(measurement.Tag)))];
        }

        /// <summary>
        ///     Another caller's commit in the window between a save's validation round trip and its
        ///     persist, on a connection of its own while the persist is composed.
        /// </summary>
        /// <param name="statement">The other caller's statement.</param>
        /// <returns>The save's caller statements, which append nothing.</returns>
        private Action<IDataBatch> Meanwhile(string statement)
        {
            return Meanwhile(() => fixture.ExecuteAsync(statement));
        }

        /// <summary>Another caller's write in the window between a save's validation round trip and its persist.</summary>
        /// <param name="write">The other caller's write.</param>
        /// <returns>The save's caller statements, which append nothing.</returns>
        private static Action<IDataBatch> Meanwhile(Func<Task> write)
        {
            // The caller's statements are composed synchronously, after the validation round trip
            // completed and before the persist is sent; the other connection's write is awaited off
            // the composing thread.
            return _ => Task.Run(write).GetAwaiter().GetResult();
        }

        /// <summary>Seeds the caller, who reads and writes parcels, nodes, shipments and widgets.</summary>
        /// <returns>The caller's id and subject.</returns>
        private async Task<(int UserId, string Subject)> SeedCallerAsync()
        {
            StackConformanceCaller caller = await fixture.SeedCallerAsync(
                [
                    new(Parcels, AccessActions.Read), new(Parcels, AccessActions.Save), new(Parcels, AccessActions.Delete),
                    new(Nodes, AccessActions.Read), new(Nodes, AccessActions.Save),
                    new(Shipments, AccessActions.Read), new(Shipments, AccessActions.Save),
                    new(Widgets, AccessActions.Read), new(Widgets, AccessActions.Save),
                ],
                Token);
            return (caller.UserId, caller.Subject);
        }

        /// <summary>Creates a parcel.</summary>
        /// <param name="subject">The caller's subject.</param>
        /// <param name="name">The parcel's name.</param>
        /// <returns>Its id.</returns>
        private async Task<int> CreateParcelAsync(string subject, string name)
        {
            EntitiesResult<Parcel> saved = await InScopeAsync(subject, scope => Service<Parcel>(scope).SaveAsync([new Parcel { Name = name }], new SaveOptions { ReturnEntities = false }, Token));
            return (int)Assert.Single(saved.Ids);
        }

        /// <summary>Creates a root node.</summary>
        /// <param name="subject">The caller's subject.</param>
        /// <param name="code">The node's code.</param>
        /// <returns>The node as stored.</returns>
        private async Task<Node> CreateNodeAsync(string subject, string code)
        {
            EntitiesResult<Node> saved = await InScopeAsync(subject, scope => Service<Node>(scope).SaveAsync([new Node { Code = code, Name = code }], new SaveOptions(), Token));
            return Assert.Single(saved.Entities);
        }

        /// <summary>Reads a parcel.</summary>
        /// <param name="subject">The caller's subject.</param>
        /// <param name="id">The parcel's id.</param>
        /// <returns>The parcel as stored.</returns>
        private async Task<Parcel> ParcelAsync(string subject, int id)
        {
            return Assert.Single((await InScopeAsync(subject, scope => Service<Parcel>(scope).GetByIdAsync(id, DetailsRequest.None, Token))).Entities);
        }

        /// <summary>Runs a call in a scope of the caller on the first tenant.</summary>
        /// <typeparam name="T">What the call answers.</typeparam>
        /// <param name="subject">The caller's subject.</param>
        /// <param name="call">The call.</param>
        /// <returns>What it answered.</returns>
        private async Task<T> InScopeAsync<T>(string subject, Func<IServiceProvider, Task<T>> call)
        {
            await using AsyncServiceScope scope = await fixture.Connect.ScopeAsync(subject, services: fixture.Services);
            return await call(scope.ServiceProvider);
        }

        /// <summary>Runs a call that must be refused in a scope of the caller.</summary>
        /// <typeparam name="TException">The refusal.</typeparam>
        /// <param name="subject">The caller's subject.</param>
        /// <param name="call">The call.</param>
        /// <returns>The refusal.</returns>
        private Task<TException> RefusedAsync<TException>(string subject, Func<IServiceProvider, Task> call)
            where TException : Exception
        {
            return InScopeAsync(subject, scope => Assert.ThrowsAsync<TException>(() => call(scope)));
        }

        /// <summary>A stack's service in a scope.</summary>
        /// <typeparam name="TEntity">The stack's entity.</typeparam>
        /// <param name="scope">The scope's services.</param>
        /// <returns>The service.</returns>
        private static EntityService<TEntity, int> Service<TEntity>(IServiceProvider scope)
            where TEntity : Entity<int>
        {
            return scope.GetRequiredService<EntityService<TEntity, int>>();
        }
    }
}
