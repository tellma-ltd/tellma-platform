// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

// The entire WebAuthn ceremony. The server produces the options JSON (WebAuthn JSON format); this
// script drives navigator.credentials and posts the resulting credential back through a normal
// form field. No secrets or tokens are held here.
"use strict";

// Some password managers implement PublicKeyCredential.toJSON incorrectly and throw
// "Illegal invocation"; fall back to manual base64url serialization when that happens.
function credentialToJson(credential) {
    try {
        if (typeof credential.toJSON === "function") {
            return JSON.stringify(credential.toJSON());
        }
    } catch (error) {
        // Fall through to the manual path below.
    }
    return JSON.stringify(serializeManually(credential));
}

function base64url(buffer) {
    const bytes = new Uint8Array(buffer);
    let binary = "";
    for (let i = 0; i < bytes.length; i++) {
        binary += String.fromCharCode(bytes[i]);
    }
    return btoa(binary).replace(/\+/g, "-").replace(/\//g, "_").replace(/=+$/, "");
}

function serializeManually(credential) {
    const response = credential.response;
    const json = {
        id: credential.id,
        rawId: base64url(credential.rawId),
        type: credential.type,
        clientExtensionResults: credential.getClientExtensionResults ? credential.getClientExtensionResults() : {},
        response: {
            clientDataJSON: base64url(response.clientDataJSON),
        },
    };
    if (response.attestationObject) {
        // Registration (attestation) response.
        json.response.attestationObject = base64url(response.attestationObject);
        if (response.getTransports) {
            json.response.transports = response.getTransports();
        }
    } else {
        // Sign-in (assertion) response.
        json.response.authenticatorData = base64url(response.authenticatorData);
        json.response.signature = base64url(response.signature);
        if (response.userHandle) {
            json.response.userHandle = base64url(response.userHandle);
        }
    }
    return json;
}

const passkey = {
    // Enrolls a new passkey: fetch creation options, run the attestation ceremony, submit.
    async register(optionsUrl, resultFieldId) {
        const optionsJson = await tellma.postForm(optionsUrl, {});
        const options = PublicKeyCredential.parseCreationOptionsFromJSON(optionsJson);
        const credential = await navigator.credentials.create({ publicKey: options });
        tellma.submitWithValue(resultFieldId, credentialToJson(credential));
    },

    // Signs in with a passkey via an explicit button: fetch request options, run assertion, submit.
    async signIn(optionsUrl, resultFieldId, email) {
        const optionsJson = await tellma.postForm(optionsUrl, email ? { email } : {});
        const options = PublicKeyCredential.parseRequestOptionsFromJSON(optionsJson);
        const credential = await navigator.credentials.get({ publicKey: options });
        tellma.submitWithValue(resultFieldId, credentialToJson(credential));
    },

    // Offers conditional-UI (autofill) sign-in where available, aborting cleanly if the user
    // starts an explicit ceremony instead.
    async startConditional(optionsUrl, resultFieldId) {
        if (!window.PublicKeyCredential || !PublicKeyCredential.isConditionalMediationAvailable) {
            return;
        }
        if (!(await PublicKeyCredential.isConditionalMediationAvailable())) {
            return;
        }

        const controller = new AbortController();
        window.addEventListener("tellma:passkey-explicit", () => controller.abort(), { once: true });

        try {
            const optionsJson = await tellma.postForm(optionsUrl, {});
            const options = PublicKeyCredential.parseRequestOptionsFromJSON(optionsJson);
            const credential = await navigator.credentials.get({
                publicKey: options,
                mediation: "conditional",
                signal: controller.signal,
            });
            tellma.submitWithValue(resultFieldId, credentialToJson(credential));
        } catch (error) {
            // An aborted or dismissed conditional ceremony is not an error the user should see.
        }
    },
};
