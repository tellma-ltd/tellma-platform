// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Jeffijoe.MessageFormat;
using Jeffijoe.MessageFormat.Formatting;
using Jeffijoe.MessageFormat.Parsing;
using Microsoft.Extensions.Localization;
using System.Globalization;

namespace Tellma.Identity.Infrastructure
{
    /// <summary>
    ///     Renders resource strings through ICU MessageFormat.
    ///     <para>
    ///         .NET's own localization is composite formatting — <c>{0}</c> substitution and
    ///         nothing more — which cannot express a sentence whose wording depends on its data.
    ///         Grammatical gender is the case that forces the issue: Arabic writes a different
    ///         verb to a man and to a woman, and the choice belongs in the translated string,
    ///         where a translator can see it, rather than in a branch in C#.
    ///     </para>
    ///     <para>
    ///         Values without ICU syntax pass through unchanged, so ordinary strings and the
    ///         positional <c>{0}</c> ones already in the catalog keep working: this decorates the
    ///         framework localizer rather than replacing it.
    ///     </para>
    /// </summary>
    /// <param name="inner">The resource lookup this wraps.</param>
    public sealed class IcuStringLocalizer<T>(IStringLocalizer<T> inner) : IStringLocalizer<T>
    {
        /// <inheritdoc />
        public LocalizedString this[string name]
        {
            get
            {
                LocalizedString value = inner[name];

                // No arguments means nothing to select on, but the value may still carry ICU
                // syntax that must not reach the user as braces.
                return Render(value, []);
            }
        }

        /// <inheritdoc />
        public LocalizedString this[string name, params object[] arguments]
        {
            get
            {
                ArgumentNullException.ThrowIfNull(arguments);
                return Render(inner[name], arguments);
            }
        }

        /// <inheritdoc />
        public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures)
        {
            return inner.GetAllStrings(includeParentCultures);
        }

        /// <summary>
        ///     Formats one looked-up value. Positional arguments are exposed under their index
        ///     (<c>{0}</c>), which is what the existing catalog uses; named arguments arrive as a
        ///     dictionary, which is how gender and any later plural reach the message.
        /// </summary>
        private static LocalizedString Render(LocalizedString value, object[] arguments)
        {
            if (value.ResourceNotFound)
            {
                return value;
            }

            Dictionary<string, object?> named = [];
            foreach ((object argument, int index) in arguments.Select(static (argument, index) => (argument, index)))
            {
                if (argument is IReadOnlyDictionary<string, object?> pairs)
                {
                    foreach ((string key, object? item) in pairs)
                    {
                        named[key] = item;
                    }

                    continue;
                }

                named[index.ToString(CultureInfo.InvariantCulture)] = argument;
            }

            return new LocalizedString(value.Name, IcuMessageFormatter.Format(value.Value, named), false, value.SearchedLocation);
        }
    }

    /// <summary>
    ///     The shared ICU formatter. <see cref="MessageFormatter" /> compiles patterns and caches
    ///     the result, which is worth keeping across requests — but the cache is not documented as
    ///     thread-safe, so each thread gets its own instance rather than sharing one behind a lock
    ///     that every rendered string would have to queue on.
    /// </summary>
    public static class IcuMessageFormatter
    {
        /// <summary>One formatter (and one pattern cache) per request-handling thread.</summary>
        private static readonly ThreadLocal<MessageFormatter> Formatter = new(static () => new MessageFormatter(useCache: true));

        /// <summary>The argument every message may select a grammatical form on.</summary>
        public const string GenderArgument = "gender";

        /// <summary>
        ///     The ICU <c>select</c> catch-all, and the value an unstated gender resolves to. Every
        ///     gendered message must carry an <c>other</c> branch that reads naturally on its own.
        /// </summary>
        public const string NeutralGender = "other";

        /// <summary>Formats an ICU pattern in the current UI culture.</summary>
        /// <param name="pattern">The resource value, with or without ICU syntax.</param>
        /// <param name="arguments">The named arguments the pattern may select on.</param>
        /// <returns>The rendered string.</returns>
        public static string Format(string pattern, IReadOnlyDictionary<string, object?> arguments)
        {
            ArgumentNullException.ThrowIfNull(pattern);
            ArgumentNullException.ThrowIfNull(arguments);

            // A value with no placeholders cannot need formatting, and skipping the parser keeps
            // the common case — most of the catalog — free.
            if (!pattern.Contains('{', StringComparison.Ordinal))
            {
                return pattern;
            }

            // A gendered message rendered without a gender must fall back to the neutral branch,
            // not to visible braces. This is also why the pre-sign-in pages read neutrally even
            // for a user we could identify: revealing that we know how to address someone would
            // disclose that the account exists.
            IReadOnlyDictionary<string, object?> resolved = arguments.ContainsKey(GenderArgument)
                ? arguments
                : new Dictionary<string, object?>(arguments, StringComparer.Ordinal) { [GenderArgument] = NeutralGender };

            // A malformed pattern is a content bug, and failing the page over it would turn a
            // typo in a translation into an outage on the sign-in path. Show the raw value.
            // The parse failures (unbalanced braces, a malformed literal) do not share a base
            // with MessageFormatterException, so each is caught by name rather than by hierarchy.
            try
            {
                return Formatter.Value!.FormatMessage(pattern, resolved, CultureInfo.CurrentUICulture);
            }
            catch (Exception exception) when (exception
                is MessageFormatterException
                or UnbalancedBracesException
                or MalformedLiteralException
                or VariableNotFoundException
                or FormatterNotFoundException
                or UnsupportedFormatStyleException)
            {
                return pattern;
            }
        }
    }
}
