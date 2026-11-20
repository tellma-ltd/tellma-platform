// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.AspNetCore.Http;
using Serilog;
using System.Collections;
using System.Globalization;

namespace Tellma.Core.AspNetCore.Api
{
    /// <summary>
    ///     The logger scope the telemetry filter adds to a web API request's: <c>Client</c>,
    ///     <c>Resource</c> and <c>Operation</c>. It is kept on the request, so a line written after
    ///     the filter's scope closed (a problem's, the request's completion) carries the same members.
    /// </summary>
    /// <param name="client">The calling client's name.</param>
    /// <param name="resource">The stack's or route's resource; null for an endpoint the route model does not describe.</param>
    /// <param name="operation">The standard segment or action name; null for an endpoint the route model does not describe.</param>
    internal sealed class ApiLogScope(string client, string? resource, string? operation) : IReadOnlyList<KeyValuePair<string, object?>>
    {
        /// <summary>The scope's member names, in order.</summary>
        private static readonly string[] Names = ["Client", "Resource", "Operation"];

        /// <summary>The request item that holds the request's scope.</summary>
        private static readonly object ItemKey = new();

        /// <summary>The calling client's name.</summary>
        public string Client { get; } = client ?? throw new ArgumentNullException(nameof(client));

        /// <summary>The stack's or route's resource, or null.</summary>
        public string? Resource { get; } = resource;

        /// <summary>The standard segment or action name, or null.</summary>
        public string? Operation { get; } = operation;

        /// <inheritdoc />
        public int Count => Names.Length;

        /// <inheritdoc />
        public KeyValuePair<string, object?> this[int index] => new(
            Names[index],
            index switch
            {
                0 => Client,
                1 => Resource,
                2 => Operation,
                _ => throw new ArgumentOutOfRangeException(nameof(index), index, "The scope has three members."),
            });

        /// <summary>Keeps a request's scope on the request.</summary>
        /// <param name="http">The request.</param>
        /// <param name="scope">The scope.</param>
        public static void Keep(HttpContext http, ApiLogScope scope)
        {
            ArgumentNullException.ThrowIfNull(http);
            http.Items[ItemKey] = scope;
        }

        /// <summary>The scope a request's telemetry filter opened.</summary>
        /// <param name="http">The request.</param>
        /// <returns>The scope, or null when the request never reached the filter.</returns>
        public static ApiLogScope? Of(HttpContext http)
        {
            ArgumentNullException.ThrowIfNull(http);
            return http.Items.TryGetValue(ItemKey, out object? scope) ? scope as ApiLogScope : null;
        }

        /// <summary>Puts a request's members on the request-completion event, which is written after the scope has closed.</summary>
        /// <param name="diagnostics">The request's diagnostic context.</param>
        /// <param name="http">The request.</param>
        public static void Enrich(IDiagnosticContext diagnostics, HttpContext http)
        {
            ArgumentNullException.ThrowIfNull(diagnostics);
            if (Of(http) is not ApiLogScope scope)
            {
                return;
            }

            foreach (KeyValuePair<string, object?> member in scope)
            {
                diagnostics.Set(member.Key, member.Value);
            }
        }

        /// <inheritdoc />
        public IEnumerator<KeyValuePair<string, object?>> GetEnumerator()
        {
            for (int i = 0; i < Names.Length; i++)
            {
                yield return this[i];
            }
        }

        /// <inheritdoc />
        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        /// <inheritdoc />
        public override string ToString()
        {
            // What a text sink prints for the scope.
            return string.Create(CultureInfo.InvariantCulture, $"Client:{Client} Resource:{Resource} Operation:{Operation}");
        }
    }
}
