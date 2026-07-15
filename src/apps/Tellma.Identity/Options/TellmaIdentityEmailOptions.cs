// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Identity.Options
{
    /// <summary>
    ///     Outgoing email configuration. SMTP is the built-in production transport (first-class
    ///     on-prem); the Development sink replaces it locally. A deployment must configure one of
    ///     the two — invitations and recovery depend on email.
    /// </summary>
    public sealed class TellmaIdentityEmailOptions
    {
        /// <summary>The SMTP host; when set, the SMTP sender is used.</summary>
        public string? SmtpHost { get; set; }

        /// <summary>The SMTP port (587 with STARTTLS by default).</summary>
        public int SmtpPort { get; set; } = 587;

        /// <summary>Negotiate STARTTLS (true, the default) or implicit TLS on connect.</summary>
        public bool UseStartTls { get; set; } = true;

        /// <summary>The SMTP username, when the server requires authentication.</summary>
        public string? Username { get; set; }

        /// <summary>The SMTP password.</summary>
        public string? Password { get; set; }

        /// <summary>The sender address.</summary>
        public string FromAddress { get; set; } = "no-reply@tellma.com";

        /// <summary>The sender display name.</summary>
        public string FromDisplayName { get; set; } = "Tellma";
    }
}
