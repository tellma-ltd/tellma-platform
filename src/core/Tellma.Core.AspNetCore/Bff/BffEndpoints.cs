// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using Microsoft.Net.Http.Headers;
using System.Globalization;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;
using Tellma.Core.Abstractions.Api;
using Tellma.Core.Abstractions.Tenancy;
using Tellma.Core.AspNetCore.Authentication;
using Tellma.Core.AspNetCore.Hosting;
using Tellma.Core.AspNetCore.Observability;
using Tellma.Core.AspNetCore.Problems;
using Tellma.Core.AspNetCore.Sessions;

namespace Tellma.Core.AspNetCore.Bff
{
    /// <summary>
    ///     The BFF's endpoints: the sign-in, the sign-out, the signed-in user and the back-channel logout
    ///     receiver. All are tenantless; the sign-out and the user read authenticate the session cookie, the
    ///     sign-in and the receiver are anonymous by design.
    /// </summary>
    internal static class BffEndpoints
    {
        /// <summary>The sign-in route.</summary>
        public const string LoginRoute = "/bff/login";

        /// <summary>The sign-out route.</summary>
        public const string LogoutRoute = "/bff/logout";

        /// <summary>The signed-in user's route.</summary>
        public const string UserRoute = "/bff/user";

        /// <summary>The back-channel logout receiver's route.</summary>
        public const string BackchannelLogoutRoute = "/bff/backchannel-logout";

        /// <summary>Why the BFF's endpoints serve no tenant.</summary>
        public const string TenantlessReason = "bff";

        /// <summary>The form parameter carrying a logout token.</summary>
        public const string LogoutTokenParameter = "logout_token";

        /// <summary>The scope of a sign-out that ends the identity server's session too.</summary>
        public const string GlobalScope = "global";

        /// <summary>The scope of a sign-out that ends this distribution's session alone.</summary>
        public const string LocalScope = "local";

        /// <summary>The longest acr values or language list a sign-in accepts.</summary>
        private const int MaxParameterLength = 256;

        /// <summary>Every answer of the BFF is about one user, so none is cached.</summary>
        private const string NoStore = "no-store";

        /// <summary>The JSON writer's options: every character as itself.</summary>
        private static readonly JsonWriterOptions WriterOptions = new() { Encoder = JavaScriptEncoder.Create(UnicodeRanges.All) };

        /// <summary>Maps the endpoints, each stamped by the caller as a platform endpoint.</summary>
        /// <param name="routes">The application's routes.</param>
        /// <returns>The anonymous endpoints and the session-authenticated ones, for the caller to stamp.</returns>
        public static (IEndpointConventionBuilder[] Anonymous, IEndpointConventionBuilder[] Session) Map(IEndpointRouteBuilder routes)
        {
            ArgumentNullException.ThrowIfNull(routes);
            return (
                [routes.MapGet(LoginRoute, LoginAsync), routes.MapPost(BackchannelLogoutRoute, BackchannelLogoutAsync)],
                [routes.MapPost(LogoutRoute, LogoutAsync), routes.MapGet(UserRoute, UserAsync)]);
        }

        // ------------------------------------------------------------------------------------------
        // GET /bff/login
        // ------------------------------------------------------------------------------------------

