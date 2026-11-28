// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Jeffijoe.MessageFormat;
using Jeffijoe.MessageFormat.Formatting;
using Jeffijoe.MessageFormat.Parsing;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Globalization;

namespace Tellma.Core.Localization
{
    /// <summary>
    ///     Formats resource values as ICU messages (plural, select, selectordinal, nesting, and dates
    ///     and times in the flow's calendar and display zone), through one formatter shared by every
    ///     localizer in the process.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The formatter's pattern cache is a concurrent dictionary keyed by pattern, and patterns
    ///         come from resources only, so the cache is safe to share and bounded by the resource
    ///         count. The culture is passed on every call.
    ///     </para>
    ///     <para>
    ///         A value that is not a valid message, or that cannot be formatted with its arguments,
    ///         renders as its raw value and is logged once per key and culture: text never fails the
    ///         work that renders it. A value read without any arguments (a caller reading the template
    ///         itself) whose only fault is an argument it was not given renders raw without a log.
    ///     </para>
    /// </remarks>
    internal sealed class IcuMessageRenderer
    {
        /// <summary>How many keys the renderer remembers having reported; beyond it, further failures are not logged.</summary>
        private const int ReportedCapacity = 10_000;

        /// <summary>The shared formatter, with the calendar-aware date and time hook.</summary>
        private readonly MessageFormatter _formatter = new(useCache: true, culture: null, customValueFormatter: new CalendarValueFormatter());

        /// <summary>The keys and cultures already reported as malformed.</summary>
        private readonly ConcurrentDictionary<(string BaseName, string Key, string Culture), byte> _reported = new();

        /// <summary>Where a malformed message is reported.</summary>
        private readonly ILogger _logger;

        /// <summary>Creates the renderer.</summary>
        /// <param name="logger">Where malformed values are reported.</param>
        /// <exception cref="ArgumentNullException"><paramref name="logger" /> is null.</exception>
        public IcuMessageRenderer(ILogger logger)
        {
            ArgumentNullException.ThrowIfNull(logger);
            _logger = logger;
        }

        /// <summary>Formats one resource value.</summary>
        /// <param name="baseName">The base name of the resources the value came from.</param>
        /// <param name="key">The value's key.</param>
        /// <param name="pattern">The value.</param>
        /// <param name="arguments">The named arguments.</param>
        /// <param name="culture">The culture to format in.</param>
        /// <param name="argumentsSupplied">Whether the caller passed an argument object at all.</param>
        /// <returns>The formatted text, or <paramref name="pattern" /> when it cannot be formatted.</returns>
        public string Render(
            string baseName,
            string key,
            string pattern,
            IReadOnlyDictionary<string, object?> arguments,
            CultureInfo culture,
            bool argumentsSupplied)
        {
            // --- A value with no placeholder needs no parse: most of a catalogue ---
            if (!pattern.Contains('{', StringComparison.Ordinal))
            {
                return pattern;
            }

            // --- Format under the target culture as the current one too ---
            // The library writes the '#' of a plural and a select's key through the current culture,
            // so a scope's culture must be current for the duration of the call. The swap is
            // synchronous and undone before returning.
            CultureInfo previous = CultureInfo.CurrentCulture;
            bool swap = !ReferenceEquals(previous, culture);
            try
            {
                if (swap)
                {
                    CultureInfo.CurrentCulture = culture;
                }

                return _formatter.FormatMessage(pattern, arguments, culture);
            }
            catch (VariableNotFoundException) when (!argumentsSupplied)
            {
                // The caller read the template without arguments: hand it back as it is.
                return pattern;
            }
            catch (Exception exception) when (IsFormattingFailure(exception))
            {
                if (!_reported.ContainsKey((baseName, key, culture.Name)) && _reported.Count < ReportedCapacity
                    && _reported.TryAdd((baseName, key, culture.Name), 0))
                {
                    LocalizationLog.MessageFormatMalformed(_logger, exception, baseName, key, culture.Name);
                }

                return pattern;
            }
            finally
            {
                if (swap)
                {
                    CultureInfo.CurrentCulture = previous;
                }
            }
        }

        /// <summary>Whether an exception is a failure to format a message, as opposed to a fault of the process.</summary>
        /// <param name="exception">The exception.</param>
        /// <returns>True for the library's parse and format failures and the conversions its formatters make.</returns>
        private static bool IsFormattingFailure(Exception exception)
        {
            // The parse failures (unbalanced braces, a malformed literal) do not share a base with
            // the library's other exceptions, so each is named. A plural over a value that is not a
            // number fails in the conversion the library makes, and an instant outside the range of
            // a zone conversion in the date hook.
            return exception
                is MessageFormatterException
                or UnbalancedBracesException
                or MalformedLiteralException
                or VariableNotFoundException
                or FormatterNotFoundException
                or UnsupportedFormatStyleException
                or FormatException
                or InvalidCastException
                or OverflowException
                or ArgumentException;
        }
    }
}
