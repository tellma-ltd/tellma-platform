// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Errors
{
    /// <summary>
    ///     The request is malformed in a way validation does not cover: an argument that does not
    ///     convert to its parameter's type, an id outside the key's range, an unknown extra name.
    ///     Maps to 400 with the problem code <c>bad-request</c>.
    /// </summary>
    /// <param name="detail">An English sentence naming what is wrong, such as the parameter or path; null when there is nothing to add.</param>
    /// <param name="arguments">
    ///     The raw display values a client renders from, such as the <c>path</c> or the
    ///     <c>parameter</c> at fault; none when null.
    /// </param>
    public sealed class BadRequestException(string? detail, IReadOnlyDictionary<string, object?>? arguments = null)
        : TellmaException(ProblemCode, string.IsNullOrWhiteSpace(detail) ? "The request is malformed." : $"The request is malformed. {detail}", arguments)
    {
        /// <summary>The problem code of this outcome.</summary>
        private const string ProblemCode = "bad-request";

        /// <summary>English text naming what is wrong; null when there is nothing to add.</summary>
        public string? Detail { get; } = detail;
    }
}
