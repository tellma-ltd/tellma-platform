// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Email
{
    /// <summary>
    ///     The configuration shape of an email address, for the <c>From</c> every transport carries
    ///     in its own section.
    /// </summary>
    /// <remarks>
    ///     Settable properties rather than the immutable <see cref="EmailAddress" /> record, because
    ///     this is what a configuration binder populates. Every transport converts to
    ///     <see cref="EmailAddress" /> once, at validation time.
    /// </remarks>
    public sealed class EmailAddressOptions
    {
        /// <summary>The address ("no-reply@example.com").</summary>
        public string? Address { get; set; }

        /// <summary>The display name mail clients show, when one is configured.</summary>
        public string? DisplayName { get; set; }

        /// <summary>Converts to the immutable contract type.</summary>
        /// <returns>The configured address.</returns>
        /// <exception cref="InvalidOperationException"><see cref="Address" /> is blank; transports
        ///     validate it at startup, so reaching this is a bug in the transport.</exception>
        public EmailAddress ToEmailAddress()
        {
            return string.IsNullOrWhiteSpace(Address)
                ? throw new InvalidOperationException("The configured email address has no address value.")
                : new EmailAddress(Address, string.IsNullOrWhiteSpace(DisplayName) ? null : DisplayName);
        }
    }
}
