// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

// The service worker's registration (spec 0024 §5.4). The CSP enforces
// Trusted Types for scripts, so a worker script URL must be a
// TrustedScriptURL: Angular's own registration passes a plain string, which
// the browser refuses. The `tellma` policy, created once per document and
// only when the browser has Trusted Types, admits exactly the same-origin
// worker script and throws for anything else.

/** The worker script's path: Angular's worker, which the build writes at the root. */
export const TM_WORKER_SCRIPT = '/ngsw-worker.js';

/** The longest the registration waits for the app to settle after its first render, in milliseconds. */
export const TM_REGISTER_WAIT_MS = 30_000;

/** The Trusted Types policy's one rule, as the browser calls it. */
export interface TmScriptUrlRules {
  /**
   * Turns a script URL into the value a TrustedScriptURL carries.
   *
   * @param input - The URL.
   * @returns The URL, when admitted.
   */
  createScriptURL(input: string): string;
}

/** A Trusted Types policy that creates script URLs. */
export interface TmScriptUrlPolicy {
  /**
   * Creates a TrustedScriptURL through the policy's rule.
   *
   * @param input - The URL.
   * @returns The trusted value.
   */
  createScriptURL(input: string): unknown;
}

/** The browser's Trusted Types policy factory, as far as the registration uses it. */
export interface TmTrustedTypes {
  /**
   * Creates a named policy; a second policy of a name throws under the CSP.
   *
   * @param name - The policy's name.
   * @param rules - Its rules.
   * @returns The policy.
   */
  createPolicy(name: string, rules: TmScriptUrlRules): TmScriptUrlPolicy;
}

/** The policy each factory created: one per document. */
const policies = new WeakMap<TmTrustedTypes, TmScriptUrlPolicy>();

/**
 * The worker script's URL as the browser's registration takes it: a
 * TrustedScriptURL through the `tellma` policy when the browser has Trusted
 * Types, else the plain path.
 *
 * @param origin - The page's origin, which the worker must share.
 * @param trustedTypes - The browser's policy factory, or `undefined` without Trusted Types.
 * @returns The URL to register.
 */
export function tmWorkerScriptUrl(origin: string, trustedTypes: TmTrustedTypes | undefined): unknown {
  if (trustedTypes === undefined) {
    return TM_WORKER_SCRIPT;
  }
  let policy = policies.get(trustedTypes);
  if (policy === undefined) {
    policy = trustedTypes.createPolicy('tellma', {
      createScriptURL: (input) => {
        // Exactly the same-origin worker script: no other path, query or fragment.
        const url = new URL(input, origin);
        if (url.origin !== origin || url.pathname !== TM_WORKER_SCRIPT || url.search !== '' || url.hash !== '') {
          throw new TypeError(`The 'tellma' Trusted Types policy admits only ${TM_WORKER_SCRIPT}, not '${input}'.`);
        }
        return url.href;
      },
    });
    policies.set(trustedTypes, policy);
  }
  return policy.createScriptURL(TM_WORKER_SCRIPT);
}

/** The service worker container, as far as the registration uses it. */
export interface TmWorkerContainer {
  /**
   * Registers a worker script.
   *
   * @param url - The script's URL: a path or a TrustedScriptURL.
   * @param options - The registration's scope and cache rule.
   * @returns The registration.
   */
  register(url: never, options: RegistrationOptions): Promise<unknown>;
}

/**
 * Registers the app's service worker once the app has settled after its
 * first render, or after 30 s, whichever comes first: at the root scope,
 * with every update check bypassing the HTTP cache, through a
 * TrustedScriptURL. A refused registration is reported, and every launch
 * stays cold.
 *
 * @param container - The page's service worker container.
 * @param stable - Resolves once the app has settled.
 * @param url - The worker script's URL, as {@link tmWorkerScriptUrl} makes it.
 * @param failed - Reports a refused registration.
 * @returns A promise that settles once the registration has.
 */
export async function tmRegisterWorker(
  container: TmWorkerContainer,
  stable: Promise<unknown>,
  url: () => unknown,
  failed: (error: unknown) => void,
): Promise<void> {
  let timer: ReturnType<typeof setTimeout> | undefined;
  await Promise.race([stable, new Promise<void>((resolve) => (timer = setTimeout(resolve, TM_REGISTER_WAIT_MS)))]);
  clearTimeout(timer);
  try {
    // A TrustedScriptURL goes where the DOM's types say string.
    await container.register(url() as never, { scope: '/', updateViaCache: 'none' });
  } catch (error) {
    failed(error);
  }
}
