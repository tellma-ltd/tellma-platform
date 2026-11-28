// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Globalization;
using System.Text.Json;
using Tellma.Core.Abstractions.Errors;

namespace Tellma.Core.Mcp.Tools
{
    /// <summary>
    ///     A tool call's arguments, read against the tool's closed schema: an argument the schema does
    ///     not name, or one of the wrong type or out of range, is a bad-request error naming the
    ///     argument, which the model reads to correct its call.
    /// </summary>
    /// <remarks>
    ///     The SDK hands a tool its arguments unvalidated, so every tool reads them through this type
    ///     and none trusts the schema it declared. A JSON <c>null</c> reads as an absent argument.
    /// </remarks>
    internal sealed class ToolArguments
    {
        /// <summary>The arguments as the call sent them.</summary>
        private readonly IDictionary<string, JsonElement> _values;

        /// <summary>Reads a call's arguments, refusing any its tool does not declare.</summary>
        /// <param name="values">The call's arguments; null for none.</param>
        /// <param name="declared">The argument names the tool's schema declares.</param>
        /// <exception cref="BadRequestException">An argument is not one the tool declares.</exception>
        public ToolArguments(IDictionary<string, JsonElement>? values, IReadOnlyCollection<string> declared)
        {
            ArgumentNullException.ThrowIfNull(declared);
            _values = values ?? new Dictionary<string, JsonElement>(StringComparer.Ordinal);
            foreach (string name in _values.Keys)
            {
                if (!declared.Contains(name, StringComparer.Ordinal))
                {
                    string expected = declared.Count == 0 ? "none" : string.Join(", ", declared);
                    throw Invalid(name, $"The argument '{name}' is not one this tool takes; it takes: {expected}.");
                }
            }
        }

        /// <summary>A string argument.</summary>
        /// <param name="name">The argument.</param>
        /// <returns>Its value, or null when absent, or empty or white space.</returns>
        /// <exception cref="BadRequestException">The value is not a string.</exception>
        public string? String(string name)
        {
            return Value(name) is not JsonElement value
                ? null
                : value.ValueKind == JsonValueKind.String
                ? NullIfBlank(value.GetString())
                : throw Invalid(name, $"The argument '{name}' must be a string.");
        }

        /// <summary>A required string argument.</summary>
        /// <param name="name">The argument.</param>
        /// <returns>Its value.</returns>
        /// <exception cref="BadRequestException">The argument is absent, blank or not a string.</exception>
        public string RequiredString(string name)
        {
            return String(name) ?? throw Invalid(name, $"The argument '{name}' is required.");
        }

        /// <summary>An integer argument within a range.</summary>
        /// <param name="name">The argument.</param>
        /// <param name="minimum">The smallest value accepted.</param>
        /// <param name="maximum">The largest value accepted.</param>
        /// <returns>Its value, or null when absent.</returns>
        /// <exception cref="BadRequestException">The value is not an integer in the range.</exception>
        public int? Integer(string name, int minimum, int maximum)
        {
            return Value(name) is not JsonElement value
                ? null
                : value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out int number) && number >= minimum && number <= maximum
                ? number
                : throw Invalid(name, string.Create(CultureInfo.InvariantCulture, $"The argument '{name}' must be an integer from {minimum} to {maximum}."));
        }

