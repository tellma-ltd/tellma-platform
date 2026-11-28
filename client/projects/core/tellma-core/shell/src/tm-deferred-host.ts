// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { DestroyRef, DOCUMENT, inject, type Signal, signal, type ViewContainerRef } from '@angular/core';
import { TmTelemetry } from '@tellma/core';

// The loader the tenant shell's deferred overlays share (spec 0027 §7.2,
// §8.5): the app bar's overlays and the pinned bar's. Each is a chunk the
// shell loads when the browser is first idle after it renders, and on the
// first hover or focus of the controls that open it, so the chunk is in
// memory by the time a press asks for it. A press that beats it runs as soon
// as it lands, and presses made meanwhile collapse to their net effect: two
// toggles of one control cancel out, and a press of another control replaces
// what waits. A chunk that fails to load is reported, and the next gesture
// loads it again.

/**
 * A deferred chunk's host in the tenant shell: it loads the chunk once,
 * creates what the chunk renders in the host the shell hands it, and runs
 * the gestures that wait for it. Created in an injection context; it ends
 * with its injector. Internal.
 */
export class TmDeferredHost<C, T> {
  readonly #telemetry = inject(TmTelemetry);
  readonly #window = inject(DOCUMENT).defaultView;
  readonly #value = signal<T | null>(null);
  readonly #source: string;
  readonly #load: () => Promise<C>;
  readonly #create: (chunk: C, host: ViewContainerRef) => T;
  #host: ViewContainerRef | null = null;
  #loading: Promise<T | null> | null = null;
  #pending: { readonly control: HTMLElement; readonly action: (value: T) => void } | null = null;
  #ended = false;

  /** What the chunk created, or `null` until it is in. */
  readonly value: Signal<T | null> = this.#value.asReadonly();

  /**
   * Creates the host.
   *
   * @param source - The telemetry source of a failed load.
   * @param load - Loads the chunk.
   * @param create - Creates the chunk's content in the shell's host, rendered at once, and returns it.
   */
  constructor(source: string, load: () => Promise<C>, create: (chunk: C, host: ViewContainerRef) => T) {
    this.#source = source;
    this.#load = load;
    this.#create = create;
    inject(DestroyRef).onDestroy(() => {
      this.#ended = true;
      this.#pending = null;
    });
  }

  /**
   * Takes the host the chunk's content renders in, and loads the chunk once
   * the browser is idle.
   *
   * @param host - Where the content renders.
   */
  attach(host: ViewContainerRef): void {
    this.#host = host;
    const view = this.#window;
    const idle = (run: () => void): unknown => (view?.requestIdleCallback ? view.requestIdleCallback(run) : setTimeout(run));
    idle(() => void this.#ready());
  }

  /** Starts loading the chunk, as a hover or focus on a control that opens it does. */
  preload(): void {
    void this.#ready();
  }

  /**
   * Runs an action on the chunk's content: at once when it is in, so what
   * it opens shows in the gesture's frame, else as soon as it lands.
   *
   * @param action - What to do with the content.
   */
  run(action: (value: T) => void): void {
    const value = this.#value();
    if (value !== null) {
      action(value);
      return;
    }
    void this.#ready().then((loaded) => loaded !== null && action(loaded));
  }

  /**
   * Runs a control's press on the chunk's content: at once when it is in,
   * else once it lands, as the net effect of every press made meanwhile.
   * Before the chunk nothing shows, so a second toggle of the same control
   * cancels the first; an opening press, or a press of another control,
   * replaces what waits.
   *
   * @param control - The pressed control.
   * @param action - What the press does with the content.
   * @param opens - Whether the press only opens, so a pending press of the same control does not cancel it.
   */
  press(control: HTMLElement, action: (value: T) => void, opens = false): void {
    const value = this.#value();
    if (value !== null) {
      action(value);
      return;
    }
    this.#pending = !opens && this.#pending?.control === control ? null : { control, action };
    // Every press waits on the same load; the first to resume runs what is left and clears it.
    void this.#ready().then((loaded) => {
      const pending = this.#pending;
      this.#pending = null;
      if (loaded !== null && pending !== null) {
        pending.action(loaded);
      }
    });
  }

  /** Drops a press that waits for the chunk, as closing every overlay does. */
  drop(): void {
    this.#pending = null;
  }

  /** Loads the chunk once and creates its content; `null` when it fails to load or the shell ended meanwhile. */
  #ready(): Promise<T | null> {
    this.#loading ??= this.#load().then(
      (chunk) => {
        const host = this.#host;
        if (this.#ended || host === null) {
          // Before the shell hands over its host, a later gesture loads again.
          this.#loading = null;
          return null;
        }
        const value = this.#create(chunk, host);
        this.#value.set(value);
        return value;
      },
      (error: unknown) => {
        // Offline, or after a deploy removed the chunk: the next gesture tries again.
        this.#telemetry.error(error, { source: this.#source });
        this.#loading = null;
        return null;
      },
    );
    return this.#loading;
  }
}
