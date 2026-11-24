// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { type BrowserContext, type Page, type Route } from '@playwright/test';

import { tmCallsRoute, tmIsServerPath, type TmFakeProblem, tmProblemResponse } from './tm-e2e-wire';

// The network seams of the e2e helpers (spec 0024 §13): holding the server's
// requests, going offline, and answering one route with a problem. Each acts
// on the page's browser context rather than on the page, because a page that
// the service worker controls sends its requests through the worker, which
// page-level routes never see; context-level routes and offline mode do. So
// a helper reaches every tab of the context, which is what a multi-tab flow
// wants. None of them unroutes to let go: a request the browser intercepts
// while its route is removed can be lost, never sent and never answered. So a
// helper that lets go keeps its route and passes every later request on.

/** One request {@link holdNetwork} holds. */
export interface TmHeldRequest {
  /** The request's URL. */
  readonly url: string;
  /** The request's method. */
  readonly method: string;
  /** When the request was held, by `Date.now()`, the clock pages share with the test. */
  readonly at: number;
}

/** The requests {@link holdNetwork} holds, and the way to let them go. */
export interface TmNetworkHold {
  /** The requests held so far, in the order they were made. */
  held(): readonly TmHeldRequest[];
  /** Lets every held request continue, and every later one pass. */
  release(): Promise<void>;
}

/**
 * Holds every request the context's pages make to the server's data
 * surfaces (the APIs, the BFF, the hub, blobs and MCP) without answering it,
 * until {@link TmNetworkHold.release}. Pages, static files and the identity
 * engine pass. A test then sees what the app shows while the server is slow,
 * and which requests it made.
 *
 * @param page - A page of the context to hold.
 * @returns The hold.
 */
export async function holdNetwork(page: Page): Promise<TmNetworkHold> {
  const context = page.context();
  const held: { readonly request: TmHeldRequest; readonly route: Route }[] = [];
  let holding = true;
  const matcher = (url: URL): boolean => tmIsServerPath(url.pathname);
  const handler = async (route: Route): Promise<void> => {
    if (!holding) {
      await route.fallback();
      return;
    }
    held.push({
      route,
      request: { url: route.request().url(), method: route.request().method(), at: Date.now() },
    });
  };
  await context.route(matcher, handler);
  return {
    held: () => held.map((entry) => entry.request),
    release: async () => {
      // The route stays: from here on the handler passes every request on.
      holding = false;
      // A request whose page has gone answers nothing; that is not a failure of the release.
      await Promise.all(held.map((entry) => entry.route.fallback().catch(() => undefined)));
    },
  };
}

/**
 * Takes the page's context offline, service worker included, until the
 * returned function brings it back.
 *
 * @param page - A page of the context.
 * @returns A function that brings the context back online.
 */
export async function goOffline(page: Page): Promise<() => Promise<void>> {
  const context: BrowserContext = page.context();
  await context.setOffline(true);
  return () => context.setOffline(false);
}

/**
 * Answers one API route with a problem body, through route interception: the
 * server is untouched, and every other route passes.
 *
 * @param page - A page of the context whose calls are answered.
 * @param route - The route after `/api/`, such as `web/client/sync`, under a tenant or not; or a whole path, such as `/bff/user`.
 * @param problem - The problem: its status, code, headers and further members.
 * @returns A function that stops answering the route, after which its calls pass.
 */
export async function fakeProblem(
  page: Page,
  route: string,
  problem: TmFakeProblem,
): Promise<() => Promise<void>> {
  const context = page.context();
  const response = tmProblemResponse(problem);
  let answering = true;
  const matcher = (url: URL): boolean => tmCallsRoute(url.pathname, route);
  const handler = (intercepted: Route): Promise<void> =>
    answering ? intercepted.fulfill(response) : intercepted.fallback();
  await context.route(matcher, handler);
  // Stopping keeps the route, which passes the calls on from then.
  return () => {
    answering = false;
    return Promise.resolve();
  };
}
