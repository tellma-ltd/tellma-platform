// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Diagnostics.CodeAnalysis;
using Tellma.Core.Abstractions.Internal;

namespace Tellma.Core.Abstractions.Jobs
{
    /// <summary>
    ///     The grammar of handler keys, the names every job row and schedule persists for the handler
    ///     that runs it.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         A key is a prefix and one or more further segments joined by dots, each segment a
    ///         lowercase letter followed by lowercase letters, digits and hyphens
    ///         (<c>^[a-z][a-z0-9-]*(\.[a-z][a-z0-9-]*)+$</c>), at most
    ///         <see cref="JobLimits.MaxKeyLength" /> characters. The platform's keys start with
    ///         <see cref="PlatformPrefix" />, which no other feature may use; a module keys its handlers
    ///         by its module slug (<c>gl.</c>) and a distribution by its deployment slug
    ///         (<c>acme.recompute-balances</c>).
    ///     </para>
    ///     <para>
    ///         Keys are stable for the life of a distribution: a breaking change to a handler's
    ///         arguments is a new key, and the old key is retired. Notification type keys and client
    ///         event names follow the same grammar.
    ///     </para>
    /// </remarks>
    public static class JobHandlerKeys
    {
        /// <summary>The prefix reserved for the platform's own handlers.</summary>
        public const string PlatformPrefix = "core.";

        /// <summary>The prefix of a handler's label resource key, which <see cref="LabelKeyOf(string)" /> completes.</summary>
        public const string LabelKeyPrefix = "JobHandler_";

        /// <summary>Whether <paramref name="key" /> is a well-formed handler key.</summary>
        /// <param name="key">The candidate; null is never a key.</param>
        /// <returns>True when it is.</returns>
        public static bool IsValid([NotNullWhen(true)] string? key)
        {
            return KeyGrammar.IsDottedKey(key, JobLimits.MaxKeyLength);
        }

        /// <summary>Whether <paramref name="key" /> carries the prefix reserved for the platform.</summary>
        /// <param name="key">The key.</param>
        /// <returns>True when it starts with <see cref="PlatformPrefix" />.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="key" /> is null.</exception>
        public static bool IsPlatformKey(string key)
        {
            ArgumentNullException.ThrowIfNull(key);
            return key.StartsWith(PlatformPrefix, StringComparison.Ordinal);
        }

        /// <summary>
        ///     The resource key of a handler's label: <see cref="LabelKeyPrefix" /> followed by the
        ///     handler key with every dot replaced by an underscore (<c>core.blob-sweep</c> →
        ///     <c>JobHandler_core_blob-sweep</c>). A job without a label of its own reads this resource,
        ///     or the handler key itself when no string resource holds it.
        /// </summary>
        /// <param name="key">The handler key.</param>
        /// <returns>The label's resource key.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="key" /> is null.</exception>
        public static string LabelKeyOf(string key)
        {
            ArgumentNullException.ThrowIfNull(key);
            return LabelKeyPrefix + key.Replace('.', '_');
        }
    }
}
