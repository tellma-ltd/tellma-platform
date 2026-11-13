// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.AspNetCore.Http;
using System.Buffers;
using System.Buffers.Text;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;
using Tellma.Core.AspNetCore.Authentication;

namespace Tellma.Core.AspNetCore.Sessions
{
    /// <summary>
    ///     The SPA's readable signed-in signal: unpadded base64url of the UTF-8 JSON
    ///     <c>{ v: 2, sub, sh, name, email, locale }</c>, with the session cookie's lifetime and no
    ///     credential. <c>sh</c> names the session; no endpoint accepts it in place of the session cookie.
    /// </summary>
    internal static class ProfileCookie
    {
        /// <summary>The shape's version.</summary>
        public const int Version = 2;

        /// <summary>The encoder of the JSON: every character as itself, which the base64url wrapping keeps cookie-safe.</summary>
        private static readonly JsonWriterOptions WriterOptions = new() { Encoder = JavaScriptEncoder.Create(UnicodeRanges.All) };

        /// <summary>Writes the cookie for a session.</summary>
        /// <param name="response">The response, not yet started.</param>
        /// <param name="principal">The session's principal.</param>
        /// <param name="handle">The session's handle.</param>
        /// <param name="expires">The session cookie's expiry.</param>
        /// <param name="secure">Whether the cookie is restricted to HTTPS.</param>
        public static void Append(HttpResponse response, ClaimsPrincipal principal, string handle, DateTimeOffset expires, bool secure)
        {
            ArgumentNullException.ThrowIfNull(response);
            ArgumentNullException.ThrowIfNull(principal);
            ArgumentNullException.ThrowIfNull(handle);
            response.Cookies.Append(TellmaAuthentication.ProfileCookieName, Encode(principal, handle), Options(secure, expires));
        }

        /// <summary>Clears the cookie.</summary>
        /// <param name="response">The response, not yet started.</param>
        /// <param name="secure">Whether the cookie is restricted to HTTPS.</param>
        public static void Delete(HttpResponse response, bool secure)
        {
            ArgumentNullException.ThrowIfNull(response);
            response.Cookies.Delete(TellmaAuthentication.ProfileCookieName, Options(secure, expires: null));
        }

        /// <summary>The cookie's value for a session.</summary>
        /// <param name="principal">The session's principal.</param>
        /// <param name="handle">The session's handle.</param>
        /// <returns>The value: unpadded base64url of the UTF-8 JSON.</returns>
        public static string Encode(ClaimsPrincipal principal, string handle)
        {
            ArgumentNullException.ThrowIfNull(principal);
            ArgumentNullException.ThrowIfNull(handle);

            // --- The JSON: every member present, an absent name or email empty and an absent locale null ---
            ArrayBufferWriter<byte> buffer = new();
            using (Utf8JsonWriter writer = new(buffer, WriterOptions))
            {
                writer.WriteStartObject();
                writer.WriteNumber("v", Version);
                writer.WriteString("sub", principal.FindFirst(TellmaClaimTypes.Subject)?.Value ?? string.Empty);
                writer.WriteString("sh", handle);
                writer.WriteString("name", principal.FindFirst(TellmaClaimTypes.Name)?.Value ?? string.Empty);
                writer.WriteString("email", principal.FindFirst(TellmaClaimTypes.Email)?.Value ?? string.Empty);
                if (principal.FindFirst(TellmaClaimTypes.Locale)?.Value is { Length: > 0 } locale)
                {
                    writer.WriteString("locale", locale);
                }
                else
                {
                    writer.WriteNull("locale");
                }

                writer.WriteEndObject();
            }

            return Base64Url.EncodeToString(buffer.WrittenSpan);
        }

        /// <summary>The cookie's attributes: the whole site, sent on top-level navigations, readable by script.</summary>
        /// <param name="secure">Whether the cookie is restricted to HTTPS.</param>
        /// <param name="expires">The expiry; null when deleting.</param>
        /// <returns>The options.</returns>
        private static CookieOptions Options(bool secure, DateTimeOffset? expires)
        {
            return new CookieOptions
            {
                Path = "/",
                Secure = secure,
                SameSite = SameSiteMode.Lax,
                HttpOnly = false,
                IsEssential = true,
                Expires = expires,
            };
        }
    }
}
