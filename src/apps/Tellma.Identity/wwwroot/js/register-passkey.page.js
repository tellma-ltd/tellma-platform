// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

// Wires the passkey-enrollment page. External module (no inline script), so the strict CSP holds.
"use strict";

document.getElementById("enroll-button").addEventListener("click", async () => {
    try {
        await passkey.register("/Identity/api/passkey/creation-options", "credential");
    } catch (error) {
        // A dismissed or failed ceremony leaves the page as-is for a retry.
    }
});
