// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.ComponentModel.DataAnnotations;
using System.Globalization;
using Tellma.Core.Mcp.Hosting;

namespace Tellma.Core.Mcp
{
    /// <summary>
    ///     The tenant MCP server's options, bound from <c>Tellma:Mcp</c> and validated before the host
    ///     serves traffic. Every numeric limit is per instance; no limiter shares state across
    ///     instances.
    /// </summary>
    public sealed class McpOptions : IValidatableObject
    {
        /// <summary>The configuration section the options bind from.</summary>
        public const string SectionPath = "Tellma:Mcp";

        /// <summary>The smallest character cap a tool result may be given: below it no table or entity header fits.</summary>
        public const int MinToolResultChars = 1000;

        /// <summary>The longest a confirmation may live.</summary>
        public static readonly TimeSpan MaxConfirmationLifetime = TimeSpan.FromHours(1);

        /// <summary>
        ///     The characters one text block of a tool result holds at most: a query truncates row by
        ///     row and a get entity by entity under it; 60,000 by default.
        /// </summary>
        public int MaxToolResultChars { get; set; } = 60000;

        /// <summary>The rows a query or an aggregate returns when the call names no <c>top</c>; 50 by default.</summary>
        public int DefaultTop { get; set; } = 50;

        /// <summary>The most rows a query or an aggregate returns in one call; 500 by default.</summary>
        public int MaxTop { get; set; } = 500;

        /// <summary>The rows of each child collection a get returns when the call names no <c>top</c>; 50 by default.</summary>
        public int DefaultChildTop { get; set; } = 50;

        /// <summary>The most rows of a child collection a get returns in one call; 500 by default.</summary>
        public int MaxChildTop { get; set; } = 500;

        /// <summary>The most ids one call names; 100 by default.</summary>
        public int MaxIdsPerCall { get; set; } = 100;

        /// <summary>
        ///     The language of the error titles, details and validation messages a tool returns, with
        ///     Gregorian dates; <c>en</c> by default. Null or empty leaves both to the user's own
        ///     preferences. Display names always follow the user's content language.
        /// </summary>
        public string? MessageLanguage { get; set; } = "en";

        /// <summary>The tool calls one user makes in one tenant per minute, on each instance; 120 by default.</summary>
        public int ToolCallsPerMinutePerUser { get; set; } = 120;

        /// <summary>How long one tool call may run; 60 seconds by default.</summary>
        public TimeSpan ToolTimeout { get; set; } = TimeSpan.FromSeconds(60);

        /// <summary>How long a confirmation token a write tool issues stays valid; five minutes by default.</summary>
        public TimeSpan ConfirmationLifetime { get; set; } = TimeSpan.FromMinutes(5);

        /// <summary>How long, in milliseconds, a client may keep the tool list before listing it again; five minutes by default.</summary>
        public int ListTtlMs { get; set; } = 300000;

        /// <summary>
        ///     The browser origins a request may come from: <c>https://claude.ai</c> and
        ///     <c>https://chatgpt.com</c> by default. A native client sends no <c>Origin</c> and
        ///     passes; any other origin is refused.
        /// </summary>
        public IReadOnlySet<string> AllowedOrigins { get; set; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "https://claude.ai",
            "https://chatgpt.com",
        };

        /// <summary>
        ///     The distribution's own tool types, written with the SDK's attributes. They run under the
        ///     same bearer, request filter and rate limit as the platform's tools, and their names must
        ///     not begin with <c>tellma_</c>. Set in code; never bound from configuration.
        /// </summary>
        public IList<Type> ToolTypes { get; set; } = [];

        /// <summary>The message language a tool call runs under: <see cref="MessageLanguage" />, or null when it is empty.</summary>
        internal string? EffectiveMessageLanguage => string.IsNullOrWhiteSpace(MessageLanguage) ? null : MessageLanguage;

