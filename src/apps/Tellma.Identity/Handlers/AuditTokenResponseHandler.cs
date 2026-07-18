// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using OpenIddict.Abstractions;
using OpenIddict.Server;
using System.Security.Claims;
using System.Text.Json;
using Tellma.Identity.Infrastructure;
using Tellma.Identity.Services.Audit;
using static OpenIddict.Abstractions.OpenIddictConstants;
using static OpenIddict.Server.OpenIddictServerEvents;

namespace Tellma.Identity.Handlers
{
    /// <summary>
    ///     Captures the authenticated subject of a token request into the transaction so the
    ///     response auditor can stamp it, even on rejections the pass-through controller never sees.
    ///     Runs during authentication, where the presented grant's principal is available (for the
    ///     authorization-code, device-code, and refresh-token grants, and for a reused refresh
    ///     token before it is rejected).
    /// </summary>
    public sealed class CaptureAuditSubjectHandler : IOpenIddictServerHandler<ProcessAuthenticationContext>
    {
        /// <summary>The transaction property the subject is stashed under.</summary>
        public const string SubjectProperty = "tellma:audit_subject";

        /// <summary>The handler registration.</summary>
        public static OpenIddictServerHandlerDescriptor Descriptor { get; }
            = OpenIddictServerHandlerDescriptor.CreateBuilder<ProcessAuthenticationContext>()
                .UseSingletonHandler<CaptureAuditSubjectHandler>()
                .SetOrder(int.MaxValue - 100_000)
                .Build();

        /// <inheritdoc />
        public ValueTask HandleAsync(ProcessAuthenticationContext context)
        {
            ArgumentNullException.ThrowIfNull(context);

            // Read the subject from whichever grant principal the request carried; a reused refresh
            // token still exposes its subject here, before the reuse check rejects it downstream.
            ClaimsPrincipal? principal = context.RefreshTokenPrincipal
                ?? context.AuthorizationCodePrincipal
                ?? context.DeviceCodePrincipal
                ?? context.SubjectTokenPrincipal
                ?? context.GenericTokenPrincipal;

            string? subject = principal?.GetClaim(Claims.Subject);
            if (!string.IsNullOrEmpty(subject))
            {
                context.Transaction.SetProperty(SubjectProperty, subject);
            }

            return ValueTask.CompletedTask;
        }
    }

    /// <summary>
    ///     Audits every token-endpoint outcome — issuance and rejection alike — including failure
    ///     paths the pass-through controller never sees (bad client credentials, replayed refresh
    ///     tokens, permission rejections), stamping the subject (§15) and recording metrics. Refresh
    ///     replay is distinguished from ordinary rejection and raised as its own alertable event.
    /// </summary>
    /// <param name="auditLogger">Audit emission.</param>
    /// <param name="metrics">Identity metrics.</param>
    public sealed class AuditTokenResponseHandler(IAuditLogger auditLogger, IdentityMetrics metrics)
        : IOpenIddictServerHandler<ApplyTokenResponseContext>
    {
        // OpenIddict's invariant description for a replayed (already-redeemed) refresh token,
        // distinct from an expired one ("no longer valid"); the substring is stable across versions.
        private const string RedeemedMarker = "already been redeemed";

        /// <summary>The handler registration, late in the apply-response stage.</summary>
        public static OpenIddictServerHandlerDescriptor Descriptor { get; }
            = OpenIddictServerHandlerDescriptor.CreateBuilder<ApplyTokenResponseContext>()
                .UseScopedHandler<AuditTokenResponseHandler>()
                .SetOrder(int.MaxValue - 100_000)
                .Build();

        /// <inheritdoc />
        public async ValueTask HandleAsync(ApplyTokenResponseContext context)
        {
            ArgumentNullException.ThrowIfNull(context);

            bool succeeded = string.IsNullOrEmpty(context.Response.Error);
            string? grantType = context.Request?.GrantType;
            string? subject = context.Transaction.GetProperty<string>(CaptureAuditSubjectHandler.SubjectProperty);

            // Refresh replay: a rejected refresh grant whose reason is the redeemed marker is a
            // reuse detection (the family is revoked by OpenIddict), not an ordinary expiry.
            bool isReuse = !succeeded
                && context.Request?.IsRefreshTokenGrantType() == true
                && context.Response.ErrorDescription?.Contains(RedeemedMarker, StringComparison.OrdinalIgnoreCase) == true;

            string details = JsonSerializer.Serialize(new
            {
                grantType,
                error = context.Response.Error,
                errorDescription = context.Response.ErrorDescription,
            });

            await auditLogger.LogAsync(new AuditEventEntry
            {
                Action = succeeded
                    ? AuditActions.TokenIssued
                    : isReuse ? AuditActions.RefreshReuseDetected : AuditActions.TokenRequestRejected,
                Subject = subject,
                ClientId = context.Request?.ClientId,
                Outcome = succeeded ? "success" : "failure",
                DetailsJson = details,
            });

            if (succeeded)
            {
                metrics.TokenIssued(grantType ?? "unknown");
            }
            else if (isReuse)
            {
                metrics.RefreshReuseDetected();
            }
        }
    }
}
