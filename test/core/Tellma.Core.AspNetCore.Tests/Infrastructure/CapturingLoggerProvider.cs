// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace Tellma.Core.AspNetCore.Tests.Infrastructure
{
    /// <summary>
    ///     A logger provider that keeps every event it receives: its level, its rendered message and
    ///     its structured members (for an event Serilog forwards, the event's properties).
    /// </summary>
    internal sealed class CapturingLoggerProvider : ILoggerProvider
    {
        /// <summary>The events received so far.</summary>
        private readonly ConcurrentQueue<CapturedEvent> _events = new();

        /// <summary>The events received so far, in order.</summary>
        public IReadOnlyList<CapturedEvent> Events => [.. _events];

        /// <summary>How many messages contain a text.</summary>
        /// <param name="text">The text.</param>
        /// <returns>The count.</returns>
        public int Count(string text)
        {
            return _events.Count(captured => captured.Message.Contains(text, StringComparison.Ordinal));
        }

        /// <inheritdoc />
        public ILogger CreateLogger(string categoryName)
        {
            return new Logger(_events);
        }

        /// <inheritdoc />
        public void Dispose()
        {
        }

        /// <summary>A logger that records each event.</summary>
        /// <param name="events">Where the events go.</param>
        private sealed class Logger(ConcurrentQueue<CapturedEvent> events) : ILogger
        {
            /// <inheritdoc />
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull
            {
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

                // A structured state lists its members; a scalar's text is quoted as Serilog renders it.
                Dictionary<string, string?> members = [];
                if (state is IEnumerable<KeyValuePair<string, object?>> structured)
                {
                    foreach (KeyValuePair<string, object?> member in structured)
                    {
                        members[member.Key] = member.Value?.ToString()?.Trim('"');
                    }
                }

                events.Enqueue(new CapturedEvent(logLevel, formatter(state, exception), members));
            }
        }
    }

    /// <summary>One captured log event.</summary>
    /// <param name="Level">Its level.</param>
    /// <param name="Message">Its rendered message.</param>
    /// <param name="Members">Its structured members, each rendered as text.</param>
    internal sealed record CapturedEvent(LogLevel Level, string Message, IReadOnlyDictionary<string, string?> Members);
}
