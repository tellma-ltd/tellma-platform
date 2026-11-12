// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Provisioning
{
    /// <summary>
    ///     Opens the write frame a provisioning step persists through, in the step's own tenant scope,
    ///     before the step is created there. The step runner calls it when one is registered, once per
    ///     step; without one a step runs with no frame of its own.
    /// </summary>
    internal interface IProvisioningFrameOpener
    {
        /// <summary>Opens the frame of one step in its scope.</summary>
        /// <param name="scopedServices">The services of the step's tenant scope, where the frame is registered.</param>
        /// <param name="stepName">The step's name, which names the frame's operation.</param>
        void Open(IServiceProvider scopedServices, string stepName);
    }
}
