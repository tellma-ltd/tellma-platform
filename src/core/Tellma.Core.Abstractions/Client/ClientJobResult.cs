// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Client
{
    /// <summary>
    ///     Where a handler's succeeded job leaves its result: the row of <see cref="Entity" /> whose
    ///     <see cref="Via" /> property holds the job's id. A job's page links to it, and a notice about
    ///     the job opens it.
    /// </summary>
    /// <param name="Entity">The result's entity name (<c>core.Export</c>).</param>
    /// <param name="Via">The entity's <see cref="int" /> property that holds the job's id (<c>JobId</c>).</param>
    public sealed record ClientJobResult(string Entity, string Via);
}
