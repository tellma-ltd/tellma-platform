// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Crud;

namespace Tellma.Core.AspNetCore.Api
{
    /// <summary>How a projected endpoint turns a request body into its service call's inputs.</summary>
    internal static class WireBinding
    {
        /// <summary>The details a request's <c>include</c> names.</summary>
        /// <param name="include">The extras by name; null for none.</param>
        /// <returns>The details request.</returns>
        public static DetailsRequest Details(IReadOnlyList<string>? include)
        {
            return include is { Count: > 0 } ? new DetailsRequest { Include = include } : DetailsRequest.None;
        }
    }
}
