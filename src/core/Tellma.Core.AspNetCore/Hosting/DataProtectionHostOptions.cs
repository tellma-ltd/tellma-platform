// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.AspNetCore.Hosting
{
    /// <summary>
    ///     Where the Data Protection key ring persists, bound from <c>Tellma:DataProtection</c>. The
    ///     host persists to <see cref="KeyRingPath" /> when it is set; a cloud bundle reads
    ///     <see cref="BlobUri" /> and <see cref="KeyId" />.
    /// </summary>
    public sealed class DataProtectionHostOptions
    {
        /// <summary>The configuration section the options bind from.</summary>
        public const string SectionPath = "Tellma:DataProtection";

        /// <summary>The blob that holds the key ring, for a cloud bundle.</summary>
        public string? BlobUri { get; set; }

        /// <summary>The key-vault key that wraps the key ring, for a cloud bundle.</summary>
        public string? KeyId { get; set; }

        /// <summary>A folder every instance shares, holding the key ring.</summary>
        public string? KeyRingPath { get; set; }
    }
}
