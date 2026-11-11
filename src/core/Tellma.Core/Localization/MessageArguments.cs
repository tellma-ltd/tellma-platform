// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Frozen;
using System.Reflection;

namespace Tellma.Core.Localization
{
    /// <summary>
    ///     Reads the named arguments of a message from the <c>arguments</c> of a localizer's indexer,
    ///     which holds at most one object: a dictionary of named arguments, or an object whose public
    ///     properties name them, such as an anonymous type.
    /// </summary>
    /// <remarks>
    ///     Messages name every argument, so a second object, or a lone value that names nothing (a
    ///     string, a number, a date, a list), is a positional argument and a programming error.
    /// </remarks>
    internal static class MessageArguments
    {
        /// <summary>No arguments.</summary>
        public static readonly IReadOnlyDictionary<string, object?> None = FrozenDictionary<string, object?>.Empty;

        /// <summary>The readers of the argument types seen so far, one per type.</summary>
        private static readonly ConcurrentDictionary<Type, PropertyInfo[]> Properties = new();

        /// <summary>Reads the named arguments.</summary>
        /// <param name="arguments">The indexer's arguments.</param>
        /// <returns>The arguments by name; <see cref="None" /> for none.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="arguments" /> is null.</exception>
        /// <exception cref="ArgumentException">
        ///     More than one object was passed, or the one passed is a lone value rather than a set of
        ///     named arguments.
        /// </exception>
        public static IReadOnlyDictionary<string, object?> From(object?[] arguments)
        {
            ArgumentNullException.ThrowIfNull(arguments);

            // --- Arity: none, or one object holding every argument by name ---
            if (arguments.Length > 1)
            {
                throw new ArgumentException(
                    $"A localized message takes its arguments by name, in one object, but {arguments.Length} were passed. "
                    + "Pass a single dictionary of named arguments, or an object (such as an anonymous type) whose properties name them.",
                    nameof(arguments));
            }

            object? bag = arguments.Length == 0 ? null : arguments[0];
            return bag switch
            {
                null => None,

                // --- The platform's message-argument shape, as it is ---
                IReadOnlyDictionary<string, object?> named => named,

                // --- Any other dictionary keyed by text ---
                IDictionary dictionary => FromDictionary(dictionary),

                // --- A lone value names no argument ---
                string or IConvertible or IFormattable or IEnumerable => throw new ArgumentException(
                    $"A localized message takes its arguments by name, but a lone value of type {bag.GetType()} was passed. "
                    + "Pass a dictionary of named arguments, or an object (such as an anonymous type) whose properties name them.",
                    nameof(arguments)),

                // --- An object whose public properties name the arguments ---
                _ => FromProperties(bag),
            };
        }

        /// <summary>Reads a non-generic dictionary whose keys are text.</summary>
        /// <param name="dictionary">The dictionary.</param>
        /// <returns>The arguments by name.</returns>
        /// <exception cref="ArgumentException">A key is not text.</exception>
        private static Dictionary<string, object?> FromDictionary(IDictionary dictionary)
        {
            Dictionary<string, object?> named = new(dictionary.Count, StringComparer.Ordinal);
            foreach (DictionaryEntry entry in dictionary)
            {
                if (entry.Key is not string key)
                {
                    throw new ArgumentException(
                        $"A dictionary of message arguments is keyed by argument name, but it holds the key '{entry.Key}' of type {entry.Key.GetType()}.",
                        nameof(dictionary));
                }

                named[key] = entry.Value;
            }

            return named;
        }

        /// <summary>Reads the public readable instance properties of an object, each an argument.</summary>
        /// <param name="bag">The object.</param>
        /// <returns>The arguments by name.</returns>
        private static Dictionary<string, object?> FromProperties(object bag)
        {
            // Reflection over a type runs once; the property list is reused for every later message.
            PropertyInfo[] properties = Properties.GetOrAdd(
                bag.GetType(),
                static type => [.. type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                    .Where(static property => property.CanRead && property.GetIndexParameters().Length == 0)]);

            Dictionary<string, object?> named = new(properties.Length, StringComparer.Ordinal);
            foreach (PropertyInfo property in properties)
            {
                named[property.Name] = property.GetValue(bag);
            }

            return named;
        }
    }
}
