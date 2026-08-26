// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Queryex
{
    /// <summary>
    ///     One compilation problem, machine-readable.
    /// </summary>
    /// <remarks>
    ///     Hosts compose display text from <see cref="Code" /> and <see cref="Arguments" />; the
    ///     engine writes no user-facing prose, so nothing here constrains a host's localization
    ///     design. Every diagnostic is an error — a severity axis will be added when the first
    ///     warning exists, and not before.
    /// </remarks>
    /// <param name="Code">The stable diagnostic code, such as "QX3001".</param>
    /// <param name="Span">The offending range within the text named by <paramref name="Location" />.</param>
    /// <param name="Location">
    ///     Which input the span indexes into: a clause and item such as "Select[2]" or "OrderBy[0]",
    ///     a filter-tree path such as "Filter.Or[1]", or null when a single expression list was
    ///     validated on its own.
    /// </param>
    /// <param name="Arguments">
    ///     Named values for message composition, such as the offending property name or the entity
    ///     it was looked for on. Values are derived from the caller's own input and must be treated
    ///     as untrusted display data.
    /// </param>
    public sealed record QueryexDiagnostic(
        string Code,
        QueryexSpan Span,
        string? Location,
        IReadOnlyList<KeyValuePair<string, string>> Arguments);
}
