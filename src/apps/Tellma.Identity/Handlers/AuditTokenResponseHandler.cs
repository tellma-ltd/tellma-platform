// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Http;
using OpenIddict.Abstractions;
using OpenIddict.Server;
using System.Text.Json;
using Tellma.Identity.Infrastructure;
using Tellma.Identity.Services.Audit;
using Tellma.Identity.Services.AuthenticationPolicy;
using static OpenIddict.Abstractions.OpenIddictConstants;
using static OpenIddict.Server.OpenIddictServerEvents;

namespace Tellma.Identity.Handlers
{
    /// <summary>Request context the protocol-pipeline auditors stamp onto their rows.</summary>
    internal static class AuditContext
    {
        /// <summary>
        ///     The client address of the request being audited. Credential-stuffing and
        ///     secret-brute-force alerting segments by it, so a token row without one is not
        ///     actionable. Behind a proxy this is only the real client when the host is configured
        ///     to honor forwarded headers.
        /// </summary>
        /// <param name="transaction">The OpenIddict transaction.</param>
        /// <returns>The remote address, or null outside an HTTP request.</returns>
        public static string? ClientIpAddress(OpenIddictServerTransaction transaction)
        {
            HttpRequest? request = transaction.GetHttpRequest();
            return request?.HttpContext.Connection.RemoteIpAddress?.ToString();
        }
    }

    /// <summary>Shared registration constants for the engine's OpenIddict pipeline handlers.</summary>
    internal static class AuditHandlerOrders
    {
        /// <summary>
        ///     Where the apply-response auditors run: after OpenIddict's error normalization and
        ///     the ASP.NET Core header handlers (100 000–102 000), before its
        ///     <c>ProcessJsonResponse</c> (500 000) writes the body and marks the request handled,
        ///     which stops dispatch and would leave a later-ordered handler dead.
        ///     <para>
        ///         Reusing this value on the authorization or end-session contexts would collide
        ///         with <c>ProcessSelfRedirection</c>, which is registered at exactly 250 000
        ///         there; pick a different order before auditing those endpoints.
        ///     </para>
        /// </summary>
        public const int ApplyResponse = 250_000;
    }
    /// <summary>
    ///     Captures the subject of a user-bound token into the transaction so the response
    ///     auditors can stamp it, even on rejections the pass-through controller never sees. Runs
    ///     inside token validation, after the cryptographic checks resolve the principal but
    ///     before the expiry and database-entry checks reject the request — the only window where
    ///     an expired or replayed (already-redeemed) refresh token still exposes its subject.
    ///     Client assertions never contribute (their subject names a client, and is not yet
    ///     consistency-checked at this point), and when several tokens validate in one request
    ///     (token exchange) the first user-bound one wins — normally the subject token, which
    ///     validates before the actor token.
    /// </summary>
    public sealed class CaptureAuditSubjectHandler : IOpenIddictServerHandler<ValidateTokenContext>
    {
        /// <summary>The transaction property the subject is stashed under.</summary>
        public const string SubjectProperty = "tellma:audit_subject";

        /// <summary>The transaction property the session identifier is stashed under.</summary>
        public const string SessionProperty = "tellma:audit_sid";

        /// <summary>
        ///     The transaction property recording that the presented token was authentic — it
        ///     carried a valid signature and resolved to a principal. Revocation always answers 200
        ///     (RFC 7009), so without this a sprayed guess is indistinguishable from a real
        ///     revocation in the audit trail. It is not proof that anything <em>was</em> revoked:
        ///     an authentic token can still be refused afterwards (a caller that is neither its
        ///     presenter nor its audience), and that refusal is normalized out of the response
        ///     before the auditors run.
        /// </summary>
        public const string TokenAuthenticProperty = "tellma:audit_token_authentic";

        /// <summary>
        ///     The handler registration: after <c>ValidatePrincipal</c> (the principal exists and
        ///     the token is authentic) and before <c>ValidateExpirationDate</c> /
        ///     <c>ValidateTokenEntry</c> (where expired and redeemed tokens are rejected and the
        ///     dispatcher stops).
        /// </summary>
        public static OpenIddictServerHandlerDescriptor Descriptor { get; }
            = OpenIddictServerHandlerDescriptor.CreateBuilder<ValidateTokenContext>()
                .UseSingletonHandler<CaptureAuditSubjectHandler>()
                .SetOrder(OpenIddictServerHandlers.Protection.ValidatePrincipal.Descriptor.Order + 500)
                .Build();

