// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using System.Globalization;
using System.Numerics;
using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Api;
using Tellma.Core.Abstractions.Crud;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Entities;
using Tellma.Core.Abstractions.Errors;
using Tellma.Core.Abstractions.Validation;
using Tellma.Core.Data;
using Tellma.Core.IntegrationTests.Access;
using Tellma.Testing.Entities;

namespace Tellma.Core.IntegrationTests.Crud
{
    /// <summary>
    ///     Enlisted writes against a real tenant database: a widget save enlists shipments ahead of and
    ///     after its own statement in its one transaction, so references by id each way hold, at no
    ///     round trip of their own, with the shipment stack's own stages run; a shipment's errors, and
    ///     a reference its row breaks, come back on the widget's paths naming the shipment stack; an
    ///     enlisted update under Check refuses a default stamp; a statement a caller or another stack's
    ///     effect appends that writes the shipments' table is refused at append; and the shipment
    ///     stack's API action that loads a shipment and enlists it with its own frame commits on three
    ///     round trips, refused for a caller without its securable.
    /// </summary>
    /// <param name="fixture">The seeded tenant databases.</param>
    [Trait("Category", "Integration")]
    public sealed class EnlistmentPipelineTests(EnlistmentDatabase fixture) : IClassFixture<EnlistmentDatabase>
    {
        /// <summary>The widgets' resource.</summary>
        private const string Widgets = "fixture.Widget";

        /// <summary>The shipments' resource.</summary>
        private const string Shipments = "fixture.Shipment";

        /// <summary>The shipments' table, which the shipment stack owns.</summary>
        private static readonly HashSet<TableName> ShipmentsTable = [new("fixture", "Shipments")];

        /// <summary>The options of a save that answers ids only.</summary>
        private static readonly SaveOptions IdsOnly = new() { ReturnEntities = false };

        /// <summary>The test's cancellation.</summary>
        private static CancellationToken Token => TestContext.Current.CancellationToken;

        // --- One transaction ---

        [Fact]
        public async Task A_widget_save_enlists_shipments_on_each_side_of_its_statement_so_references_each_way_hold_in_one_transaction_at_no_round_trip_of_their_own()
        {
            fixture.Probe.Reset();
            (_, string subject) = await fixture.SeedCallerAsync((Widgets, AccessActions.Read, null), (Widgets, AccessActions.Save, null));
            await WarmAsync<Widget, int>(subject);

            // --- One enlisted save first, so both id buffers are warm: a cold buffer's reservation
            // rides a round, and an enlistment awaited after it lands in the next one ---
            fixture.Probe.ValidateWidgets = async (service, context) => _ = await service.EnlistSaveAsync(context.Host, [new Shipment { Payload = "warm-1" }]);
            _ = await CountedAsync(subject, scope => Service(scope).SaveAsync([NewWidget("warm-1")], IdsOnly, Token));
            fixture.Probe.Reset();

            // --- A plain create: the round trips the enlistments must not add to ---
            (_, int plain) = await CountedAsync(subject, scope => Service(scope).SaveAsync([NewWidget("plain-1")], IdsOnly, Token));

            // --- The widget references, by the id its enlistment answers, the shipment enlisted ahead
            // of it, whose payload the shipment stack's preprocessing trims; the shipment enlisted after
            // it references the widget by the widget's own id ---
            (long Ahead, int Widget) ids = default;
            fixture.Probe.ValidateWidgets = async (service, context) =>
            {
                EnlistedSave<Shipment> ahead = await service.EnlistSaveAsync(
                    context.Host, [new Shipment { Payload = "  ahead-1  " }], new EnlistSaveOptions { Order = EnlistOrder.BeforeHost });
                ids = (ahead.Rows[0].Id, context.Entities[0].Id);
                context.Entities[0].Code = Invariant($"{EnlistmentDatabase.WidgetReferencePrefix}{ids.Ahead}");
                _ = await service.EnlistSaveAsync(context.Host, [new Shipment { Payload = Invariant($"{EnlistmentDatabase.ShipmentReferencePrefix}{ids.Widget}") }]);
            };
            (_, int trips) = await CountedAsync(subject, scope => Service(scope).SaveAsync([NewWidget("both-1")], IdsOnly, Token));

            // --- No round trip of their own, so the three rows committed on the save's one persist,
            // each reference naming the id the other row was stored under ---
            string referencing = Invariant($"{EnlistmentDatabase.ShipmentReferencePrefix}{ids.Widget}");
            Assert.Equal(plain, trips);
            Assert.Equal(Invariant($"{EnlistmentDatabase.WidgetReferencePrefix}{ids.Ahead}"), await ScalarAsync<string>(Invariant($"SELECT [Code] FROM [fixture].[Widgets] WHERE [Id] = {ids.Widget};")));
            Assert.Equal("ahead-1", await ScalarAsync<string>(Invariant($"SELECT [Payload] FROM [fixture].[Shipments] WHERE [Id] = {ids.Ahead};")));
            Assert.Equal(1, await CountAsync(Invariant($"[fixture].[Shipments] WHERE [Payload] = N'{referencing}'")));

            // --- The shipment stack's own stages ran for both, its post-commit once committed; the two
            // groups run side by side, so each stage's lines are compared in ordinal order ---
            Assert.Equal(
                [
                    "preprocess ahead-1", $"preprocess {referencing}", "validate ahead-1", $"validate {referencing}",
                    "contribute ahead-1", $"contribute {referencing}", "committed ahead-1", $"committed {referencing}",
                ],
                fixture.Probe.Shipments.Order(StringComparer.Ordinal).OrderBy(Stage));
        }

