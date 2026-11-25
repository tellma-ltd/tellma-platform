// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.Metrics.Testing;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Caching;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.AspNetCore.IntegrationTests.Infrastructure;
using Tellma.Core.AspNetCore.IntegrationTests.Stacks;

namespace Tellma.Core.AspNetCore.IntegrationTests.Serial
{
    /// <summary>
    ///     The round trips each projected center, user and role operation and each access and
    ///     notification-preference operation takes, as the data layer records them per operation on
    ///     <c>tellma.data.roundtrips</c>, held to the platform's round-trip budget with warm caches: an
    ///     operation above its budget fails the suite. Each measured call follows a read by the same
    ///     caller, so a cold connection or a tag a write moved is paid by the read, as the budget's
    ///     warm figures assume. Every request the case sends, the set-up ones among them, waits for
    ///     the measurement carrying its own operation tag, so no request's measurement is ever left
    ///     behind to be taken for a later one's. Every entity action shares one tag, so those are told
    ///     apart by the route; the case runs alone, so no other caller's unit of work lands among the
    ///     measurements. An operation above its budget is reported with what was recorded while it
    ///     ran that costs a round trip on a warm path: a cache lookup that missed, a connection that
    ///     was not warm, and a round trip re-run after a failure.
    /// </summary>
    /// <param name="deployment">The suite's deployment.</param>
    [Trait("Category", "Integration")]
    public sealed class RoundTripBudgetTests(AcmeDeployment deployment)
    {
        private const int Tenant = AcmeDeployment.LiveTenant;

        /// <summary>How long a measurement, recorded when the request's scope ends after its answer, may take to arrive.</summary>
        private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

