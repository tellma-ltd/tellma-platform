// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Globalization;
using System.Net;
using System.Text.Json.Nodes;
using Tellma.Core.Abstractions.Api;
using Tellma.Core.AspNetCore.IntegrationTests.Infrastructure;

namespace Tellma.Core.AspNetCore.IntegrationTests.Surface
{
    /// <summary>
    ///     The activity stamp through the composed host: a read the browser marks as background
    ///     (a poll or a refresh nobody asked for) leaves the member's last activity untouched, and the
    ///     member's next foreground request stamps it. The sales member, whom no other case signs in,
    ///     starts with no activity.
    /// </summary>
    /// <param name="deployment">The suite's deployment.</param>
    [Trait("Category", "Integration")]
    public sealed class ActivityTests(AcmeDeployment deployment)
    {
        private const int Tenant = AcmeDeployment.LiveTenant;

        [Fact]
        public async Task A_background_read_stamps_no_activity_and_the_next_foreground_request_does()
        {
            Member sales = deployment.Sales;
            Assert.Null(await LastActiveAtAsync(sales));
            using SpaClient client = await deployment.SignInAsync(sales);

            // --- Background reads, the first of them the member's first connection to the tenant ---
            for (int i = 0; i < 2; i++)
            {
                _ = (await client.PostAsync(Tenant, "users/me", configure: static request => request.Headers.Add(TellmaHeaders.Activity, "background")))
                    .Expect(HttpStatusCode.OK);
            }

            Assert.Null(await LastActiveAtAsync(sales));

            // --- A foreground request stamps the member's activity ---
            _ = (await client.PostAsync(Tenant, "centers/query", new JsonObject { ["select"] = "Code", ["take"] = 1 })).Expect(HttpStatusCode.OK);
            Assert.NotNull(await LastActiveAtAsync(sales));
        }

        /// <summary>When a member was last active in the live tenant, as the member's stamp row records it.</summary>
        /// <param name="member">The member.</param>
        /// <returns>The stamp; null when the member was never active.</returns>
        private Task<DateTimeOffset?> LastActiveAtAsync(Member member)
        {
            return deployment.ScalarAsync<DateTimeOffset?>(
                Tenant, string.Create(CultureInfo.InvariantCulture, $"SELECT [LastActiveAt] FROM [core].[UserStamps] WHERE [UserId] = {member.UserId};"));
        }
    }
}
