// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.ComponentModel.DataAnnotations;

namespace Tellma.Core.AspNetCore.Hosting
{
    /// <summary>The cookie session's lifetimes and the step-up bar, bound from <c>Tellma:Session</c>.</summary>
    public sealed class TellmaSessionOptions : IValidatableObject
    {
        /// <summary>The configuration section the options bind from.</summary>
        public const string SectionPath = "Tellma:Session";

        /// <summary>
        ///     The sliding idle lifetime of a session; 7 days by default, equal to the authority's
        ///     refresh-token idle window. Also the client's idle window for its warm cache.
        /// </summary>
        public TimeSpan IdleLifetime { get; set; } = TimeSpan.FromDays(7);

        /// <summary>The absolute lifetime of a session; 90 days by default.</summary>
        public TimeSpan AbsoluteLifetime { get; set; } = TimeSpan.FromDays(90);

        /// <summary>
        ///     How long an instance serves a session from its cache; 60 seconds by default. The longest
        ///     a revoked session keeps working on another instance.
        /// </summary>
        public TimeSpan CacheTtl { get; set; } = TimeSpan.FromMinutes(1);

        /// <summary>How long before the stored access token expires the session refreshes it; 60 seconds by default.</summary>
        public TimeSpan RefreshSkew { get; set; } = TimeSpan.FromMinutes(1);

        /// <summary>How long a session survives an unreachable authority; 1 hour by default.</summary>
        public TimeSpan MaxAuthorityOutage { get; set; } = TimeSpan.FromHours(1);

        /// <summary>How often expired session rows are swept; 15 minutes by default.</summary>
        public TimeSpan SweepInterval { get; set; } = TimeSpan.FromMinutes(15);

        /// <summary>The assurance bar sensitive operations require.</summary>
        public StepUpOptions StepUp { get; set; } = new();

        /// <summary>The idle lifetime in whole days, rounded up and at least one: what the information document publishes.</summary>
        public int IdleDays => Math.Max(1, (int)Math.Ceiling(IdleLifetime.TotalDays));

        /// <inheritdoc />
        public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
        {
            // --- Every duration is positive ---
            foreach ((string name, TimeSpan value) in new[]
            {
                (nameof(IdleLifetime), IdleLifetime),
                (nameof(AbsoluteLifetime), AbsoluteLifetime),
                (nameof(CacheTtl), CacheTtl),
                (nameof(RefreshSkew), RefreshSkew),
                (nameof(MaxAuthorityOutage), MaxAuthorityOutage),
                (nameof(SweepInterval), SweepInterval),
            })
            {
                if (value <= TimeSpan.Zero)
                {
                    yield return new ValidationResult($"{SectionPath}:{name} is {value}; it must be positive.", [name]);
                }
            }

            // --- A session never outlives its absolute lifetime by idling ---
            if (AbsoluteLifetime < IdleLifetime)
            {
                yield return new ValidationResult(
                    $"{SectionPath}:{nameof(AbsoluteLifetime)} ({AbsoluteLifetime}) is shorter than {nameof(IdleLifetime)} ({IdleLifetime}).",
                    [nameof(AbsoluteLifetime)]);
            }

            // --- The step-up bar ---
            if (StepUp is null)
            {
                yield return new ValidationResult($"{SectionPath}:{nameof(StepUp)} is missing.", [nameof(StepUp)]);
                yield break;
            }

            if (string.IsNullOrWhiteSpace(StepUp.Acr))
            {
                yield return new ValidationResult(
                    $"{SectionPath}:{nameof(StepUp)}:{nameof(StepUpOptions.Acr)} is empty; it names the authentication context class sensitive operations require.",
                    [nameof(StepUp)]);
            }

            if (StepUp.MaxAge is { } maxAge && maxAge <= TimeSpan.Zero)
            {
                yield return new ValidationResult(
                    $"{SectionPath}:{nameof(StepUp)}:{nameof(StepUpOptions.MaxAge)} is {maxAge}; it must be positive, or unset for no age limit.",
                    [nameof(StepUp)]);
            }
        }
    }
}
