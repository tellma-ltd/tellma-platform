// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Errors;
using Tellma.Core.Abstractions.Tenancy;
using Tellma.Core.AspNetCore.Problems;
using Tellma.Core.AspNetCore.Tenancy;
using Tellma.Core.Tenancy;

namespace Tellma.Core.AspNetCore.Realtime
{
    /// <summary>
    ///     The hub's front door, which the tenant middleware runs for every request to the hub's
    ///     endpoints (the negotiate and, self-hosted, the connect): no endpoint filter reaches a hub's
    ///     connection endpoints, so the door runs the tenant's access guard itself.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Three steps, in order: the access guard for a read, which connects the subject to its
    ///         user; one prologue-only round trip, even when the connect cache is warm, because a
    ///         connection sends no business batch that would carry the membership check; and the hub
    ///         identity, built from the bound context, added to the request's principal.
    ///     </para>
    ///     <para>
    ///         A refusal at any step, or at the tenant's resolution before it, is counted as a refused
    ///         connection attempt and logged with its problem code, then answered by the problem
    ///         mapping as the web surface's read would be. So is a request to the hub that the
    ///         pipeline refused before the door, with a 401 from authentication or a 403 from
    ///         authorization or the CSRF control.
    ///     </para>
    /// </remarks>
    /// <param name="metrics">The realtime instruments.</param>
    /// <param name="logger">Where refusals are logged.</param>
    internal sealed class HubFrontDoor(RealtimeMetrics metrics, ILogger<HubFrontDoor> logger)
    {
        /// <summary>The reason logged for a refusal that carries no problem code.</summary>
        public const string ServerErrorReason = "server-error";

        /// <summary>Runs the door's three steps over a request whose tenant and principal are bound.</summary>
        /// <param name="context">The request.</param>
        /// <param name="holder">The request scope's context holder.</param>
        /// <param name="guard">The request scope's access guard.</param>
        /// <returns>A task that completes when the request may reach the hub.</returns>
        /// <exception cref="TenantNotFoundException">The caller is no member of the tenant, or is deactivated there.</exception>
        /// <exception cref="TenantUnavailableException">The tenant refuses reads in its state.</exception>
        /// <exception cref="InvalidOperationException">The principal already carries a hub claim, or the context lacks a user or a session.</exception>
        public static async Task EnterAsync(HttpContext context, IRequestContextHolder holder, ITenantAccessGuard guard)
        {
            ArgumentNullException.ThrowIfNull(context);
            ArgumentNullException.ThrowIfNull(holder);
            ArgumentNullException.ThrowIfNull(guard);

            // --- 1. The access guard, for a read: the hub never writes ---
            await guard.EnsureAccessAsync(
                new TenantAccessRequirement(IsMutation: false, Assurance: null),
                RequestInputs.Of(context),
                context.RequestAborted).ConfigureAwait(false);
            holder.Current.ApplyCultures();

            // --- 2. One prologue-only round trip: membership verified against the tenant's own users ---
            // The guard's connect may have been answered by the cache, which a deactivation committed
            // on another instance has not reached yet; the prologue reads the user afresh.
            IGuardedBatchRunner runner = context.RequestServices.GetRequiredService<IGuardedBatchRunner>();
            _ = await runner.RunAsync(
                BatchPurpose.Read,
                static (_, _) => ValueTask.CompletedTask,
                static (_, _) => true,
                context.RequestAborted).ConfigureAwait(false);

            // --- 3. The hub identity, which only this door adds ---
            if (TellmaHubClaims.Carries(context.User))
            {
                throw new InvalidOperationException("The request's principal carries a hub claim before the hub's front door added one; only the front door mints hub claims.");
            }

            context.User.AddIdentity(TellmaHubClaims.Create(holder.Current));
        }

        /// <summary>
        ///     Counts and logs a request to the hub the pipeline answered 401 or 403 without throwing:
        ///     a refusal before the door, by authentication, authorization or the CSRF control. A
        ///     refusal the door raised propagates as an exception instead, and is counted by
        ///     <see cref="Refused" />.
        /// </summary>
        /// <param name="context">The answered request.</param>
        public void Answered(HttpContext context)
        {
            ArgumentNullException.ThrowIfNull(context);
            int status = context.Response.StatusCode;
            if (status is not (StatusCodes.Status401Unauthorized or StatusCodes.Status403Forbidden) || context.RequestAborted.IsCancellationRequested)
            {
                return;
            }

            // The code of the problem written, else the one the status would be answered with.
            string reason = ProblemWriter.WrittenCode(context) ?? ProblemMapper.ForStatus(status, routeMiss: false)?.Code ?? ServerErrorReason;
            metrics.RecordRefused();
            RealtimeLog.HubConnectionRefused(logger, reason);
        }

        /// <summary>Counts and logs a refused connection attempt; a request its client abandoned is neither.</summary>
        /// <param name="exception">What refused it.</param>
        /// <param name="context">The request.</param>
        public void Refused(Exception exception, HttpContext context)
        {
            ArgumentNullException.ThrowIfNull(exception);
            ArgumentNullException.ThrowIfNull(context);
            if (exception is OperationCanceledException && context.RequestAborted.IsCancellationRequested)
            {
                return;
            }

            metrics.RecordRefused();
            RealtimeLog.HubConnectionRefused(logger, exception is TellmaException tellma ? tellma.Code : ServerErrorReason);
        }
    }
}