        [Fact]
        public async Task A_referenced_shipment_enlisted_after_the_widget_fails_the_reference_and_nothing_of_the_transaction_commits()
        {
            fixture.Probe.Reset();
            (_, string subject) = await fixture.SeedCallerAsync((Widgets, AccessActions.Read, null), (Widgets, AccessActions.Save, null));

            // --- The control: the reference by id holds only because of the order ---
            fixture.Probe.ValidateWidgets = async (service, context) =>
            {
                EnlistedSave<Shipment> late = await service.EnlistSaveAsync(context.Host, [new Shipment { Payload = "late-1" }]);
                context.Entities[0].Code = Invariant($"{EnlistmentDatabase.WidgetReferencePrefix}{late.Rows[0].Id}");
            };

            await using AsyncServiceScope scope = await fixture.ScopeAsync(subject);
            DataAccessException failed = await Assert.ThrowsAsync<DataAccessException>(() => Service(scope).SaveAsync([NewWidget("late-1")], IdsOnly, Token));

            Assert.Contains("CK_Widgets_ReferencedShipment", failed.Message, StringComparison.Ordinal);
            Assert.Equal(0, await CountAsync("[fixture].[Widgets] WHERE [Subject] = 'late-1'"));
            Assert.Equal(0, await CountAsync("[fixture].[Shipments] WHERE [Payload] = N'late-1'"));
            Assert.DoesNotContain(fixture.Probe.Shipments, static line => line.StartsWith("committed", StringComparison.Ordinal));
        }

        // --- Errors and concurrency ---

        [Fact]
        public async Task A_shipments_validation_error_comes_back_on_the_widgets_path_naming_the_shipment_stack_and_nothing_commits()
        {
            fixture.Probe.Reset();
            (_, string subject) = await fixture.SeedCallerAsync((Widgets, AccessActions.Read, null), (Widgets, AccessActions.Save, null));
            fixture.Probe.ValidateWidgets = async (service, context) => _ = await service.EnlistSaveAsync(
                context.Host,
                [new Shipment { Payload = "accepted-1" }, new Shipment { Payload = EnlistedShipmentService.Refused }],
                new EnlistSaveOptions { MapPath = Under(ValidationPath.Root.Index(0).Property("Shipments")) });

            await using AsyncServiceScope scope = await fixture.ScopeAsync(subject);
            ValidationException refused = await Assert.ThrowsAsync<ValidationException>(() => Service(scope).SaveAsync([NewWidget("errors-1")], IdsOnly, Token));

            Assert.Equal([$"[0].Shipments[1].Payload {EnlistedShipmentService.RefusedCode} {Shipments}"], Lines(refused));
            Assert.Equal(0, await CountAsync("[fixture].[Widgets] WHERE [Code] = 'errors-1'"));
            Assert.Equal(0, await CountAsync("[fixture].[Shipments] WHERE [Payload] = N'accepted-1'"));
        }

