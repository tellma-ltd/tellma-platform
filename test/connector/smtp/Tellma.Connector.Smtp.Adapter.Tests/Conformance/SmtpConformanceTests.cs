// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Connector.Smtp.Adapter.Tests.Infrastructure;
using Tellma.Core.Abstractions.Email;
using Tellma.Core.Testing.Email;

namespace Tellma.Connector.Smtp.Adapter.Tests.Conformance
{
    /// <summary>The SMTP transport answering the shared contract, over a real socket.</summary>
    public class SmtpConformanceTests : EmailSenderConformanceTests
    {
        /// <inheritdoc />
        protected override async ValueTask<IEmailSenderHarness> CreateHarnessAsync()
        {
            return await SmtpSenderHarness.StartAsync();
        }

        private sealed class SmtpSenderHarness : IEmailSenderHarness
        {
            private readonly InProcessSmtpServer _server;

            private SmtpSenderHarness(InProcessSmtpServer server)
            {
                _server = server;

                // A user name is always configured so the authentication cases have a credential to
                // refuse; the in-process server accepts anything unless told otherwise.
                Sender = SmtpSenderFactory.Sender(SmtpSenderFactory.Options(server.Port, "mailer"));
            }

            public IEmailSender Sender { get; }

            // A serially connected transport authenticates once, before anything is sent, so it has
            // no mid-batch credential failure; and its transient refusals carry no throttling signal
            // distinct from any other 4xx.
            public SenderCapabilities Capabilities =>
                SenderCapabilities.ScriptedOutcomes | SenderCapabilities.UpFrontAuthFailure;

            public int MaxConcurrency => 1;

            public IReadOnlyList<int> AttemptedOrdinals
            {
                get
                {
                    List<int> ordinals = [];
                    foreach (MimeKit.MimeMessage message in _server.Store.Messages)
                    {
                        if (TryGetOrdinal(message.Subject, out int ordinal))
                        {
                            ordinals.Add(ordinal);
                        }
                    }

                    return ordinals;
                }
            }

            public static async Task<SmtpSenderHarness> StartAsync()
            {
                return new SmtpSenderHarness(await InProcessSmtpServer.StartAsync());
            }

            public void Script(int ordinal, ScriptedReplyKind reply)
            {
                string subject = SubjectPrefix + ordinal.ToString(System.Globalization.CultureInfo.InvariantCulture);

                switch (reply)
                {
                    case ScriptedReplyKind.TransientRefusal:
                        _server.Store.RespondTo(subject, 451, "4.7.1 try again later");
                        break;
                    case ScriptedReplyKind.PermanentRefusal:
                        _server.Store.RespondTo(subject, 550, "5.1.1 unknown user");
                        break;
                    case ScriptedReplyKind.AuthFailure:
                        // Authentication happens once, up front, so any scripted credential failure
                        // refuses the whole session.
                        _server.Authenticator.RefuseEveryone = true;
                        break;
                    case ScriptedReplyKind.Accept:
                    case ScriptedReplyKind.Throttle:
                    default:
                        // Accepting is the default, and SMTP has no throttling signal of its own.
                        break;
                }
            }

            public ValueTask DisposeAsync()
            {
                return _server.DisposeAsync();
            }
        }
    }
}
