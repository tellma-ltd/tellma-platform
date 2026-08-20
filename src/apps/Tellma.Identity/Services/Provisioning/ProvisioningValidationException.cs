// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Identity.Services.Provisioning
{
    /// <summary>
    ///     A caller-correctable provisioning error (for example a service account requesting an
    ///     audience it does not own). Surfaced by the management API as a 400, distinct from an
    ///     integrity failure.
    /// </summary>
    /// <param name="message">The validation message.</param>
    public sealed class ProvisioningValidationException(string message) : Exception(message)
    {
    }
}