        [Fact]
        public async Task A_reference_an_enlisted_row_breaks_is_translated_by_its_group_onto_the_widgets_path_naming_the_shipment_stack()
        {
            fixture.Probe.Reset();
            (_, string subject) = await fixture.SeedCallerAsync((Widgets, AccessActions.Read, null), (Widgets, AccessActions.Save, null));

            // --- The writer supplies the server-owned job reference, to a job that does not exist ---
            fixture.Probe.ValidateWidgets = async (service, context) => _ = await service.EnlistSaveAsync(
                context.Host,
                [new Shipment { Payload = "orphan-1", JobId = EnlistmentDatabase.MissingJob }],
                new EnlistSaveOptions
                {
                    ServerOwned = new HashSet<string>(StringComparer.Ordinal) { nameof(Shipment.JobId) },
                    MapPath = Under(ValidationPath.Root.Index(0).Property("Shipments")),
                });

            await using AsyncServiceScope scope = await fixture.ScopeAsync(subject);
            ValidationException refused = await Assert.ThrowsAsync<ValidationException>(() => Service(scope).SaveAsync([NewWidget("orphan-1")], IdsOnly, Token));

            // --- The shipment's own translation, at the widget's path, naming the shipment stack ---
            Assert.Equal([$"[0].Shipments[0].JobId {ValidationCodes.FkNotFound} {Shipments}"], Lines(refused));
            Assert.Equal(0, await CountAsync("[fixture].[Widgets] WHERE [Subject] = 'orphan-1'"));
            Assert.Equal(0, await CountAsync("[fixture].[Shipments] WHERE [Payload] = N'orphan-1'"));
        }

        [Fact]
        public async Task An_enlisted_update_under_Check_refuses_a_default_stamp_at_the_mapped_path_and_commits_with_the_stamp_it_loaded()
        {
            fixture.Probe.Reset();
            (_, string subject) = await fixture.SeedCallerAsync((Widgets, AccessActions.Read, null), (Widgets, AccessActions.Save, null));
            Shipment update = new() { Id = EnlistmentDatabase.StoredShipment, Payload = "checked" };
            fixture.Probe.ValidateWidgets = async (service, context) => _ = await service.EnlistSaveAsync(
                context.Host,
                [update],
                new EnlistSaveOptions { Concurrency = ConcurrencyMode.Check, MapPath = Under(ValidationPath.Root.Index(0).Property("Shipment")) });

            // --- No stamp: refused before the persist ---
            await using (AsyncServiceScope scope = await fixture.ScopeAsync(subject))
            {
                ValidationException refused = await Assert.ThrowsAsync<ValidationException>(() => Service(scope).SaveAsync([NewWidget("check-1")], IdsOnly, Token));

                Assert.Equal([$"[0].Shipment[0].ModifiedAt {ValidationCodes.ConcurrencyStampRequired} {Shipments}"], Lines(refused));
                Assert.Equal(0, await CountAsync("[fixture].[Widgets] WHERE [Code] = 'check-1'"));
            }

            // --- The stamp as stored: the update commits with the widget ---
            update.ModifiedAt = await ScalarAsync<DateTimeOffset>(Invariant($"SELECT [ModifiedAt] FROM [fixture].[Shipments] WHERE [Id] = {EnlistmentDatabase.StoredShipment};"));
            await using (AsyncServiceScope scope = await fixture.ScopeAsync(subject))
            {
                _ = await Service(scope).SaveAsync([NewWidget("check-2")], IdsOnly, Token);
            }

            Assert.Equal("checked", await ScalarAsync<string>(Invariant($"SELECT [Payload] FROM [fixture].[Shipments] WHERE [Id] = {EnlistmentDatabase.StoredShipment};")));
        }

        // --- Bypass resistance ---

        [Fact]
        public async Task A_statement_a_caller_or_another_stacks_effect_appends_that_writes_the_shipments_table_is_refused_at_append()
        {
            fixture.Probe.Reset();
            (_, string subject) = await fixture.SeedCallerAsync((Widgets, AccessActions.Read, null), (Widgets, AccessActions.Save, null));

            // --- The caller's channel ---
            await using (AsyncServiceScope scope = await fixture.ScopeAsync(subject))
            {
                SaveOptions options = new() { ReturnEntities = false, OnPersist = WriteShipments };
                InvalidOperationException refused = await Assert.ThrowsAsync<InvalidOperationException>(() => Service(scope).SaveAsync([NewWidget("bypass-1")], options, Token));

                Assert.Contains($"which the stack '{Shipments}' owns", refused.Message, StringComparison.Ordinal);
            }

            // --- The widget stack's own persist hook, which holds the widgets' authority alone ---
            fixture.Probe.ContributeWidgets = static context => WriteShipments(context.Batch);
            await using (AsyncServiceScope scope = await fixture.ScopeAsync(subject))
            {
                InvalidOperationException refused = await Assert.ThrowsAsync<InvalidOperationException>(() => Service(scope).SaveAsync([NewWidget("bypass-2")], IdsOnly, Token));

                Assert.Contains($"which the stack '{Shipments}' owns", refused.Message, StringComparison.Ordinal);
            }

            // --- Nothing committed ---
            Assert.Equal(0, await CountAsync("[fixture].[Widgets] WHERE [Code] IN ('bypass-1', 'bypass-2')"));
            Assert.Equal(0, await CountAsync("[fixture].[Shipments] WHERE [Payload] = N'bypassed'"));
        }

