// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

// The fixture project's one file on disk: stand-ins for the services whose
// calls the telemetry-names rule checks. The rule's test cases are inline and
// sit beside it in the same project.

/** Stands in for the platform's telemetry service. */
export declare abstract class TmTelemetry {
  /** Records an event. */
  event(name: string, props?: object): void;
  /** Records a duration. */
  timing(name: string, ms: number, props?: object): void;
}

/** Stands in for a test's telemetry, a subclass of the service. */
export declare class TmFakeTelemetry extends TmTelemetry {}

/** Stands in for the page actions service. */
export declare class TmPageActions {
  /** Invokes an action by its id. */
  invoke(id: string, args: unknown, origin: string): Promise<unknown>;
}

/** Something else with an event method. */
export declare class AcmeCalendar {
  /** Adds an event. */
  event(title: string): void;
}
