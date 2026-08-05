// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Identity.Data
{
    /// <summary>
    ///     How to address a user grammatically.
    ///     <para>
    ///         This is a grammar setting, not a statement about identity: many languages inflect
    ///         verbs and adjectives for the person being addressed — Arabic writes
    ///         <c>أدخل</c> to a man and <c>أدخلي</c> to a woman for the same instruction — and a
    ///         message cannot be written without knowing which. It is optional precisely because
    ///         it is a grammar setting: unstated is a first-class answer, and every message has a
    ///         neutral form for it.
    ///     </para>
    ///     <para>
    ///         The values are the ones the standard <c>gender</c> claim uses, so a relying party
    ///         reads them without a Tellma-specific mapping.
    ///     </para>
    /// </summary>
    public enum UserGender
    {
        /// <summary>Address the user with feminine forms.</summary>
        Female = 0,

        /// <summary>Address the user with masculine forms.</summary>
        Male = 1,
    }
}
