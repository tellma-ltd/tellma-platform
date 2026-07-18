// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

// Small, dependency-free helpers shared by the WebAuthn ceremony. No inline scripts run on any
// identity page, so this file is loaded as an external module and reads its configuration from
// data- attributes.
"use strict";

const tellma = {
    // Reads the anti-forgery token the page rendered into a hidden field.
    antiforgeryToken() {
        const input = document.querySelector('input[name="__RequestVerificationToken"]');
        return input ? input.value : "";
    },

    // POSTs a form-encoded body with the anti-forgery header and returns the parsed JSON body.
    async postForm(url, fields) {
        const body = new URLSearchParams(fields || {});
        const response = await fetch(url, {
            method: "POST",
            headers: {
                "RequestVerificationToken": this.antiforgeryToken(),
                "Content-Type": "application/x-www-form-urlencoded",
            },
            body,
        });
        if (!response.ok) {
            throw new Error("Request failed with status " + response.status);
        }
        return response.json();
    },

    // Writes a value into a hidden field and submits its owning form.
    submitWithValue(fieldId, value) {
        const field = document.getElementById(fieldId);
        field.value = value;
        field.form.submit();
    },
};
