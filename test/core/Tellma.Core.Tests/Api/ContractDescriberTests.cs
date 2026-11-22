// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Tellma.Core.Abstractions.Api;
using Tellma.Core.Abstractions.Crud;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Api;
using Tellma.Core.Tests.Crud;

namespace Tellma.Core.Tests.Api
{
    /// <summary>
    ///     What a contract fingerprint covers: each change to what the wire carries moves it, and a
    ///     change the wire never sees leaves it. The goldens pin today's fingerprints; these pairs pin
    ///     that the fingerprint keeps following the shape when the goldens are rewritten.
    /// </summary>
    public class ContractDescriberTests
    {
        [Theory]
        [InlineData(typeof(Ranked), typeof(RenamedRank))]
        [InlineData(typeof(Ranked), typeof(LongRank))]
        [InlineData(typeof(Ranked), typeof(DecimalRank))]
        [InlineData(typeof(Ranked), typeof(OptionalRank))]
        [InlineData(typeof(Labelled), typeof(OptionalLabel))]
        [InlineData(typeof(Shading), typeof(MoreShading))]
        [InlineData(typeof(Ranks), typeof(RenamedRanks))]
        [InlineData(typeof(Ranked), typeof(RankAsString))]
        [InlineData(typeof(Ranked), typeof(RankedAsStrings))]
        [InlineData(typeof(RankList), typeof(RanksAsStrings))]
        [InlineData(typeof(Measured), typeof(MeasuredWithNamedLiterals))]
        public async Task A_change_to_what_the_wire_carries_moves_the_fingerprint(Type before, Type after)
        {
            await using ServiceProvider provider = StackScenarios.Gadgets().BuildServiceProvider(validateScopes: true);
            ContractDescriber describer = Describer(provider, TellmaJson.Options);

            Assert.NotEqual(Fingerprint(describer, before), Fingerprint(describer, after));
        }

        [Theory]
        [InlineData(typeof(Ranked), typeof(SameRank))]
        [InlineData(typeof(Ranked), typeof(RankWithSecret))]
        [InlineData(typeof(Ranked), typeof(RankReadFromString))]
        public async Task A_change_the_wire_never_sees_leaves_the_fingerprint(Type before, Type after)
        {
            await using ServiceProvider provider = StackScenarios.Gadgets().BuildServiceProvider(validateScopes: true);
            ContractDescriber describer = Describer(provider, TellmaJson.Options);

            Assert.Equal(describer.Describe(null, before, 0, 0, 0), describer.Describe(null, after, 0, 0, 0));
        }

        [Fact]
        public async Task A_number_written_under_number_handling_is_described_with_it()
        {
            await using ServiceProvider provider = StackScenarios.Gadgets().BuildServiceProvider(validateScopes: true);
            ContractDescriber describer = Describer(provider, TellmaJson.Options);

            Assert.Contains("\"rank\":\"integer:int32|string\"", describer.Describe(typeof(RankAsString), typeof(void), 0, 0, 0), StringComparison.Ordinal);
            Assert.Contains("\"rank\":\"integer:int32|string\"", describer.Describe(typeof(RankedAsStrings), typeof(void), 0, 0, 0), StringComparison.Ordinal);
            Assert.Contains("\"ranks\":{\"array\":\"integer:int32|string\"}", describer.Describe(typeof(RanksAsStrings), typeof(void), 0, 0, 0), StringComparison.Ordinal);
            Assert.Contains("\"ratio\":\"number:double|named\"", describer.Describe(typeof(MeasuredWithNamedLiterals), typeof(void), 0, 0, 0), StringComparison.Ordinal);
        }

        [Fact]
        public async Task A_pre_serialized_member_is_described_as_the_type_its_bytes_were_serialized_from()
        {
            await using ServiceProvider provider = StackScenarios.Gadgets().BuildServiceProvider(validateScopes: true);
            ContractDescriber describer = Describer(provider, TellmaJson.Options);

            Assert.Equal(describer.Describe(null, typeof(HeldRank), 0, 0, 0), describer.Describe(null, typeof(PreSerializedRank), 0, 0, 0));
        }

