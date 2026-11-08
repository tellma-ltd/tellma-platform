// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Localization
{
    /// <summary>
    ///     Which digits the browser application shows numbers and dates in. Stored as the member's
    ///     name, like every enum. Server text always uses Latin digits.
    /// </summary>
    public enum DigitStyle
    {
        /// <summary>The digits 0 to 9.</summary>
        Latin,

        /// <summary>The language's native digits (Arabic-Indic for Arabic); Latin when the language has none.</summary>
        Native,
    }
}
