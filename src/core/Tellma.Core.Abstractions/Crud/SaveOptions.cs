// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Data;

namespace Tellma.Core.Abstractions.Crud
{
    /// <summary>How a save runs and what it answers.</summary>
    public sealed class SaveOptions
    {
        /// <summary>
        ///     Whether the save answers the saved rows in details shape. The read-back rides the persist
        ///     transaction; false skips it, and the result carries the ids alone. Default true.
        /// </summary>
        public bool ReturnEntities { get; set; } = true;

        /// <summary>The extras the read-back includes; none by default.</summary>
        /// <exception cref="ArgumentNullException">Set to null.</exception>
        public DetailsRequest Details
        {
            get;
            set => field = value ?? throw new ArgumentNullException(nameof(Details));
        } = DetailsRequest.None;

        /// <summary>
        ///     Whether an update's stamp is compared. Under <see cref="ConcurrencyMode.Check" />, the
        ///     default, an update carrying no stamp is refused; <see cref="ConcurrencyMode.Override" />
        ///     skips the comparison, never the existence check.
        /// </summary>
        public ConcurrencyMode Concurrency { get; set; } = ConcurrencyMode.Check;

        /// <summary>Who asked for the save; recorded, never a rule input. Default <see cref="SaveSource.Web" />.</summary>
        public SaveSource Source { get; set; } = SaveSource.Web;

        /// <summary>
        ///     The caller's own statements on the persist batch, invoked once after the effects and
        ///     before the access guards, under the caller's authority. Everything it appends commits or
        ///     rolls back with the save, and a validation failure never reaches it. It may write tables
        ///     no stack owns and append through the platform composers; a statement writing a
        ///     stack-owned table is refused when appended. Never set from the wire.
        /// </summary>
        public Action<IDataBatch>? OnPersist { get; set; }
    }
}