        [Fact]
        public async Task Every_center_user_role_access_and_notification_preference_operation_stays_within_its_round_trip_budget()
        {
            // The session is opened before the collector starts, so its sign-in's units of work are never among the measurements.
            using SpaClient admin = await deployment.SteppedUpAsync(deployment.Administrator);
            IMeterFactory meters = deployment.Host.Services.GetRequiredService<IMeterFactory>();
            using MetricCollector<int> trips = new(meters, DataTelemetryNames.MeterName, DataTelemetryNames.RoundTrips);

            // The instruments that name why a warm operation took more round trips than its budget.
            using MetricCollector<long> lookups = new(meters, CacheTelemetryNames.MeterName, CacheTelemetryNames.Requests);
            using MetricCollector<long> connects = new(meters, AccessTelemetryNames.MeterName, AccessTelemetryNames.ConnectResults);
            using MetricCollector<long> retries = new(meters, DataTelemetryNames.MeterName, DataTelemetryNames.Retries);
            Budget budget = new(trips, new Causes(lookups, connects, retries), admin);

            // --- Centers. A save names its parent and carries its natural key, so even a create
            // validates in a round of its own; a delete runs the stack's own delete validation ---
            JsonNode roots = await budget.SetUpAsync(
                "centers/get-by-parent-ids", new JsonObject { ["parentIds"] = new JsonArray(), ["includeRoots"] = true }, "gl.Center:get_by_parent_ids");
            int root = Assert.Single(Wire.Rows(roots))[0]!.GetValue<int>();
            _ = await budget.SetUpAsync("centers/save", Wire.Save(CenterOperationTests.Center(0, "RT-WARM", "Warms the id buffer", "Sale", root)), "gl.Center:save");
            JsonNode created = await budget.MeasureAsync("centers/save", Wire.Save(CenterOperationTests.Center(-1, "RT-C", "Budget", "Sale", root)), "gl.Center:save", 2);
            int center = Wire.IdsOf(created)[0];
            JsonObject details = Wire.Entities(created)[0];
            details["name"] = "Budget, edited";
            _ = await budget.MeasureAsync("centers/save", Wire.Save(details), "gl.Center:save", 2);
            _ = await budget.MeasureAsync("centers/query", Wire.Query("Code,Parent.Name", "Code = 'RT-C'"), "gl.Center:query", 1);
            _ = await budget.MeasureAsync(
                "centers/get-by-parent-ids", new JsonObject { ["parentIds"] = Wire.Array(root), ["includeRoots"] = true }, "gl.Center:get_by_parent_ids", 1);
            _ = await budget.MeasureAsync("centers/get", new JsonObject { ["id"] = center }, "gl.Center:get", 1);
            _ = await budget.MeasureAsync("centers/get-by-ids", Wire.Ids(center), "gl.Center:get_by_ids", 1);
            _ = await budget.MeasureAsync("centers/deactivate", Wire.Ids(center), "gl.Center:action", 1);
            _ = await budget.MeasureAsync("centers/activate", Wire.Ids(center), "gl.Center:action", 1);
            _ = await budget.MeasureAsync("centers/delete", Wire.Ids(center), "gl.Center:delete", 2);
            JsonNode subtree = await budget.SetUpAsync("centers/save", Wire.Save(CenterOperationTests.Center(0, "RT-SUB", "Budget subtree", "Sale", root)), "gl.Center:save");
            _ = await budget.MeasureAsync("centers/delete-with-descendants", Wire.Ids(Wire.IdsOf(subtree)[0]), "gl.Center:delete_with_descendants", 2);

            // --- Roles. Every write loads the before images and the role access rules' loads first ---
            JsonNode role = await budget.MeasureAsync("roles/save", Wire.Save(new JsonObject
            {
                ["id"] = 0,
                ["name"] = "Budget role",
                ["permissions"] = new JsonArray(new JsonObject { ["id"] = 0, ["resource"] = "gl.Center", ["action"] = "Read" }),
            }), "core.Role:save", 2);
            int roleId = Wire.IdsOf(role)[0];
            string byRole = string.Create(CultureInfo.InvariantCulture, $"RoleId = {roleId}");
            JsonObject roleDetails = Wire.Entities(role)[0];
            roleDetails["name"] = "Budget role, edited";
            _ = await budget.MeasureAsync("roles/save", Wire.Save(roleDetails), "core.Role:save", 2);
            _ = await budget.MeasureAsync("roles/query", Wire.Query("Name", "Name = 'Budget role, edited'"), "core.Role:query", 1);
            _ = await budget.MeasureAsync("roles/get", new JsonObject { ["id"] = roleId }, "core.Role:get", 1);
            _ = await budget.MeasureAsync("roles/get-by-ids", Wire.Ids(roleId), "core.Role:get_by_ids", 1);
            _ = await budget.MeasureAsync("permissions/query", Wire.Query("Action", byRole), "core.Permission:query", 1);
            _ = await budget.MeasureAsync("role-memberships/query", Wire.Query("UserId", byRole), "core.RoleMembership:query", 1);
            _ = await budget.MeasureAsync("roles/deactivate", Wire.Ids(roleId), "core.Role:action", 2);
            _ = await budget.MeasureAsync("roles/activate", Wire.Ids(roleId), "core.Role:action", 2);
            _ = await budget.MeasureAsync("roles/delete", Wire.Ids(roleId), "core.Role:delete", 2);

            // --- Users. Every write loads the user access rules' loads first; an invite or an issue calls the identity engine between its two ---
            JsonNode user = await budget.MeasureAsync(
                "users/save", Wire.Save(new JsonObject { ["id"] = 0, ["email"] = "budget@example.com", ["name"] = "Budget User" }), "core.User:save", 2);
            int userId = Wire.IdsOf(user)[0];
            JsonObject userDetails = Wire.Entities(user)[0];
            userDetails["name"] = "Budget User, edited";
            _ = await budget.MeasureAsync("users/save", Wire.Save(userDetails), "core.User:save", 2);
            _ = await budget.MeasureAsync("users/query", Wire.Query("Name", "Email = 'budget@example.com'"), "core.User:query", 1);
            _ = await budget.MeasureAsync("users/get", new JsonObject { ["id"] = userId }, "core.User:get", 1);
            _ = await budget.MeasureAsync("users/get-by-ids", Wire.Ids(userId), "core.User:get_by_ids", 1);
            _ = await budget.MeasureAsync("users/invite", Wire.Ids(userId), "core.User:action", 2);
            _ = await budget.MeasureAsync("users/invitation-status", Wire.Ids(userId), "core.User:invitation-status", 1);
            _ = await budget.MeasureAsync("users/preferences/get", new JsonObject { ["userId"] = userId }, "core.User:preferences/get", 1);
            _ = await budget.MeasureAsync(
                "users/preferences/set", new JsonObject { ["id"] = userId, ["arguments"] = new JsonObject { ["items"] = new JsonArray(new JsonObject { ["key"] = "k", ["value"] = "v" }) } },
                "core.User:action", 2);
            _ = await budget.MeasureAsync(
                "users/preferences/delete", new JsonObject { ["id"] = userId, ["arguments"] = new JsonObject { ["keys"] = new JsonArray("k") } }, "core.User:action", 2);
            _ = await budget.MeasureAsync("users/deactivate", Wire.Ids(userId), "core.User:action", 2);
            _ = await budget.MeasureAsync("users/activate", Wire.Ids(userId), "core.User:action", 2);
            JsonNode doomed = await budget.SetUpAsync(
                "users/save", Wire.Save(new JsonObject { ["id"] = 0, ["email"] = "budget-delete@example.com", ["name"] = "Budget Delete" }), "core.User:save");
            _ = await budget.MeasureAsync("users/delete", Wire.Ids(Wire.IdsOf(doomed)[0]), "core.User:delete", 2);
            JsonNode account = await budget.SetUpAsync("users/save", Wire.Save(new JsonObject { ["id"] = 0, ["kind"] = "Service", ["name"] = "Budget Account" }), "core.User:save");
            _ = await budget.MeasureAsync("users/issue-credentials", new JsonObject { ["id"] = Wire.IdsOf(account)[0] }, "core.User:action", 2);

            // --- The caller's own routes ---
            JsonNode me = await budget.MeasureAsync("users/me", null, "core.User:me", 1);
            _ = await budget.MeasureAsync(
                "users/me/save", new JsonObject { ["name"] = "admin", ["modifiedAt"] = me["user"]!["modifiedAt"]!.DeepClone() }, "core.User:me/save", 3);
            _ = await budget.MeasureAsync("users/me/locale", new JsonObject { ["timeZone"] = "Africa/Addis_Ababa" }, "core.User:me/locale", 1);
            _ = await budget.MeasureAsync(
                "users/me/preferences/set", new JsonObject { ["items"] = new JsonArray(new JsonObject { ["key"] = "budget", ["value"] = "1" }) }, "core.User:me/preferences/set", 1);
            _ = await budget.MeasureAsync("users/me/preferences/delete", new JsonObject { ["keys"] = new JsonArray("budget") }, "core.User:me/preferences/delete", 1);

            // --- Access. The caller's own check is decided from the permissions the warming read
            // cached; another user's loads that user's permissions after the visibility read ---
            JsonArray securables = new(new JsonObject { ["resource"] = "gl.Center", ["action"] = "Read" });
            _ = await budget.MeasureAsync("access/check", new JsonObject { ["securables"] = securables.DeepClone() }, "access:check", 0);
            _ = await budget.MeasureAsync(
                "access/check", new JsonObject { ["userId"] = deployment.Sales.UserId, ["securables"] = securables.DeepClone() }, "access:check", 2);
            _ = await budget.MeasureAsync("access/holders", new JsonObject { ["resource"] = "gl.Center", ["action"] = "Read" }, "access:holders", 1);

            // --- The caller's notification preferences: a save writes the caller's deviations from
            // each channel's default in one persist however many rows it touches, and a read answers
            // the stored ones. The first save inserts two; the second keeps one, deletes the other and
            // inserts a third. A stored deviation can only flip back to its default, which drops it, so
            // a save never runs the statement's update on a reference composition ---
            JsonArray first = Preferences(("core.user.added", "email", true), ("core.user.added", "inbox", false));
            JsonArray second = Preferences(("core.job.failed", "email", true), ("core.user.added", "email", true));
            _ = await budget.MeasureAsync("notification-preferences/save", first, "notification-preferences:save", 2);
            JsonNode stored = await budget.MeasureAsync("notification-preferences/get", null, "notification-preferences:get", 1);
            Assert.True(JsonNode.DeepEquals(first, stored), $"The stored preferences differ from the saved ones: {stored.ToJsonString()}");
            _ = await budget.MeasureAsync("notification-preferences/save", second, "notification-preferences:save", 2);
            stored = await budget.SetUpAsync("notification-preferences/get", null, "notification-preferences:get");
            Assert.True(JsonNode.DeepEquals(second, stored), $"The stored preferences differ from the saved ones: {stored.ToJsonString()}");

            // The defaults again, so no other case's notification reaches the administrator's email.
            _ = await budget.SetUpAsync("notification-preferences/save", new JsonArray(), "notification-preferences:save");

            // Every measurement goes to the test's output, so a run shows how close each operation is to its budget.
            TestContext.Current.TestOutputHelper?.WriteLine(budget.All.ToString());
            Assert.True(budget.Over.Length == 0, "Operations above their round-trip budget:\n" + budget.Over + "\nAll measured:\n" + budget.All);
        }