        [Fact]
        public async Task A_change_to_a_projected_member_of_an_entity_the_details_reach_moves_the_entities_results_fingerprint()
        {
            await using ServiceProvider provider = StackScenarios.Gadgets().BuildServiceProvider(validateScopes: true);
            StackDescriptor gadget = provider.GetRequiredService<IStackRegistry>().Find(StackModel.Gadgets)!;
            Type result = typeof(EntitiesResult<>).MakeGenericType(gadget.EntityType);
            Type user = provider.GetRequiredService<IEntityMetadataProvider>().Get(gadget.EntityType)
                .References.First(static reference => reference.Target.Name == "core.User").Target.ClrType;

            string original = Fingerprint(Describer(provider, TellmaJson.Options), result);

            // --- A member in the user's related projection, renamed or written as a string ---
            Assert.NotEqual(original, Fingerprint(Describer(provider, Revised(user, "Name2", static member => member.Name = "secondName")), result));
            Assert.NotEqual(original, Fingerprint(Describer(provider, Revised(user, "ImageId", static member => member.NumberHandling = JsonNumberHandling.WriteAsString)), result));

            // --- A user member outside the projection, and an entity the details never reach ---
            Assert.Equal(original, Fingerprint(Describer(provider, Revised(user, "Email", static member => member.Name = "mail")), result));
            Assert.Equal(original, Fingerprint(Describer(provider, Revised(typeof(SmSprocket), "Name", static member => member.Name = "title")), result));
        }

        /// <summary>A describer over the gadget composition's stacks and metadata under the given options.</summary>
        /// <param name="provider">The gadget composition.</param>
        /// <param name="options">The JSON rules.</param>
        /// <returns>The describer.</returns>
        private static ContractDescriber Describer(IServiceProvider provider, JsonSerializerOptions options)
        {
            return new ContractDescriber(provider.GetRequiredService<IStackRegistry>(), provider.GetRequiredService<IEntityMetadataProvider>(), options);
        }

        /// <summary>The fingerprint of an endpoint answering the given type, with no body and no revisions.</summary>
        /// <param name="describer">The describer.</param>
        /// <param name="response">The response type.</param>
        /// <returns>The fingerprint.</returns>
        private static string Fingerprint(ContractDescriber describer, Type response)
        {
            return ContractDescriber.Fingerprint(describer.Describe(null, response, 0, 0, 0));
        }

        /// <summary>The platform's options with one member of one type revised by a contract modifier.</summary>
        /// <param name="type">The type whose member is revised.</param>
        /// <param name="clrName">The member's CLR name.</param>
        /// <param name="revise">Revises the member's contract.</param>
        /// <returns>The options.</returns>
        private static JsonSerializerOptions Revised(Type type, string clrName, Action<JsonPropertyInfo> revise)
        {
            return new JsonSerializerOptions(TellmaJson.Options)
            {
                TypeInfoResolver = TellmaJson.Options.TypeInfoResolver!.WithAddedModifier(contract =>
                {
                    if (contract.Type == type)
                    {
                        revise(contract.Properties.Single(member => member.AttributeProvider is System.Reflection.MemberInfo clr && clr.Name == clrName));
                    }
                }),
            };
        }

        // --- The paired shapes: each differs from its partner in one thing alone ---

        /// <summary>A rank.</summary>
        /// <param name="Rank">The rank.</param>
        public sealed record Ranked(int Rank);

        /// <summary>The rank under another JSON name.</summary>
        /// <param name="Rank">The rank.</param>
        public sealed record RenamedRank([property: JsonPropertyName("position")] int Rank);

        /// <summary>The rank as a long.</summary>
        /// <param name="Rank">The rank.</param>
        public sealed record LongRank(long Rank);

