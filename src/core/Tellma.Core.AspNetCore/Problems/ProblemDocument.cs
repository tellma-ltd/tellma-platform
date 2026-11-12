// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.AspNetCore.Problems
{
    /// <summary>An RFC 9457 problem body, camelCase on the wire.</summary>
    /// <param name="Type">The problem type URI: the type base followed by the code.</param>
    /// <param name="Title">The code's title.</param>
    /// <param name="Status">The status code.</param>
    /// <param name="Detail">A sentence on this occurrence, or null.</param>
    /// <param name="Instance">The request's W3C trace id.</param>
    /// <param name="Code">The kebab-case problem code.</param>
    /// <param name="Errors">The validation items; empty for every problem this host writes.</param>
    /// <param name="Arguments">The problem's raw display data, or null.</param>
    internal sealed record ProblemDocument(
        string Type,
        string Title,
        int Status,
        string? Detail,
        string Instance,
        string Code,
        IReadOnlyList<object> Errors,
        IReadOnlyDictionary<string, object?>? Arguments);
}
