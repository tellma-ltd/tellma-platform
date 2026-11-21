// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.ComponentModel.DataAnnotations;

namespace Tellma.Core.AspNetCore.Hosting
{
    /// <summary>The web host's tenancy timers, bound from <c>Tellma:Tenancy</c>.</summary>
    public sealed class TenancyHostOptions : IValidatableObject
    {
        /// <summary>The configuration section the options bind from.</summary>
        public const string SectionPath = "Tellma:Tenancy";

        /// <summary>How often the membership hints are rebuilt from every active tenant's users; a day by default.</summary>
        public TimeSpan MembershipReconcileInterval { get; set; } = TimeSpan.FromDays(1);

        /// <inheritdoc />
        public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
        {
            if (MembershipReconcileInterval <= TimeSpan.Zero)
            {
                yield return new ValidationResult(
                    $"{SectionPath}:{nameof(MembershipReconcileInterval)} is {MembershipReconcileInterval}; it must be positive.",
                    [nameof(MembershipReconcileInterval)]);
            }
        }
    }
}