        /// <inheritdoc />
        public ValueTask HandleAsync(ValidateTokenContext context)
        {
            ArgumentNullException.ThrowIfNull(context);

            // Client assertions are client authentication, not a grant. Their subject names a
            // client — and until the issuer/subject consistency checks run, later in the
            // authentication pipeline, its value is whatever the presenter signed — so they are
            // excluded structurally, by token type, never by comparing claim values.
            if (context.ValidTokenTypes.Contains(TokenTypeIdentifiers.Private.ClientAssertion, StringComparer.Ordinal))
            {
                return ValueTask.CompletedTask;
            }

            // The presented token was authentic — recorded even when nothing below is captured,
            // since the revocation auditor needs it to tell a real token from a sprayed guess.
            if (context.Principal is not null)
            {
                context.Transaction.SetProperty(TokenAuthenticProperty, "true");
            }

            // First writer wins: token exchange validates the subject token before the actor
            // token, and the audit subject must name the user the exchange is about — never the
            // actor presented alongside it.
            if (context.Transaction.GetProperty<string>(SubjectProperty) is not null)
            {
                return ValueTask.CompletedTask;
            }

            // A token whose subject is the requesting client itself (a machine access token
            // presented for revocation or exchange) contributes nothing: machines are recorded
            // through the audit row's client id, not as a user subject.
            string? subject = context.Principal?.GetClaim(Claims.Subject);
            if (!string.IsNullOrEmpty(subject)
                && !string.Equals(subject, context.Transaction.Request?.ClientId, StringComparison.Ordinal))
            {
                context.Transaction.SetProperty(SubjectProperty, subject);

                // The session the grant belongs to, so §15 can pivot a token event onto the
                // sign-in that produced it and onto everything else that session did.
                if (context.Principal?.GetClaim(TellmaClaims.Sid) is { Length: > 0 } sid)
                {
                    context.Transaction.SetProperty(SessionProperty, sid);
                }
            }

            return ValueTask.CompletedTask;
        }
    }

    /// <summary>
    ///     Audits every token-endpoint outcome — issuance and rejection alike — including failure
    ///     paths the pass-through controller never sees (bad client credentials, replayed refresh
    ///     tokens, permission rejections), stamping the subject and recording metrics. Refresh
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

        /// <summary>The handler registration; see <see cref="AuditHandlerOrders.ApplyResponse" />.</summary>
        public static OpenIddictServerHandlerDescriptor Descriptor { get; }
            = OpenIddictServerHandlerDescriptor.CreateBuilder<ApplyTokenResponseContext>()
                .UseScopedHandler<AuditTokenResponseHandler>()
                .SetOrder(AuditHandlerOrders.ApplyResponse)
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
                Sid = context.Transaction.GetProperty<string>(CaptureAuditSubjectHandler.SessionProperty),
                IpAddress = AuditContext.ClientIpAddress(context.Transaction),
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

    /// <summary>
    ///     Audits every revocation-endpoint outcome. OpenIddict's built-in handlers perform the
    ///     revocation itself (no pass-through is needed), so this is the endpoint's only seam for
    ///     the audit trail; the revoked token's subject comes from the capture handler, which ran
    ///     while the presented token was validated.
    /// </summary>
    /// <param name="auditLogger">Audit emission.</param>
    public sealed class AuditRevocationResponseHandler(IAuditLogger auditLogger)
        : IOpenIddictServerHandler<ApplyRevocationResponseContext>
    {
        /// <summary>The handler registration; see <see cref="AuditHandlerOrders.ApplyResponse" />.</summary>
        public static OpenIddictServerHandlerDescriptor Descriptor { get; }
            = OpenIddictServerHandlerDescriptor.CreateBuilder<ApplyRevocationResponseContext>()
                .UseScopedHandler<AuditRevocationResponseHandler>()
                .SetOrder(AuditHandlerOrders.ApplyResponse)
                .Build();

        /// <inheritdoc />
        public async ValueTask HandleAsync(ApplyRevocationResponseContext context)
        {
            ArgumentNullException.ThrowIfNull(context);

            bool succeeded = string.IsNullOrEmpty(context.Response.Error);
            string? subject = context.Transaction.GetProperty<string>(CaptureAuditSubjectHandler.SubjectProperty);

            // RFC 7009 answers 200 for an unknown token, so the response alone cannot separate a
            // real revocation from a sprayed guess; whether the presented token was authentic
            // can. It is a weaker signal than "was revoked" — an authentic token presented by a
            // caller that neither issued nor is the audience of it is refused afterwards, and
            // that refusal is normalized out of the response before this handler runs — so the
            // row records what is actually known rather than implying more.
            bool tokenAuthentic =
                context.Transaction.GetProperty<string>(CaptureAuditSubjectHandler.TokenAuthenticProperty) is not null;

            await auditLogger.LogAsync(new AuditEventEntry
            {
                Action = AuditActions.TokenRevoked,
                Subject = subject,
                ClientId = context.Request?.ClientId,
                Sid = context.Transaction.GetProperty<string>(CaptureAuditSubjectHandler.SessionProperty),
                IpAddress = AuditContext.ClientIpAddress(context.Transaction),
                Outcome = succeeded && tokenAuthentic ? "success" : "failure",
                DetailsJson = JsonSerializer.Serialize(new
                {
                    tokenTypeHint = context.Request?.TokenTypeHint,
                    tokenAuthentic,
                    error = context.Response.Error,
                    errorDescription = context.Response.ErrorDescription,
                }),
            });
        }
    }
}
