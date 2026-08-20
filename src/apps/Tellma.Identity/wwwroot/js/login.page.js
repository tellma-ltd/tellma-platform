// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

// Wires the sign-in page's passkey affordances: an explicit "Sign in with a passkey" button, plus
// conditional-UI (autofill) sign-in offered on load where the platform supports it.
"use strict";

// The endpoint is generated server-side (via routing) so it carries the in-proc path prefix.
const assertionUrl = document.getElementById("passkey-form").dataset.assertionUrl;

document.getElementById("passkey-button").addEventListener("click", async () => {
    // Signal any in-flight conditional ceremony to abort before the explicit one starts.
    window.dispatchEvent(new Event("tellma:passkey-explicit"));
    try {
        await passkey.signIn(assertionUrl, "passkey-credential");
    } catch (error) {
        // A dismissed ceremony leaves the page for the user to choose another method.
    }
});

// Offer autofill/conditional sign-in on load (no-op where unsupported).
passkey.startConditional(assertionUrl, "passkey-credential");
