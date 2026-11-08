// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Api
{
    /// <summary>
    ///     The body of a stack's <c>get-by-ids</c>: rows in details shape, in the requested order. The
    ///     answer is partial: an absent or hidden id is omitted, never a not-found error.
    /// </summary>
    /// <remarks>
    ///     <see cref="Ids" /> follows the rules of <see cref="IdsRequest.Ids" />, a missing list binding
    ///     empty.
    /// </remarks>
    /// <param name="Ids">The rows' ids; numbers or strings on the wire, narrowed to the stack's key type.</param>
    /// <param name="Include">The details extras to include, by name; an unknown name is a bad-request error. Null or empty for none.</param>
    public sealed record GetByIdsRequest(IReadOnlyList<long> Ids, IReadOnlyList<string>? Include)
    {
        /// <inheritdoc cref="GetByIdsRequest(IReadOnlyList{long}, IReadOnlyList{string})" />
        public IReadOnlyList<long> Ids { get; } = Ids ?? [];
    }
}
