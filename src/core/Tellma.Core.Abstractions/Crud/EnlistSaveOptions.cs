// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Collections.Frozen;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Validation;

namespace Tellma.Core.Abstractions.Crud
{
    /// <summary>How an enlisted save runs inside its host.</summary>
    public sealed class EnlistSaveOptions
    {
        /// <summary>Where the group sits in the host's persist batch; after the host by default.</summary>
        public EnlistOrder Order { get; set; } = EnlistOrder.AfterHost;

        /// <summary>
        ///     Maps a path on the target's payload onto the host's, applied to every error the target's
        ///     checks and validators raise; null maps everything to the root of the host's payload.
        /// </summary>
        public Func<ValidationPath, ValidationPath>? MapPath { get; set; }

        /// <summary>
        ///     Whether an update's stamp is compared: <see cref="ConcurrencyMode.Override" /> by default,
        ///     since an enlisted save is a platform command; <see cref="ConcurrencyMode.Check" /> for a
        ///     row the writer loaded itself and carries the stamp of.
        /// </summary>
        public ConcurrencyMode Concurrency { get; set; } = ConcurrencyMode.Override;

        /// <summary>
        ///     The server-owned members the writer supplies, such as a job entity's job id, re-applied
        ///     from the writer's rows after the platform resets server-owned members; never an audit
        ///     column or the active flag. Empty by default.
        /// </summary>
        /// <exception cref="ArgumentNullException">Set to null.</exception>
        public IReadOnlySet<string> ServerOwned
        {
            get;
            set => field = value ?? throw new ArgumentNullException(nameof(ServerOwned));
        } = FrozenSet<string>.Empty;

        /// <summary>Who the save is recorded as coming from; null for the host's source.</summary>
        public SaveSource? Source { get; set; }
    }
}
