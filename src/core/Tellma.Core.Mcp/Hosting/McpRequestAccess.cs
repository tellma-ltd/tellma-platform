// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.Options;
using Tellma.Core.Abstractions.Errors;
using Tellma.Core.Abstractions.Tenancy;
using Tellma.Core.AspNetCore.Tenancy;
using Tellma.Core.Tenancy;

namespace Tellma.Core.Mcp.Hosting
{
    /// <summary>
    ///     The MCP surface's front door: the tenant access guard, run once per request for a read,
    ///     with the server's message language. One per request scope, so the server's identity and
    ///     the tool call of one request share a single decision.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The tenant middleware has already bound the tenant and the principal. The door refuses
    ///         a service account, which no tenant surface of this release accepts, then runs the
    ///         guard, whose state verdicts and initializers complete the request's context: the
    ///         caller's user, content language, calendar and zone.
    ///     </para>
    ///     <para>
    ///         A refusal is kept, not thrown: the caller decides what it means. A tool call answers
    ///         it as an error result; the server's identity answers without the tenant's name.
    ///     </para>
    /// </remarks>
    /// <param name="accessor">The request's context.</param>
    /// <param name="guard">The request's access guard.</param>
    /// <param name="options">The server's options.</param>
    internal sealed class McpRequestAccess(IRequestContextAccessor accessor, ITenantAccessGuard guard, IOptions<McpOptions> options)
    {
        /// <summary>The code of the Gregorian calendar, which messages written for the model use.</summary>
        private const string GregorianCode = "gc";

        /// <summary>The decision once made: null until then.</summary>
        private Task<Exception?>? _decision;

        /// <summary>Runs the door once, and answers its decision on every call after.</summary>
        /// <param name="cancellationToken">Cancels the decision.</param>
        /// <returns>Null when the caller may proceed; the refusal otherwise.</returns>
        /// <exception cref="OperationCanceledException">The request was abandoned.</exception>
        public Task<Exception?> EnsureAsync(CancellationToken cancellationToken)
        {
            return _decision ??= DecideAsync(cancellationToken);
        }

        /// <summary>Applies the completed context's cultures to the caller's flow, after an admitted decision.</summary>
        /// <remarks>Called in the frame that runs the tool: a culture set inside an awaited method does not flow back to its caller.</remarks>
        public void ApplyCultures()
        {
            accessor.Current.ApplyCultures();
        }

        /// <summary>The door's decision.</summary>
        /// <param name="cancellationToken">Cancels the decision.</param>
        /// <returns>Null when the caller may proceed; the refusal otherwise.</returns>
        private async Task<Exception?> DecideAsync(CancellationToken cancellationToken)
        {
            // --- An individual alone: no tenant surface of this release accepts a service account ---
            if (accessor.Current.IsServiceAccount)
            {
                return new IndividualRequiredException();
            }

            // --- The guard, for a read: every tool call is admitted as one, its own writes checked by the service ---
            // Messages in the server's language with Gregorian dates, so the model reads them alike in
            // every tenant; display names keep the caller's content language (spec 0016 §11.4).
            string? language = options.Value.EffectiveMessageLanguage;
            RequestContextInputs inputs = new(
                AcceptLanguage: null,
                RequestedCalendar: null,
                RequestedTimeZone: null,
                Client: RequestInputs.McpClient,
                MessageLanguage: language,
                MessageCalendar: language is null ? null : GregorianCode);
            try
            {
                await guard.EnsureAccessAsync(new TenantAccessRequirement(IsMutation: false, Assurance: null), inputs, cancellationToken).ConfigureAwait(false);
                return null;
            }
            catch (Exception refusal) when (refusal is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                return refusal;
            }
        }
    }
}
