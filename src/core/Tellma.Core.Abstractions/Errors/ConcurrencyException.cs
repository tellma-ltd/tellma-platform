// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Globalization;
using Tellma.Core.Abstractions.Internal;

namespace Tellma.Core.Abstractions.Errors
{
    /// <summary>
    ///     A save under the <c>Check</c> concurrency mode found rows changed since the caller loaded
    ///     them: every conflicting row, with its stored stamp and its modifier. Nothing was written.
    ///     Maps to 409 with the problem code <c>concurrency-conflict</c> and the conflicts in
    ///     <c>errorDetails.conflicts</c>.
    /// </summary>
    /// <param name="code">The problem code: <c>concurrency-conflict</c> at every platform raise site.</param>
    /// <param name="conflicts">Every conflicting row; at least one, none null.</param>
    /// <exception cref="ArgumentException"><paramref name="code" /> is empty, or <paramref name="conflicts" /> is empty or holds a null entry.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="conflicts" /> is null.</exception>
    public sealed class ConcurrencyException(string code, IReadOnlyList<ConcurrencyConflict> conflicts)
        : TellmaException(code, Describe(conflicts))
    {
        // Member initializers run before the base constructor, so the conflicts are validated here,
        // before the log message is rendered from them.
        /// <summary>Every conflicting row.</summary>
        public IReadOnlyList<ConcurrencyConflict> Conflicts { get; } = ReadOnlyCopies.OfEntries(conflicts, requireOne: true, nameof(conflicts));

        /// <summary>Renders the log message.</summary>
        /// <param name="conflicts">The conflicts, already validated.</param>
        /// <returns>The message.</returns>
        private static string Describe(IReadOnlyList<ConcurrencyConflict> conflicts)
        {
            string ids = LogText.List(conflicts, static conflict =>
                conflict.IsMissing
                    ? string.Create(CultureInfo.InvariantCulture, $"{conflict.Id} (missing)")
                    : conflict.Id.ToString(CultureInfo.InvariantCulture));

            return string.Create(CultureInfo.InvariantCulture, $"{conflicts.Count} row(s) changed since they were loaded: {ids}.");
        }
    }
}
