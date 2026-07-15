// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.ActionConstraints;
using Microsoft.AspNetCore.Routing;

namespace Tellma.Identity.Infrastructure
{
    /// <summary>
    ///     Routes a POST to the action whose named form value is present — how a single protocol
    ///     endpoint dispatches its consent form's Accept/Deny buttons to separate actions.
    /// </summary>
    /// <param name="name">The required form field name (for example <c>submit.Accept</c>).</param>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
    public sealed class FormValueRequiredAttribute(string name) : ActionMethodSelectorAttribute
    {
        /// <inheritdoc />
        public override bool IsValidForRequest(RouteContext routeContext, ActionDescriptor action)
        {
            ArgumentNullException.ThrowIfNull(routeContext);

            HttpRequest request = routeContext.HttpContext.Request;
            return !HttpMethods.IsGet(request.Method)
                && !HttpMethods.IsHead(request.Method)
                && !HttpMethods.IsDelete(request.Method)
                && !HttpMethods.IsTrace(request.Method)
                && !string.IsNullOrEmpty(request.ContentType)
                && request.ContentType.StartsWith("application/x-www-form-urlencoded", StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrEmpty(request.Form[name]);
        }
    }
}
