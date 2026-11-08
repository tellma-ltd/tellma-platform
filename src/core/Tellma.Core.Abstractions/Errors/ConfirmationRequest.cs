// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Collections.ObjectModel;
using Tellma.Core.Abstractions.Internal;

namespace Tellma.Core.Abstractions.Errors
{
    /// <summary>
    ///     One warning a caller must confirm by its key before the operation proceeds: the rows it
    ///     concerns and the raw arguments of its message. A
    ///     <see cref="ConfirmationRequiredException" /> carries one per unconfirmed warning.
    /// </summary>
    /// <remarks>
    ///     A warning is legitimate but risky enough that the user must see its size first ("this
    ///     posting re-costs 1,240 later withdrawals"). Confirming one key never confirms another.
    ///     Every member is get-only and copied on construction, and two requests are equal when their
    ///     keys, their ids in order and their arguments (name by name) are equal.
    /// </remarks>
    /// <param name="Key">The warning's key, a dotted PascalCase resource key such as <c>Inventory.BackdatedRecost</c>.</param>
    /// <param name="Ids">The ids of the rows the warning concerns; empty when it concerns none. Display data on the wire.</param>
    /// <param name="Arguments">The raw values its message renders from; empty when it has none.</param>
    public sealed record ConfirmationRequest(string Key, IReadOnlyList<long> Ids, IReadOnlyDictionary<string, object?> Arguments)
    {
        /// <inheritdoc cref="ConfirmationRequest(string, IReadOnlyList{long}, IReadOnlyDictionary{string, object?})" />
        public string Key { get; } = ValidateKey(Key);

        /// <inheritdoc cref="ConfirmationRequest(string, IReadOnlyList{long}, IReadOnlyDictionary{string, object?})" />
        public IReadOnlyList<long> Ids { get; } = CopyIds(Ids);

        /// <inheritdoc cref="ConfirmationRequest(string, IReadOnlyList{long}, IReadOnlyDictionary{string, object?})" />
        public IReadOnlyDictionary<string, object?> Arguments { get; } = CopyArguments(Arguments);

        /// <summary>Whether another request has an equal key, equal ids in order and equal arguments.</summary>
        /// <param name="other">The other request.</param>
        /// <returns>True when the two are equal.</returns>
        public bool Equals(ConfirmationRequest? other)
        {
            // Compared by content: each request holds its own copies, so the reference comparison a
            // record would synthesise could never find two requests equal.
            return other is not null
                && string.Equals(Key, other.Key, StringComparison.Ordinal)
                && Ids.SequenceEqual(other.Ids)
                && ArgumentBags.Equal(Arguments, other.Arguments);
        }

        /// <summary>A hash consistent with <see cref="Equals(ConfirmationRequest)" />.</summary>
        /// <returns>The hash code.</returns>
        public override int GetHashCode()
        {
            return HashCode.Combine(StringComparer.Ordinal.GetHashCode(Key), Ids.Count, Arguments.Count);
        }

        /// <summary>Refuses an empty key.</summary>
        /// <param name="key">The key.</param>
        /// <returns>The key.</returns>
        private static string ValidateKey(string key)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(key);
            return key;
        }

        /// <summary>Refuses null ids and copies the rest.</summary>
        /// <param name="ids">The ids.</param>
        /// <returns>A read-only copy.</returns>
        private static ReadOnlyCollection<long> CopyIds(IReadOnlyList<long> ids)
        {
            ArgumentNullException.ThrowIfNull(ids);
            return ReadOnlyCopies.Of(ids);
        }

        /// <summary>Refuses a null argument bag and copies the rest.</summary>
        /// <param name="arguments">The arguments.</param>
        /// <returns>A read-only copy.</returns>
        private static IReadOnlyDictionary<string, object?> CopyArguments(IReadOnlyDictionary<string, object?> arguments)
        {
            ArgumentNullException.ThrowIfNull(arguments);
            return ReadOnlyCopies.Of(arguments);
        }
    }
}
