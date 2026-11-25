// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

// The plain rules behind the e2e helpers, free of Playwright and of Node, so
// the unit suite pins them: which requests are the server's, which API route
// a request names, the problem body a faked refusal answers with, and the
// sign-in code in a message.

/** One message the e2e host's development mail drop wrote. */
export interface TmDroppedMail {
  /** The file format's version: 1. */
  readonly version: number;
  /** When the message was dropped, as an ISO 8601 instant. */
  readonly sentAt: string;
  /** The channel the message took: `live` or `sandbox`. */
  readonly channel: string;
  /** Whose inbox the message targeted: `internal` or `external`. */
  readonly audience: string;
  /** The primary recipients. */
  readonly to: readonly TmDroppedAddress[];
  /** The carbon-copy recipients. */
  readonly cc: readonly TmDroppedAddress[];
  /** The blind-carbon-copy recipients. */
  readonly bcc: readonly TmDroppedAddress[];
  /** The subject. */
  readonly subject: string;
  /** The plain-text body. */
  readonly textBody: string;
  /** The HTML body, or `null`. */
  readonly htmlBody: string | null;
}

/** One address of a dropped message. */
export interface TmDroppedAddress {
  /** The address. */
  readonly address: string;
  /** The display name, or `null`. */
  readonly displayName: string | null;
}

/** A problem a faked route answers with: the status, the code and any further members of the body. */
export interface TmFakeProblem {
  /** The HTTP status. */
  readonly status: number;
  /** The problem's code, such as `forbidden` or `tenant-not-found`. */
  readonly code: string;
  /** The problem's title; a generic one when omitted. */
  readonly title?: string;
  /** Response headers beside the content type, such as `Tellma-Build` or `WWW-Authenticate`. */
  readonly headers?: Readonly<Record<string, string>>;
  /** Further members of the problem body, such as `reason` or `permission`. */
  readonly extensions?: Readonly<Record<string, unknown>>;
}

/** The first segments of the server's tenantless surfaces whose requests the helpers treat as the server's. */
const SERVER_FIRST = new Set(['api', 'bff', 'mcp']);

/** The second segments of the server's tenant surfaces under `/{tenantId}`. */
const SERVER_TENANT = new Set(['api', 'hub', 'blobs', 'mcp']);

/** A tenant id: a positive integer without leading zeros. */
const TENANT_ID = /^[1-9]\d*$/;

/** The identity engine's sign-in codes: eight digits. */
const SIGN_IN_CODE = /\b\d{8}\b/;

/**
 * Whether a path names one of the server's data surfaces: an API, the BFF,
 * the hub, blobs or MCP, under a tenant or not. Pages, static files, the
 * health probes and the identity engine's pages are not.
 *
 * @param pathname - The request's path, such as `/17/api/web/client/sync`.
 * @returns True for a request the app makes to the server for data.
 */
export function tmIsServerPath(pathname: string): boolean {
  const [first, second] = pathname.split('/').filter((segment) => segment.length > 0);
  if (first === undefined) {
    return false;
  }
  return (
    SERVER_FIRST.has(first) ||
    (TENANT_ID.test(first) && second !== undefined && SERVER_TENANT.has(second))
  );
}

/**
 * Whether a path calls an API route. A route without a leading `/` names the
 * route after `/api/`, under a tenant or not: `web/client/sync` matches
 * `/17/api/web/client/sync`. A route with a leading `/` is a whole path, such
 * as `/bff/user`. The query string is never part of the match.
 *
 * @param pathname - The request's path.
 * @param route - The route.
 * @returns True when the path calls the route.
 */
export function tmCallsRoute(pathname: string, route: string): boolean {
  if (route.startsWith('/')) {
    return pathname === route;
  }
  const segments = pathname.split('/').filter((segment) => segment.length > 0);
  const api =
    TENANT_ID.test(segments[0] ?? '') && segments[1] === 'api' ? 2 : segments[0] === 'api' ? 1 : -1;
  return api > 0 && segments.slice(api).join('/') === route.replace(/^\/+|\/+$/g, '');
}

/**
 * The response a faked problem answers with: its status, its headers and the
 * problem body in the server's shape.
 *
 * @param problem - The problem.
 * @returns What Playwright's `route.fulfill` takes.
 */
export function tmProblemResponse(problem: TmFakeProblem): {
  readonly status: number;
  readonly headers: Readonly<Record<string, string>>;
  readonly body: string;
} {
  const body = {
    type: `https://tellma.com/problems/${problem.code}`,
    title: problem.title ?? 'The request was refused.',
    status: problem.status,
    code: problem.code,
    errors: [],
    errorDetails: null,
    ...problem.extensions,
  };
  return {
    status: problem.status,
    headers: { 'content-type': 'application/problem+json', ...problem.headers },
    body: JSON.stringify(body),
  };
}

/**
 * The sign-in code in a message's text.
 *
 * @param text - The message's plain-text body.
 * @returns The eight-digit code, or `null` when the text carries none.
 */
export function tmSignInCode(text: string): string | null {
  return SIGN_IN_CODE.exec(text)?.[0] ?? null;
}

/**
 * Whether a dropped message went to an address, which is matched without
 * regard to case, as mail addresses are.
 *
 * @param mail - The message.
 * @param email - The address.
 * @returns True when the address is among the message's primary recipients.
 */
export function tmMailIsFor(mail: TmDroppedMail, email: string): boolean {
  const wanted = email.toLowerCase();
  return mail.to.some((address) => address.address.toLowerCase() === wanted);
}