        /// <summary>The rank as a decimal.</summary>
        /// <param name="Rank">The rank.</param>
        public sealed record DecimalRank(decimal Rank);

        /// <summary>The rank, optional.</summary>
        /// <param name="Rank">The rank.</param>
        public sealed record OptionalRank(int? Rank);

        /// <summary>The rank written as a string by the member's number handling.</summary>
        /// <param name="Rank">The rank.</param>
        public sealed record RankAsString([property: JsonNumberHandling(JsonNumberHandling.WriteAsString)] int Rank);

        /// <summary>The rank written as a string by the type's number handling.</summary>
        /// <param name="Rank">The rank.</param>
        [JsonNumberHandling(JsonNumberHandling.WriteAsString)]
        public sealed record RankedAsStrings(int Rank);

        /// <summary>The rank, read leniently from a string but written as a number.</summary>
        /// <param name="Rank">The rank.</param>
        public sealed record RankReadFromString([property: JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)] int Rank);

        /// <summary>The rank under another CLR name alone.</summary>
        /// <param name="Rank">The rank.</param>
        public sealed record SameRank(int Rank);

        /// <summary>The rank with a member that is never on the wire.</summary>
        /// <param name="Rank">The rank.</param>
        /// <param name="Secret">A member never written.</param>
        public sealed record RankWithSecret(int Rank, [property: JsonIgnore] string? Secret);

        /// <summary>A rank held as a value.</summary>
        /// <param name="Rank">The rank, when there is one.</param>
        public sealed record HeldRank(Ranked? Rank);

        /// <summary>The rank held as the bytes it was serialized to.</summary>
        /// <param name="Rank">The rank, when there is one.</param>
        public sealed record PreSerializedRank(PreSerializedJson<Ranked>? Rank);

        /// <summary>A label.</summary>
        /// <param name="Label">The label.</param>
        public sealed record Labelled(string Label);

        /// <summary>The label, optional.</summary>
        /// <param name="Label">The label.</param>
        public sealed record OptionalLabel(string? Label);

        /// <summary>A shade.</summary>
        /// <param name="Shade">The shade.</param>
        public sealed record Shading(Shade Shade);

        /// <summary>A shade of the larger enum.</summary>
        /// <param name="Shade">The shade.</param>
        public sealed record MoreShading(MoreShade Shade);

        /// <summary>Ranked children.</summary>
        /// <param name="Children">The children.</param>
        public sealed record Ranks(IReadOnlyList<Ranked> Children);

        /// <summary>Children whose rank goes by another JSON name.</summary>
        /// <param name="Children">The children.</param>
        public sealed record RenamedRanks(IReadOnlyList<RenamedRank> Children);

        /// <summary>Ranks as an array of numbers.</summary>
        /// <param name="Ranks">The ranks.</param>
        public sealed record RankList(IReadOnlyList<int> Ranks);

        /// <summary>Ranks as an array written as strings by the member's number handling.</summary>
        /// <param name="Ranks">The ranks.</param>
        public sealed record RanksAsStrings([property: JsonNumberHandling(JsonNumberHandling.WriteAsString)] IReadOnlyList<int> Ranks);

        /// <summary>A ratio.</summary>
        /// <param name="Ratio">The ratio.</param>
        public sealed record Measured(double Ratio);

        /// <summary>A ratio that may be written as a named literal.</summary>
        /// <param name="Ratio">The ratio.</param>
        public sealed record MeasuredWithNamedLiterals([property: JsonNumberHandling(JsonNumberHandling.AllowNamedFloatingPointLiterals)] double Ratio);

        /// <summary>Two shades.</summary>
        public enum Shade
        {
            /// <summary>Light.</summary>
            Light,

            /// <summary>Dark.</summary>
            Dark,
        }

        /// <summary>The two shades and one more.</summary>
        public enum MoreShade
        {
            /// <summary>Light.</summary>
            Light,

            /// <summary>Dark.</summary>
            Dark,

            /// <summary>Medium.</summary>
            Medium,
        }
    }
}
