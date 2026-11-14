// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace Tellma.Distro.Acme.IntegrationTests.Infrastructure
{
    /// <summary>
    ///     A logger provider that keeps a host's warnings and errors, so a failed assertion can say
    ///     what the host said about it.
    /// </summary>
    internal sealed class WarningLog : ILoggerProvider
    {
        private readonly ConcurrentQueue<string> _entries = new();

        /// <summary>The warnings and errors so far, one line each with its category and any exception.</summary>
        public override string ToString()
        {
            return string.Join('\n', _entries);
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

        /// <summary>One category's logger.</summary>
        /// <param name="category">The category.</param>
        /// <param name="entries">Where entries go.</param>
        private sealed class Logger(string category, ConcurrentQueue<string> entries) : ILogger
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
                return logLevel >= LogLevel.Warning;
            }

            /// <inheritdoc />
            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                ArgumentNullException.ThrowIfNull(formatter);
                if (IsEnabled(logLevel))
                {
                    entries.Enqueue($"{logLevel} {category}: {formatter(state, exception)}{(exception is null ? string.Empty : " | " + exception.GetType().Name + ": " + exception.Message)}");
                }
            }
        }
    }
}
