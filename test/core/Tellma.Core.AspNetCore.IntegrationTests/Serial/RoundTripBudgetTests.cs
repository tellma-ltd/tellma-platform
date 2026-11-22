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
using Tellma.Core.Abstractions.Data;
using Tellma.Core.AspNetCore.IntegrationTests.Infrastructure;
using Tellma.Core.AspNetCore.IntegrationTests.Stacks;

namespace Tellma.Core.AspNetCore.IntegrationTests.Serial
{
    /// <summary>
    ///     The round trips each projected center, user and role operation takes, as the data layer
    ///     records them per operation on <c>tellma.data.roundtrips</c>, held to the platform's
    ///     round-trip budget with warm caches: an operation above its budget fails the suite. Each
    ///     measured call follows a read by the same caller, so a cold connection or a tag a write
    ///     moved is paid by the read, as the budget's warm figures assume. Each unit of work is tagged
    ///     with its pipeline operation's kind, which every entity action shares, so the measured
    ///     operations are told apart by the route; the case runs alone, so no other caller's unit of
    ///     work lands among the measurements.
    /// </summary>
    /// <param name="deployment">The suite's deployment.</param>
    [Trait("Category", "Integration")]
    public sealed class RoundTripBudgetTests(AcmeDeployment deployment)
    {
        private const int Tenant = AcmeDeployment.LiveTenant;

        /// <summary>How long a measurement, recorded when the request's scope ends after its answer, may take to arrive.</summary>
        private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