        /// <summary>A set of notification preferences as the wire carries it, in the order the server answers them.</summary>
        /// <param name="rows">Each preference's type, channel and whether it is enabled.</param>
        /// <returns>The preferences.</returns>
        private static JsonArray Preferences(params (string Type, string Channel, bool Enabled)[] rows)
        {
            return [.. rows.Select(static row => (JsonNode)new JsonObject { ["type"] = row.Type, ["channel"] = row.Channel, ["enabled"] = row.Enabled })];
        }

        /// <summary>
        ///     The collectors of what costs a warm operation a round trip of its own: the cache lookups,
        ///     the connect results and the re-runs.
        /// </summary>
        /// <param name="Lookups">The cache lookups, by kind and outcome.</param>
        /// <param name="Connects">The connect results.</param>
        /// <param name="Retries">The round trips re-run after a failure.</param>
        private sealed record Causes(MetricCollector<long> Lookups, MetricCollector<long> Connects, MetricCollector<long> Retries)
        {
            /// <summary>The outcome of a cache lookup that a stored entry answered.</summary>
            private const string Hit = "hit";

            /// <summary>Forgets everything recorded until now.</summary>
            public void Clear()
            {
                Lookups.Clear();
                Connects.Clear();
                Retries.Clear();
            }

            /// <summary>
            ///     What was recorded since the last <see cref="Clear" /> that costs a round trip on a
            ///     warm path: every lookup but a hit, every connection but a warm one, and every re-run.
            /// </summary>
            /// <returns>The measurements, comma-separated; empty when none was recorded.</returns>
            public string Describe()
            {
                IEnumerable<string> lookups = Lookups.GetMeasurementSnapshot()
                    .Where(static measured => !string.Equals(measured.Tags.GetValueOrDefault(CacheTelemetryNames.OutcomeTag) as string, Hit, StringComparison.Ordinal))
                    .Select(static measured => $"{measured.Tags.GetValueOrDefault(CacheTelemetryNames.KindTag)} lookup {measured.Tags.GetValueOrDefault(CacheTelemetryNames.OutcomeTag)}");
                IEnumerable<string> connects = Connects.GetMeasurementSnapshot()
                    .Where(static measured => !string.Equals(measured.Tags.GetValueOrDefault(AccessTelemetryNames.ResultTag) as string, AccessTelemetryNames.ResultWarm, StringComparison.Ordinal))
                    .Select(static measured => $"connect {measured.Tags.GetValueOrDefault(AccessTelemetryNames.ResultTag)}");
                IEnumerable<string> retries = Retries.GetMeasurementSnapshot()
                    .Select(static measured => $"re-run {measured.Tags.GetValueOrDefault(DataTelemetryNames.ClassTag)} {measured.Tags.GetValueOrDefault(DataTelemetryNames.ErrorTag)}");
                return string.Join(", ", lookups.Concat(connects).Concat(retries));
            }
        }

