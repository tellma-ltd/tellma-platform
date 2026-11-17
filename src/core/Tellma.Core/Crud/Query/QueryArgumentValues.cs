// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Globalization;
using System.Text;
using System.Text.Json;
using Tellma.Core.Abstractions.Api;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Errors;
using Tellma.Core.Queryex;

namespace Tellma.Core.Crud.Query
{
    // The conversion is spec 0015 §4.3; the scalar grammars are the wire's (spec 0016 §3.2).
    /// <summary>
    ///     Binds a query's JSON arguments to the parameters its clauses declare: each value converted
    ///     to its parameter's inferred type by the wire's exact grammars, a parameter without a value
    ///     bound to null, and a value no clause mentions ignored.
    /// </summary>
    internal static class QueryArgumentValues
    {
        /// <summary>Binds every declared parameter.</summary>
        /// <param name="declarations">The declarations, from the discovery.</param>
        /// <param name="arguments">The caller's arguments, by name ignoring case.</param>
        /// <returns>The arguments the batch binds.</returns>
        /// <exception cref="BadRequestException">A value does not convert to its parameter's type.</exception>
        public static QueryArguments Bind(IReadOnlyList<QueryexParameterDeclaration> declarations, IReadOnlyDictionary<string, JsonElement> arguments)
        {
            ArgumentNullException.ThrowIfNull(declarations);
            ArgumentNullException.ThrowIfNull(arguments);
            QueryArguments bound = new();
            foreach (QueryexParameterDeclaration declaration in declarations)
            {
                object? value = arguments.TryGetValue(declaration.Name, out JsonElement element) ? Convert(declaration, element) : null;
                _ = bound.Add(declaration, value);
            }

            return bound;
        }

        /// <summary>Converts one JSON value to its parameter's type.</summary>
        /// <param name="declaration">The parameter.</param>
        /// <param name="element">The value.</param>
        /// <returns>The CLR value the batch binds; null for a JSON null.</returns>
        /// <exception cref="BadRequestException">The value does not convert.</exception>
        internal static object? Convert(QueryexParameterDeclaration declaration, JsonElement element)
        {
            ArgumentNullException.ThrowIfNull(declaration);
            if (element.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            {
                return null;
            }

            object? value = declaration.Type switch
            {
                QueryexType.QxBool => Bool(element),
                QueryexType.QxString => element.ValueKind == JsonValueKind.String ? element.GetString() : null,
                QueryexType.QxNumeric => Number(element),
                QueryexType.QxGuid => element.ValueKind == JsonValueKind.String && Guid.TryParseExact(element.GetString(), "D", out Guid guid) ? guid : null,
                QueryexType.QxDate => element.ValueKind == JsonValueKind.String
                    && DateOnly.TryParseExact(element.GetString(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateOnly date) ? date : null,
                QueryexType.QxDateTime => element.ValueKind == JsonValueKind.String && ScalarText.TryParseDateTime(Encoding.UTF8.GetBytes(element.GetString()!), out DateTime dateTime) ? dateTime : null,
                QueryexType.QxDateTimeOffset => element.ValueKind == JsonValueKind.String && ScalarText.TryParseDateTimeOffset(Encoding.UTF8.GetBytes(element.GetString()!), out DateTimeOffset instant) ? instant : null,

                // A hierarchy or geography value has no wire form a caller could send.
                QueryexType.QxHierarchyId or QueryexType.QxGeography => null,
                _ => null,
            };

            return value ?? throw new BadRequestException(
                $"The argument '{declaration.Name}' is not a {TypeName(declaration.Type)} value.",
                new Dictionary<string, object?>(StringComparer.Ordinal) { ["parameter"] = declaration.Name });
        }

        /// <summary>A boolean: a JSON <c>true</c> or <c>false</c>.</summary>
        /// <param name="element">The value.</param>
        /// <returns>The boolean; null for any other JSON value.</returns>
        private static bool? Bool(JsonElement element)
        {
            return element.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.Undefined or JsonValueKind.Object or JsonValueKind.Array or JsonValueKind.String or JsonValueKind.Number or JsonValueKind.Null => null,
                _ => null,
            };
        }

        /// <summary>A number as a JSON number, its exponent allowed, or as the wire's decimal string.</summary>
        /// <param name="element">The value.</param>
        /// <returns>The decimal, never rounded; null when the value is neither or is outside the grammar.</returns>
        private static decimal? Number(JsonElement element)
        {
            return element.ValueKind switch
            {
                JsonValueKind.Number => Decimal(element.GetRawText(), allowExponent: true),
                JsonValueKind.String => Decimal(element.GetString()!, allowExponent: false),
                JsonValueKind.Undefined or JsonValueKind.Object or JsonValueKind.Array or JsonValueKind.True or JsonValueKind.False or JsonValueKind.Null => null,
                _ => null,
            };
        }

        /// <summary>A decimal in the wire's grammar, never rounded; null when the text is outside it.</summary>
        /// <param name="text">The text.</param>
        /// <param name="allowExponent">Whether an exponent is accepted.</param>
        /// <returns>The value, or null.</returns>
        private static decimal? Decimal(string text, bool allowExponent)
        {
            return text.Length <= ScalarText.MaxTokenLength && ScalarText.TryParseDecimal(Encoding.UTF8.GetBytes(text), allowExponent, out decimal value) ? value : null;
        }

        /// <summary>A type's name as the wire writes it.</summary>
        /// <param name="type">The type.</param>
        /// <returns>The name, without the <c>Qx</c> prefix.</returns>
        private static string TypeName(QueryexType type)
        {
            string name = type.ToString();
            return name.StartsWith("Qx", StringComparison.Ordinal) ? name[2..] : name;
        }
    }
}
