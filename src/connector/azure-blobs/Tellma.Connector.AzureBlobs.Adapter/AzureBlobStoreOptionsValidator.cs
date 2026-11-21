// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.Options;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Tellma.Connector.AzureBlobs
{
    /// <summary>
    ///     Validates the Azure blob store's settings: exactly one way to reach the account, a
    ///     container prefix that yields a valid container name for every tenant id, and positive
    ///     limits.
    /// </summary>
    internal sealed partial class AzureBlobStoreOptionsValidator : IValidateOptions<AzureBlobStoreOptions>
    {
        /// <summary>
        ///     The container name of the largest tenant id: the longest suffix any prefix is joined
        ///     with, so a prefix valid with it is valid with every tenant id.
        /// </summary>
        internal static readonly string LongestSuffix = "t" + int.MaxValue.ToString(CultureInfo.InvariantCulture);

        /// <inheritdoc />
        public ValidateOptionsResult Validate(string? name, AzureBlobStoreOptions options)
        {
            ArgumentNullException.ThrowIfNull(options);
            IReadOnlyList<string> failures = Describe(options);
            return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
        }

        /// <summary>Every rule the settings break, each as a sentence naming the setting.</summary>
        /// <param name="options">The settings.</param>
        /// <returns>The failures; empty when the settings are valid.</returns>
        internal static IReadOnlyList<string> Describe(AzureBlobStoreOptions options)
        {
            ArgumentNullException.ThrowIfNull(options);
            List<string> failures = [];
            string section = AzureBlobStoreOptions.SectionName;

            // --- The account: by its endpoint and the host's identity, or by a connection string, never both ---
            bool hasUri = options.ServiceUri is not null;
            bool hasConnectionString = !string.IsNullOrWhiteSpace(options.ConnectionString);
            if (hasUri == hasConnectionString)
            {
                failures.Add(hasUri
                    ? $"{section}:{nameof(AzureBlobStoreOptions.ServiceUri)} and {section}:{nameof(AzureBlobStoreOptions.ConnectionString)} are both set; set exactly one."
                    : $"Neither {section}:{nameof(AzureBlobStoreOptions.ServiceUri)} nor {section}:{nameof(AzureBlobStoreOptions.ConnectionString)} is set; set exactly one.");
            }

            // A bearer token never travels over plain HTTP, so the endpoint is an absolute HTTPS one.
            if (options.ServiceUri is Uri uri && (!uri.IsAbsoluteUri || uri.Scheme != Uri.UriSchemeHttps))
            {
                failures.Add($"{section}:{nameof(AzureBlobStoreOptions.ServiceUri)} '{uri}' is not an absolute HTTPS address.");
            }

            // --- The container prefix: with the largest tenant id it is still a valid container name ---
            string prefix = options.ContainerPrefix ?? string.Empty;
            if (!IsValidContainerName(prefix + LongestSuffix))
            {
                failures.Add(
                    $"{section}:{nameof(AzureBlobStoreOptions.ContainerPrefix)} '{prefix}' does not yield a valid container name for every tenant: "
                    + $"'{prefix}{LongestSuffix}' must be 3 to 63 lowercase letters, digits and hyphens, start and end with a letter or digit, and hold no two hyphens in a row.");
            }

            // --- The limits ---
            if (options.MaxParallelism < 1)
            {
                failures.Add($"{section}:{nameof(AzureBlobStoreOptions.MaxParallelism)} is {options.MaxParallelism.ToString(CultureInfo.InvariantCulture)}; it must be at least 1.");
            }

            if (options.ReadBufferSize < 1)
            {
                failures.Add($"{section}:{nameof(AzureBlobStoreOptions.ReadBufferSize)} is {options.ReadBufferSize.ToString(CultureInfo.InvariantCulture)}; it must be at least 1 byte.");
            }

            return failures;
        }

        /// <summary>Whether a name is a valid Azure container name, which is also a DNS label.</summary>
        /// <param name="name">The name.</param>
        /// <returns>True when it is.</returns>
        internal static bool IsValidContainerName(string name)
        {
            ArgumentNullException.ThrowIfNull(name);
            return ContainerName().IsMatch(name) && !name.Contains("--", StringComparison.Ordinal);
        }

        /// <summary>The container name grammar, a DNS label of 3 to 63 characters.</summary>
        /// <returns>The expression.</returns>
        [GeneratedRegex("^[a-z0-9][a-z0-9-]{1,61}[a-z0-9]$", RegexOptions.CultureInvariant)]
        private static partial Regex ContainerName();
    }
}
