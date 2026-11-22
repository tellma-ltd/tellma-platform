// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Distro.Acme.Services
{
    /// <summary>The arguments of the customers' <c>set-credit-limit</c> action.</summary>
    /// <param name="CreditLimit">The new credit limit, in each customer's own currency; never negative.</param>
    public sealed record SetCreditLimitArguments(decimal CreditLimit);
}
