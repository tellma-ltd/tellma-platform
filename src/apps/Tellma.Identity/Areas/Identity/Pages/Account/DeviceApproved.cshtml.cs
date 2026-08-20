// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Tellma.Identity.Areas.Identity.Pages.Account
{
    /// <summary>The terminal page shown after a device authorization is approved or denied.</summary>
    [AllowAnonymous]
    public sealed class DeviceApprovedModel : PageModel
    {
    }
}
