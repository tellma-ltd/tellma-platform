// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Notifications
{
    /// <summary>
    ///     The inner exception of the <see cref="Abstractions.Errors.DependencyUnavailableException" />
    ///     a platform channel raises when its transport refused a test message outright, rather
    ///     than failing to take it: the address will not work on a retry, and the test is counted
    ///     as refused rather than as an outage. Its message is fixed and names no recipient.
    /// </summary>
    internal sealed class TestMessageRejectedException : Exception
    {
        /// <summary>Initializes the refusal.</summary>
        public TestMessageRejectedException()
            : base("The transport refused the test message.")
        {
        }
    }
}
