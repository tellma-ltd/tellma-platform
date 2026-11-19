// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.AspNetCore.Http;

namespace Tellma.Core.AspNetCore.Problems
{
    /// <summary>A problem body as an endpoint result, for a filter that replaces a handler's result.</summary>
    /// <param name="status">The status code.</param>
    /// <param name="code">The kebab-case problem code.</param>
    internal sealed class ProblemResult(int status, string code) : IResult, IStatusCodeHttpResult
    {
        /// <inheritdoc />
        public int? StatusCode => status;

        /// <inheritdoc />
        public Task ExecuteAsync(HttpContext httpContext)
        {
            ArgumentNullException.ThrowIfNull(httpContext);
            return ProblemWriter.WriteAsync(httpContext, status, code);
        }
    }
}
