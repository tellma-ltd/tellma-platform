// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.AspNetCore.Authentication;
using System.Globalization;

namespace Tellma.Core.AspNetCore.Sessions
{
    /// <summary>
    ///     The items the store and the session events keep in a session ticket's properties: the
    ///     handle, stamped at creation; the row's tokens version, stamped on every read; and the
    ///     creation time, which bounds the session's absolute lifetime.
    /// </summary>
    internal static class SessionProperties
    {
        /// <summary>The handle's item, which the tenant middleware binds as the request's session handle.</summary>
        public const string Handle = TellmaAuthentication.SessionHandleProperty;

        /// <summary>The tokens version's item: the row's compare-and-set counter as the ticket was read.</summary>
        public const string TokensVersion = ".tellma.session.version";

        /// <summary>The creation time's item: when the user signed in, in UTC ticks.</summary>
        public const string CreatedAt = ".tellma.session.created";

        /// <summary>The token item the OpenID Connect handler saves the access token's expiry under.</summary>
        public const string ExpiresAtToken = "expires_at";

        /// <summary>The session's handle.</summary>
        /// <param name="properties">The ticket's properties.</param>
        /// <returns>The handle, or null when the ticket was never stored.</returns>
        public static string? GetHandle(AuthenticationProperties properties)
        {
            return properties.Items.TryGetValue(Handle, out string? handle) ? handle : null;
        }

        /// <summary>The row's tokens version the ticket was read at.</summary>
        /// <param name="properties">The ticket's properties.</param>
        /// <returns>The version, or null when none is stamped.</returns>
        public static int? GetTokensVersion(AuthenticationProperties properties)
        {
            return properties.Items.TryGetValue(TokensVersion, out string? value)
                && int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int version)
                ? version
                : null;
        }

        /// <summary>Stamps the row's tokens version.</summary>
        /// <param name="properties">The ticket's properties.</param>
        /// <param name="version">The version.</param>
        public static void SetTokensVersion(AuthenticationProperties properties, int version)
        {
            properties.Items[TokensVersion] = version.ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>When the session was created.</summary>
        /// <param name="properties">The ticket's properties.</param>
        /// <returns>The instant, or null when none is stamped.</returns>
        public static DateTimeOffset? GetCreatedAt(AuthenticationProperties properties)
        {
            return properties.Items.TryGetValue(CreatedAt, out string? value)
                && long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out long ticks)
                && ticks <= DateTimeOffset.MaxValue.UtcTicks
                ? new DateTimeOffset(ticks, TimeSpan.Zero)
                : null;
        }

        /// <summary>Stamps when the session was created.</summary>
        /// <param name="properties">The ticket's properties.</param>
        /// <param name="createdAt">The instant.</param>
        public static void SetCreatedAt(AuthenticationProperties properties, DateTimeOffset createdAt)
        {
            properties.Items[CreatedAt] = createdAt.UtcTicks.ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>When the stored access token expires, as the OpenID Connect handler saved it.</summary>
        /// <param name="properties">The ticket's properties.</param>
        /// <returns>The instant, or null when no expiry is saved or it cannot be read.</returns>
        public static DateTimeOffset? GetAccessTokenExpiry(AuthenticationProperties properties)
        {
            return properties.GetTokenValue(ExpiresAtToken) is string value
                && DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTimeOffset expiresAt)
                ? expiresAt
                : null;
        }

        /// <summary>The instant a session expires when it is renewed now: the idle window, capped by the absolute one.</summary>
        /// <param name="now">The current instant.</param>
        /// <param name="createdAt">When the session was created.</param>
        /// <param name="idle">The idle lifetime.</param>
        /// <param name="absolute">The absolute lifetime.</param>
        /// <returns>The expiry, in whole seconds.</returns>
        public static DateTimeOffset ExpiryAt(DateTimeOffset now, DateTimeOffset createdAt, TimeSpan idle, TimeSpan absolute)
        {
            DateTimeOffset sliding = now + idle;
            DateTimeOffset cap = createdAt + absolute;
            DateTimeOffset expiry = sliding < cap ? sliding : cap;

            // Whole seconds: the ticket keeps its expiry and the cookie states its Expires to the
            // second, so the row's ExpiresAt, the ticket and the cookie all name one instant.
            return new DateTimeOffset(expiry.UtcTicks - (expiry.UtcTicks % TimeSpan.TicksPerSecond), TimeSpan.Zero);
        }
    }
}
