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
    ///     Validates the Azure blob store's settings: exactly one way to reach the account, an
    ///     endpoint that is the account's HTTPS address alone, a container prefix that yields a valid container name for every tenant id, and positive
    ///     limits.
    /// </summary>
    internal sealed partial class AzureBlobStoreOptionsValidator : IValidateOptions<AzureBlobStoreOptions>
    {
        /// <summary>
        ///     The container name of the largest tenant id: the longest suffix any prefix is joined
        ///     with, so a prefix valid with it is valid with every tenant id.
        /// </summary>
        internal static readonly string LongestSuffix = "t" + int.MaxValue.ToString(CultureInfo.InvariantCulture);

        /// <summary>
        ///     The container name of the first tenant id: the shortest suffix any prefix is joined
        ///     with, so a prefix valid with it and with <see cref="LongestSuffix" /> is long enough
        ///     for every tenant id.
        /// </summary>
        internal static readonly string ShortestSuffix = "t1";

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

            // --- The endpoint: the account's own, over HTTPS, and nothing else ---
            // A bearer token never travels over plain HTTP. A path would put every tenant's container
            // under one container, and a query is most likely a pasted shared access signature, so
            // neither is accepted; no message prints more of the address than its scheme and host.
            string uriSetting = $"{section}:{nameof(AzureBlobStoreOptions.ServiceUri)}";
            if (options.ServiceUri is Uri uri)
            {
                if (!uri.IsAbsoluteUri || uri.Scheme != Uri.UriSchemeHttps)
                {
                    failures.Add($"{uriSetting} is not an absolute HTTPS address.");
                }
                else if (uri.AbsolutePath != "/" || uri.Query.Length > 0 || uri.Fragment.Length > 0 || uri.UserInfo.Length > 0)
                {
                    failures.Add(
                        $"{uriSetting} for '{Shown(uri)}' carries a path, a query, a fragment or user information; "
                        + "set the account's blob endpoint alone, such as 'https://<account>.blob.core.windows.net'. "
                        + "The store signs in with the host's identity, so no shared access signature is configured.");
                }
            }

            // --- The container prefix: with the largest tenant id, and with the smallest, it is still a valid container name ---
            // The longest name finds a prefix too long or of the wrong characters; the shortest
            // finds one too short for the service's three-character minimum (an empty prefix).
            string prefix = options.ContainerPrefix ?? string.Empty;
            string? invalid = !IsValidContainerName(prefix + LongestSuffix) ? prefix + LongestSuffix
                : !IsValidContainerName(prefix + ShortestSuffix) ? prefix + ShortestSuffix
                : null;
            if (invalid is not null)
            {
                failures.Add(
                    $"{section}:{nameof(AzureBlobStoreOptions.ContainerPrefix)} '{prefix}' does not yield a valid container name for every tenant: "
                    + $"'{invalid}' must be 3 to 63 lowercase letters, digits and hyphens, start and end with a letter or digit, and hold no two hyphens in a row.");
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

        /// <summary>An absolute address's scheme, host and any explicit port: never its user information, path or query.</summary>
        /// <param name="uri">The absolute address.</param>
        /// <returns>The part of it a message may print.</returns>
        private static string Shown(Uri uri)
        {
            return uri.IsDefaultPort
                ? $"{uri.Scheme}://{uri.Host}"
                : string.Create(CultureInfo.InvariantCulture, $"{uri.Scheme}://{uri.Host}:{uri.Port}");
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
