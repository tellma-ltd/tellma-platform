// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Text.Json.Serialization;

namespace Tellma.Core.AspNetCore.Problems
{
    /// <summary>An RFC 9457 problem body, camelCase on the wire.</summary>
    /// <param name="Type">The problem type URI: the type base followed by the code.</param>
    /// <param name="Title">The code's title in the reader's culture.</param>
    /// <param name="Status">The status code.</param>
    /// <param name="Detail">A sentence on this occurrence in the reader's culture, or null.</param>
    /// <param name="Instance">The request's W3C trace id.</param>
    /// <param name="Code">The kebab-case problem code.</param>
    /// <param name="Errors">The validation items; empty for a problem without any.</param>
    /// <param name="ErrorDetails">The structured extras, or null; written as <c>null</c> rather than left out.</param>
    /// <param name="Arguments">The problem's raw display data, or null.</param>
    internal sealed record ProblemDocument(
        string Type,
        string Title,
        int Status,
        string? Detail,
        string Instance,
        string Code,
        IReadOnlyList<ProblemErrorDocument> Errors,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] object? ErrorDetails,
        IReadOnlyDictionary<string, object?>? Arguments);

    /// <summary>One validation item of a problem body.</summary>
    /// <param name="Path">The value's path; null for an import item, which names its cell instead.</param>
    /// <param name="Sheet">An import item's sheet, or null.</param>
    /// <param name="Row">An import item's Excel row, or null.</param>
    /// <param name="Column">An import item's column letters, or null.</param>
    /// <param name="Header">An import item's column header, or null.</param>
    /// <param name="Property">An import item's property, or null.</param>
    /// <param name="Code">The item's code.</param>
    /// <param name="Message">The item's message in the reader's culture.</param>
    /// <param name="Arguments">The raw values the message renders from.</param>
    internal sealed record ProblemErrorDocument(
        string? Path,
        string? Sheet,
        int? Row,
        string? Column,
        string? Header,
        string? Property,
        string Code,
        string Message,
        IReadOnlyDictionary<string, object?> Arguments);
}
