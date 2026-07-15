// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Text.Json;

namespace Tellma.Identity.Services.Provisioning
{
    /// <summary>
    ///     Tellma-defined keys stored in an OpenIddict application's properties bag, plus helpers
    ///     for reading/writing them (the bag holds raw JSON elements).
    /// </summary>
    public static class TellmaClientProperties
    {
        /// <summary>The browser origin of a distribution BFF; drives audience derivation.</summary>
        public const string Origin = "tellma:origin";

        /// <summary>The client's OIDC back-channel logout endpoint.</summary>
        public const string BackchannelLogoutUri = "tellma:backchannel_logout_uri";

        /// <summary>Marks first-party clients (consent is implicit for them).</summary>
        public const string FirstParty = "tellma:first_party";

        /// <summary>Marks seeded platform clients (CLI, native apps, control plane).</summary>
        public const string Platform = "tellma:platform";

        /// <summary>
        ///     Marks clients allowed to name per-distribution API audiences via the <c>resource</c>
        ///     parameter (the CLI and native apps). Set at seed time so provisioning grants a new
        ///     distribution's audience only to these clients, never to the control plane, whose sole
        ///     audience is the control-plane surface.
        /// </summary>
        public const string CallsDistributionApis = "tellma:calls_distribution_apis";

        /// <summary>Marks runtime-provisioned service accounts.</summary>
        public const string ServiceAccount = "tellma:service_account";

        /// <summary>When the client registration was created (ISO 8601).</summary>
        public const string CreatedUtc = "tellma:created_utc";

        /// <summary>Writes a string property value.</summary>
        /// <param name="properties">The descriptor's properties bag.</param>
        /// <param name="key">The property key.</param>
        /// <param name="value">The value; nothing is written when null.</param>
        public static void Set(IDictionary<string, JsonElement> properties, string key, string? value)
        {
            ArgumentNullException.ThrowIfNull(properties);

            if (value is not null)
            {
                properties[key] = JsonSerializer.SerializeToElement(value);
            }
        }

        /// <summary>Reads a string property value.</summary>
        /// <param name="properties">The application's properties bag.</param>
        /// <param name="key">The property key.</param>
        /// <returns>The value, or null when absent or not a string.</returns>
        public static string? Get(IReadOnlyDictionary<string, JsonElement> properties, string key)
        {
            ArgumentNullException.ThrowIfNull(properties);

            return properties.TryGetValue(key, out JsonElement value) && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;
        }

        /// <summary>Checks a boolean-ish marker property (stored as the string "true").</summary>
        /// <param name="properties">The application's properties bag.</param>
        /// <param name="key">The property key.</param>
        /// <returns>Whether the marker is present and set.</returns>
        public static bool IsSet(IReadOnlyDictionary<string, JsonElement> properties, string key)
        {
            return string.Equals(Get(properties, key), "true", StringComparison.OrdinalIgnoreCase);
        }
    }
}
