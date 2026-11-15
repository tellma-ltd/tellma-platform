// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Text.Json;
using System.Text.Json.Serialization;
using Tellma.Core.Abstractions.Api;
using Tellma.Core.Abstractions.Crud;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Entities;

namespace Tellma.Core.Abstractions.Tests.Api
{
    /// <summary>
    ///     The entities envelope on the wire: ids in the stack's key type, whole entities, related rows
    ///     restricted to their projections, and the extras as they were serialized.
    /// </summary>
    public class EntityEnvelopeTests
    {
        [Fact]
        public void An_int_keyed_result_writes_its_ids_as_numbers_and_its_permissions_ids_too()
        {
            EntitiesResult<Center> result = new()
            {
                Ids = [5],
                Entities = [new Center { Id = 5, Name = "East" }],
                Extras = new Dictionary<string, JsonElement> { ["permissions"] = Permissions(new RecordPermissions<int>(5, Refusal())) },
            };

            Assert.Equal(
                /*lang=json,strict*/ """{"ids":[5],"entities":[{"name":"East","id":5}],"extras":{"permissions":[{"id":5,"refused":{"delete":"outside-filter"}}]}}""",
                Write(result));
        }

        [Fact]
        public void A_long_keyed_result_writes_its_ids_as_strings_and_its_permissions_ids_too()
        {
            EntitiesResult<Ledger> result = new()
            {
                Ids = [9_007_199_254_740_993],
                Entities = [new Ledger { Id = 9_007_199_254_740_993, Memo = "big" }],
                Extras = new Dictionary<string, JsonElement> { ["permissions"] = Permissions(new RecordPermissions<long>(9_007_199_254_740_993, Refusal())) },
            };

            Assert.Equal(
                /*lang=json,strict*/ """{"ids":["9007199254740993"],"entities":[{"memo":"big","id":"9007199254740993"}],"extras":{"permissions":[{"id":"9007199254740993","refused":{"delete":"outside-filter"}}]}}""",
                Write(result));
        }

        [Fact]
        public void A_result_of_ids_alone_writes_an_empty_entities_array_and_no_related_or_extras()
        {
            EntitiesResult<Center> result = new() { Ids = [4, 6], Entities = [] };

            Assert.Equal(/*lang=json,strict*/ """{"ids":[4,6],"entities":[]}""", Write(result));
        }

        [Fact]
        public void An_entity_of_a_derived_leaf_writes_the_leafs_own_members()
        {
            EntitiesResult<Center> result = new() { Ids = [3], Entities = [new RegionalCenter { Id = 3, Name = "North", Region = "N" }] };

            Assert.Equal(/*lang=json,strict*/ """{"ids":[3],"entities":[{"region":"N","name":"North","id":3}]}""", Write(result));
        }

        [Fact]
        public void Related_rows_carry_only_their_projection_and_write_a_null_member_as_null()
        {
            RelatedEntities related = new();
            related.Add("core.User", [new Person { Id = 1, Name = "Administrator", Email = "admin@example.com" }, new Person { Id = 3, Name = "Sara", ImageId = 812 }], new HashSet<string> { "Id", "Name", "ImageId" });
            related.Add("gl.Center", [new Center { Id = 1, Name = "Regions" }], new HashSet<string> { "Id", "Name" });
            EntitiesResult<Center> result = new() { Ids = [5], Entities = [], Related = related };

            // The sets are in ordinal order of their names; the email is outside the projection.
            Assert.Equal(
                /*lang=json,strict*/ """{"ids":[5],"entities":[],"related":{"core.User":[{"name":"Administrator","imageId":null,"id":1},{"name":"Sara","imageId":812,"id":3}],"gl.Center":[{"name":"Regions","id":1}]}}""",
                Write(result));
        }

