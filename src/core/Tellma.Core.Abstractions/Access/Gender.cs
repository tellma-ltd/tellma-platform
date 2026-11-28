// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Access
{
    /// <summary>
    ///     The grammatical gender a user is addressed in, by invitations and by the browser
    ///     application's gender-inflected strings. Stored as the member's name; a user without one is
    ///     addressed in neutral forms.
    /// </summary>
    public enum Gender
    {
        /// <summary>Feminine forms.</summary>
        Female,

        /// <summary>Masculine forms.</summary>
        Male,
    }
}
