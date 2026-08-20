// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

// Wires the passkey-enrollment page. External module (no inline script), so the strict CSP holds.
"use strict";

// The endpoint is generated server-side (via routing) so it carries the in-proc path prefix.
const form = document.getElementById("passkey-form");
const creationUrl = form.dataset.creationUrl;
const errorBox = document.getElementById("passkey-error");

// Messages are rendered server-side and read from data attributes, so they stay localized and
// no string is built in script.
function showError(name) {
    // A ceremony the user dismissed is not a failure worth reporting; anything else is, and
    // saying nothing is what made a refused enrollment look like a dead button.
    if (name === "NotAllowedError" || name === "AbortError") {
        return;
    }

    errorBox.textContent = name === "InvalidStateError"
        ? errorBox.dataset.duplicateMessage
        : errorBox.dataset.failedMessage;
    errorBox.hidden = false;
}

document.getElementById("enroll-button").addEventListener("click", async () => {
    errorBox.hidden = true;
    try {
        await passkey.register(creationUrl, "credential");
    } catch (error) {
        showError(error && error.name);
    }
});
