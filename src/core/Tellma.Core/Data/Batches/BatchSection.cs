// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Data.Batches
{
    /// <summary>
    ///     Where in the frame a statement is emitted. Statements are emitted section by section in
    ///     this order, and by ordinal within a section.
    /// </summary>
    internal enum BatchSection
    {
        /// <summary>
        ///     Before the guard wrapper and the transaction, autocommit: the contributors' prologues
        ///     and the connect prologue.
        /// </summary>
        Prologue = 0,

        /// <summary>
        ///     First inside the transaction, before the caller's statements: the application lock,
        ///     the version-tag guard and the caller re-check.
        /// </summary>
        TransactionHead = 1,

        /// <summary>The caller's statements and the platform's own composers'.</summary>
        Body = 2,

        /// <summary>After the caller's statements, last before the commit: the contributors' epilogues.</summary>
        Epilogue = 3,
    }
}
