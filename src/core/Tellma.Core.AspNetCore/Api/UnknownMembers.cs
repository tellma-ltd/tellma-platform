// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using System.Collections;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Tellma.Core.Composition;

namespace Tellma.Core.AspNetCore.Api
{
    // Spec 0016 §3.1: the wire tolerates every member on the way in, so a details payload
    // round-trips into a save unchanged, and every member it skips is counted on
    // tellma.api.unknown_members{type}. System.Text.Json skips an unknown member silently, so each
    // object contract of the host's HTTP options gets an extension-data member whose dictionary
    // counts what it is handed and keeps nothing: the member is skipped exactly as before, and
    // never written.

    /// <summary>Counts the request body members no property of their type declares.</summary>
    internal static class UnknownMembers
    {
        /// <summary>The name of the counting member, which no wire member can take.</summary>
        internal const string MemberName = "$tellma.unknownMembers";

        /// <summary>Adds the counting member to every object contract of the host's HTTP JSON options.</summary>
        /// <param name="services">The host's services.</param>
        public static void Register(IServiceCollection services)
        {
            ArgumentNullException.ThrowIfNull(services);

            // After the platform's rules, so the modifier wraps the resolver chain they set.
            _ = services.AddOptions<JsonOptions>().Configure<ApiMetrics>(static (options, metrics) => Apply(options.SerializerOptions, metrics));
        }

        /// <summary>Wraps options' resolver so each object contract counts its unknown members.</summary>
        /// <param name="options">The options, not yet read-only.</param>
        /// <param name="metrics">The instruments the count goes to.</param>
        internal static void Apply(JsonSerializerOptions options, ApiMetrics metrics)
        {
            ArgumentNullException.ThrowIfNull(options);
            ArgumentNullException.ThrowIfNull(metrics);
            ConcurrentDictionary<Type, UnknownMemberSink> sinks = new();
            IJsonTypeInfoResolver resolver = options.TypeInfoResolver
                ?? throw new InvalidOperationException("The host's JSON options have no type resolver to count unknown members with.");
            options.TypeInfoResolver = resolver.WithAddedModifier(info => Modify(info, sinks, metrics));
        }

        /// <summary>Adds the counting member to an object contract that has no extension data of its own.</summary>
        /// <param name="info">The contract.</param>
        /// <param name="sinks">The sinks, one per type.</param>
        /// <param name="metrics">The instruments the count goes to.</param>
        private static void Modify(JsonTypeInfo info, ConcurrentDictionary<Type, UnknownMemberSink> sinks, ApiMetrics metrics)
        {
            // --- Only an object contract that would skip an unknown member, and has nowhere else to put it ---
            if (info.Kind != JsonTypeInfoKind.Object
                || info.UnmappedMemberHandling == JsonUnmappedMemberHandling.Disallow
                || info.Properties.Any(static property => property.IsExtensionData))
            {
                return;
            }

            // --- The member: read as the type's sink, never set, never written ---
            UnknownMemberSink sink = sinks.GetOrAdd(info.Type, static (type, metrics) => new UnknownMemberSink(TypeDisplay.Short(type), metrics), metrics);
            JsonPropertyInfo member = info.CreateJsonPropertyInfo(typeof(UnknownMemberSink), MemberName);
            member.Get = _ => sink;
            member.Set = static (_, _) => { };
            member.ShouldSerialize = static (_, _) => false;
            member.IsExtensionData = true;
            info.Properties.Add(member);
        }
    }

    /// <summary>
    ///     The dictionary an unknown member is handed: it counts the member against its type and keeps
    ///     nothing, so it is always empty and every member is new to it.
    /// </summary>
    /// <param name="type">The type's display name, the count's tag.</param>
    /// <param name="metrics">The instruments the count goes to.</param>
    [SuppressMessage(
        "Naming",
        "CA1710:Identifiers should have correct suffix",
        Justification = "A sink, not a collection anyone reads: it is handed members and keeps none.")]
    internal sealed class UnknownMemberSink(string type, ApiMetrics metrics) : IDictionary<string, JsonElement>
    {
        /// <summary>The type's display name, the count's tag.</summary>
        public string Type { get; } = type;

        /// <inheritdoc />
        public ICollection<string> Keys => [];

        /// <inheritdoc />
        public ICollection<JsonElement> Values => [];

        /// <inheritdoc />
        public int Count => 0;

        /// <inheritdoc />
        public bool IsReadOnly => false;

        /// <inheritdoc />
        public JsonElement this[string key]
        {
            get => throw new KeyNotFoundException($"The unknown member '{key}' was counted, not kept.");
            set => Add(key, value);
        }

        /// <inheritdoc />
        public void Add(string key, JsonElement value)
        {
            metrics.RecordUnknownMember(Type);
        }

        /// <inheritdoc />
        public void Add(KeyValuePair<string, JsonElement> item)
        {
            Add(item.Key, item.Value);
        }

        /// <inheritdoc />
        public void Clear()
        {
        }

        /// <inheritdoc />
        public bool Contains(KeyValuePair<string, JsonElement> item)
        {
            return false;
        }

        /// <inheritdoc />
        public bool ContainsKey(string key)
        {
            return false;
        }

        /// <inheritdoc />
        public void CopyTo(KeyValuePair<string, JsonElement>[] array, int arrayIndex)
        {
        }

        /// <inheritdoc />
        public IEnumerator<KeyValuePair<string, JsonElement>> GetEnumerator()
        {
            return Enumerable.Empty<KeyValuePair<string, JsonElement>>().GetEnumerator();
        }

        /// <inheritdoc />
        public bool Remove(string key)
        {
            return false;
        }

        /// <inheritdoc />
        public bool Remove(KeyValuePair<string, JsonElement> item)
        {
            return false;
        }

        /// <inheritdoc />
        public bool TryGetValue(string key, out JsonElement value)
        {
            value = default;
            return false;
        }

        /// <inheritdoc />
        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }
}
