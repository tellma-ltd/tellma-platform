// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Identity.Controllers.ViewModels
{
    /// <summary>The consent form's display data (third-party clients only).</summary>
    public sealed class ConsentViewModel
    {
        /// <summary>The requesting application's display name.</summary>
        public string? ApplicationName { get; set; }

        /// <summary>The requested scopes, space-delimited.</summary>
        public string Scope { get; set; } = string.Empty;
    }

    /// <summary>The error page's display data.</summary>
    public sealed class ErrorViewModel
    {
        /// <summary>The protocol error code, when the failure was protocol-level.</summary>
        public string? Error { get; set; }

        /// <summary>The human-readable error description.</summary>
        public string? ErrorDescription { get; set; }
    }
}
