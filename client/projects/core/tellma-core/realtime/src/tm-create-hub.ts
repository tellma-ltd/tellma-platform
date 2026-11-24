// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import type { Injector } from '@angular/core';
import { TM_NOW, TmApi, type TmHubConnection, type TmHubSink, TmSession, TmTelemetry } from '@tellma/core';

import { TmHub } from './tm-hub';
import { tmSignalRConnect } from './tm-hub-signalr';

/**
 * Creates the realtime connection to a tenant's hub, which the leader tab
 * holds: it connects once started and reconnects until stopped, refused or
 * ended, and reports to the sink. Internal: the leader election calls it.
 *
 * @param tenantId - The tenant whose hub it connects to.
 * @param sink - Where it reports its state and the server's events.
 * @param injector - The app's injector, which supplies the session, the HTTP layer and the telemetry.
 * @returns The connection, not yet started.
 * @internal
 */
export function tmCreateHub(tenantId: number, sink: TmHubSink, injector: Injector): TmHubConnection {
  const session = injector.get(TmSession);
  const telemetry = injector.get(TmTelemetry);
  return new TmHub(sink, {
    connect: tmSignalRConnect({ url: `/${tenantId}/hub`, check: () => session.check(), telemetry }),
    online: injector.get(TmApi).online,
    telemetry,
    now: injector.get(TM_NOW),
    random: () => Math.random(),
    injector,
  });
}
