// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Validation;

namespace Tellma.Core.Abstractions.Crud
{
    /// <summary>How an enlisted delete runs inside its host. It carries no expected stamps.</summary>
    public sealed class EnlistDeleteOptions
    {
        /// <summary>Where the group sits in the host's persist batch; ahead of the host by default.</summary>
        public EnlistOrder Order { get; set; } = EnlistOrder.BeforeHost;

        /// <summary>
        ///     Maps a path on the target's payload onto the host's, applied to every error the target's
        ///     checks and validators raise; null maps everything to the root of the host's payload.
        /// </summary>
        public Func<ValidationPath, ValidationPath>? MapPath { get; set; }

        /// <summary>Who the delete is recorded as coming from; null for the host's source.</summary>
        public SaveSource? Source { get; set; }
    }
}
