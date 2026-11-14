// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;
using System.Globalization;

namespace Tellma.Identity.Infrastructure
{
    /// <summary>
    ///     Renders an authorization request's own pages in the language the request asked for,
    ///     when nothing on the URL or in the browser has already settled the language.
    ///     <para>
    ///         Request localization reads the query, the language cookie and the browser's
    ///         preference, and it runs before the authorization request is known. A pushed
    ///         request states its language where none of those look: the browser arrives with a
    ///         reference to the request and nothing else. This filter runs once the request has
    ///         been restored and fills that gap, so the consent page of a pushed request follows
    ///         the language its sign-in pages were shown in.
    ///     </para>
    ///     <para>
    ///         It stands in for the browser's preference only. A language named on the URL has
    ///         already been applied, and a language the user picked — which is what the cookie
    ///         records — is theirs to keep: the picker on these pages would otherwise do nothing.
    ///     </para>
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
    public sealed class RequestedLanguageAttribute : Attribute, IAsyncResourceFilter
    {
        /// <inheritdoc />
        public async Task OnResourceExecutionAsync(ResourceExecutingContext context, ResourceExecutionDelegate next)
        {
            ArgumentNullException.ThrowIfNull(context);
            ArgumentNullException.ThrowIfNull(next);

            HttpContext http = context.HttpContext;

            // Which source decided the language so far: none (the default applied), the browser's
            // preference, or something explicit — the URL or the cookie — which stands.
            IRequestCultureProvider? decidedBy = http.Features.Get<IRequestCultureFeature>()?.Provider;
            if (decidedBy is null or AcceptLanguageHeaderRequestCultureProvider
                && http.GetOpenIddictServerRequest()?.UiLocales is { } requested
                && http.RequestServices.GetRequiredService<LanguageCatalog>().Match(requested) is { } offered)
            {
                // Set here, around the rest of the request, rather than in the action: a culture
                // set inside an action is gone by the time its view renders, because each async
                // method hands its caller back the context it was called with.
                var culture = CultureInfo.GetCultureInfo(offered);
                http.Features.Set<IRequestCultureFeature>(new RequestCultureFeature(new RequestCulture(culture), provider: null));
                CultureInfo.CurrentCulture = culture;
                CultureInfo.CurrentUICulture = culture;
            }

            await next();
        }
    }
}
