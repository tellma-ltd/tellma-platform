// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Provisioning
{
    /// <summary>The rules a provisioning step's name follows, which the tenant database's step record imposes.</summary>
    internal static class ProvisioningStepNames
    {
        /// <summary>The longest name the step record's key column holds.</summary>
        public const int MaxLength = 128;

        /// <summary>Whether a name is 1 to 128 characters, with no control character and no surrounding white space.</summary>
        /// <param name="name">The candidate name.</param>
        /// <returns>True when the step record can hold it as written.</returns>
        public static bool IsValid(string? name)
        {
            return !string.IsNullOrEmpty(name)
                && name.Length <= MaxLength
                && !char.IsWhiteSpace(name[0])
                && !char.IsWhiteSpace(name[^1])
                && !name.Any(char.IsControl);
        }
    }
}
