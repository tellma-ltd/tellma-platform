// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { InjectionToken } from '@angular/core';

// The parts of the client state's support dump section (spec 0025 §15) that
// the preferences, the drafts and the page state report when they are
// created, so reading the section never creates them. The first frame
// reaches this file through the preferences and the session's paths; the
// section's builder, TmStateDump, is a file of its own, which only the
// support host's chunk reaches.

/**
 * The parts of the dump the services report once created, each a reader of
 * the service's names. Internal: each service sets its own member when it is
 * created, and a part no service set reads as empty.
 */
export interface TmStateParts {
  /** The preference keys this tab set and has not yet saved. */
  preferences?: () => readonly string[];
  /** This tab's draft keys, without the user part. */
  drafts?: () => readonly string[];
  /** The page-state keys this tab holds. */
  pageState?: () => readonly string[];
  /**
   * The drafts service, which the session's paths count and clear through
   * once it exists: its count of the user's drafts, and its delete of a
   * user's drafts, pending saves included.
   */
  draftsService?: { readonly dirtyCount: () => number; clearAll(sub: string): void };
}

/** The parts the services report: the dump's readers, and the drafts service. Internal. */
export const TM_STATE_PARTS = new InjectionToken<TmStateParts>('TM_STATE_PARTS', {
  providedIn: 'root',
  factory: () => ({}),
});
