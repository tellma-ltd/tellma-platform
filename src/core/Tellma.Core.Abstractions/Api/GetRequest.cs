// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Api
{
    /// <summary>
    ///     The body of a stack's <c>get</c>: one row in details shape. A row that is absent or hidden
    ///     by row-level security is a not-found error, identical in both cases.
    /// </summary>
    /// <param name="Id">The row's id; a number or a string on the wire, narrowed to the stack's key type.</param>
    /// <param name="Include">The details extras to include, by name; an unknown name is a bad-request error. Null or empty for none.</param>
    public sealed record GetRequest(long Id, IReadOnlyList<string>? Include);
}
