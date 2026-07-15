// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

// Wires the sign-in page's passkey affordances: an explicit "Sign in with a passkey" button, plus
// conditional-UI (autofill) sign-in offered on load where the platform supports it.
"use strict";

const assertionUrl = "/Identity/api/passkey/assertion-options";

document.getElementById("passkey-button").addEventListener("click", async () => {
    // Signal any in-flight conditional ceremony to abort before the explicit one starts.
    window.dispatchEvent(new Event("tellma:passkey-explicit"));
    const emailInput = document.getElementById("email");
    const email = emailInput ? emailInput.value : "";
    try {
        await passkey.signIn(assertionUrl, "passkey-credential", email);
    } catch (error) {
        // A dismissed ceremony leaves the page for the user to choose another method.
    }
});

// Offer autofill/conditional sign-in on load (no-op where unsupported).
passkey.startConditional(assertionUrl, "passkey-credential");
