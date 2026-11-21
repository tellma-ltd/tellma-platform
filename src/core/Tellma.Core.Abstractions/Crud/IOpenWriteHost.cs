// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Crud
{
    /// <summary>
    ///     An open frame: the frame of an API action, a job's handler or a provisioning step, which
    ///     the work inside it persists itself. A scoped service the method, handler or step injects.
    /// </summary>
    /// <remarks>
    ///     Writes enlisted with an open frame are composed until <see cref="PersistAsync" />, which
    ///     runs any pending validation and then sends every enlisted group in one persist batch. A
    ///     job's frame appends its groups to the job's completion batch instead, so they commit with
    ///     the job's outcome.
    /// </remarks>
    public interface IOpenWriteHost : IWriteHost
    {
        /// <summary>Validates and persists every write enlisted with the frame. A frame persists once.</summary>
        /// <param name="cancellationToken">Cancels the validation and the persist together with the frame's own token, such as a job's lease.</param>
        /// <returns>The batch's stamp and the version tags after its bumps.</returns>
        /// <exception cref="InvalidOperationException">The frame already persisted.</exception>
        Task<EnlistmentOutcome> PersistAsync(CancellationToken cancellationToken = default);
    }
}
