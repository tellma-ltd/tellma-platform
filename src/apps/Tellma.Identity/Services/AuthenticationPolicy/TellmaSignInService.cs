// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using System.Globalization;
using System.Security.Claims;
using Tellma.Identity.Data;
using Tellma.Identity.Infrastructure;
using Tellma.Identity.Services.Audit;
using Tellma.Identity.Services.Sessions;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Tellma.Identity.Services.AuthenticationPolicy
{
    /// <summary>The evidence one completed authentication step contributes to the session.</summary>
    /// <param name="Method">The concrete method, from the allow-list vocabulary.</param>
    /// <param name="PasskeyIsDeviceBound">
    ///     For passkey sign-ins: whether the credential is device-bound (non-synced), read from
    ///     the authenticator's backup-state flags.
    /// </param>
    public sealed record SignInEvidence(string Method, bool PasskeyIsDeviceBound = false);

    /// <summary>
    ///     Signs users into the SSO session cookie with the engine's session state stamped in:
    ///     the <c>sid</c>, a fresh <c>auth_time</c>, and the concrete methods used — the evidence
    ///     the policy engine derives <c>acr</c>/<c>amr</c> from at every authorization. All
    ///     interactive sign-in paths must go through this service (the framework's one-line
    ///     sign-in helpers record nothing).
    /// </summary>
    /// <param name="signInManager">The Identity sign-in manager.</param>
    /// <param name="userManager">The Identity user manager.</param>
    /// <param name="sessionRegistry">The sid registry.</param>
    /// <param name="auditLogger">Audit emission.</param>
    /// <param name="metrics">Identity metrics.</param>
    /// <param name="timeProvider">The clock.</param>
    /// <param name="httpContextAccessor">Request context for audit/session detail.</param>
    public sealed class TellmaSignInService(
        SignInManager<TellmaIdentityUser> signInManager,
        UserManager<TellmaIdentityUser> userManager,
        ISessionRegistry sessionRegistry,
        IAuditLogger auditLogger,
        IdentityMetrics metrics,
        TimeProvider timeProvider,
        IHttpContextAccessor httpContextAccessor)
    {
        /// <summary>
        ///     Signs the user in, merging the new evidence with any existing session of the same
        ///     user (step-up preserves the <c>sid</c> and accumulates methods; a different or
        ///     absent user mints a fresh <c>sid</c> — the session-fixation control).
        /// </summary>
        /// <param name="user">The user signing in.</param>
        /// <param name="evidence">The authentication step just completed.</param>
        /// <param name="isPersistent">Whether the cookie persists across browser sessions ("remember me").</param>
        /// <param name="cancellationToken">Aborts the operation.</param>
        /// <returns>A task that completes when the cookie is issued and the session recorded.</returns>
        public async Task SignInAsync(
            TellmaIdentityUser user, SignInEvidence evidence, bool isPersistent, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(user);
            ArgumentNullException.ThrowIfNull(evidence);

            HttpContext httpContext = httpContextAccessor.HttpContext
                ?? throw new InvalidOperationException("Sign-in requires an active HTTP request.");

            // Merge with the current session only when it belongs to the same user.
            List<string> methods = [];
            bool deviceBound = evidence.PasskeyIsDeviceBound;
            string? sid = null;

            ClaimsPrincipal? current = httpContext.User;
            if (current?.Identity?.IsAuthenticated == true
                && string.Equals(userManager.GetUserId(current), user.Id, StringComparison.Ordinal))
            {
                methods.AddRange(current.FindAll(TellmaClaims.Methods).Select(static claim => claim.Value));
                deviceBound |= string.Equals(
                    current.FindFirst(SignInClaims.PasskeyDeviceBound)?.Value, "true", StringComparison.OrdinalIgnoreCase);
                sid = current.FindFirst(TellmaClaims.Sid)?.Value;
            }

            if (!methods.Contains(evidence.Method, StringComparer.Ordinal))
            {
                methods.Add(evidence.Method);
            }

            // A merge into an existing sid is a step-up; a fresh sid is a new session.
            bool isNewSession = sid is null;
            sid ??= Guid.NewGuid().ToString("N");
            long authTime = timeProvider.GetUtcNow().ToUnixTimeSeconds();

            List<Claim> claims =
            [
                new Claim(TellmaClaims.Sid, sid),
                new Claim(Claims.AuthenticationTime, authTime.ToString(CultureInfo.InvariantCulture)),
                new Claim(SignInClaims.PasskeyDeviceBound, deviceBound ? "true" : "false"),
                .. methods.Select(static method => new Claim(TellmaClaims.Methods, method)),
            ];

            await signInManager.SignInWithClaimsAsync(user, isPersistent, claims);

            await sessionRegistry.UpsertSessionAsync(
                sid,
                user.Id,
                httpContext.Request.Headers.UserAgent.ToString(),
                httpContext.Connection.RemoteIpAddress?.ToString(),
                cancellationToken);

            user.LastSignInUtc = timeProvider.GetUtcNow();
            await userManager.UpdateAsync(user);

            string? ipAddress = httpContext.Connection.RemoteIpAddress?.ToString();
            await auditLogger.LogAsync(
                new AuditEventEntry
                {
                    Action = AuditActions.LoginSucceeded,
                    Subject = user.Id,
                    Sid = sid,
                    IpAddress = ipAddress,
                    Outcome = "success",
                    DetailsJson = System.Text.Json.JsonSerializer.Serialize(new { method = evidence.Method }),
                },
                cancellationToken);

            // Session lifecycle: a brand-new sid is a session creation; adding a factor to an
            // existing sid is a step-up. Both are distinct §15 audit events.
            await auditLogger.LogAsync(
                new AuditEventEntry
                {
                    Action = isNewSession ? AuditActions.SessionCreated : AuditActions.StepUpCompleted,
                    Subject = user.Id,
                    Sid = sid,
                    IpAddress = ipAddress,
                    Outcome = "success",
                },
                cancellationToken);

            metrics.LoginAttempt(evidence.Method, "success", isNewSession ? "primary" : "step_up");
        }
    }
}