        /// <inheritdoc />
        public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
        {
            // --- The result cap: room for a table's header and an entity's header ---
            if (MaxToolResultChars < MinToolResultChars)
            {
                yield return Invalid(nameof(MaxToolResultChars), MaxToolResultChars, $"it must be at least {MinToolResultChars.ToString(CultureInfo.InvariantCulture)}");
            }

            // --- The page sizes: positive, each default within its ceiling ---
            (string Name, int Value)[] counts =
            [
                (nameof(DefaultTop), DefaultTop),
                (nameof(MaxTop), MaxTop),
                (nameof(DefaultChildTop), DefaultChildTop),
                (nameof(MaxChildTop), MaxChildTop),
                (nameof(MaxIdsPerCall), MaxIdsPerCall),
                (nameof(ToolCallsPerMinutePerUser), ToolCallsPerMinutePerUser),
                (nameof(ListTtlMs), ListTtlMs),
            ];
            foreach ((string name, int value) in counts)
            {
                if (value <= 0)
                {
                    yield return Invalid(name, value, "it must be positive");
                }
            }

            if (DefaultTop > MaxTop)
            {
                yield return Invalid(nameof(DefaultTop), DefaultTop, $"it must be at most {nameof(MaxTop)} ({MaxTop.ToString(CultureInfo.InvariantCulture)})");
            }

            if (DefaultChildTop > MaxChildTop)
            {
                yield return Invalid(nameof(DefaultChildTop), DefaultChildTop, $"it must be at most {nameof(MaxChildTop)} ({MaxChildTop.ToString(CultureInfo.InvariantCulture)})");
            }

            // --- The durations: positive and bounded ---
            if (ToolTimeout <= TimeSpan.Zero || ToolTimeout > AspNetCore.Hosting.McpLimitOptions.MaxToolTimeout)
            {
                yield return Invalid(nameof(ToolTimeout), ToolTimeout, $"it must be positive and at most {AspNetCore.Hosting.McpLimitOptions.MaxToolTimeout.ToString("c", CultureInfo.InvariantCulture)}");
            }

            if (ConfirmationLifetime <= TimeSpan.Zero || ConfirmationLifetime > MaxConfirmationLifetime)
            {
                yield return Invalid(nameof(ConfirmationLifetime), ConfirmationLifetime, $"it must be positive and at most {MaxConfirmationLifetime.ToString("c", CultureInfo.InvariantCulture)}");
            }

            // --- The message language: a culture name, or none ---
            if (!string.IsNullOrWhiteSpace(MessageLanguage) && !IsCultureName(MessageLanguage))
            {
                yield return Invalid(nameof(MessageLanguage), MessageLanguage, "it must be a language code such as en, or empty for the user's own language");
            }

            // --- The origins: each an absolute https origin, with no path ---
            foreach (string origin in AllowedOrigins ?? new HashSet<string>())
            {
                if (!McpOrigins.IsOrigin(origin))
                {
                    yield return Invalid(nameof(AllowedOrigins), origin, "each entry must be a scheme, a host and an optional port, such as https://claude.ai");
                }
            }

            if (AllowedOrigins is null)
            {
                yield return new ValidationResult($"{SectionPath}:{nameof(AllowedOrigins)} is null; it must be a set, empty to admit native clients alone.", [nameof(AllowedOrigins)]);
            }
        }

        /// <summary>A validation failure naming the setting and its value.</summary>
        /// <param name="name">The setting.</param>
        /// <param name="value">Its value.</param>
        /// <param name="rule">What it must be.</param>
        /// <returns>The failure.</returns>
        private static ValidationResult Invalid(string name, object value, string rule)
        {
            return new ValidationResult(string.Create(CultureInfo.InvariantCulture, $"{SectionPath}:{name} is {value}; {rule}."), [name]);
        }

        /// <summary>Whether a value names a culture .NET knows.</summary>
        /// <param name="value">The value.</param>
        /// <returns>True for a known, non-invariant culture name.</returns>
        private static bool IsCultureName(string value)
        {
            try
            {
                return CultureInfo.GetCultureInfo(value, predefinedOnly: true).Name.Length > 0;
            }
            catch (CultureNotFoundException)
            {
                return false;
            }
        }
    }
}