        /// <summary>A boolean argument.</summary>
        /// <param name="name">The argument.</param>
        /// <returns>Its value, or false when absent.</returns>
        /// <exception cref="BadRequestException">The value is not a boolean.</exception>
        public bool Boolean(string name)
        {
            return Value(name) is JsonElement value && value.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.Undefined or JsonValueKind.Object or JsonValueKind.Array or JsonValueKind.String or JsonValueKind.Number or JsonValueKind.Null
                    => throw Invalid(name, $"The argument '{name}' must be true or false."),
                _ => throw Invalid(name, $"The argument '{name}' must be true or false."),
            };
        }

        /// <summary>A string argument from a closed set, matched ignoring case.</summary>
        /// <param name="name">The argument.</param>
        /// <param name="allowed">The values accepted, as the schema spells them.</param>
        /// <returns>The value as the set spells it, or null when absent.</returns>
        /// <exception cref="BadRequestException">The value is not one of the set.</exception>
        public string? OneOf(string name, IReadOnlyList<string> allowed)
        {
            ArgumentNullException.ThrowIfNull(allowed);
            return String(name) is not string value
                ? null
                : allowed.FirstOrDefault(entry => string.Equals(entry, value, StringComparison.OrdinalIgnoreCase))
                ?? throw Invalid(name, $"The argument '{name}' must be one of: {string.Join(", ", allowed)}.");
        }

        /// <summary>A list of record ids, each an integer or a string of digits.</summary>
        /// <param name="name">The argument.</param>
        /// <param name="minimum">The fewest ids accepted.</param>
        /// <param name="maximum">The most ids accepted.</param>
        /// <returns>The ids, in order; empty when the argument is absent and none are required.</returns>
        /// <exception cref="BadRequestException">The value is not such a list, or its length is out of range.</exception>
        public IReadOnlyList<long> Ids(string name, int minimum, int maximum)
        {
            if (Value(name) is not JsonElement value)
            {
                return minimum == 0 ? [] : throw Invalid(name, $"The argument '{name}' is required.");
            }

            string rule = string.Create(CultureInfo.InvariantCulture, $"The argument '{name}' must be a list of {minimum} to {maximum} record ids.");
            if (value.ValueKind != JsonValueKind.Array)
            {
                throw Invalid(name, rule);
            }

            int count = value.GetArrayLength();
            if (count < minimum || count > maximum)
            {
                throw Invalid(name, rule);
            }

            // --- Each id: a JSON integer, or the digits the wire writes a long id as ---
            List<long> ids = new(count);
            int index = 0;
            foreach (JsonElement item in value.EnumerateArray())
            {
                bool read = item.ValueKind switch
                {
                    JsonValueKind.Number => item.TryGetInt64(out long number) && Add(ids, number),
                    JsonValueKind.String => long.TryParse(item.GetString(), NumberStyles.None, CultureInfo.InvariantCulture, out long parsed) && Add(ids, parsed),
                    JsonValueKind.Undefined or JsonValueKind.Object or JsonValueKind.Array or JsonValueKind.True or JsonValueKind.False or JsonValueKind.Null => false,
                    _ => false,
                };
                if (!read)
                {
                    string path = string.Create(CultureInfo.InvariantCulture, $"{name}[{index}]");
                    throw Invalid(path, $"The id at '{path}' must be a positive whole number.");
                }

                index++;
            }

            return ids;
        }

        /// <summary>A list of strings.</summary>
        /// <param name="name">The argument.</param>
        /// <returns>The strings, or null when absent or empty.</returns>
        /// <exception cref="BadRequestException">The value is not a list of non-blank strings.</exception>
        public IReadOnlyList<string>? Strings(string name)
        {
            if (Value(name) is not JsonElement value)
            {
                return null;
            }

            if (value.ValueKind != JsonValueKind.Array)
            {
                throw Invalid(name, $"The argument '{name}' must be a list of strings.");
            }

            List<string> items = [];
            foreach (JsonElement item in value.EnumerateArray())
            {
                items.Add(item.ValueKind == JsonValueKind.String && NullIfBlank(item.GetString()) is string text
                    ? text
                    : throw Invalid(name, $"The argument '{name}' must be a list of non-empty strings."));
            }

            return items.Count == 0 ? null : items;
        }

        /// <summary>An object of JSON scalars keyed by name: the values of a query's parameters.</summary>
        /// <param name="name">The argument.</param>
        /// <returns>The values by name, or null when absent or empty.</returns>
        /// <exception cref="BadRequestException">The value is not an object, or a member is an object or a list.</exception>
        public IReadOnlyDictionary<string, JsonElement>? Scalars(string name)
        {
            if (Value(name) is not JsonElement value)
            {
                return null;
            }

            if (value.ValueKind != JsonValueKind.Object)
            {
                throw Invalid(name, $"The argument '{name}' must be an object whose members are the parameters' values.");
            }

            Dictionary<string, JsonElement> scalars = new(StringComparer.OrdinalIgnoreCase);
            foreach (JsonProperty member in value.EnumerateObject())
            {
                if (member.Value.ValueKind is JsonValueKind.Object or JsonValueKind.Array)
                {
                    string path = $"{name}.{member.Name}";
                    throw Invalid(path, $"The parameter value at '{path}' must be a string, a number, a boolean or null.");
                }

                // A parameter named twice, in any case, would bind whichever came last.
                if (!scalars.TryAdd(member.Name, member.Value.Clone()))
                {
                    string path = $"{name}.{member.Name}";
                    throw Invalid(path, $"The parameter '{member.Name}' is given more than once.");
                }
            }

            return scalars.Count == 0 ? null : scalars;
        }

        /// <summary>Whether the call sent an argument, null aside.</summary>
        /// <param name="name">The argument.</param>
        /// <returns>True when it did.</returns>
        public bool Has(string name)
        {
            return Value(name) is not null;
        }

        /// <summary>The refusal of an argument, naming its path.</summary>
        /// <param name="path">The argument's path.</param>
        /// <param name="detail">What is wrong, in English.</param>
        /// <returns>The exception to throw.</returns>
        public static BadRequestException Invalid(string path, string detail)
        {
            return new BadRequestException(detail, new Dictionary<string, object?>(StringComparer.Ordinal) { ["path"] = path });
        }

        /// <summary>An argument's value, a JSON null read as absent.</summary>
        /// <param name="name">The argument.</param>
        /// <returns>The value, or null.</returns>
        private JsonElement? Value(string name)
        {
            return _values.TryGetValue(name, out JsonElement value) && value.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined)
                ? value
                : null;
        }

        /// <summary>Adds an id that is positive.</summary>
        /// <param name="ids">The ids so far.</param>
        /// <param name="id">The id.</param>
        /// <returns>True when it was added.</returns>
        private static bool Add(List<long> ids, long id)
        {
            if (id <= 0)
            {
                return false;
            }

            ids.Add(id);
            return true;
        }

        /// <summary>A string, or null when it is empty or white space.</summary>
        /// <param name="value">The string.</param>
        /// <returns>The string, or null.</returns>
        private static string? NullIfBlank(string? value)
        {
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }
    }
}
