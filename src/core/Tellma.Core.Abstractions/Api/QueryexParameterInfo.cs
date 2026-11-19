// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Queryex;

namespace Tellma.Core.Abstractions.Api
{
    /// <summary>One parameter the clauses mention, with the type it takes.</summary>
    /// <param name="Name">The parameter's name as written, without its <c>@</c>.</param>
    /// <param name="Sites">Where each occurrence is written.</param>
    /// <param name="Type">The confirmed type, else the inferred one; null when nothing constrains the parameter or its uses conflict.</param>
    /// <param name="Conflicts">The incompatible types its uses demand; empty when they agree.</param>
    /// <param name="ComparedWith">The segments of the one path the parameter is compared with, from the root; null when it meets none, or several.</param>
    /// <param name="Entity">The entity a key or foreign key at the end of that path identifies; null otherwise.</param>
    public sealed record QueryexParameterInfo(
        string Name,
        IReadOnlyList<QueryexSite> Sites,
        QueryexType? Type,
        IReadOnlyList<QueryexType> Conflicts,
        IReadOnlyList<string>? ComparedWith,
        string? Entity);
}
