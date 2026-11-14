// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Text.Json;

namespace Tellma.Identity.Services.AuthenticationPolicy
{
    /// <summary>
    ///     The resources a permanent authorization covers, recorded on it as a property: every
    ///     audience the user has granted the client through it. An interactive grant that names a
    ///     resource the authorization does not hold yet adds it, after the user was asked when the
    ///     client's consent is explicit. Remembered consent reaches no further than these, so a
    ///     grant for one place never stands in for another: a distribution, or one tenant of it.
    /// </summary>
    public static class AuthorizationResources
    {
        /// <summary>The authorization property holding the covered resources, as a JSON array of strings.</summary>
        public const string Property = "tellma:resources";

        /// <summary>Reads the resources an authorization covers.</summary>
        /// <param name="properties">The authorization's properties bag.</param>
        /// <returns>The covered resources; empty when none were recorded or the value is malformed.</returns>
        public static HashSet<string> Read(IReadOnlyDictionary<string, JsonElement> properties)
        {
            ArgumentNullException.ThrowIfNull(properties);

            HashSet<string> resources = new(StringComparer.Ordinal);
            if (!properties.TryGetValue(Property, out JsonElement value) || value.ValueKind != JsonValueKind.Array)
            {
                return resources;
            }

            // A malformed entry covers nothing: it is skipped rather than trusted.
            foreach (JsonElement item in value.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String && item.GetString() is { Length: > 0 } resource)
                {
                    resources.Add(resource);
                }
            }

            return resources;
        }

        /// <summary>Whether an authorization covers every requested resource, compared ordinally.</summary>
        /// <param name="properties">The authorization's properties bag.</param>
        /// <param name="resources">The resources a grant asks for; none are always covered.</param>
        /// <returns>True when each requested resource is among the covered ones.</returns>
        public static bool Covers(IReadOnlyDictionary<string, JsonElement> properties, IEnumerable<string> resources)
        {
            ArgumentNullException.ThrowIfNull(resources);

            HashSet<string> covered = Read(properties);
            return resources.All(covered.Contains);
        }

        /// <summary>Records the resources an authorization covers, in ordinal order so equal sets store equal values.</summary>
        /// <param name="properties">The authorization descriptor's properties bag.</param>
        /// <param name="resources">The covered resources.</param>
        public static void Write(IDictionary<string, JsonElement> properties, IEnumerable<string> resources)
        {
            ArgumentNullException.ThrowIfNull(properties);
            ArgumentNullException.ThrowIfNull(resources);

            string[] ordered = [.. resources.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];
            properties[Property] = JsonSerializer.SerializeToElement(ordered);
        }
    }
}