        [Fact]
        public async Task Every_center_user_and_role_operation_stays_within_its_round_trip_budget()
        {
            using MetricCollector<int> trips = new(
                deployment.Host.Services.GetRequiredService<IMeterFactory>(), DataTelemetryNames.MeterName, DataTelemetryNames.RoundTrips);
            using SpaClient admin = await deployment.SteppedUpAsync(deployment.Administrator);
            Budget budget = new(trips, admin);

            // --- Centers. A save names its parent and carries its natural key, so even a create
            // validates in a round of its own; a delete runs the stack's own delete validation ---
            int root = await CenterOperationTests.RootAsync(admin);
            _ = await CenterOperationTests.CreateAsync(admin, "RT-WARM", "Warms the id buffer", root);
            JsonNode created = await budget.MeasureAsync("centers/save", Wire.Save(CenterOperationTests.Center(-1, "RT-C", "Budget", "Sale", root)), "gl.Center", 2);
            int center = Wire.IdsOf(created)[0];
            JsonObject details = Wire.Entities(created)[0];
            details["name"] = "Budget, edited";
            _ = await budget.MeasureAsync("centers/save", Wire.Save(details), "gl.Center", 2);
            _ = await budget.MeasureAsync("centers/query", Wire.Query("Code,Parent.Name", "Code = 'RT-C'"), "gl.Center", 1);
            _ = await budget.MeasureAsync("centers/get-by-parent-ids", new JsonObject { ["parentIds"] = Wire.Array(root), ["includeRoots"] = true }, "gl.Center", 1);
            _ = await budget.MeasureAsync("centers/get", new JsonObject { ["id"] = center }, "gl.Center", 1);
            _ = await budget.MeasureAsync("centers/get-by-ids", Wire.Ids(center), "gl.Center", 1);
            _ = await budget.MeasureAsync("centers/deactivate", Wire.Ids(center), "gl.Center", 1);
            _ = await budget.MeasureAsync("centers/activate", Wire.Ids(center), "gl.Center", 1);
            _ = await budget.MeasureAsync("centers/delete", Wire.Ids(center), "gl.Center", 2);
            int subtree = await CenterOperationTests.CreateAsync(admin, "RT-SUB", "Budget subtree", root);
            _ = await budget.MeasureAsync("centers/delete-with-descendants", Wire.Ids(subtree), "gl.Center", 2);

            // --- Roles. Every write loads the before images and the role access rules' loads first ---
            JsonNode role = await budget.MeasureAsync("roles/save", Wire.Save(new JsonObject
            {
                ["id"] = 0,
                ["name"] = "Budget role",
                ["permissions"] = new JsonArray(new JsonObject { ["id"] = 0, ["resource"] = "gl.Center", ["action"] = "Read" }),
            }), "core.Role", 2);
            int roleId = Wire.IdsOf(role)[0];
            string byRole = string.Create(CultureInfo.InvariantCulture, $"RoleId = {roleId}");
            JsonObject roleDetails = Wire.Entities(role)[0];
            roleDetails["name"] = "Budget role, edited";
            _ = await budget.MeasureAsync("roles/save", Wire.Save(roleDetails), "core.Role", 2);
            _ = await budget.MeasureAsync("roles/query", Wire.Query("Name", "Name = 'Budget role, edited'"), "core.Role", 1);
            _ = await budget.MeasureAsync("roles/get", new JsonObject { ["id"] = roleId }, "core.Role", 1);
            _ = await budget.MeasureAsync("roles/get-by-ids", Wire.Ids(roleId), "core.Role", 1);
            _ = await budget.MeasureAsync("permissions/query", Wire.Query("Action", byRole), "core.Permission", 1);
            _ = await budget.MeasureAsync("role-memberships/query", Wire.Query("UserId", byRole), "core.RoleMembership", 1);
            _ = await budget.MeasureAsync("roles/deactivate", Wire.Ids(roleId), "core.Role", 2);
            _ = await budget.MeasureAsync("roles/activate", Wire.Ids(roleId), "core.Role", 2);
            _ = await budget.MeasureAsync("roles/delete", Wire.Ids(roleId), "core.Role", 2);

            // --- Users. Every write loads the user access rules' loads first; an invite or an issue calls the identity engine between its two ---
            JsonNode user = await budget.MeasureAsync(
                "users/save", Wire.Save(new JsonObject { ["id"] = 0, ["email"] = "budget@example.com", ["name"] = "Budget User" }), "core.User", 2);
            int userId = Wire.IdsOf(user)[0];
            JsonObject userDetails = Wire.Entities(user)[0];
            userDetails["name"] = "Budget User, edited";
            _ = await budget.MeasureAsync("users/save", Wire.Save(userDetails), "core.User", 2);
            _ = await budget.MeasureAsync("users/query", Wire.Query("Name", "Email = 'budget@example.com'"), "core.User", 1);
            _ = await budget.MeasureAsync("users/get", new JsonObject { ["id"] = userId }, "core.User", 1);
            _ = await budget.MeasureAsync("users/get-by-ids", Wire.Ids(userId), "core.User", 1);
            _ = await budget.MeasureAsync("users/invite", Wire.Ids(userId), "core.User", 2);
            _ = await budget.MeasureAsync("users/invitation-status", Wire.Ids(userId), "core.User", 1);
            _ = await budget.MeasureAsync("users/preferences/get", new JsonObject { ["userId"] = userId }, "core.User", 1);
            _ = await budget.MeasureAsync(
                "users/preferences/set", new JsonObject { ["id"] = userId, ["arguments"] = new JsonObject { ["items"] = new JsonArray(new JsonObject { ["key"] = "k", ["value"] = "v" }) } },
                "core.User", 2);
            _ = await budget.MeasureAsync(
                "users/preferences/delete", new JsonObject { ["id"] = userId, ["arguments"] = new JsonObject { ["keys"] = new JsonArray("k") } }, "core.User", 2);
            _ = await budget.MeasureAsync("users/deactivate", Wire.Ids(userId), "core.User", 2);
            _ = await budget.MeasureAsync("users/activate", Wire.Ids(userId), "core.User", 2);
            JsonNode doomed = (await admin.PostAsync(Tenant, "users/save", Wire.Save(new JsonObject { ["id"] = 0, ["email"] = "budget-delete@example.com", ["name"] = "Budget Delete" })))
                .Expect(HttpStatusCode.OK);
            _ = await budget.MeasureAsync("users/delete", Wire.Ids(Wire.IdsOf(doomed)[0]), "core.User", 2);
            JsonNode account = (await admin.PostAsync(Tenant, "users/save", Wire.Save(new JsonObject { ["id"] = 0, ["kind"] = "Service", ["name"] = "Budget Account" })))
                .Expect(HttpStatusCode.OK);
            _ = await budget.MeasureAsync("users/issue-credentials", new JsonObject { ["id"] = Wire.IdsOf(account)[0] }, "core.User", 2);

            // --- The caller's own routes ---
            JsonNode me = await budget.MeasureAsync("users/me", null, "core.User", 1);
            _ = await budget.MeasureAsync("users/me/save", new JsonObject { ["name"] = "admin", ["modifiedAt"] = me["user"]!["modifiedAt"]!.DeepClone() }, "core.User", 3);
            _ = await budget.MeasureAsync("users/me/locale", new JsonObject { ["timeZone"] = "Africa/Addis_Ababa" }, "core.User", 1);
            _ = await budget.MeasureAsync(
                "users/me/preferences/set", new JsonObject { ["items"] = new JsonArray(new JsonObject { ["key"] = "budget", ["value"] = "1" }) }, "core.User", 1);
            _ = await budget.MeasureAsync("users/me/preferences/delete", new JsonObject { ["keys"] = new JsonArray("budget") }, "core.User", 1);

            // Every measurement goes to the test's output, so a run shows how close each operation is to its budget.
            TestContext.Current.TestOutputHelper?.WriteLine(budget.All.ToString());
            Assert.True(budget.Over.Length == 0, "Operations above their round-trip budget:\n" + budget.Over + "\nAll measured:\n" + budget.All);
        }

