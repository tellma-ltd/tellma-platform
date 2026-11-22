// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Schema;
using Tellma.Core.Abstractions.Api;
using Tellma.Core.Abstractions.Settings;
using Tellma.Core.Abstractions.Tenancy;

namespace Tellma.Core.Abstractions.Tests.Api
{
    /// <summary>
    ///     A value serialized once and written as its bytes: the bytes reach the wire unchanged, an
    ///     absent value is omitted like any null member, and a reader gets back the value they hold.
    /// </summary>
    public class PreSerializedJsonTests
    {
        [Fact]
        public void A_member_writes_its_bytes_unchanged_in_their_own_member_order()
        {
            // The bytes' member order is not the record's, so only a raw write keeps it.
            PreSerializedJson<Pair> value = new(Encoding.UTF8.GetBytes(/*lang=json,strict*/ """{"right":2,"left":1}"""));

            Assert.Equal(
                /*lang=json,strict*/ """{"pair":{"right":2,"left":1}}""",
                JsonSerializer.Serialize(new Holder(value), TellmaJson.Options));
        }

        [Fact]
        public void An_absent_member_is_omitted()
        {
            Assert.Equal("{}", JsonSerializer.Serialize(new Holder(null), TellmaJson.Options));
        }

        [Fact]
        public void A_read_keeps_the_values_bytes_and_deserializes_back_to_the_value()
        {
            Holder read = JsonSerializer.Deserialize<Holder>(/*lang=json,strict*/ """{"pair":{ "left" : 1, "right" : 2 }}""", TellmaJson.Options)!;

            Assert.Equal(/*lang=json,strict*/ """{"left":1,"right":2}""", Encoding.UTF8.GetString(read.Pair!.Utf8.Span));
            Assert.Equal(new Pair(1, 2), read.Pair.Deserialize());
        }

        [Fact]
        public void Deserializing_the_json_null_fails()
        {
            PreSerializedJson<Pair> value = new(Encoding.UTF8.GetBytes("null"));

            _ = Assert.Throws<JsonException>(value.Deserialize);
        }

        [Fact]
        public void A_sync_answer_writes_the_settings_bytes_through_the_platforms_generated_contracts()
        {
            // The answer is a platform record, so the generated context serializes it, and must still
            // hand the pre-serialized member to its converter.
            PreSerializedJson<TenantSettingsForClient> settings = new(Encoding.UTF8.GetBytes(/*lang=json,strict*/ """{"marker":true}"""));
            ClientSyncResult answer = new(
                new TenantFacts(3, TenantCategory.Live, TenantState.Active, null, Realtime: false),
                new Dictionary<string, string>(StringComparer.Ordinal) { ["settings"] = "s1" },
                settings,
                Me: null,
                StaleEntities: []);

            Assert.Equal(
                /*lang=json,strict*/ """{"tenant":{"id":3,"category":"Live","state":"Active","realtime":false},"tags":{"settings":"s1"},"settings":{"marker":true},"staleEntities":[]}""",
                JsonSerializer.Serialize(answer, TellmaJson.Options));
        }

        [Fact]
        public void A_members_schema_is_any_value()
        {
            JsonNode schema = TellmaJson.Options.GetJsonSchemaAsNode(typeof(Holder));

            Assert.Equal(JsonValueKind.True, schema["properties"]!["pair"]!.GetValueKind());
        }

        [Fact]
        public void A_null_utf8_buffer_is_refused()
        {
            _ = Assert.Throws<ArgumentNullException>(static () => new PreSerializedJson<Pair>(null!));
        }

        /// <summary>Two numbers.</summary>
        /// <param name="Left">The left number.</param>
        /// <param name="Right">The right number.</param>
        public sealed record Pair(int Left, int Right);

        /// <summary>A record holding a pre-serialized pair.</summary>
        /// <param name="Pair">The pair's bytes; null when absent.</param>
        public sealed record Holder(PreSerializedJson<Pair>? Pair);
    }
}
