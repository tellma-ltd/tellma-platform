// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Validation;

namespace Tellma.Core.Abstractions.Crud
{
    /// <summary>
    ///     A frame the platform opened around a piece of work that writes: the pipeline around a save,
    ///     a delete or an action, an API action, a job's handler or a provisioning step. A write into
    ///     another stack's table enlists with the current frame, so it runs through that stack's own
    ///     pipeline and commits in the frame's one transaction.
    /// </summary>
    /// <remarks>
    ///     Frames nest: an operation entered inside an open frame runs its own pipeline frame, and the
    ///     innermost frame is the host its participants see.
    /// </remarks>
    public interface IWriteHost
    {
        /// <summary>The kind of frame.</summary>
        WriteHostKind Kind { get; }

        /// <summary>
        ///     The work the frame runs, for logs and errors: <c>&lt;Resource&gt;:&lt;operation&gt;</c>
        ///     for a pipeline or API action (<c>gl.Invoice:post</c>), <c>job:&lt;key&gt;</c> for a job
        ///     and <c>provisioning:&lt;step&gt;</c> for a provisioning step.
        /// </summary>
        string Operation { get; }

        /// <summary>The scope's confirmations; a nested frame shares its host's.</summary>
        IConfirmations Confirmations { get; }
    }
}
