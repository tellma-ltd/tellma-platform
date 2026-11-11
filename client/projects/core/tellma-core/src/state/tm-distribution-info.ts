// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

// The distribution information document: the camelCase mirror of the web
// host's anonymous `GET /api/distribution-info` answer (spec 0011 §5.9),
// which the launch cache keeps as the user document `distribution` (spec 0025
// §3.1). Unlike the client metadata, it is runtime configuration: changing
// one of its members needs no client rebuild.

/** The identity authority a distribution signs in through. */
export interface TmDistributionIdentityInfo {
  /** The authority's origin. */
  readonly authority: string;
  /** Whether the authority is the shared identity service or runs inside the distribution's host. */
  readonly mode: 'Standalone' | 'InProc';
  /** The account page for this distribution, where a user manages their sign-in methods. */
  readonly accountUrl: string;
}

/** The legal links the About dialog shows. */
export interface TmDistributionLinks {
  /** The terms of service, an absolute `https` URL; absent when not configured. */
  readonly terms?: string;
  /** The privacy notice, an absolute `https` URL; absent when not configured. */
  readonly privacy?: string;
}

/** Whether support requests are enabled. */
export interface TmDistributionSupportInfo {
  /** Whether users may send support requests. */
  readonly enabled: boolean;
  /** Whether the provider can read support requests through the admin surface. */
  readonly providerAccess: boolean;
}

/** Whether, and how much, client telemetry is sent. */
export interface TmDistributionTelemetryInfo {
  /** Whether the client sends telemetry at all. */
  readonly enabled: boolean;
  /** The share of tabs that send telemetry, from 0 to 1. */
  readonly sampleRate: number;
}

/** The session rules the client follows. */
export interface TmDistributionSessionInfo {
  /** The idle window, in whole days, within which a launch may be warm. */
  readonly idleDays: number;
}

/**
 * The distribution information document: who the distribution is, where it
 * signs in, and the runtime switches the client follows. It holds no tenant
 * fact and no secret.
 */
export interface TmDistributionInfo {
  /** The distribution's slug, such as `acme`. */
  readonly slug: string;
  /** The distribution's display name, in one language. */
  readonly displayName: string;
  /** The deployment's identifier. */
  readonly deploymentId: string;
  /** The platform's informational version, carrying the commit. */
  readonly platformVersion: string;
  /** The distribution's informational version, carrying the commit. */
  readonly distributionVersion: string;
  /** The identity authority. */
  readonly identity: TmDistributionIdentityInfo;
  /** The base path of each API surface, or `null` when the surface is off. */
  readonly surfaces: Readonly<Record<string, string | null>>;
  /** The sign-in path, such as `/bff/login`. */
  readonly login: string;
  /** The admin surface versions this instance maps; empty when the admin surface is off. */
  readonly adminVersions: readonly number[];
  /** The legal links. */
  readonly links: TmDistributionLinks;
  /** The support request switches. */
  readonly support: TmDistributionSupportInfo;
  /** The client telemetry switches. */
  readonly telemetry: TmDistributionTelemetryInfo;
  /** The session rules. */
  readonly session: TmDistributionSessionInfo;
}