        /// <summary>Measures operations one at a time and keeps those above their budget.</summary>
        /// <param name="trips">The round-trip instrument's collector.</param>
        /// <param name="client">The caller.</param>
        private sealed class Budget(MetricCollector<int> trips, SpaClient client)
        {
            /// <summary>The operations above their budget, one per line.</summary>
            public StringBuilder Over { get; } = new();

            /// <summary>Every operation measured, one per line.</summary>
            public StringBuilder All { get; } = new();

            /// <summary>
            ///     Sends a read that absorbs whatever a cold cache or a moved tag costs, then the
            ///     operation, and compares the operation's round trips with its budget.
            /// </summary>
            /// <param name="route">The route, which names the operation in the report.</param>
            /// <param name="body">The body.</param>
            /// <param name="resource">
            ///     The resource the operation's unit of work is tagged with. The tag names the
            ///     pipeline's operation kind, which every entity action shares, so the route rather than
            ///     the tag tells the measured operations apart.
            /// </param>
            /// <param name="allowed">The operation's budget.</param>
            /// <returns>The operation's answer.</returns>
            public async Task<JsonNode> MeasureAsync(string route, JsonNode? body, string resource, int allowed)
            {
                // --- The warming read, measured and set aside ---
                _ = (await client.PostAsync(Tenant, "users/me")).Expect(HttpStatusCode.OK);
                _ = await AwaitAsync("core.User");

                // --- The operation ---
                JsonNode answer = (await client.PostAsync(Tenant, route, body)).Expect(HttpStatusCode.OK);
                (string operation, int taken) = await AwaitAsync(resource);
                _ = All.Append(CultureInfo.InvariantCulture, $"{route} ({operation}): {taken} (budget {allowed})").Append('\n');
                if (taken > allowed)
                {
                    _ = Over.Append(CultureInfo.InvariantCulture, $"{route} ({operation}): {taken} round trips, budget {allowed}").Append('\n');
                }

                return answer;
            }

            /// <summary>
            ///     Waits for the next measurement of a unit of work on a resource, clearing everything
            ///     recorded until then. Units of work outside any request, a job's or a sign-in's, carry
            ///     no resource and are passed over.
            /// </summary>
            /// <param name="resource">The resource.</param>
            /// <returns>The unit of work's operation tag and its round trips.</returns>
            private async Task<(string Operation, int RoundTrips)> AwaitAsync(string resource)
            {
                string prefix = resource + ":";
                var elapsed = Stopwatch.StartNew();
                List<CollectedMeasurement<int>> seen = [];
                while (true)
                {
                    seen.AddRange(trips.GetMeasurementSnapshot(clear: true));
                    foreach (CollectedMeasurement<int> measured in seen)
                    {
                        if (measured.Tags.GetValueOrDefault(DataTelemetryNames.OperationTag) is string operation && operation.StartsWith(prefix, StringComparison.Ordinal))
                        {
                            return (operation, measured.Value);
                        }
                    }

                    Assert.True(
                        elapsed.Elapsed < Patience,
                        $"No round trips recorded on {resource}; recorded: {string.Join(", ", seen.Select(static m => $"{m.Tags.GetValueOrDefault(DataTelemetryNames.OperationTag)}={m.Value}"))}");
                    await Task.Delay(TimeSpan.FromMilliseconds(25), TestContext.Current.CancellationToken);
                }
            }
        }
    }
}