        /// <summary>Challenges the sign-in scheme, carrying the sign-in's constraints and where to return.</summary>
        /// <param name="context">The request.</param>
        /// <returns>A task that completes when the challenge is written.</returns>
        internal static async Task LoginAsync(HttpContext context)
        {
            IQueryCollection query = context.Request.Query;

            // --- The parameters, each at most once: a local return address, a bounded assurance request, a language ---
            string? returnUrl = Single(query["returnUrl"]);
            string? acrValues = Single(query["acr_values"]);
            string? maxAgeText = Single(query["max_age"]);
            string? uiLocales = Single(query["ui_locales"]);
            int? maxAge = null;
            if (query["returnUrl"].Count > 1 || query["acr_values"].Count > 1 || query["max_age"].Count > 1 || query["ui_locales"].Count > 1
                || (returnUrl is not null && !IsLocalUrl(returnUrl))
                || (acrValues is not null && !IsParameterText(acrValues))
                || (uiLocales is not null && !IsLanguageList(uiLocales))
                || (maxAgeText is not null && !TryParseMaxAge(maxAgeText, out maxAge)))
            {
                await ProblemWriter.WriteAsync(context, StatusCodes.Status400BadRequest, "bad-request").ConfigureAwait(false);
                return;
            }

            // --- The distribution's own constraints on a sign-in that has no tenant yet ---
            LoginPolicy policy = await context.RequestServices.GetRequiredService<IAuthenticationPolicyProvider>()
                .GetLoginPolicyAsync(tenantId: null, context.RequestAborted).ConfigureAwait(false);

            OpenIdConnectChallengeProperties properties = new()
            {
                RedirectUri = returnUrl ?? "/",
                IsPersistent = true,
            };
            // The policy bounds the caller: the stronger of the two assurances and the stricter of the
            // two recencies win, so no query weakens what the distribution requires and a step-up above
            // the policy still reaches the identity server.
            string stepUp = context.RequestServices.GetRequiredService<IOptions<TellmaSessionOptions>>().Value.StepUp.Acr;
            if (StrongerAcr(policy.AcrValues, acrValues, stepUp) is { Length: > 0 } acr)
            {
                properties.SetParameter(BffOidcEvents.AcrValuesParameter, acr);
            }

            TimeSpan? requested = maxAge is int seconds ? TimeSpan.FromSeconds(seconds) : null;
            properties.MaxAge = (requested, policy.MaxAge) switch
            {
                (TimeSpan caller, TimeSpan required) => caller < required ? caller : required,
                (TimeSpan caller, null) => caller,
                (null, TimeSpan required) => required,
                _ => null,
            };

            if (uiLocales is not null)
            {
                properties.SetParameter(BffOidcEvents.UiLocalesParameter, uiLocales);
            }

            if (policy.AllowedMethods is { Count: > 0 } methods)
            {
                properties.SetParameter(BffOidcEvents.AllowedMethodsParameter, string.Join(' ', methods));
            }

            context.Response.Headers[HeaderNames.CacheControl] = NoStore;
            await context.ChallengeAsync(TellmaAuthentication.OidcScheme, properties).ConfigureAwait(false);
        }

        // ------------------------------------------------------------------------------------------
        // POST /bff/logout
        // ------------------------------------------------------------------------------------------

        /// <summary>
        ///     Signs the caller out: the listeners told, the session deleted and both cookies cleared, the
        ///     cache cleared, and the address to go to next answered rather than redirected to.
        /// </summary>
        /// <param name="context">The request, past the session policy.</param>
        /// <returns>A task that completes when the answer is written.</returns>
        internal static async Task LogoutAsync(HttpContext context)
        {
            // --- The scope: global unless the caller asks for local ---
            string? scope = await ReadScopeAsync(context).ConfigureAwait(false);
            if (scope is not (GlobalScope or LocalScope))
            {
                await ProblemWriter.WriteAsync(context, StatusCodes.Status400BadRequest, "bad-request").ConfigureAwait(false);
                return;
            }

            // --- 1. The listeners, with the caller's own handle, before the row goes ---
            AuthenticateResult session = await context.AuthenticateAsync(TellmaAuthentication.SessionScheme).ConfigureAwait(false);
            string? subject = session.Principal?.FindFirst(TellmaClaimTypes.Subject)?.Value;
            string? handle = session.Properties is { } properties ? SessionProperties.GetHandle(properties) : null;
            if (subject is not null && handle is not null)
            {
                await context.RequestServices.GetRequiredService<SessionTerminationNotifier>().NotifyAsync(subject, [handle]).ConfigureAwait(false);
            }

            // The end-session address needs the session's identity token, so it is built before the
            // session is deleted; the sign-in scheme's event captures it instead of redirecting.
            string redirectUrl = TellmaAuthenticationComposition.SignedOutPath;
            if (scope == GlobalScope)
            {
                ILogger logger = context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(BffEndpoints));
                try
                {
                    await context.SignOutAsync(TellmaAuthentication.OidcScheme, new AuthenticationProperties { RedirectUri = TellmaAuthenticationComposition.SignedOutPath })
                        .ConfigureAwait(false);
                    if (BffOidcEvents.CapturedEndSession(context) is string endSession)
                    {
                        redirectUrl = endSession;
                    }
                    else
                    {
                        // The identity server's metadata names no end-session endpoint: its session
                        // survives, so the fallback to a local sign-out is recorded.
                        HostLog.BffGlobalSignOutUnavailable(logger, exception: null);
                    }
                }
                catch (Exception exception) when (exception is not OperationCanceledException || !context.RequestAborted.IsCancellationRequested)
                {
                    // An unreachable identity server never keeps a user signed in here.
                    HostLog.BffGlobalSignOutUnavailable(logger, exception);
                }
            }

