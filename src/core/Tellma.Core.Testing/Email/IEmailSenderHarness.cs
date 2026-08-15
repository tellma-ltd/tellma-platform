// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Email;

namespace Tellma.Core.Testing.Email
{
    /// <summary>What a transport's wire can be told to answer for one message.</summary>
    public enum ScriptedReplyKind
    {
        /// <summary>Accept the message.</summary>
        Accept,

        /// <summary>Refuse it in a way the caller may retry (429, 4xx SMTP, 5xx HTTP).</summary>
        TransientRefusal,

        /// <summary>Refuse it permanently (400 HTTP, 5xx SMTP).</summary>
        PermanentRefusal,

        /// <summary>Refuse the credential (401/403, AUTH failure).</summary>
        AuthFailure,

        /// <summary>Refuse with the provider's throttling signal (429).</summary>
        Throttle,
    }

    /// <summary>Which conformance cases a transport can express on its own wire.</summary>
    [Flags]
    public enum SenderCapabilities
    {
        /// <summary>Nothing beyond accepting messages — the development log sink.</summary>
        None = 0,

        /// <summary>Per-message refusals can be scripted.</summary>
        ScriptedOutcomes = 1,

        /// <summary>The credential can be refused before anything is sent.</summary>
        UpFrontAuthFailure = 2,

        /// <summary>
        ///     The credential can be refused after a message has already succeeded. A serially
        ///     connected transport has no analogue: its authentication happens once, up front.
        /// </summary>
        MidBatchAuthFailure = 4,

        /// <summary>The provider's throttling signal can be scripted.</summary>
        Throttling = 8,
    }

    /// <summary>
    ///     A transport under conformance test, plus the seam that lets the suite tell its wire what to
    ///     answer.
    /// </summary>
    /// <remarks>
    ///     Each conformance message carries its ordinal in its subject (see
    ///     <see cref="EmailSenderConformanceTests.ConformanceMessage" />), which is how a harness keys
    ///     its script without needing a transport-specific correlation channel.
    /// </remarks>
    public interface IEmailSenderHarness : IAsyncDisposable
    {
        /// <summary>The sender under test.</summary>
        IEmailSender Sender { get; }

        /// <summary>Which cases this transport can express.</summary>
        SenderCapabilities Capabilities { get; }

        /// <summary>How many requests the sender issues at once; 1 for a serial transport.</summary>
        int MaxConcurrency { get; }

        /// <summary>Tells the wire what to answer for one message.</summary>
        /// <param name="ordinal">The message's conformance ordinal.</param>
        /// <param name="reply">What to answer.</param>
        void Script(int ordinal, ScriptedReplyKind reply);

        /// <summary>
        ///     The ordinals whose messages actually reached the wire, in arrival order. Read after
        ///     the send has returned, so it is a settled value rather than a race.
        /// </summary>
        IReadOnlyList<int> AttemptedOrdinals { get; }
    }
}
