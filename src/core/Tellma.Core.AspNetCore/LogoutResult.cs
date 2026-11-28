// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.AspNetCore
{
    /// <summary>The answer of <c>POST /bff/logout</c>: where the browser goes next.</summary>
    /// <param name="RedirectUrl">
    ///     The identity server's end-session URL for a global sign-out, or the local signed-out page
    ///     for a local one.
    /// </param>
    public sealed record LogoutResult(string RedirectUrl);
}
