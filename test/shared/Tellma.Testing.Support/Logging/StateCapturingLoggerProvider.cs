// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.Logging;
using System.Collections;
using System.Collections.Concurrent;
using System.Globalization;

namespace Tellma.Testing.Support.Logging
{
    /// <summary>
    ///     A logger provider, enabled at every level, that keeps each event's rendered message and
    ///     every text its structured state and scopes carry — for asserting that a value never
    ///     reaches a log in any form a sink could serialise.
    /// </summary>
    /// <remarks>
    ///     A structured sink serialises an event's state rather than its message, and a state's
    ///     members may be anything: the HTTP client factory's header events, for one, carry each
    ///     header's values as a sequence of strings. So the state is walked, not rendered: every
    ///     key, every value and every element of a nested sequence is kept as text, which is what a
    ///     sink that serialises state would write.
    /// </remarks>
    public sealed class StateCapturingLoggerProvider : ILoggerProvider
    {
        /// <summary>How deep a state is walked; deeper than any real log state nests.</summary>
        private const int MaxDepth = 8;

        /// <summary>The entries received so far.</summary>
        private readonly ConcurrentQueue<CapturedLogEntry> _entries = new();

        /// <summary>Every event and scope received so far, in order.</summary>
        public IReadOnlyList<CapturedLogEntry> Entries => [.. _entries];

        /// <summary>The entries any of whose texts contains a value.</summary>
        /// <param name="value">The value, compared ordinally.</param>
        /// <returns>The entries, in order.</returns>
        public IReadOnlyList<CapturedLogEntry> Mentioning(string value)
        {
            ArgumentException.ThrowIfNullOrEmpty(value);

            return [.. _entries.Where(entry => entry.Texts.Any(text => text.Contains(value, StringComparison.Ordinal)))];
        }

        /// <inheritdoc />
        public ILogger CreateLogger(string categoryName)
        {
            return new Logger(categoryName, _entries);
        }

        /// <inheritdoc />
        public void Dispose()
        {
        }

        /// <summary>Every text a log state carries: its own rendering, then each member, walked.</summary>
        /// <param name="state">The state.</param>
        /// <returns>The texts.</returns>
        private static List<string> Flatten(object? state)
        {
            List<string> texts = [];
            Walk(state, texts, depth: 0);
            return texts;
        }

        /// <summary>Adds a value's texts: a string as is, a sequence element by element, a pair as its key and value.</summary>
        /// <param name="value">The value.</param>
        /// <param name="texts">Where the texts go.</param>
        /// <param name="depth">How deep the walk is.</param>
        private static void Walk(object? value, List<string> texts, int depth)
        {
            if (value is null || depth > MaxDepth)
            {
                return;
            }

            // A value's own rendering first: for a top-level state that is the formatted message
            // (redacted, where the producer redacts), for a leaf it is what a sink would write.
            string? rendered = Convert.ToString(value, CultureInfo.InvariantCulture);
            if (!string.IsNullOrEmpty(rendered))
            {
                texts.Add(rendered);
            }

            if (value is string)
            {
                return;
            }

            // A KeyValuePair of any type arguments: the structured member a sink names and writes.
            Type type = value.GetType();
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(KeyValuePair<,>))
            {
                Walk(type.GetProperty(nameof(KeyValuePair<,>.Key))!.GetValue(value), texts, depth + 1);
                Walk(type.GetProperty(nameof(KeyValuePair<,>.Value))!.GetValue(value), texts, depth + 1);
                return;
            }

            // Any other sequence — the state's member list, a header's values — element by element.
            if (value is IEnumerable sequence)
            {
                foreach (object? element in sequence)
                {
                    Walk(element, texts, depth + 1);
                }
            }
        }

        /// <summary>A logger that records each event and each scope it is given.</summary>
        /// <param name="category">The logger's category.</param>
        /// <param name="entries">Where the entries go.</param>
        private sealed class Logger(string category, ConcurrentQueue<CapturedLogEntry> entries) : ILogger
        {
            /// <inheritdoc />
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull
            {
                // A scope's state reaches a sink as properties of every event under it.
                entries.Enqueue(new CapturedLogEntry(category, Level: null, default, Message: null, Flatten(state)));
                return null;
            }

            /// <inheritdoc />
            public bool IsEnabled(LogLevel logLevel)
            {
                return true;
            }

            /// <inheritdoc />
            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                ArgumentNullException.ThrowIfNull(formatter);

                string message = formatter(state, exception);
                List<string> texts = Flatten(state);
                texts.Add(message);
                if (exception is not null)
                {
                    texts.Add(exception.ToString());
                }

                entries.Enqueue(new CapturedLogEntry(category, logLevel, eventId, message, texts));
            }
        }
    }

    /// <summary>One captured event, or one scope a logger began.</summary>
    /// <param name="Category">The logger's category.</param>
    /// <param name="Level">The event's level; null for a scope.</param>
    /// <param name="EventId">The event's id; the default for a scope.</param>
    /// <param name="Message">The event's rendered message; null for a scope.</param>
    /// <param name="Texts">Every text the event or scope carries: its state walked, its message, its exception.</param>
    public sealed record CapturedLogEntry(
        string Category, LogLevel? Level, EventId EventId, string? Message, IReadOnlyList<string> Texts);
}
