// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Distro.Acme
{
    /// <summary>
    ///     The validation codes Acme's stacks and settings refuse a write with. Each is a dotted
    ///     PascalCase resource key; its message is the string of the same key with each dot as an
    ///     underscore.
    /// </summary>
    public static class AcmeValidationCodes
    {
        /// <summary>A customer's credit limit, saved or set by the <c>set-credit-limit</c> action, is below zero.</summary>
        public const string NegativeCreditLimit = "Acme.Customers.NegativeCreditLimit";

        /// <summary>The invoice-number padding setting is below one digit or above the most digits allowed.</summary>
        public const string InvoicePaddingOutOfRange = "Acme.Settings.InvoicePaddingOutOfRange";
    }
}