        // --- The API action ---

        [Fact]
        public async Task An_api_action_that_loads_a_shipment_and_enlists_it_with_its_own_frame_under_Check_commits_on_three_round_trips()
        {
            fixture.Probe.Reset();
            (_, string subject) = await fixture.SeedCallerAsync((Shipments, AccessActions.Read, null), (Shipments, AccessActions.Save, null));
            await WarmAsync<Shipment, long>(subject);
            const long id = EnlistmentDatabase.RestampedShipment;

            // --- The load, the validation round with the row's before image, and the persist ---
            (object? restamped, int trips) = await CountedAsync(subject, scope => Invoker(scope).InvokeAsync(Restamp(scope), new ShipmentRestamp { Id = id, Payload = " restamped " }, Token));

            Assert.Equal(3, trips);
            Shipment shipment = Assert.IsType<Shipment>(restamped);
            Assert.Equal((id, "restamped"), (shipment.Id, shipment.Payload));
            Assert.Equal("restamped", await ScalarAsync<string>(Invariant($"SELECT [Payload] FROM [fixture].[Shipments] WHERE [Id] = {id};")));
            Assert.Equal(["committed restamped"], fixture.Probe.Shipments.Where(static line => line.StartsWith("committed", StringComparison.Ordinal)));
        }

        [Fact]
        public async Task An_api_action_invoked_for_a_caller_without_its_securable_is_refused_and_writes_nothing()
        {
            fixture.Probe.Reset();
            (_, string subject) = await fixture.SeedCallerAsync((Shipments, AccessActions.Read, null));
            const long id = EnlistmentDatabase.ForbiddenShipment;

            await using AsyncServiceScope scope = await fixture.ScopeAsync(subject);
            ForbiddenException refused = await Assert.ThrowsAsync<ForbiddenException>(
                () => Invoker(scope).InvokeAsync(Restamp(scope), new ShipmentRestamp { Id = id, Payload = "forbidden" }, Token));

            Assert.Equal((Shipments, AccessActions.Save), (refused.Resource, refused.Action));
            Assert.Empty(fixture.Probe.Shipments);
            Assert.Equal("stored", await ScalarAsync<string>(Invariant($"SELECT [Payload] FROM [fixture].[Shipments] WHERE [Id] = {id};")));
        }

        // --- Helpers ---

        /// <summary>Appends a write to the shipments' table on the caller's channel, which the shipment stack owns.</summary>
        /// <param name="batch">The persist batch.</param>
        private static void WriteShipments(IDataBatch batch)
        {
            // A raw write to a stack's table from outside it, deliberately: the batch refuses it at append.
#pragma warning disable TELLMA0006
            _ = batch.Sql(
                $"UPDATE [fixture].[Shipments] SET [Payload] = N'bypassed' WHERE [Id] = {EnlistmentDatabase.StoredShipment};",
                new SqlOptions(writes: ShipmentsTable));
#pragma warning restore TELLMA0006
        }

        /// <summary>Runs a call in a scope of the caller and counts the round trips it took.</summary>
        /// <typeparam name="T">What the call answers.</typeparam>
        /// <param name="subject">The caller's subject.</param>
        /// <param name="call">The call.</param>
        /// <returns>What it answered, and its round trips.</returns>
        private async Task<(T Result, int Trips)> CountedAsync<T>(string subject, Func<AsyncServiceScope, Task<T>> call)
        {
            await using AsyncServiceScope scope = await fixture.ScopeAsync(subject);
            DataAccessScope access = scope.ServiceProvider.GetRequiredService<DataAccessScope>();
            int before = access.RoundTrips;
            T result = await call(scope);
            return (result, access.RoundTrips - before);
        }

