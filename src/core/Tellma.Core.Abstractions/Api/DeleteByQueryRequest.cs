// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Text.Json;

namespace Tellma.Core.Abstractions.Api
{
    /// <summary>
    ///     The body of a stack's <c>delete-by-query</c>: delete every visible row a filter matches,
    ///     provided there are exactly as many as the caller was shown. Web surface only; never an
    ///     agent tool.
    /// </summary>
    /// <remarks>
    ///     The count is verified inside the delete's transaction, before any row is deleted: a
    ///     mismatch is a count-mismatch error with nothing deleted, so the most dangerous endpoint is
    ///     a two-phase confirm without server state.
    /// </remarks>
    /// <param name="Filter">The Queryex filter selecting the rows; never blank.</param>
    /// <param name="Arguments">The values of the filter's parameters, as JSON scalars keyed by parameter name.</param>
    /// <param name="ExpectedCount">The number of rows the caller was shown and expects to delete.</param>
    public sealed record DeleteByQueryRequest(string Filter, IReadOnlyDictionary<string, JsonElement>? Arguments, int ExpectedCount);
}
