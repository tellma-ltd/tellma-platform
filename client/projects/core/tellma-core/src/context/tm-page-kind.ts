// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

// The page kinds of the page context (spec 0026 §9.1). A route's kind names
// the page family it renders, which telemetry, the page context and the first
// frame's skeleton read.

/**
 * The family of a routed page: one of the platform's page frameworks, or
 * `custom` for a page a distribution writes itself.
 */
export type TmPageKind = 'home' | 'search' | 'details' | 'report' | 'studio' | 'dashboard' | 'settings' | 'access' | 'custom';
