// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Tellma.Core.Abstractions.Api;

namespace Tellma.Core.Abstractions.Settings
{
    /// <summary>
    ///     A typed tenant setting, declared once as a public static field of a holder type that the
    ///     feature registers in one line of its contribution:
    ///     <code>
    ///     public static readonly SettingKey&lt;bool&gt; AutoNumberOnPost =
    ///         new("gl.posting.autoNumberOnPost", true, visibility: SettingVisibility.Client);
    ///     public static readonly SettingKey&lt;int?&gt; DefaultCenterId =
    ///         new("gl.posting.defaultCenterId", null, references: typeof(Center));
    ///     </code>
    ///     A service reads the tenant's value with <see cref="TenantSettings.Get{T}" />.
    /// </summary>
    /// <remarks>
    ///     Values travel and are stored under the platform's JSON rules: a <see cref="long" /> or
    ///     <see cref="decimal" /> is a JSON string (a number is accepted on input), an enum its
    ///     member's name. A value type the platform's rules cannot know (a pack's own record) comes
    ///     with its serialization contract from the declaring package's source-generated context.
    /// </remarks>
    /// <typeparam name="T">The value's type.</typeparam>
    public sealed record SettingKey<T> : SettingKey
    {
        /// <summary>Declares a setting.</summary>
        /// <param name="name">The name, dotted camelCase of at least two segments; the stored row's key.</param>
        /// <param name="defaultValue">The code default: null for a nullable key and for every secret key.</param>
        /// <param name="category">The settings tab; null for the name's first segment.</param>
        /// <param name="group">The card inside the tab; null to derive one from the name.</param>
        /// <param name="visibility">Who may see the value.</param>
        /// <param name="validate">
        ///     Checks a value beyond its type, returning a dotted PascalCase validation code or null.
        ///     It must not throw: an exception it throws fails the request.
        /// </param>
        /// <param name="typeInfo">The value's serialization contract, for a type the platform's JSON rules cannot know.</param>
        /// <param name="references">The top-level entity an id value points at.</param>
        /// <exception cref="ArgumentException"><paramref name="name" /> is null, empty or white space.</exception>
        public SettingKey(
            string name,
            T defaultValue,
            string? category = null,
            string? group = null,
            SettingVisibility visibility = SettingVisibility.Server,
            Func<T, string?>? validate = null,
            JsonTypeInfo<T>? typeInfo = null,
            Type? references = null)
            : base(name, category, group, visibility, typeof(T), references, Serialize(defaultValue, typeInfo))
        {
            DefaultValue = defaultValue;
            Validate = validate;
            TypeInfo = typeInfo;
        }

        /// <summary>The code default, which a tenant without a stored value reads.</summary>
        public T DefaultValue { get; }

        /// <summary>The check beyond the value's type: a dotted PascalCase validation code, or null for a valid value.</summary>
        public Func<T, string?>? Validate { get; }

        /// <summary>The value's own serialization contract; null when the platform's JSON rules apply.</summary>
        public JsonTypeInfo<T>? TypeInfo { get; }

        /// <summary>
        ///     Reads a stored JSON value as <typeparamref name="T" />. A JSON null reads only for a key
        ///     whose default is null; any value that does not deserialize is refused.
        /// </summary>
        /// <param name="json">The stored value.</param>
        /// <param name="value">The value read.</param>
        /// <returns>True when the value reads as <typeparamref name="T" />.</returns>
        public bool TryRead(JsonElement json, out T value)
        {
            value = DefaultValue;
            if (json.ValueKind == JsonValueKind.Null)
            {
                // Only a nullable key (whose default is null by the declaration rules) stores null.
                if (default(T) is null && DefaultValue is null)
                {
                    value = default!;
                    return true;
                }

                return false;
            }

            try
            {
                T? read = TypeInfo is null ? json.Deserialize<T>(TellmaJson.Options) : json.Deserialize(TypeInfo);
                if (read is null)
                {
                    return false;
                }

                value = read;
                return true;
            }
            catch (JsonException)
            {
                return false;
            }
        }

        /// <inheritdoc />
        public override bool TryReadValue(JsonElement json, out object? value, out string? validationCode)
        {
            validationCode = null;
            if (!TryRead(json, out T read))
            {
                value = DefaultValue;
                return false;
            }

            validationCode = Validate?.Invoke(read);
            value = validationCode is null ? read : DefaultValue;
            return validationCode is null;
        }

        /// <summary>Serializes the default once, under the key's contract or the platform's rules.</summary>
        /// <param name="value">The default.</param>
        /// <param name="typeInfo">The key's contract, if any.</param>
        /// <returns>The JSON value.</returns>
        private static JsonElement Serialize(T value, JsonTypeInfo<T>? typeInfo)
        {
            return typeInfo is null
                ? JsonSerializer.SerializeToElement(value, TellmaJson.Options)
                : JsonSerializer.SerializeToElement(value, typeInfo);
        }
    }
}
