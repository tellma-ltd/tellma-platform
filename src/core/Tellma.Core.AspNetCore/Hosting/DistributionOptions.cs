// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.ComponentModel.DataAnnotations;

namespace Tellma.Core.AspNetCore.Hosting
{
    /// <summary>
    ///     What the distribution calls itself, bound from the <c>Tellma</c> section's top-level
    ///     <c>DisplayName</c> and <c>Copyright</c>.
    /// </summary>
    public sealed class DistributionOptions
    {
        /// <summary>The configuration section the options bind from.</summary>
        public const string SectionPath = "Tellma";

        /// <summary>
        ///     The distribution's name in the anonymous information document: one value, since that
        ///     document has no language.
        /// </summary>
        [Required(AllowEmptyStrings = false)]
        public string? DisplayName { get; set; }

        /// <summary>The About dialog's copyright line; null omits it.</summary>
        public string? Copyright { get; set; }
    }
}
