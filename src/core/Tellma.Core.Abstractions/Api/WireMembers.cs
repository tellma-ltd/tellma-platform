// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Text.Json;

namespace Tellma.Core.Abstractions.Api
{
    /// <summary>
    ///     Refuses a request record's required member that a body omitted or sent as null. The
    ///     refusal is a <see cref="JsonException" />: the serializer reports it as the body's binding
    ///     error, with the member's path, so the caller gets a bad request rather than a failure deep
    ///     in the service.
    /// </summary>
    internal static class WireMembers
    {
        /// <summary>Returns a required member's value, or refuses a missing one.</summary>
        /// <typeparam name="T">The member's type.</typeparam>
        /// <param name="value">The value the body bound.</param>
        /// <param name="member">The member's wire name, such as <c>arguments</c>.</param>
        /// <returns>The value.</returns>
        /// <exception cref="JsonException">The value is null.</exception>
        public static T Required<T>(T value, string member)
        {
            return value is null
                ? throw new JsonException($"The request's '{member}' member is required and cannot be null.")
                : value;
        }
    }
}
