// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Text.Json;

namespace Tellma.Core.AspNetCore.Tests.Infrastructure
{
    /// <summary>A problem body, as a client reads it.</summary>
    /// <param name="Document">The body.</param>
    internal sealed record Problem(JsonElement Document)
    {
        /// <summary>The problem's code.</summary>
        public string? Code => Document.TryGetProperty("code", out JsonElement code) ? code.GetString() : null;

        /// <summary>The problem's instance: the trace id.</summary>
        public string? Instance => Document.TryGetProperty("instance", out JsonElement instance) ? instance.GetString() : null;

        /// <summary>The problem's type.</summary>
        public string? Type => Document.TryGetProperty("type", out JsonElement type) ? type.GetString() : null;

        /// <summary>An argument's value.</summary>
        /// <param name="name">The argument.</param>
        /// <returns>Its text, or null.</returns>
        public string? Argument(string name)
        {
            return Document.TryGetProperty("arguments", out JsonElement arguments) && arguments.TryGetProperty(name, out JsonElement value)
                ? value.ToString()
                : null;
        }

        /// <summary>Reads a problem body.</summary>
        /// <param name="response">The response.</param>
        /// <returns>The problem.</returns>
        public static async Task<Problem> ReadAsync(HttpResponseMessage response)
        {
            ArgumentNullException.ThrowIfNull(response);
            Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
            string body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
            using var document = JsonDocument.Parse(body);
            return new Problem(document.RootElement.Clone());
        }
    }
}
