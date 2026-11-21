// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Validation
{
    /// <summary>
    ///     The warnings of one scope that the caller must confirm before the operation commits: one
    ///     per request scope, shared by every write frame and enlisted group in it.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         A confirmation is a warning, not an error: the operation is legitimate but risky enough
    ///         that the user must see its size first. The caller confirms each warning by its key and
    ///         resends; confirming one never confirms another.
    ///     </para>
    ///     <para>
    ///         Keys are dotted PascalCase resource keys, like validation codes
    ///         (<c>Inventory.BackdatedRecost</c>), each declared once in a feature's composition. The
    ///         scope's entry point seeds the confirmed keys once. Under a system scope every key counts
    ///         as confirmed, since there is nobody to ask.
    ///     </para>
    /// </remarks>
    public interface IConfirmations
    {
        /// <summary>The keys the scope's entry point seeded as confirmed.</summary>
        IReadOnlyCollection<string> Confirmed { get; }

        /// <summary>Whether a key is confirmed in this scope.</summary>
        /// <param name="key">The warning's key.</param>
        /// <returns>True when the key is confirmed.</returns>
        bool IsConfirmed(string key);

        /// <summary>
        ///     Records a warning when its key is not confirmed; the operation carries on, and every
        ///     unconfirmed warning surfaces together once validation ends.
        /// </summary>
        /// <param name="key">The warning's declared key.</param>
        /// <param name="rows">The ids of the rows the warning concerns, or <see langword="null" />.</param>
        /// <param name="arguments">The arguments the warning's message renders from, or <see langword="null" />.</param>
        /// <exception cref="InvalidOperationException"><paramref name="key" /> was not declared.</exception>
        void Require(string key, IReadOnlyList<long>? rows = null, IReadOnlyDictionary<string, object?>? arguments = null);
    }
}
