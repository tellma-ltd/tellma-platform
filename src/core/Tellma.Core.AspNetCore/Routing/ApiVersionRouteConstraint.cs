// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using System.Globalization;

namespace Tellma.Core.AspNetCore.Routing
{
    /// <summary>
    ///     The <c>apiVersion</c> constraint of the public API's group: a major version, optionally with
    ///     a minor one (<c>1</c>, <c>1.0</c>). The web API's versioning replaces it when composed.
    /// </summary>
    internal sealed class ApiVersionRouteConstraint : IRouteConstraint
    {
        /// <summary>The constraint's name in route templates.</summary>
        public const string Name = "apiVersion";

        /// <inheritdoc />
        public bool Match(HttpContext? httpContext, IRouter? route, string routeKey, RouteValueDictionary values, RouteDirection routeDirection)
        {
            ArgumentNullException.ThrowIfNull(values);
            return values.TryGetValue(routeKey, out object? value)
                && Convert.ToString(value, CultureInfo.InvariantCulture) is string text
                && IsVersion(text);
        }

        /// <summary>Whether text is a version: digits, optionally a dot and more digits.</summary>
        /// <param name="text">The text.</param>
        /// <returns>True when it is.</returns>
        internal static bool IsVersion(string text)
        {
            int dot = text.IndexOf('.', StringComparison.Ordinal);
            return dot < 0
                ? IsNumber(text)
                : IsNumber(text[..dot]) && IsNumber(text[(dot + 1)..]);
        }

        /// <summary>Whether text is one to four ASCII digits.</summary>
        /// <param name="text">The text.</param>
        /// <returns>True when it is.</returns>
        private static bool IsNumber(string text)
        {
            return text.Length is > 0 and <= 4 && text.All(char.IsAsciiDigit);
        }
    }
}