            // --- 2. The local sign-out: the row deleted, the session and profile cookies cleared ---
            await context.SignOutAsync(TellmaAuthentication.SessionScheme).ConfigureAwait(false);

            // --- 3. Nothing the user saw stays in the browser's cache ---
            context.Response.Headers["Clear-Site-Data"] = "\"cache\"";
            context.Response.Headers[HeaderNames.CacheControl] = NoStore;
            context.Response.StatusCode = StatusCodes.Status200OK;
            context.Response.ContentType = "application/json; charset=utf-8";
            await JsonSerializer.SerializeAsync(context.Response.Body, new LogoutResult(redirectUrl), TellmaJson.Options, context.RequestAborted).ConfigureAwait(false);
        }

        /// <summary>The scope a sign-out body names: <c>global</c> when there is no body or it names none.</summary>
        /// <param name="context">The request.</param>
        /// <returns>The scope, or null when the body is malformed.</returns>
        private static async Task<string?> ReadScopeAsync(HttpContext context)
        {
            if (context.Request.ContentLength is 0 || (context.Request.ContentLength is null && !context.Request.HasJsonContentType()))
            {
                return GlobalScope;
            }

            try
            {
                using JsonDocument body = await JsonDocument.ParseAsync(context.Request.Body, cancellationToken: context.RequestAborted).ConfigureAwait(false);
                return body.RootElement.ValueKind != JsonValueKind.Object
                    ? null
                    : !body.RootElement.TryGetProperty("scope", out JsonElement scope) || scope.ValueKind == JsonValueKind.Null
                        ? GlobalScope
                        : scope.ValueKind == JsonValueKind.String ? scope.GetString() : null;
            }
            catch (JsonException)
            {
                return null;
            }
        }

        // ------------------------------------------------------------------------------------------
        // GET /bff/user
        // ------------------------------------------------------------------------------------------

        /// <summary>The signed-in user and the tenants the membership hints list for them.</summary>
        /// <param name="context">The request, past the session policy.</param>
        /// <returns>A task that completes when the answer is written.</returns>
        internal static async Task UserAsync(HttpContext context)
        {
            ClaimsPrincipal user = context.User;
            string subject = user.FindFirst(TellmaClaimTypes.Subject)?.Value
                ?? throw new InvalidOperationException("The session policy admitted a principal with no subject.");
            IReadOnlyList<TenantMembership> memberships = await context.RequestServices.GetRequiredService<ITenantMembershipDirectory>()
                .ListAsync(subject, context.RequestAborted).ConfigureAwait(false);

            context.Response.Headers[HeaderNames.CacheControl] = NoStore;
            await WriteJsonAsync(context, writer =>
            {
                writer.WriteStartObject();
                writer.WriteString("sub", subject);
                WriteClaim(writer, "name", user, TellmaClaimTypes.Name);
                WriteClaim(writer, "email", user, TellmaClaimTypes.Email);
                WriteClaim(writer, "locale", user, TellmaClaimTypes.Locale);
                WriteClaim(writer, "acr", user, TellmaClaimTypes.AuthenticationContextClass);
                writer.WriteStartArray("tenants");
                foreach (TenantMembership membership in memberships)
                {
                    TenantDescriptor tenant = membership.Tenant;
                    writer.WriteStartObject();
                    writer.WriteNumber("id", tenant.Id);
                    writer.WriteString("name", tenant.Name);
                    writer.WriteString("name2", tenant.Name2);
                    writer.WriteString("name3", tenant.Name3);
                    writer.WriteStartArray("languages");
                    foreach (string language in tenant.Languages)
                    {
                        writer.WriteStringValue(language);
                    }

                    writer.WriteEndArray();
                    writer.WriteString("category", tenant.Category.ToString());
                    writer.WriteString("state", tenant.State.ToString());
                    if (tenant.LiveTenantId is int live)
                    {
                        writer.WriteNumber("liveTenantId", live);
                    }
                    else
                    {
                        writer.WriteNull("liveTenantId");
                    }

                    writer.WriteBoolean("isActive", membership.IsActive);
                    writer.WriteEndObject();
                }

                writer.WriteEndArray();
                writer.WriteEndObject();
            }).ConfigureAwait(false);
        }

