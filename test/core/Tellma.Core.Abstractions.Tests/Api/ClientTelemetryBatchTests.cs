// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Text.Json;
using Tellma.Core.Abstractions.Api;

namespace Tellma.Core.Abstractions.Tests.Api
{
    /// <summary>
    ///     The client telemetry relay's batch under the platform's JSON rules: the SPA's batch reads
    ///     with its items' properties as raw values, and a member it leaves out stays empty for the
    ///     relay to refuse.
    /// </summary>
    public sealed class ClientTelemetryBatchTests
    {
        [Fact]
        public void The_spa_s_batch_reads_with_its_properties_as_raw_values()
        {
            ClientTelemetryBatch batch = JsonSerializer.Deserialize<ClientTelemetryBatch>(
                """
                { "version": "1.4.0+3f9c2ab1e5d4c0a7b6f8e9d2c1b0a9f8e7d6c5b4",
                  "tabId": "k3J7q2M9xA", "tenantId": 17, "sentAt": "2026-10-20T11:03:12.512Z",
                  "items": [
                    { "kind": "timing", "name": "tm.page.visit", "at": "2026-10-20T11:02:41.020Z",
                      "properties": { "route": "/:t/centers", "pageKind": "search", "ms": 412, "chromeMs": 38, "outcome": "ready" } },
                    { "kind": "event", "name": "tm.api.slow", "at": "2026-10-20T11:02:55.300Z",
                      "traceId": "0af7651916cd43dd8448eb211c80319c",
                      "properties": { "route": "/:t/centers", "pageKind": "search", "api": "centers/query", "durationMs": 4810 } } ] }
                """,
                TellmaJson.Options)!;

            Assert.Equal("1.4.0+3f9c2ab1e5d4c0a7b6f8e9d2c1b0a9f8e7d6c5b4", batch.Version);
            Assert.Equal("k3J7q2M9xA", batch.TabId);
            Assert.Equal(17, batch.TenantId);
            Assert.Equal(new DateTimeOffset(2026, 10, 20, 11, 3, 12, 512, TimeSpan.Zero), batch.SentAt);
            Assert.Equal(2, batch.Items.Count);

            ClientTelemetryItem visit = batch.Items[0];
            Assert.Equal("timing", visit.Kind);
            Assert.Null(visit.TraceId);
            Assert.Equal(["route", "pageKind", "ms", "chromeMs", "outcome"], visit.Properties!.Keys);
            Assert.Equal(412, visit.Properties["ms"].GetInt32());
            Assert.Equal("0af7651916cd43dd8448eb211c80319c", batch.Items[1].TraceId);
        }

        [Fact]
        public void A_member_the_spa_leaves_out_stays_empty()
        {
            ClientTelemetryBatch batch = JsonSerializer.Deserialize<ClientTelemetryBatch>("""{ "items": [ {} ] }""", TellmaJson.Options)!;

            Assert.Null(batch.Version);
            Assert.Null(batch.TabId);
            Assert.Null(batch.TenantId);
            Assert.Equal(default, batch.SentAt);
            ClientTelemetryItem item = Assert.Single(batch.Items);
            Assert.Null(item.Kind);
            Assert.Equal(default, item.At);
            Assert.Null(item.Properties);
        }

        [Fact]
        public void A_property_named_twice_is_malformed()
        {
            _ = Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<ClientTelemetryBatch>(
                """{ "items": [ { "properties": { "ms": 1, "ms": 2 } } ] }""", TellmaJson.Options));
        }
    }
}
