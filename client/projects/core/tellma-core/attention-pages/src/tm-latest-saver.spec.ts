// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { type TmSaveOutcome, type TmSaveState, TmLatestSaver } from './tm-latest-saver';

// The saving rule of a self-saving control, on fake timers: the wait and its
// restart, one save in flight with the latest value after it, a failure
// while a newer value waits, a reset that drops what waited, and leaving
// within the wait or during a flight.

/** The wait every case asks for. */
const WAIT_MS = 600;

/** One save the test answers. */
interface Flight {
  /** The value sent. */
  readonly value: string;
  /** Ends the save. */
  readonly resolve: (outcome: TmSaveOutcome) => void;
  /** Fails the save. */
  readonly reject: (error: unknown) => void;
}

/** What a case drives. */
interface Rig {
  /** The saver. */
  readonly saver: TmLatestSaver<string>;
  /** Each save sent, in order. */
  readonly flights: Flight[];
  /** The states seen at each step, a repeat left out. */
  readonly states: TmSaveState[];
  /** Lets the timers run for a duration and the settled saves' callbacks run, then notes the state. */
  step(ms: number): Promise<void>;
}

/** A saver whose saves the test answers. */
function rig(): Rig {
  const flights: Flight[] = [];
  const saver = new TmLatestSaver<string>(
    (value) =>
      new Promise<TmSaveOutcome>((resolve, reject) => {
        flights.push({ value, resolve, reject });
      }),
  );
  const states: TmSaveState[] = [saver.state()];
  return {
    saver,
    flights,
    states,
    step: async (ms) => {
      await vi.advanceTimersByTimeAsync(ms);
      await Promise.resolve();
      if (states.at(-1) !== saver.state()) {
        states.push(saver.state());
      }
    },
  };
}

/** The values of the saves sent. */
const sent = (flights: readonly Flight[]): string[] => flights.map((flight) => flight.value);

describe('TmLatestSaver', () => {
  beforeEach(() => vi.useFakeTimers());

  afterEach(() => vi.useRealTimers());

  it('saves the latest value once the wait after its change ends, and a later change restarts the wait', async () => {
    const { saver, flights, states, step } = rig();
    saver.want('a', WAIT_MS);
    await step(WAIT_MS - 100);
    saver.want('b', WAIT_MS);
    await step(WAIT_MS - 100);
    expect(sent(flights)).toEqual([]);
    await step(100);
    expect(sent(flights)).toEqual(['b']);
    flights[0]!.resolve('saved');
    await step(0);
    expect(states).toEqual(['idle', 'pending', 'saving', 'saved']);
  });

  it('sends a value wanted during a flight after it, and the state follows', async () => {
    const { saver, flights, states, step } = rig();
    saver.want('a', 0);
    await step(0);
    saver.want('b', 0);
    await step(0);
    // One save at a time: 'b' waits for 'a'.
    expect(sent(flights)).toEqual(['a']);
    flights[0]!.resolve('saved');
    await step(0);
    expect(sent(flights)).toEqual(['a', 'b']);
    flights[1]!.resolve('saved');
    await step(0);
    expect(states).toEqual(['idle', 'saving', 'saved']);
  });

  it('sends the newer value when a save fails while it waits out its delay, so the state follows it', async () => {
    const { saver, flights, states, step } = rig();
    saver.want('a', 0);
    await step(0);
    saver.want('b', WAIT_MS);
    flights[0]!.reject(new Error('The save failed.'));
    await step(0);
    // The failure is not shown: a newer change is waiting.
    expect(saver.state()).toBe('pending');
    await step(WAIT_MS);
    expect(sent(flights)).toEqual(['a', 'b']);
    flights[1]!.resolve('saved');
    await step(0);
    expect(states).toEqual(['idle', 'saving', 'pending', 'saving', 'saved']);
  });

  it('sends the newer value at once when a save fails after its wait ended', async () => {
    const { saver, flights, step } = rig();
    saver.want('a', 0);
    await step(0);
    saver.want('b', 0);
    await step(0);
    flights[0]!.reject(new Error('The save failed.'));
    await step(0);
    expect(sent(flights)).toEqual(['a', 'b']);
    expect(saver.state()).toBe('saving');
  });

  it('keeps a failed value for Try again, which sends it', async () => {
    const { saver, flights, step } = rig();
    saver.want('a', 0);
    await step(0);
    flights[0]!.reject(new Error('The save failed.'));
    await step(0);
    expect(saver.state()).toBe('failed');
    saver.retry();
    expect(sent(flights)).toEqual(['a', 'a']);
    flights[1]!.resolve('saved');
    await step(0);
    expect(saver.state()).toBe('saved');
  });

  it('drops a value wanted during a flight that ends in a reset', async () => {
    const { saver, flights, states, step } = rig();
    saver.want('a', 0);
    await step(0);
    saver.want('b', WAIT_MS);
    flights[0]!.resolve('reset');
    await step(WAIT_MS);
    expect(sent(flights)).toEqual(['a']);
    expect(states).toEqual(['idle', 'saving', 'idle']);
  });

  it('sends a value still waiting at once when the control leaves, and nothing wanted after', async () => {
    const { saver, flights, step } = rig();
    saver.want('a', WAIT_MS);
    await step(100);
    saver.dispose();
    expect(sent(flights)).toEqual(['a']);
    saver.want('b', 0);
    await step(WAIT_MS);
    expect(sent(flights)).toEqual(['a']);
  });

  it('sends a value still waiting after the flight when the control leaves during one', async () => {
    const { saver, flights, step } = rig();
    saver.want('a', 0);
    await step(0);
    saver.want('b', WAIT_MS);
    saver.dispose();
    expect(sent(flights)).toEqual(['a']);
    flights[0]!.resolve('saved');
    await step(0);
    expect(sent(flights)).toEqual(['a', 'b']);
  });
});
