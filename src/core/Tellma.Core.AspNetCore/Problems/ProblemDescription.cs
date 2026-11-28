// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.AspNetCore.Problems
{
    /// <summary>
    ///     What the edge answers an outcome with, before it is rendered in the reader's culture: the
    ///     status, the problem code and the raw data of the body and its headers. Nothing in it is
    ///     localized text, so the web edge and the agent tools render the same description each in
    ///     their own way.
    /// </summary>
    /// <param name="Status">The HTTP status.</param>
    /// <param name="Code">The kebab-case problem code.</param>
    internal sealed record ProblemDescription(int Status, string Code)
    {
        /// <summary>The body's raw display data; null when the problem carries none.</summary>
        public IReadOnlyDictionary<string, object?>? Arguments { get; init; }

        /// <summary>How the body's <c>detail</c> is formed; null for the code's own detail resource, rendered with <see cref="Arguments" />.</summary>
        public ProblemDetail? Detail { get; init; }

        /// <summary>The body's validation items; empty for a problem without any.</summary>
        public IReadOnlyList<ProblemError> Errors { get; init; } = [];

        /// <summary>The body's structured extras (conflicts, diagnostics, partial results); null for none.</summary>
        public object? ErrorDetails { get; init; }

        /// <summary>The wait a <c>Retry-After</c> header names; null for a problem waiting does not change.</summary>
        public TimeSpan? RetryAfter { get; init; }

        /// <summary>The step-up challenge a <c>WWW-Authenticate</c> header carries; null for every other problem.</summary>
        public StepUpDemand? StepUp { get; init; }

        /// <summary>Whether the problem is the backstop of an exception outside the mapped set.</summary>
        public bool IsUnhandled { get; init; }
    }

    /// <summary>How a problem's <c>detail</c> is formed when it is not the code's own detail resource.</summary>
    internal abstract record ProblemDetail
    {
        /// <summary>No detail: the code's detail resource would need arguments the problem does not have.</summary>
        public static ProblemDetail None { get; } = new NoDetail();

        /// <summary>A sentence carried verbatim, which the raise site wrote for this occurrence.</summary>
        /// <param name="Text">The sentence.</param>
        internal sealed record Verbatim(string Text) : ProblemDetail;

        /// <summary>Another resource's text, such as a warning's own message; the code's detail resource when no feature declares it.</summary>
        /// <param name="Key">The resource key.</param>
        /// <param name="Arguments">The text's arguments.</param>
        internal sealed record Resource(string Key, IReadOnlyDictionary<string, object?> Arguments) : ProblemDetail;

        /// <summary>
        ///     A refused securable's detail: the code's detail resource, with the resource and the
        ///     action rendered as the permission's labels.
        /// </summary>
        /// <param name="Reason">The wire form of the refusal's reason.</param>
        /// <param name="SecurableResource">The refused resource.</param>
        /// <param name="Action">The refused action; null when the refusal names none.</param>
        /// <param name="Path">The path a restricted-path refusal read; null for every other reason.</param>
        internal sealed record Securable(string Reason, string SecurableResource, string? Action, string? Path) : ProblemDetail;

        /// <summary>The absence of a detail.</summary>
        private sealed record NoDetail : ProblemDetail;
    }

    /// <summary>One validation item of a problem body, before its message is rendered.</summary>
    /// <param name="Code">The item's code, a dotted PascalCase resource key or a query diagnostic code.</param>
    /// <param name="MessageKey">The resource its message renders from.</param>
    /// <param name="Arguments">The raw values its message renders from.</param>
    internal sealed record ProblemError(string Code, string MessageKey, IReadOnlyDictionary<string, object?> Arguments)
    {
        /// <summary>The rendered path of the value concerned; null for an import item, which names its cell instead.</summary>
        public string? Path { get; init; }

        /// <summary>The entity whose property <c>arguments.property</c> names, for the message's label; null when unknown.</summary>
        public Type? Entity { get; init; }

        /// <summary>An import item's sheet; null otherwise or when the item has no place in the workbook.</summary>
        public string? Sheet { get; init; }

        /// <summary>An import item's Excel row, 1-based; null otherwise.</summary>
        public int? Row { get; init; }

        /// <summary>An import item's column letters; null otherwise.</summary>
        public string? Column { get; init; }

        /// <summary>An import item's column header; null otherwise.</summary>
        public string? Header { get; init; }

        /// <summary>An import item's property, by its CLR name; null otherwise.</summary>
        public string? Property { get; init; }
    }

    /// <summary>A step-up challenge: the assurance the caller must reach to repeat the request.</summary>
    /// <param name="Acr">The authentication context class required.</param>
    /// <param name="MaxAgeSeconds">The largest age the sign-in may have, in whole seconds; null when any age passes.</param>
    internal sealed record StepUpDemand(string Acr, int? MaxAgeSeconds);
}
