// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import golden from './display-name-golden.json';

// The display-name golden the server's suite and the client's share, for
// the specs of other entry points, which reach the package root's files
// only through its test rigs.

/** The shared display-name golden: its render, expansion and findable rows. */
export const TM_DISPLAY_NAME_GOLDEN: typeof golden = golden;
