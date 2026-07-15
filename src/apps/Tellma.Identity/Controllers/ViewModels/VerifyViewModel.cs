// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Identity.Controllers.ViewModels
{
    /// <summary>The device end-user verification page's display data.</summary>
    public sealed class VerifyViewModel
    {
        /// <summary>The resolved user code, when the user arrived via the complete verification URI.</summary>
        public string? UserCode { get; set; }

        /// <summary>The requesting device's client display name, shown so the user can recognize it.</summary>
        public string? ApplicationName { get; set; }

        /// <summary>The scopes the device is requesting, space-delimited.</summary>
        public string? Scope { get; set; }

        /// <summary>A protocol error code when the user code could not be resolved.</summary>
        public string? Error { get; set; }

        /// <summary>A human-readable error description.</summary>
        public string? ErrorDescription { get; set; }
    }
}