        /// <summary>Measures operations one at a time and keeps those above their budget.</summary>
        /// <param name="trips">The round-trip instrument's collector.</param>
        /// <param name="causes">The collectors of what costs a warm operation a round trip of its own.</param>
        /// <param name="client">The caller.</param>
        private sealed class Budget(MetricCollector<int> trips, Causes causes, SpaClient client)
        {
            /// <summary>The warming read's route.</summary>
            private const string WarmingRoute = "users/me";

            /// <summary>The warming read's operation tag.</summary>
            private const string WarmingOperation = "core.User:me";

            /// <summary>The operations above their budget, one per line.</summary>
            public StringBuilder Over { get; } = new();

            /// <summary>Every operation measured, one per line.</summary>
            public StringBuilder All { get; } = new();

            /// <summary>
            ///     Sends a request the measurements need but do not measure, and waits for its own
            ///     measurement, so it can never be taken for a later request's.
            /// </summary>
            /// <param name="route">The route.</param>
            /// <param name="body">The body.</param>
            /// <param name="operation">The operation tag the request's unit of work carries.</param>
            /// <returns>The request's answer.</returns>
            public async Task<JsonNode> SetUpAsync(string route, JsonNode? body, string operation)
            {
                JsonNode answer = (await client.PostAsync(Tenant, route, body)).Expect(HttpStatusCode.OK);
                _ = await AwaitAsync(operation, route);
                return answer;
            }

