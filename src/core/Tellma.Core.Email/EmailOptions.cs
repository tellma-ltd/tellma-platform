// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Email
{
    /// <summary>
    ///     The pipeline's own configuration, bound from the <c>Email</c> section. Everything else
    ///     under that section belongs to a transport, which binds and validates its own subsection.
    /// </summary>
    public sealed class EmailOptions
    {
        /// <summary>The configuration section these options bind from.</summary>
        public const string SectionName = "Email";

        /// <summary>
        ///     The configuration name of the transport that sends mail ("acs-email", "sendgrid",
        ///     "smtp", "log-sink"), matched case-insensitively against the registered transports.
        ///     Required outside the Development environment, where it defaults to the log sink.
        /// </summary>
        public string? Provider { get; set; }
    }
}