        // ------------------------------------------------------------------------------------------
        // POST /bff/backchannel-logout
        // ------------------------------------------------------------------------------------------

        /// <summary>
        ///     Receives a back-channel logout: a valid token ends the sessions it names and answers
        ///     <c>200</c>, a replay included; anything else answers <c>400</c> with no body.
        /// </summary>
        /// <param name="context">The request.</param>
        /// <returns>A task that completes when the answer is written.</returns>
        internal static async Task BackchannelLogoutAsync(HttpContext context)
        {
            HostMetrics metrics = context.RequestServices.GetRequiredService<HostMetrics>();
            ILogger logger = context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(BffEndpoints));
            context.Response.Headers[HeaderNames.CacheControl] = NoStore;

            // --- The token: one form parameter of a form body ---
            string? token = null;
            if (context.Request.HasFormContentType)
            {
                try
                {
                    IFormCollection form = await context.Request.ReadFormAsync(context.RequestAborted).ConfigureAwait(false);
                    token = Single(form[LogoutTokenParameter]);
                }
                catch (Exception exception) when (exception is InvalidDataException or IOException or BadHttpRequestException)
                {
                    token = null;
                }
            }

            if (token is null)
            {
                Refuse(context, metrics, logger, "the request carries no single logout_token form parameter");
                return;
            }

            // --- The validation ---
            LogoutTokenResult result = await context.RequestServices.GetRequiredService<BackchannelLogoutValidator>()
                .ValidateAsync(token, context.RequestAborted).ConfigureAwait(false);
            switch (result.Status)
            {
                case LogoutTokenStatus.Invalid:
                    Refuse(context, metrics, logger, result.Reason ?? "invalid");
                    return;

                case LogoutTokenStatus.Unavailable:
                    // The identity server retries a delivery that failed on this side.
                    metrics.RecordBackchannelLogout(accepted: false);
                    HostLog.BackchannelLogoutRejected(logger, result.Reason ?? "unavailable");
                    context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
                    return;

                case LogoutTokenStatus.Valid:
                default:
                    break;
            }

            // --- The sessions it names end: by the identity server's session, or by the subject alone ---
            ICatalogSessionStore store = context.RequestServices.GetRequiredService<ICatalogSessionStore>();
            if (result.Sid is string sid)
            {
                await store.RevokeBySidAsync(sid, context.RequestAborted).ConfigureAwait(false);
            }
            else
            {
                await store.RevokeBySubjectAsync(result.Subject!, context.RequestAborted).ConfigureAwait(false);
            }

