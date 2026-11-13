// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Net.Http.Headers;
using System.Net;

namespace Tellma.Core.AspNetCore.Tests.Infrastructure
{
    /// <summary>What a browser holds after a sign-in through the BFF.</summary>
    /// <param name="Login">The answer of <c>/bff/login</c>.</param>
    /// <param name="Callback">The answer of the sign-in callback.</param>
    /// <param name="Cookies">The cookies the callback set, by name.</param>
    internal sealed record SignInResult(HttpResponseMessage Login, HttpResponseMessage Callback, IReadOnlyDictionary<string, SetCookieHeaderValue> Cookies)
    {
        /// <summary>The <c>Cookie</c> header that carries the session cookie.</summary>
        /// <param name="host">The host, whose cookie name it reads.</param>
        /// <returns>The header's value.</returns>
        public string SessionCookie(TellmaTestHost host)
        {
            string name = host.SessionCookieName;
            return $"{name}={Cookies[name].Value}";
        }
    }

    /// <summary>
    ///     The browser's half of a sign-in: <c>/bff/login</c>, the pushed request the identity server
    ///     records, and the callback with the code and the state, carrying the correlation and nonce
    ///     cookies the login set.
    /// </summary>
    internal static class SignInFlow
    {
        /// <summary>Signs in through the BFF.</summary>
        /// <param name="host">The host.</param>
        /// <param name="query">The login's query string, with its leading <c>?</c>.</param>
        /// <param name="cookie">A session cookie the browser already carries, if any.</param>
        /// <returns>What the browser holds.</returns>
        public static async Task<SignInResult> SignInAsync(TellmaTestHost host, string query = "", string? cookie = null)
        {
            CancellationToken cancellationToken = TestContext.Current.CancellationToken;

            // --- The login: a challenge that pushes the request and redirects to the authorize endpoint ---
            using HttpRequestMessage login = new(HttpMethod.Get, "/bff/login" + query);
            HttpResponseMessage loginResponse = await host.Client.SendAsync(login, cancellationToken);
            Assert.Equal(HttpStatusCode.Redirect, loginResponse.StatusCode);
            IdentityRequest pushed = host.IdentityServer.Requests.Last(static request => request.Path == "/connect/par");
            string state = pushed.Form["state"];

            // --- The callback: the code, the state and the cookies the login set ---
            List<string> cookies = [.. Parse(loginResponse).Select(static pair => $"{pair.Key}={pair.Value.Value}")];
            if (cookie is not null)
            {
                cookies.Add(cookie);
            }

            using HttpRequestMessage callback = new(HttpMethod.Get, $"/signin-oidc?code=code-1&state={Uri.EscapeDataString(state)}");
            callback.Headers.Add("Cookie", string.Join("; ", cookies));
            HttpResponseMessage callbackResponse = await host.Client.SendAsync(callback, cancellationToken);
            return new SignInResult(loginResponse, callbackResponse, Parse(callbackResponse));
        }

        /// <summary>The cookies a response sets, by name; a later one wins.</summary>
        /// <param name="response">The response.</param>
        /// <returns>The cookies.</returns>
        public static Dictionary<string, SetCookieHeaderValue> Parse(HttpResponseMessage response)
        {
            Dictionary<string, SetCookieHeaderValue> cookies = new(StringComparer.Ordinal);
            if (response.Headers.TryGetValues(HeaderNames.SetCookie, out IEnumerable<string>? values))
            {
                foreach (SetCookieHeaderValue value in SetCookieHeaderValue.ParseList([.. values]))
                {
                    cookies[value.Name.Value!] = value;
                }
            }

            return cookies;
        }
    }
}
