// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.Logging;
using Xunit;

namespace Tellma.Core.Testing.Diagnostics
{
    /// <summary>
    ///     Routes log messages into the running test's output, so a failure carries the code's own
    ///     account of what happened alongside the assertion.
    /// </summary>
    /// <remarks>
    ///     Written for the live suites, where the failure a maintainer reads is hours old and the run
    ///     that produced it cannot be stepped through: the transport's own log line naming a status
    ///     and an error code is often the whole diagnosis. <c>AddLogging()</c> registers no provider
    ///     of its own, so without this one those lines go nowhere.
    /// </remarks>
    public sealed class TestOutputLoggerProvider : ILoggerProvider
    {
        /// <inheritdoc />
        public ILogger CreateLogger(string categoryName)
        {
            return new TestOutputLogger(categoryName);
        }

        /// <inheritdoc />
        public void Dispose()
        {
            // The sink is the ambient test context, so this provider holds nothing to release.
        }

        /// <summary>A logger that writes one line per record to the ambient test's output.</summary>
        /// <param name="categoryName">The category the records are attributed to.</param>
        private sealed class TestOutputLogger(string categoryName) : ILogger
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
                return logLevel != LogLevel.None;
            }

            /// <inheritdoc />
            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                ArgumentNullException.ThrowIfNull(formatter);

                // The output helper is ambient per test and absent outside one, so a record written
                // by a straggling continuation is dropped rather than allowed to fail the run.
                ITestOutputHelper? output = TestContext.Current.TestOutputHelper;
                if (output is null)
                {
                    return;
                }

                output.WriteLine($"[{logLevel}] {categoryName}: {formatter(state, exception)}");

                if (exception is not null)
                {
                    output.WriteLine(exception.ToString());
                }
            }
        }
    }
}
