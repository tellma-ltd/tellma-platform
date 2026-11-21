// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Serilog.Core;
using Serilog.Events;
using System.Collections.Concurrent;
using System.Globalization;

namespace Tellma.Identity.IntegrationTests.Infrastructure
{
    /// <summary>
    ///     A Serilog sink that keeps every event the host logs. The standalone host reads its sinks
    ///     from the service container as well as from configuration, so registering one as an
    ///     <see cref="ILogEventSink" /> service makes the host's own log observable to a test.
    /// </summary>
    public sealed class CapturingLogSink : ILogEventSink
    {
        private readonly ConcurrentQueue<LogEvent> _events = new();

        /// <summary>Every event logged so far, in order.</summary>
        public IReadOnlyList<LogEvent> Events => [.. _events];

        /// <inheritdoc />
        public void Emit(LogEvent logEvent)
        {
            ArgumentNullException.ThrowIfNull(logEvent);

            _events.Enqueue(logEvent);
        }

        /// <summary>The rendered messages of every event at or above a level.</summary>
        /// <param name="level">The lowest level to include.</param>
        /// <returns>The messages, in order.</returns>
        public IReadOnlyList<string> Messages(LogEventLevel level)
        {
            return [.. _events.Where(e => e.Level >= level).Select(static e => e.RenderMessage(CultureInfo.InvariantCulture))];
        }
    }
}