            /// <summary>
            ///     Sends a read that absorbs whatever a cold cache or a moved tag costs, then the
            ///     operation, and compares the operation's round trips with its budget.
            /// </summary>
            /// <param name="route">The route, which names the operation in the report.</param>
            /// <param name="body">The body.</param>
            /// <param name="operation">
            ///     The operation tag the operation's unit of work carries, matched exactly. Every entity
            ///     action shares one tag, so the route rather than the tag tells those apart.
            /// </param>
            /// <param name="allowed">The operation's budget.</param>
            /// <returns>The operation's answer.</returns>
            public async Task<JsonNode> MeasureAsync(string route, JsonNode? body, string operation, int allowed)
            {
                // --- The warming read, measured and set aside with what it recorded ---
                _ = await SetUpAsync(WarmingRoute, null, WarmingOperation);
                causes.Clear();

                // --- The operation; one above its budget names what was recorded while it ran ---
                JsonNode answer = (await client.PostAsync(Tenant, route, body)).Expect(HttpStatusCode.OK);
                int taken = await AwaitAsync(operation, route);
                _ = All.Append(CultureInfo.InvariantCulture, $"{route} ({operation}): {taken} (budget {allowed})").Append('\n');
                if (taken > allowed)
                {
                    string recorded = causes.Describe();
                    _ = Over.Append(
                        CultureInfo.InvariantCulture,
                        $"{route} ({operation}): {taken} round trips, budget {allowed}; recorded meanwhile: {(recorded.Length == 0 ? "nothing" : recorded)}").Append('\n');
                }

                return answer;
            }

            /// <summary>
            ///     Waits for the measurement of the unit of work tagged exactly with an operation,
            ///     clearing everything recorded until then. Units of work outside any request, a
            ///     job's, carry other tags and are passed over.
            /// </summary>
            /// <param name="operation">The operation tag.</param>
            /// <param name="route">The route sent, which names the request in a failure.</param>
            /// <returns>The unit of work's round trips; zero for one that took none.</returns>
            private async Task<int> AwaitAsync(string operation, string route)
            {
                var elapsed = Stopwatch.StartNew();
                List<CollectedMeasurement<int>> seen = [];
                while (true)
                {
                    seen.AddRange(trips.GetMeasurementSnapshot(clear: true));
                    foreach (CollectedMeasurement<int> measured in seen)
                    {
                        if (measured.Tags.GetValueOrDefault(DataTelemetryNames.OperationTag) is string tag && string.Equals(tag, operation, StringComparison.Ordinal))
                        {
                            return measured.Value;
                        }
                    }

                    Assert.True(
                        elapsed.Elapsed < Patience,
                        $"No round trips recorded as {operation} for {route}; recorded: {string.Join(", ", seen.Select(static m => $"{m.Tags.GetValueOrDefault(DataTelemetryNames.OperationTag)}={m.Value}"))}");
                    await Task.Delay(TimeSpan.FromMilliseconds(25), TestContext.Current.CancellationToken);
                }
            }
        }
    }
}
