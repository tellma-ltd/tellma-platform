// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.ComponentModel.DataAnnotations;

namespace Tellma.Core.Abstractions.Sharing
{
    /// <summary>
    ///     The limits of sharing, bound from <c>Tellma:Sharing</c> and validated by the startup gate. A
    ///     distribution changes a limit in configuration, never in code.
    /// </summary>
    public sealed class SharingOptions
    {
        /// <summary>The configuration section the options bind from.</summary>
        public const string SectionPath = "Tellma:Sharing";

        /// <summary>The most members an audience read lists one by one; 2,000 by default, from 1 to 100,000.</summary>
        [Range(1, 100_000)]
        public int MaxAudienceMembers { get; set; } = 2000;
    }
}
