// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.Logging;
using System.Collections;
using System.Collections.Concurrent;
using System.Globalization;
using System.Reflection;

namespace Tellma.Testing.Support.Logging
{
    /// <summary>
    ///     A logger provider, enabled at every level, that keeps each event's rendered message and
    ///     every text its structured state and scopes carry — for asserting that a value never
    ///     reaches a log in any form a sink could serialise.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         A structured sink serialises an event's state rather than its message, and a
    ///         state's members may be anything: the HTTP client factory's header events, for one,
    ///         carry each header's values as a sequence of strings. So the state is walked, not
    ///         rendered, which is what a sink that serialises state would write: each value's own
    ///         rendering; the key and value of every pair and dictionary entry; every element of a
    ///         sequence; and every readable public property of any other object, as a destructuring
    ///         sink writes a logged object.
    ///     </para>
    ///     <para>
    ///         The walk stops at strings, primitives, enums and the framework's value-like types,
    ///         at reflection and delegate objects, which describe code rather than data, and at
    ///         tasks, whose result a property read would wait for. Each object is walked once, so
    ///         a cycle ends, and no deeper than any real log state nests.
    ///     </para>
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
            Walk(state, texts, new HashSet<object>(ReferenceEqualityComparer.Instance), depth: 0);
            return texts;
        }

        /// <summary>
        ///     Adds a value's texts: a string as is, a pair or a dictionary entry as its key and value,
        ///     a sequence element by element, and any other object property by property.
        /// </summary>
        /// <param name="value">The value.</param>
        /// <param name="texts">Where the texts go.</param>
        /// <param name="visited">The objects already walked, so a cycle is walked once.</param>
        /// <param name="depth">How deep the walk is.</param>
        private static void Walk(object? value, List<string> texts, HashSet<object> visited, int depth)
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

            Type type = value.GetType();
            if (IsScalar(type))
            {
                return;
            }

            // The cycle guard: a reference already walked adds nothing new. A boxed struct is a
            // fresh box each time, so only a reference type can close a cycle.
            if (!type.IsValueType && !visited.Add(value))
            {
                return;
            }

            // A KeyValuePair of any type arguments, or a non-generic dictionary's entry: the
            // structured member a sink names and writes.
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(KeyValuePair<,>))
            {
                Walk(type.GetProperty(nameof(KeyValuePair<,>.Key))!.GetValue(value), texts, visited, depth + 1);
                Walk(type.GetProperty(nameof(KeyValuePair<,>.Value))!.GetValue(value), texts, visited, depth + 1);
                return;
            }

            if (value is DictionaryEntry entry)
            {
                Walk(entry.Key, texts, visited, depth + 1);
                Walk(entry.Value, texts, visited, depth + 1);
                return;
            }

            // Any other sequence (the state's member list, a header's values, a dictionary),
            // element by element.
            if (value is IEnumerable sequence)
            {
                foreach (object? element in sequence)
                {
                    Walk(element, texts, visited, depth + 1);
                }

                return;
            }

            // Any other object, property by property: what a destructuring sink serialises for a
            // logged object. Indexers take arguments and are not members a sink writes; a getter
            // that throws contributes nothing, as a sink would skip it.
            foreach (PropertyInfo property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (!property.CanRead || property.GetIndexParameters().Length > 0)
                {
                    continue;
                }

                object? member;
                try
                {
                    member = property.GetValue(value);
                }
                catch (TargetInvocationException)
                {
                    continue;
                }

                texts.Add(property.Name);
                Walk(member, texts, visited, depth + 1);
            }
        }

        /// <summary>
        ///     Whether a type's rendering is all it carries: primitives, enums, strings and the
        ///     framework's value-like types; reflection and delegate objects, whose properties
        ///     describe code rather than data and fan out across the runtime; and tasks, whose
        ///     result a property read would wait for.
        /// </summary>
        /// <param name="type">The value's type.</param>
        /// <returns>True when the value is not walked further.</returns>
        private static bool IsScalar(Type type)
        {
            return type.IsPrimitive
                || type.IsEnum
                || type.IsPointer
                || type == typeof(string)
                || type == typeof(decimal)
                || type == typeof(DateTime)
                || type == typeof(DateTimeOffset)
                || type == typeof(DateOnly)
                || type == typeof(TimeOnly)
                || type == typeof(TimeSpan)
                || type == typeof(Guid)
                || type == typeof(Uri)
                || type == typeof(Version)
                || typeof(MemberInfo).IsAssignableFrom(type)
                || typeof(Assembly).IsAssignableFrom(type)
                || typeof(Module).IsAssignableFrom(type)
                || typeof(Delegate).IsAssignableFrom(type)
                || typeof(IAsyncResult).IsAssignableFrom(type);
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