        /// <summary>Reads rows of a stack the caller may read once, which warms the caller's connection and permissions.</summary>
        /// <typeparam name="TEntity">The stack's entity.</typeparam>
        /// <typeparam name="TKey">The stack's key.</typeparam>
        /// <param name="subject">The caller's subject.</param>
        /// <returns>A task that completes when the read ran.</returns>
        private async Task WarmAsync<TEntity, TKey>(string subject)
            where TEntity : Entity<TKey>
            where TKey : struct, IBinaryInteger<TKey>
        {
            await using AsyncServiceScope scope = await fixture.ScopeAsync(subject);
            _ = await scope.ServiceProvider.GetRequiredService<EntityService<TEntity, TKey>>().QueryAsync(new QueryRequest { Select = "Id" }, Token);
        }

        /// <summary>How many rows of a table match a condition.</summary>
        /// <param name="where">The table and its condition: <c>[schema].[Table] WHERE …</c>.</param>
        /// <returns>The count.</returns>
        private Task<int> CountAsync(string where)
        {
            return ScalarAsync<int>("SELECT COUNT(*) FROM " + where + ";");
        }

        /// <summary>Reads one value from the first tenant's database.</summary>
        /// <typeparam name="T">The value's type.</typeparam>
        /// <param name="query">The query.</param>
        /// <returns>The value.</returns>
        private Task<T?> ScalarAsync<T>(string query)
        {
            return fixture.Connect.ScalarAsync<T>(ConnectDatabase.TenantA, query);
        }

        /// <summary>A new widget, coded, named and subjected after a code.</summary>
        /// <param name="code">The code.</param>
        /// <returns>The widget.</returns>
        private static Widget NewWidget(string code)
        {
            return new Widget { Code = code, Kind = WidgetKind.Plain, Name = code, Subject = code };
        }

        /// <summary>A path map that prefixes every path of the enlisted payload.</summary>
        /// <param name="prefix">The widget's path the enlisted payload sits under.</param>
        /// <returns>The map.</returns>
        private static Func<ValidationPath, ValidationPath> Under(ValidationPath prefix)
        {
            return path => new ValidationPath([.. prefix.Segments, .. path.Segments]);
        }

        /// <summary>A refusal's errors, each as its path, its code and the stack it names; <c>-</c> for none.</summary>
        /// <param name="refused">The refusal.</param>
        /// <returns>The lines.</returns>
        private static string[] Lines(ValidationException refused)
        {
            return [.. refused.Errors.Select(static error => $"{error.Path} {error.Code} {error.Arguments.GetValueOrDefault("enlistedResource") ?? "-"}")];
        }

        /// <summary>The stage a probe line records, its first word.</summary>
        /// <param name="line">The line.</param>
        /// <returns>The stage's place in the pipeline.</returns>
        private static int Stage(string line)
        {
            return Array.IndexOf(["preprocess", "validate", "contribute", "committed"], line[..line.IndexOf(' ', StringComparison.Ordinal)]);
        }

        /// <summary>Formats text in the invariant culture.</summary>
        /// <param name="text">The text.</param>
        /// <returns>The formatted text.</returns>
        private static string Invariant(FormattableString text)
        {
            return text.ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>A scope's widget service.</summary>
        /// <param name="scope">The scope.</param>
        /// <returns>The service.</returns>
        private static EntityService<Widget, int> Service(AsyncServiceScope scope)
        {
            return scope.ServiceProvider.GetRequiredService<EntityService<Widget, int>>();
        }

        /// <summary>A scope's API action invoker.</summary>
        /// <param name="scope">The scope.</param>
        /// <returns>The invoker.</returns>
        private static IApiActionInvoker Invoker(AsyncServiceScope scope)
        {
            return scope.ServiceProvider.GetRequiredService<IApiActionInvoker>();
        }

        /// <summary>The registry's own descriptor of the shipment's restamp action.</summary>
        /// <param name="scope">The scope.</param>
        /// <returns>The descriptor.</returns>
        private static ApiActionDescriptor Restamp(AsyncServiceScope scope)
        {
            StackDescriptor shipments = scope.ServiceProvider.GetRequiredService<IStackRegistry>().Find(Shipments)!;
            return shipments.Actions.OfType<ApiActionDescriptor>().Single(static action => action.Name == EnlistedShipmentService.Restamp);
        }
    }
}
