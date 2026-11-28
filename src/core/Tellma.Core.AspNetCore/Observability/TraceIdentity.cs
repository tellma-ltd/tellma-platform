// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.AspNetCore.Http;
using System.Diagnostics;

namespace Tellma.Core.AspNetCore.Observability
{
    /// <summary>
    ///     The request's W3C trace id, read once and kept on the request, so the <c>Tellma-Trace-Id</c>
    ///     header and every problem's <c>instance</c> name the same trace.
    /// </summary>
    internal static class TraceIdentity
    {
        /// <summary>The request item the trace id is kept under.</summary>
        private static readonly object Key = new();

        /// <summary>The request's trace id: the request activity's, or a fresh one when no activity runs.</summary>
        /// <param name="context">The request.</param>
        /// <returns>The 32-character lowercase hex trace id.</returns>
        public static string Of(HttpContext context)
        {
            ArgumentNullException.ThrowIfNull(context);
            if (context.Items.TryGetValue(Key, out object? kept) && kept is string traceId)
            {
                return traceId;
            }

            // The host creates the request activity whenever anything listens, and the platform's
            // tracing always does; a fresh id keeps the header present even without it.
            string id = (Activity.Current?.TraceId ?? ActivityTraceId.CreateRandom()).ToHexString();
            context.Items[Key] = id;
            return id;
        }
    }
}
