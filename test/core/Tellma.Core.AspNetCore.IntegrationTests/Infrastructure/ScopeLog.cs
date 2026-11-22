// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Globalization;

namespace Tellma.Core.AspNetCore.IntegrationTests.Infrastructure
{
    /// <summary>One log event as a structured sink receives it.</summary>
    /// <param name="Category">The logger's category.</param>
    /// <param name="EventName">The event's name.</param>
    /// <param name="Level">The level.</param>
    /// <param name="Properties">
    ///     The event's properties by name: its own values and those of every scope it was written in,
    ///     which the platform's structured logging folds into each event.
    /// </param>
    internal sealed record LoggedEvent(string Category, string? EventName, LogLevel Level, IReadOnlyDictionary<string, string?> Properties);

    /// <summary>
    ///     A logging provider that keeps the host's Information events and above with their
    ///     properties, as the platform's structured logging forwards them to the providers registered
    ///     after it, so a case can read what a request's log scope carried.
    /// </summary>
    internal sealed class ScopeLog : ILoggerProvider, ISupportExternalScope
    {
        /// <summary>The most events kept; older ones are dropped first.</summary>
        private const int Capacity = 50_000;

        private readonly ConcurrentQueue<LoggedEvent> _events = new();
        private IExternalScopeProvider _scopes = new LoggerExternalScopeProvider();

        /// <summary>The events kept, oldest first.</summary>
        public IReadOnlyCollection<LoggedEvent> Events => _events;

        /// <inheritdoc />
        public ILogger CreateLogger(string categoryName)
        {
            return new Logger(this, categoryName);
        }

        /// <inheritdoc />
        public void SetScopeProvider(IExternalScopeProvider scopeProvider)
        {
            _scopes = scopeProvider;
        }

        /// <inheritdoc />
        public void Dispose()
        {
            _events.Clear();
        }

        /// <summary>The values of a log state or scope, by name; the message template left out.</summary>
        /// <param name="state">The state.</param>
        /// <param name="into">The values found so far, which a later value of the same name overwrites.</param>
        private static void Collect(object? state, Dictionary<string, string?> into)
        {
            if (state is IEnumerable<KeyValuePair<string, object?>> pairs)
            {
                foreach (KeyValuePair<string, object?> pair in pairs)
                {
                    if (pair.Key != "{OriginalFormat}")
                    {
                        into[pair.Key] = Convert.ToString(pair.Value, CultureInfo.InvariantCulture);
                    }
                }
            }
        }

        /// <summary>A logger of one category.</summary>
        /// <param name="owner">The provider keeping the events.</param>
        /// <param name="category">The category.</param>
        private sealed class Logger(ScopeLog owner, string category) : ILogger
        {
            /// <inheritdoc />
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull
            {
                return owner._scopes.Push(state);
            }

            /// <inheritdoc />
            public bool IsEnabled(LogLevel logLevel)
            {
                return logLevel >= LogLevel.Information;
            }

            /// <inheritdoc />
            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                if (!IsEnabled(logLevel))
                {
                    return;
                }

                // --- Any scope the forwarding kept apart, then the event's own values, which win ---
                Dictionary<string, string?> properties = new(StringComparer.Ordinal);
                owner._scopes.ForEachScope(static (item, into) => Collect(item, into), properties);
                Collect(state, properties);

                owner._events.Enqueue(new LoggedEvent(category, eventId.Name, logLevel, properties));
                if (owner._events.Count > Capacity)
                {
                    _ = owner._events.TryDequeue(out _);
                }
            }
        }
    }
}
