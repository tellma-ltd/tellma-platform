// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

// Every class shares one deployment and its live tenant. A role or membership save moves the
// tenant's permissions tag, and the platform answers any save that was running across that move
// as stale for its client to retry; the serial cases hold real locks on the tenant's centers,
// measure round trips and stop a host. Running the classes one at a time keeps each case's
// outcome its own.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