            metrics.RecordBackchannelLogout(accepted: true);
            context.Response.StatusCode = StatusCodes.Status200OK;
        }

        /// <summary>Refuses a logout token: <c>400</c> with no body, counted and logged with the reason.</summary>
        /// <param name="context">The request.</param>
        /// <param name="metrics">The host's instruments.</param>
        /// <param name="logger">The logger.</param>
        /// <param name="reason">Why.</param>
        private static void Refuse(HttpContext context, HostMetrics metrics, ILogger logger, string reason)
        {
            metrics.RecordBackchannelLogout(accepted: false);
            HostLog.BackchannelLogoutRejected(logger, reason);
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
        }

        // ------------------------------------------------------------------------------------------
        // Helpers
        // ------------------------------------------------------------------------------------------

        /// <summary>
        ///     Whether a return address stays on this origin: a path with one leading slash, not followed by
        ///     a slash or a backslash, and only visible ASCII characters. A control character is one a
        ///     browser would drop or follow elsewhere; a space or a character beyond ASCII cannot stand in
        ///     the <c>Location</c> header the sign-in's callback answers with, so the address must arrive
        ///     percent-encoded.
        /// </summary>
        /// <param name="url">The address.</param>
        /// <returns>True when it is local.</returns>
        internal static bool IsLocalUrl(string url)
        {
            return url.Length > 0
                && url[0] == '/'
                && (url.Length == 1 || (url[1] != '/' && url[1] != '\\'))
                && url.All(static c => c is > ' ' and <= '~');
        }

        /// <summary>
        ///     The acr values a sign-in sends: the stronger of the distribution's policy and the caller's
        ///     request, never the weaker.
        /// </summary>
        /// <param name="policy">The policy's acr values; null or empty when it names none.</param>
        /// <param name="caller">The caller's acr values; null or empty when it named none.</param>
        /// <param name="stepUp">The class sensitive operations require, which the platform itself asks callers to step up to.</param>
        /// <returns>The values to send, or null when neither names any.</returns>
        /// <remarks>
        ///     Values are ranked by the identity server's tier scheme (aal1, then aal2 or <c>phr</c>, then
        ///     aal3 or <c>phrh</c>); a list ranks as its weakest member, since the identity server may
        ///     satisfy any of them. When either side cannot be ranked, the caller's values win only when
        ///     they are the step-up class, which the distribution configured as its own bar; otherwise the
        ///     policy's do.
        /// </remarks>
        internal static string? StrongerAcr(string? policy, string? caller, string stepUp)
        {
            if (string.IsNullOrEmpty(caller))
            {
                return string.IsNullOrEmpty(policy) ? null : policy;
            }

            if (string.IsNullOrEmpty(policy))
            {
                return caller;
            }

            // --- Both name a class: the higher tier, the policy's on a tie; unranked, the step-up class ---
            return AcrRank(policy) is int policyRank && AcrRank(caller) is int callerRank
                ? (callerRank > policyRank ? caller : policy)
                : (string.Equals(caller, stepUp, StringComparison.Ordinal) ? caller : policy);
        }

        /// <summary>The tier an acr value or list reaches in the identity server's scheme.</summary>
        /// <param name="values">The space-separated values.</param>
        /// <returns>The weakest member's tier, or null when any member is outside the scheme.</returns>
        private static int? AcrRank(string values)
        {
            int? weakest = null;
            foreach (string value in values.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                int? rank = value switch
                {
                    "urn:tellma:acr:aal1" => 1,
                    "urn:tellma:acr:aal2" or "phr" => 2,
                    "urn:tellma:acr:aal3" or "phrh" => 3,
                    _ => null,
                };
                if (rank is not int known)
                {
                    return null;
                }

                weakest = weakest is int current && current < known ? current : known;
            }

            return weakest;
        }

        /// <summary>Writes a JSON answer.</summary>
        /// <param name="context">The request.</param>
        /// <param name="write">Writes the document.</param>
        /// <returns>A task that completes when the answer is written.</returns>
        private static async Task WriteJsonAsync(HttpContext context, Action<Utf8JsonWriter> write)
        {
            context.Response.StatusCode = StatusCodes.Status200OK;
            context.Response.ContentType = "application/json; charset=utf-8";
            await using Utf8JsonWriter writer = new(context.Response.Body, WriterOptions);
            write(writer);
            await writer.FlushAsync(context.RequestAborted).ConfigureAwait(false);
        }

        /// <summary>Writes a claim's value, or null when the principal lacks it.</summary>
        /// <param name="writer">The writer.</param>
        /// <param name="name">The member's name.</param>
        /// <param name="user">The principal.</param>
        /// <param name="type">The claim type.</param>
        private static void WriteClaim(Utf8JsonWriter writer, string name, ClaimsPrincipal user, string type)
        {
            writer.WriteString(name, user.FindFirst(type)?.Value);
        }

        /// <summary>A parameter's one value.</summary>
        /// <param name="values">The parameter's values.</param>
        /// <returns>The value, or null when absent or repeated.</returns>
        private static string? Single(StringValues values)
        {
            return values.Count == 1 ? values[0] : null;
        }

        /// <summary>Whether a value is bounded, printable ASCII.</summary>
        /// <param name="value">The value.</param>
        /// <returns>True when it is.</returns>
        private static bool IsParameterText(string value)
        {
            return value.Length is > 0 and <= MaxParameterLength && value.All(static c => c is >= ' ' and <= '~');
        }

        /// <summary>Whether a value is a space-separated list of language tags.</summary>
        /// <param name="value">The value.</param>
        /// <returns>True when it is.</returns>
        private static bool IsLanguageList(string value)
        {
            return value.Length is > 0 and <= MaxParameterLength
                && value.Split(' ').All(static tag => tag.Length is > 0 and <= 35 && tag.All(static c => char.IsAsciiLetterOrDigit(c) || c == '-'));
        }

        /// <summary>Parses a maximum authentication age in whole seconds.</summary>
        /// <param name="value">The value.</param>
        /// <param name="seconds">The age.</param>
        /// <returns>True when it is a non-negative integer.</returns>
        private static bool TryParseMaxAge(string value, out int? seconds)
        {
            bool parsed = int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int age);
            seconds = parsed ? age : null;
            return parsed;
        }
    }
}
