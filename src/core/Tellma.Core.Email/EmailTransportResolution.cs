// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.Hosting;
using Tellma.Core.Abstractions.Email;

namespace Tellma.Core.Email
{
    /// <summary>The outcome of resolving the configured provider against the registered transports.</summary>
    /// <param name="Active">The transport that will send mail, or null when resolution failed.</param>
    /// <param name="Failures">Every problem found, in one list.</param>
    internal sealed record EmailTransportResolutionResult(
        EmailTransportRegistration? Active, IReadOnlyList<string> Failures);

    /// <summary>
    ///     The single implementation of the provider-selection rules, so the options validator and
    ///     the runtime selector can never disagree about which transport is active or why.
    /// </summary>
    internal static class EmailTransportResolution
    {
        /// <summary>The configuration name of the Development log sink.</summary>
        internal const string LogSinkTransportName = "log-sink";

        /// <summary>The full configuration key of the provider setting, used in every failure message.</summary>
        internal const string ProviderConfigurationKey = $"{EmailOptions.SectionName}:{nameof(EmailOptions.Provider)}";

        /// <summary>Applies the selection rules and collects every violation.</summary>
        /// <param name="options">The bound pipeline options.</param>
        /// <param name="environment">The host environment, which decides the Development defaults.</param>
        /// <param name="registrations">Every transport the composition declared.</param>
        /// <returns>The active transport when resolvable, plus every failure found.</returns>
        internal static EmailTransportResolutionResult Resolve(
            EmailOptions options,
            IHostEnvironment environment,
            IReadOnlyList<EmailTransportRegistration> registrations)
        {
            List<string> failures = [];

            // Registration hygiene first: a duplicate or malformed name makes any provider lookup
            // ambiguous, so report it even when the provider itself resolves.
            HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
            foreach (EmailTransportRegistration registration in registrations)
            {
                if (registration is null)
                {
                    failures.Add("A null email transport registration was found in the composition.");
                    continue;
                }

                if (!IsLowercaseKebabCase(registration.Name))
                {
                    failures.Add(
                        $"The email transport name '{registration.Name}' is not lowercase kebab-case (letters, digits, and hyphens only).");
                    continue;
                }

                if (registration.Live is null)
                {
                    failures.Add($"The email transport '{registration.Name}' declares no live sender factory.");
                }

                if (!seen.Add(registration.Name))
                {
                    failures.Add($"More than one email transport is registered under the name '{registration.Name}'.");
                }
            }

            bool isDevelopment = environment.IsDevelopment();
            string? provider = string.IsNullOrWhiteSpace(options.Provider) ? null : options.Provider.Trim();

            if (provider is null)
            {
                if (!isDevelopment)
                {
                    failures.Add(
                        $"{ProviderConfigurationKey} is required outside the Development environment. Set it to one of the registered transports: {DescribeRegistered(registrations)}.");
                    return new EmailTransportResolutionResult(null, failures);
                }

                // A fresh clone sends mail with no configuration at all.
                provider = LogSinkTransportName;
            }

            // The log sink is the one transport whose activation is environment-gated: it discards
            // mail, which is correct in Development and catastrophic anywhere else.
            if (string.Equals(provider, LogSinkTransportName, StringComparison.OrdinalIgnoreCase) && !isDevelopment)
            {
                failures.Add(
                    $"{ProviderConfigurationKey} is '{LogSinkTransportName}', which discards mail and is only permitted in the Development environment (this host runs in '{environment.EnvironmentName}'). Configure {ProviderConfigurationKey} to a real transport.");
            }

            EmailTransportRegistration? active = registrations.FirstOrDefault(
                r => r is not null && string.Equals(r.Name, provider, StringComparison.OrdinalIgnoreCase));

            if (active is null)
            {
                // Listing the registered names turns a typo ("sendgird") into a one-glance fix.
                failures.Add(
                    $"{ProviderConfigurationKey} is '{provider}', which matches no registered email transport. Registered transports: {DescribeRegistered(registrations)}.");
            }

            return new EmailTransportResolutionResult(failures.Count == 0 ? active : null, failures);
        }

        private static bool IsLowercaseKebabCase(string? value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return false;
            }

            foreach (char c in value)
            {
                if (c is not ((>= 'a' and <= 'z') or (>= '0' and <= '9') or '-'))
                {
                    return false;
                }
            }

            return true;
        }

        private static string DescribeRegistered(IReadOnlyList<EmailTransportRegistration> registrations)
        {
            IEnumerable<string> names = registrations
                .Where(static r => r is not null && !string.IsNullOrWhiteSpace(r.Name))
                .Select(static r => r.Name)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Order(StringComparer.Ordinal);

            string joined = string.Join(", ", names);
            return string.IsNullOrEmpty(joined) ? "(none)" : joined;
        }
    }
}
