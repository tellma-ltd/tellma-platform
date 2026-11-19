// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Provisioning
{
    /// <summary>
    ///     Runs a provisioning step inside the write frame it persists through, in the step's own
    ///     tenant scope: the frame opens before the step is created there and closes once the step
    ///     completed. The step runner calls it when one is registered, once per step; without one a
    ///     step runs with no frame of its own.
    /// </summary>
    internal interface IProvisioningFrameOpener
    {
        /// <summary>Runs one step inside its frame.</summary>
        /// <param name="scopedServices">The services of the step's tenant scope, where the frame is registered.</param>
        /// <param name="stepName">The step's name, which names the frame's operation.</param>
        /// <param name="stepType">The step's class, which may enlist writes with the frame.</param>
        /// <param name="step">Creates the step in the scope and runs it.</param>
        /// <param name="cancellationToken">Cancels the frame's rounds.</param>
        /// <returns>A task that completes when the step completed and the frame closed.</returns>
        Task RunAsync(IServiceProvider scopedServices, string stepName, Type stepType, Func<Task> step, CancellationToken cancellationToken);
    }
}
