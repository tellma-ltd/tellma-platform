// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.ComponentModel.DataAnnotations;

namespace Tellma.Core.AspNetCore.Hosting
{
    /// <summary>Support requests, bound from <c>Tellma:Support</c>.</summary>
    public sealed class SupportOptions
    {
        /// <summary>The configuration section the options bind from.</summary>
        public const string SectionPath = "Tellma:Support";

        /// <summary>Whether users may submit support requests; off by default.</summary>
        public bool Enabled { get; set; }

        /// <summary>How many days a support request is kept; 30 by default.</summary>
        [Range(1, 3650)]
        public int RetentionDays { get; set; } = 30;

        /// <summary>How many support requests one user may submit a day; 20 by default.</summary>
        [Range(1, 1000)]
        public int MaxRequestsPerUserPerDay { get; set; } = 20;
    }
}
