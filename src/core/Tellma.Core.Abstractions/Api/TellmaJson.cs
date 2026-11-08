// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace Tellma.Core.Abstractions.Api
{
    /// <summary>
    ///     The platform's one set of JSON rules. Every tenant endpoint, the agent tools, the Excel
    ///     hand-off, every JSON column the platform writes and the contract fingerprints serialize
    ///     with them, so stored JSON, fingerprints and the wire always agree. There is no DTO layer:
    ///     an entity class is its own wire shape under these rules.
    /// </summary>
    /// <remarks>
    ///     <para>The rules:</para>
    ///     <list type="bullet">
    ///         <item>Members are camelCase on the way out and matched without regard to case on the way in.</item>
    ///         <item>A null member is omitted on the way out.</item>
    ///         <item>
    ///             Numbers are strict: no quoted <see cref="int" /> or <see cref="double" />, and no
    ///             <c>NaN</c> or infinity.
    ///         </item>
    ///         <item>
    ///             <see cref="long" /> and <see cref="decimal" /> travel as strings, every digit and
    ///             full scale, never in exponent form, because a client that reads JSON numbers as
    ///             doubles is exact only to 15 digits; each also reads a number. A decimal is never
    ///             rounded: one it cannot hold exactly is refused.
    ///         </item>
    ///         <item>
    ///             <see cref="DateTime" /> is <c>yyyy-MM-ddTHH:mm:ss.fffffff</c> with no offset,
    ///             <see cref="DateTimeOffset" /> the same with its offset, <see cref="TimeOnly" />
    ///             <c>HH:mm:ss.fffffff</c>: seven fractional digits always, so a stamp round-trips bit
    ///             for bit. <see cref="DateOnly" /> is <c>yyyy-MM-dd</c>, a <see cref="Guid" /> its
    ///             canonical lowercase form and a byte array base64.
    ///         </item>
    ///         <item>Enums are their member names as strings; an integer is refused.</item>
    ///         <item>A duplicate member is refused.</item>
    ///         <item>An unknown member is skipped, so a details payload round-trips into a save unchanged.</item>
    ///         <item>
    ///             Nesting is at most 16 deep: parent, child and grandchild need six, and the
    ///             framework's 64 is a stack margin nobody needs.
    ///         </item>
    ///     </list>
    ///     <para>
    ///         The platform's wire records resolve through the source-generated
    ///         <see cref="TellmaApiJsonContext" />; entity, child and operation body types resolve by
    ///         reflection after it, so a distribution declares nothing.
    ///     </para>
    /// </remarks>
    public static class TellmaJson
    {
        /// <summary>The deepest nesting a document may have.</summary>
        private const int MaxDepth = 16;

        /// <summary>
        ///     The one read-only options instance with the platform's rules, for every serializer
        ///     outside an HTTP pipeline: services, the migrator, stored JSON, fingerprints.
        /// </summary>
        public static JsonSerializerOptions Options { get; } = CreateOptions();

        /// <summary>
        ///     Applies the platform's rules to options a host owns, such as the web host's HTTP JSON
        ///     options, so the host serializes exactly as <see cref="Options" /> does. The platform's
        ///     converters go first and so win over any the host added. Applying twice changes nothing.
        /// </summary>
        /// <param name="options">The options to configure; they must not be read-only yet.</param>
        /// <exception cref="ArgumentNullException"><paramref name="options" /> is null.</exception>
        /// <exception cref="InvalidOperationException"><paramref name="options" /> is already read-only.</exception>
        public static void Configure(JsonSerializerOptions options)
        {
            ArgumentNullException.ThrowIfNull(options);

            // --- The rules ---
            options.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
            options.PropertyNameCaseInsensitive = true;
            options.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
            options.NumberHandling = JsonNumberHandling.Strict;
            options.AllowDuplicateProperties = false;
            options.UnmappedMemberHandling = JsonUnmappedMemberHandling.Skip;
            options.MaxDepth = MaxDepth;

            // --- The converters, ahead of any the host added ---
            JsonConverter[] converters =
            [
                new StrictEnumJsonConverter(),
                new Int64JsonConverter(),
                new DecimalJsonConverter(),
                new DateTimeJsonConverter(),
                new DateTimeOffsetJsonConverter(),
                new TimeOnlyJsonConverter(),
            ];

            int position = 0;
            foreach (JsonConverter converter in converters)
            {
                // The first converter that accepts a type wins, so the platform's go to the front;
                // one already present (a second Configure) keeps its place.
                if (!options.Converters.Any(existing => existing.GetType() == converter.GetType()))
                {
                    options.Converters.Insert(position, converter);
                }

                position++;
            }

            // --- Type resolution: the platform's records first, then reflection for everything else ---
            IList<IJsonTypeInfoResolver> chain = options.TypeInfoResolverChain;
            if (!chain.Contains(TellmaApiJsonContext.Default))
            {
                chain.Insert(0, TellmaApiJsonContext.Default);
            }

            if (!chain.Any(static resolver => resolver is DefaultJsonTypeInfoResolver))
            {
                chain.Add(new DefaultJsonTypeInfoResolver());
            }
        }

        /// <summary>Creates the platform's options and freezes them.</summary>
        /// <returns>The read-only options.</returns>
        private static JsonSerializerOptions CreateOptions()
        {
            JsonSerializerOptions options = new();
            Configure(options);
            options.MakeReadOnly();
            return options;
        }
    }
}