        [Fact]
        public void A_related_projection_keeps_the_members_json_names_and_converters_and_leaves_out_members_never_on_the_wire()
        {
            RelatedEntities related = new();
            related.Add("acme.Tagged", [new Tagged { Id = 2, Label = "x", Secret = "s", Weight = 1.5m }], new HashSet<string> { "Id", "Label", "Secret", "Weight" });

            Assert.Equal(
                /*lang=json,strict*/ """{"acme.Tagged":[{"tag":"X","weight":"1.5","id":2}]}""",
                Write(related));
        }

        [Fact]
        public void An_empty_related_bag_is_an_empty_object()
        {
            Assert.Equal("{}", Write(new RelatedEntities()));
        }

        [Fact]
        public void An_entities_result_is_written_only_and_only_over_an_entity()
        {
            _ = Assert.Throws<NotSupportedException>(() => JsonSerializer.Deserialize<EntitiesResult<Center>>("{}", TellmaJson.Options));
            _ = Assert.Throws<NotSupportedException>(() => JsonSerializer.Deserialize<RelatedEntities>("{}", TellmaJson.Options));
            _ = Assert.Throws<NotSupportedException>(() => Write(new EntitiesResult<string> { Ids = [], Entities = [] }));
        }

        /// <summary>One refusal, as the permissions extra maps an ability to its reason.</summary>
        /// <returns>The refusal.</returns>
        private static Dictionary<string, string> Refusal()
        {
            return new Dictionary<string, string> { ["delete"] = "outside-filter" };
        }

        /// <summary>A permissions extra of one row, serialized as the pipeline serializes every extra.</summary>
        /// <typeparam name="TKey">The key type.</typeparam>
        /// <param name="permissions">The row's permissions.</param>
        /// <returns>The extra.</returns>
        private static JsonElement Permissions<TKey>(RecordPermissions<TKey> permissions)
            where TKey : struct
        {
            return JsonSerializer.SerializeToElement<IReadOnlyList<RecordPermissions<TKey>>>([permissions], TellmaJson.Options);
        }

        /// <summary>Serializes a value with the platform's options.</summary>
        /// <typeparam name="T">The value's type.</typeparam>
        /// <param name="value">The value.</param>
        /// <returns>The JSON text.</returns>
        private static string Write<T>(T value)
        {
            return JsonSerializer.Serialize(value, TellmaJson.Options);
        }

        /// <summary>An int-keyed entity.</summary>
        public class Center : Entity<int>
        {
            /// <summary>A name.</summary>
            public string? Name { get; set; }
        }

        /// <summary>A leaf deriving from <see cref="Center" />.</summary>
        public sealed class RegionalCenter : Center
        {
            /// <summary>A member of the leaf alone.</summary>
            public string? Region { get; set; }
        }

        /// <summary>A long-keyed entity.</summary>
        public sealed class Ledger : Entity<long>
        {
            /// <summary>A memo.</summary>
            public string? Memo { get; set; }
        }

        /// <summary>An entity reached through a navigation, with a column its projection leaves out.</summary>
        public sealed class Person : Entity<int>
        {
            /// <summary>A name.</summary>
            public string? Name { get; set; }

            /// <summary>A column outside the projection.</summary>
            public string? Email { get; set; }

            /// <summary>An avatar.</summary>
            public int? ImageId { get; set; }
        }

        /// <summary>An entity whose members rename, convert and hide themselves.</summary>
        public sealed class Tagged : Entity<int>
        {
            /// <summary>A member renamed and converted on the wire.</summary>
            [JsonPropertyName("tag")]
            [JsonConverter(typeof(UpperCaseConverter))]
            public string? Label { get; set; }

            /// <summary>A member never on the wire.</summary>
            [JsonIgnore]
            public string? Secret { get; set; }

            /// <summary>A decimal, written as a string.</summary>
            public decimal Weight { get; set; }
        }

        /// <summary>Writes a string in upper case.</summary>
        public sealed class UpperCaseConverter : JsonConverter<string>
        {
            /// <inheritdoc />
            public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            {
                return reader.GetString();
            }

            /// <inheritdoc />
            public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options)
            {
                writer.WriteStringValue(value.ToUpperInvariant());
            }
        }
    }
}
