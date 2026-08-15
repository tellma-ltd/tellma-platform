// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.Logging.Testing;
using Tellma.Core.Abstractions.Email;
using Tellma.Core.Email.Tests.LogSink;
using Tellma.Core.Testing.Email;

namespace Tellma.Core.Email.Tests.Conformance
{
    /// <summary>
    ///     The development sink is an <see cref="IEmailSender" /> like any other, so it answers to
    ///     the same contract. It is also the transport a developer's first impression of the platform
    ///     runs on, which makes conformance here worth more than its simplicity suggests.
    /// </summary>
    public class LogSinkConformanceTests : EmailSenderConformanceTests
    {
        /// <inheritdoc />
        protected override ValueTask<IEmailSenderHarness> CreateHarnessAsync()
        {
            return ValueTask.FromResult<IEmailSenderHarness>(new LogSinkHarness());
        }

        private sealed class LogSinkHarness : IEmailSenderHarness
        {
            private readonly FakeLogger<LogSinkEmailSender> _logger = new();

            public LogSinkHarness()
            {
                Sender = new LogSinkEmailSender(
                    LogSinkEmailSenderTests.Environment("Development"), _logger, EmailChannel.Live);
            }

            public IEmailSender Sender { get; }

            // The sink accepts everything: there is no wire to refuse on, and no credential.
            public SenderCapabilities Capabilities => SenderCapabilities.None;

            public int MaxConcurrency => 1;

            public IReadOnlyList<int> AttemptedOrdinals
            {
                get
                {
                    // What "reached the wire" means for a sink is what it wrote, so the log is the
                    // record of attempts.
                    List<int> ordinals = [];
                    foreach (FakeLogRecord record in _logger.Collector.GetSnapshot())
                    {
                        string? subject = record.StructuredState?
                            .FirstOrDefault(static kv => kv.Key == "Subject").Value;

                        if (TryGetOrdinal(subject, out int ordinal))
                        {
                            ordinals.Add(ordinal);
                        }
                    }

                    return ordinals;
                }
            }

            public void Script(int ordinal, ScriptedReplyKind reply)
            {
                throw new NotSupportedException("The log sink has no wire to script.");
            }

            public ValueTask DisposeAsync()
            {
                return ValueTask.CompletedTask;
            }
        }
    }
}
