// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { createEnvironmentInjector, DestroyRef, EnvironmentInjector, signal, type WritableSignal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { TM_GRID_KEYS } from '@tellma/core-ui/grid';
import { tmAriaKeyShortcuts, tmKeysText } from '@tellma/core-ui/kbd';
import { ɵtmModalOpened, ɵtmRegisterBusy } from '@tellma/core-ui/overlay';

import { TM_DEV_MODE } from '../app/tm-dev-mode';
import { TmTelemetry, type TmTelemetryItem, type TmTelemetryOptions, type TmTelemetryProps } from '../observe/tm-telemetry';
import { TmSession } from '../session/tm-session';
import type { TmSessionState } from '../session/tm-session-paths';
import {
  TM_BROWSER_KEYS,
  TM_GRID_MODIFIED_KEYS,
  TM_RESERVED_SHORTCUTS,
  TmShortcutConflictError,
  type TmShortcut,
  type TmShortcutRefusal,
} from './tm-shortcut';
import { TM_APPLE_PLATFORM, TmShortcuts } from './tm-shortcuts';

// Section: the rig. A recording telemetry that keeps the options (the shared
// fake drops them), a session whose state the test sets, and a lifetime per
// registration that the test ends.

/** One recorded telemetry event, with its options. */
interface TmRecorded {
  /** The event's name. */
  readonly name: string;
  /** Its properties. */
  readonly props: TmTelemetryProps | undefined;
  /** Its options. */
  readonly options: TmTelemetryOptions | undefined;
}

/** A telemetry that records events with their options and ignores the rest. */
class TmOptionsTelemetry extends TmTelemetry {
  /** The events, in order. */
  readonly events: TmRecorded[] = [];
  /** Nothing is kept for the diagnostics view. */
  override readonly recent = signal<readonly TmTelemetryItem[]>([]).asReadonly();
  /** Records an event. */
  override event(name: string, props?: TmTelemetryProps, options?: TmTelemetryOptions): void {
    this.events.push({ name, props, options });
  }
  /** Ignores a timing. */
  override timing(): void {}
  /** Ignores an error. */
  override error(): void {}
  /** Ignores a flush. */
  override flush(): void {}
}

/** The rig's handles. */
interface TmRig {
  /** The registry under test. */
  readonly shortcuts: TmShortcuts;
  /** The telemetry it reports to. */
  readonly telemetry: TmOptionsTelemetry;
  /** The session state it reads. */
  readonly state: WritableSignal<TmSessionState>;
}

/** Configures the registry on one platform, in development or production. */
function rig(options: { apple?: boolean; dev?: boolean } = {}): TmRig {
  const telemetry = new TmOptionsTelemetry();
  const state = signal<TmSessionState>('active');
  TestBed.configureTestingModule({
    providers: [
      { provide: TmTelemetry, useValue: telemetry },
      { provide: TmSession, useValue: { state: state.asReadonly() } },
      { provide: TM_DEV_MODE, useValue: () => options.dev ?? true },
      { provide: TM_APPLE_PLATFORM, useValue: options.apple ?? false },
    ],
  });
  return { shortcuts: TestBed.inject(TmShortcuts), telemetry, state };
}

/** A lifetime the test ends, as a page's would. */
function lifetime(): EnvironmentInjector {
  return createEnvironmentInjector([], TestBed.inject(EnvironmentInjector));
}

/** A page shortcut on `keys` that counts its runs. */
function counted(keys: string, patch: Partial<TmShortcut> = {}): TmShortcut & { runs: number } {
  const shortcut = {
    id: 'acme.test',
    keys,
    labelKey: 'Acme_Test',
    scope: 'page' as const,
    runs: 0,
    run(): void {
      shortcut.runs++;
    },
    ...patch,
  };
  return shortcut;
}

/** Presses a key on `target`, bubbling and cancelable as a real one is, and returns the event. */
function press(init: KeyboardEventInit, target: EventTarget = document.body): KeyboardEvent {
  const event = new KeyboardEvent('keydown', { bubbles: true, cancelable: true, ...init });
  target.dispatchEvent(event);
  return event;
}

/** Appends an element to the page for one test. */
function attach<T extends HTMLElement>(element: T): T {
  document.body.append(element);
  return element;
}

/** An element of the given tag, with its attributes. */
function element(tag: string, attributes: Readonly<Record<string, string>> = {}): HTMLElement {
  const created = document.createElement(tag);
  for (const [name, value] of Object.entries(attributes)) {
    created.setAttribute(name, value);
  }
  return created;
}

/** The refusal a registration meets in development, or `null` when it registers. */
function refusalOf(shortcuts: TmShortcuts, shortcut: TmShortcut): TmShortcutRefusal | null {
  try {
    shortcuts.register(shortcut, lifetime().get(DestroyRef));
    return null;
  } catch (error) {
    if (error instanceof TmShortcutConflictError) {
      return error.reason;
    }
    throw error;
  }
}

describe('TmShortcuts', () => {
  // Every test cleans the page and the open modals it left behind.
  const cleanups: (() => void)[] = [];
  afterEach(() => {
    cleanups.splice(0).forEach((cleanup) => cleanup());
    document.body.replaceChildren();
  });

  /** Opens a modal over the page until the test ends, and returns its host. */
  function modal(): HTMLElement {
    const host = attach(element('div'));
    cleanups.push(ɵtmModalOpened(host));
    return host;
  }

  describe('dispatch', () => {
    it('runs a matching shortcut, then prevents and stops the key and reports it, sampled', () => {
      const { shortcuts, telemetry } = rig();
      const shortcut = counted('Mod+J');
      shortcuts.register(shortcut, lifetime().get(DestroyRef));
      let reachedWindow = false;
      const onWindow = (): void => {
        reachedWindow = true;
      };
      window.addEventListener('keydown', onWindow);
      cleanups.push(() => window.removeEventListener('keydown', onWindow));

      const event = press({ key: 'j', code: 'KeyJ', ctrlKey: true });

      expect(shortcut.runs).toBe(1);
      expect(event.defaultPrevented).toBe(true);
      expect(reachedWindow).toBe(false);
      expect(telemetry.events).toEqual([{ name: 'tm.shortcut.used', props: { id: 'acme.test' }, options: { sampled: true } }]);
    });

    it('listens in the bubble phase, so a focused component that handles the key wins it', () => {
      const { shortcuts } = rig();
      const shortcut = counted('F2');
      shortcuts.register(shortcut, lifetime().get(DestroyRef));
      const cell = attach(element('div', { tabindex: '0' }));
      cell.addEventListener('keydown', (event) => event.preventDefault());

      press({ key: 'F2', code: 'F2' }, cell);
      expect(shortcut.runs).toBe(0);

      press({ key: 'F2', code: 'F2' }, attach(element('h1')));
      expect(shortcut.runs).toBe(1);
    });

    it('ignores a prevented key, a composition, an IME key and an auto-repeat', () => {
      const { shortcuts, telemetry } = rig();
      const shortcut = counted('Mod+J');
      shortcuts.register(shortcut, lifetime().get(DestroyRef));
      const prevented = new KeyboardEvent('keydown', { key: 'j', code: 'KeyJ', ctrlKey: true, bubbles: true, cancelable: true });
      prevented.preventDefault();
      document.body.dispatchEvent(prevented);

      press({ key: 'j', code: 'KeyJ', ctrlKey: true, isComposing: true });
      press({ key: 'Process', code: 'KeyJ', ctrlKey: true });
      press({ key: 'j', code: 'KeyJ', ctrlKey: true, repeat: true });

      expect(shortcut.runs).toBe(0);
      expect(telemetry.events).toEqual([]);
    });

    it('ignores every key while the busy overlay runs', () => {
      const { shortcuts } = rig();
      const shortcut = counted('Mod+J');
      shortcuts.register(shortcut, lifetime().get(DestroyRef));
      let busy = true;
      cleanups.push(ɵtmRegisterBusy(() => busy));

      press({ key: 'j', code: 'KeyJ', ctrlKey: true });
      expect(shortcut.runs).toBe(0);

      busy = false;
      press({ key: 'j', code: 'KeyJ', ctrlKey: true });
      expect(shortcut.runs).toBe(1);
    });

    it('ignores every key while the session is not active', () => {
      const { shortcuts, state } = rig();
      const shortcut = counted('Mod+J');
      shortcuts.register(shortcut, lifetime().get(DestroyRef));

      for (const inactive of ['expired', 'signing-out'] as const) {
        state.set(inactive);
        press({ key: 'j', code: 'KeyJ', ctrlKey: true });
      }
      expect(shortcut.runs).toBe(0);

      state.set('active');
      press({ key: 'j', code: 'KeyJ', ctrlKey: true });
      expect(shortcut.runs).toBe(1);
    });

    it('lets the event continue, unreported, when the shortcut does not handle it', () => {
      const { shortcuts, telemetry } = rig();
      const declining = counted('Mod+J', { run: () => false });
      shortcuts.register(declining, lifetime().get(DestroyRef));
      let reachedWindow = false;
      const onWindow = (): void => {
        reachedWindow = true;
      };
      window.addEventListener('keydown', onWindow);
      cleanups.push(() => window.removeEventListener('keydown', onWindow));

      const event = press({ key: 'j', code: 'KeyJ', ctrlKey: true });

      expect(event.defaultPrevented).toBe(false);
      expect(reachedWindow).toBe(true);
      expect(telemetry.events).toEqual([]);
    });

    it('matches Mod as Ctrl off Apple platforms and as Command on them', () => {
      const off = rig({ apple: false });
      const shortcut = counted('Mod+J');
      off.shortcuts.register(shortcut, lifetime().get(DestroyRef));
      press({ key: 'j', code: 'KeyJ', metaKey: true });
      expect(shortcut.runs).toBe(0);
      press({ key: 'j', code: 'KeyJ', ctrlKey: true });
      expect(shortcut.runs).toBe(1);

      TestBed.resetTestingModule();
      const on = rig({ apple: true });
      const apple = counted('Mod+J');
      on.shortcuts.register(apple, lifetime().get(DestroyRef));
      press({ key: 'j', code: 'KeyJ', ctrlKey: true });
      expect(apple.runs).toBe(0);
      press({ key: 'j', code: 'KeyJ', metaKey: true });
      expect(apple.runs).toBe(1);
    });

    it('stops listening once the app ends', () => {
      const { shortcuts } = rig();
      const shortcut = counted('Mod+J');
      shortcuts.register(shortcut, lifetime().get(DestroyRef));
      TestBed.resetTestingModule();

      press({ key: 'j', code: 'KeyJ', ctrlKey: true });
      expect(shortcut.runs).toBe(0);
    });
  });

  describe('editable targets', () => {
    it('runs a shortcut in a field only with inFields, which keys with Mod or Ctrl default to', () => {
      const { shortcuts } = rig();
      const save = counted('Mod+S', { id: 'details.save' });
      const edit = counted('F2', { id: 'details.edit' });
      const next = counted('Alt+PageDown', { id: 'details.next', inFields: true });
      for (const shortcut of [save, edit, next]) {
        shortcuts.register(shortcut, lifetime().get(DestroyRef));
      }
      const input = attach(element('input', { type: 'text' }));

      press({ key: 's', code: 'KeyS', ctrlKey: true }, input);
      press({ key: 'F2', code: 'F2' }, input);
      press({ key: 'PageDown', code: 'PageDown', altKey: true }, input);

      expect([save.runs, edit.runs, next.runs]).toEqual([1, 0, 1]);
    });

    it('counts text inputs, text areas, selects, editable regions and open grid cell editors as fields', () => {
      const { shortcuts } = rig();
      const edit = counted('F2');
      shortcuts.register(edit, lifetime().get(DestroyRef));
      const region = attach(element('div', { contenteditable: 'true' }));
      const regionChild = element('span');
      region.append(regionChild);
      const cellEditor = attach(element('div', { class: 'tm-grid__cell--editing' }));
      const cellInput = element('span');
      cellEditor.append(cellInput);
      const pane = attach(element('tm-grid-editor-pane'));
      const paneChild = element('div');
      pane.append(paneChild);
      const fields = [
        attach(element('input')),
        attach(element('input', { type: 'search' })),
        attach(element('textarea')),
        attach(element('select')),
        region,
        regionChild,
        cellInput,
        paneChild,
      ];

      for (const field of fields) {
        press({ key: 'F2', code: 'F2' }, field);
      }
      expect(edit.runs).toBe(0);
    });

    it('does not count an input that types nothing, such as a checkbox or a button, as a field', () => {
      const { shortcuts } = rig();
      const edit = counted('F2');
      shortcuts.register(edit, lifetime().get(DestroyRef));

      for (const type of ['checkbox', 'radio', 'button', 'submit', 'range', 'color', 'file']) {
        press({ key: 'F2', code: 'F2' }, attach(element('input', { type })));
      }
      expect(edit.runs).toBe(7);
    });

    it('runs Mod+K and Mod+/ in a field on an Arabic layout, where the keys type ن and ظ', () => {
      const { shortcuts } = rig();
      const launcher = counted('Mod+K', { id: 'tm.launcher.open', scope: 'global' });
      const help = counted('Mod+/', { id: 'tm.shortcuts.open', scope: 'global' });
      shortcuts.register(launcher, lifetime().get(DestroyRef));
      shortcuts.register(help, lifetime().get(DestroyRef));
      const input = attach(element('input', { type: 'text' }));

      const k = press({ key: 'ن', code: 'KeyK', ctrlKey: true }, input);
      const slash = press({ key: 'ظ', code: 'Slash', ctrlKey: true }, input);

      expect([launcher.runs, help.runs]).toEqual([1, 1]);
      expect([k.defaultPrevented, slash.defaultPrevented]).toEqual([true, true]);
    });

    it('never runs on an Apple platform when Option types another character in a field', () => {
      const { shortcuts } = rig({ apple: true });
      const notifications = counted('Alt+Shift+U', { id: 'tm.attention.notifications', scope: 'global', inFields: true });
      shortcuts.register(notifications, lifetime().get(DestroyRef));
      const input = attach(element('input', { type: 'text' }));
      const option = { code: 'KeyU', altKey: true, shiftKey: true };

      // Option+Shift+U types ¨ on a US layout, and Option+U starts a dead key.
      press({ ...option, key: '¨' }, input);
      press({ ...option, key: 'Dead' }, input);
      expect(notifications.runs).toBe(0);

      // Outside a field the same keys are the shortcut, and a layout that
      // types the binding's own letter is too.
      press({ ...option, key: '¨' }, attach(element('h1')));
      press({ ...option, key: 'U' }, input);
      expect(notifications.runs).toBe(2);
    });

    it('runs the shortcut off Apple platforms, where Alt types nothing', () => {
      const { shortcuts } = rig({ apple: false });
      const notifications = counted('Alt+Shift+U', { id: 'tm.attention.notifications', scope: 'global', inFields: true });
      shortcuts.register(notifications, lifetime().get(DestroyRef));

      press({ key: 'U', code: 'KeyU', altKey: true, shiftKey: true }, attach(element('textarea')));
      expect(notifications.runs).toBe(1);
    });
  });

  describe('modals', () => {
    it('runs only shortcuts over modals while a modal is open', () => {
      const { shortcuts } = rig();
      const page = counted('Mod+J');
      const toasts = counted('Alt+Shift+J', { id: 'tm.toasts.focus', scope: 'global', overModals: true });
      shortcuts.register(page, lifetime().get(DestroyRef));
      shortcuts.register(toasts, lifetime().get(DestroyRef));
      const host = modal();

      press({ key: 'j', code: 'KeyJ', ctrlKey: true }, host);
      press({ key: 'J', code: 'KeyJ', altKey: true, shiftKey: true }, host);

      expect([page.runs, toasts.runs]).toEqual([0, 1]);
    });

    it("runs a dialog's own page shortcuts while that dialog is on top, and not under a dialog opened above it", () => {
      const { shortcuts } = rig();
      const dialog = modal();
      const content = element('section');
      dialog.append(content);
      const save = counted('Mod+S', { id: 'details.save' });
      const remove = shortcuts.ɵregisterPage(save, content);
      const outside = counted('Mod+J');
      shortcuts.register(outside, lifetime().get(DestroyRef));

      press({ key: 's', code: 'KeyS', ctrlKey: true }, content);
      press({ key: 'j', code: 'KeyJ', ctrlKey: true }, content);
      expect([save.runs, outside.runs]).toEqual([1, 0]);

      // A confirmation opened over the dialog takes the keys.
      const confirm = modal();
      press({ key: 's', code: 'KeyS', ctrlKey: true }, confirm);
      expect(save.runs).toBe(1);

      // The returned function ends the registration, and frees its keys.
      remove!();
      expect(shortcuts.active()).toEqual([outside]);
      expect(refusalOf(shortcuts, counted('Mod+S', { id: 'details.other' }))).toBeNull();
    });

    it("runs a routed page action's shortcut, registered without a dialog, only while no modal is open", () => {
      const { shortcuts } = rig();
      const save = counted('Mod+S', { id: 'details.save' });
      shortcuts.ɵregisterPage(save, null);
      press({ key: 's', code: 'KeyS', ctrlKey: true });
      const host = modal();
      press({ key: 's', code: 'KeyS', ctrlKey: true }, host);
      expect(save.runs).toBe(1);
    });
  });

  describe('registration', () => {
    it('lists the live shortcuts, and drops each when its lifetime ends', () => {
      const { shortcuts } = rig();
      const first = counted('Mod+J', { id: 'acme.first' });
      const second = counted('Mod+Shift+J', { id: 'acme.second' });
      const firstLife = lifetime();
      shortcuts.register(first, firstLife.get(DestroyRef));
      shortcuts.register(second, lifetime().get(DestroyRef));
      expect(shortcuts.active()).toEqual([first, second]);

      firstLife.destroy();
      expect(shortcuts.active()).toEqual([second]);
      press({ key: 'j', code: 'KeyJ', ctrlKey: true });
      expect(first.runs).toBe(0);

      // Its keys are free again.
      expect(refusalOf(shortcuts, counted('Mod+J', { id: 'acme.again' }))).toBeNull();
    });

    it("ends a registration with the caller's injection context by default", () => {
      const { shortcuts } = rig();
      const page = lifetime();
      const shortcut = counted('Mod+J');
      page.runInContext(() => shortcuts.register(shortcut));
      expect(shortcuts.active()).toEqual([shortcut]);

      page.destroy();
      expect(shortcuts.active()).toEqual([]);
    });

    it('admits the page family: Mod and Mod+Shift keys, Mod+Enter, F2, Alt+PageUp and Alt+PageDown', () => {
      const { shortcuts } = rig();
      const admitted = ['Mod+S', 'Mod+Shift+M', 'Mod+Enter', 'Mod+Shift+Enter', 'Mod+1', 'F2', 'Alt+PageUp', 'Alt+PageDown'];
      admitted.forEach((keys, index) => {
        expect(refusalOf(shortcuts, counted(keys, { id: `acme.page-${index}` })), keys).toBeNull();
      });
      expect(refusalOf(shortcuts, counted('Escape', { id: 'dashboard.exit-presentation' }))).toBeNull();
    });

    it('admits every reserved shortcut on its own keys', () => {
      const { shortcuts } = rig();
      for (const { id, keys } of TM_RESERVED_SHORTCUTS) {
        expect(refusalOf(shortcuts, counted(keys, { id, scope: 'global' })), id).toBeNull();
      }
    });

    it("refuses keys that are not a combination in the grammar, with the grammar's error as the cause", () => {
      const { shortcuts } = rig();
      for (const keys of ['Mod+Shift+/', 'Hyper+K', 'Mod+', '']) {
        expect(refusalOf(shortcuts, counted(keys)), keys).toBe('invalid');
      }
      let caught: unknown;
      try {
        shortcuts.register(counted('Hyper+K'), lifetime().get(DestroyRef));
      } catch (error) {
        caught = error;
      }
      expect((caught as Error).cause).toBeInstanceOf(Error);
      expect((caught as Error).message).toMatch(/^TmShortcuts refused "acme\.test" with Hyper\+K: /);
    });

    it("refuses a 'tm.' id that is not reserved, and a reserved id on other keys", () => {
      const { shortcuts } = rig();
      expect(refusalOf(shortcuts, counted('Alt+Shift+G', { id: 'tm.custom.open' }))).toBe('family');
      expect(refusalOf(shortcuts, counted('Mod+J', { id: 'tm.launcher.open' }))).toBe('family');
      expect(() => shortcuts.register(counted('Mod+J', { id: 'tm.launcher.open' }), lifetime().get(DestroyRef))).toThrow(
        'the platform reserves Mod+K for it.',
      );
    });

    it('refuses a reserved id on a spelling of its keys that differs on one platform, on both platforms', () => {
      for (const apple of [true, false]) {
        TestBed.resetTestingModule();
        const { shortcuts } = rig({ apple });
        // Ctrl+K is ⌃K rather than ⌘K on a Mac; Meta+K is the Windows key with K elsewhere.
        for (const keys of ['Ctrl+K', 'Meta+K']) {
          expect(refusalOf(shortcuts, counted(keys, { id: 'tm.launcher.open', scope: 'global' })), `${keys} ${apple}`).toBe(
            'family',
          );
        }
        expect(refusalOf(shortcuts, counted('Mod+K', { id: 'tm.launcher.open', scope: 'global' }))).toBeNull();
      }
    });

    it('refuses a page id on a platform key, a component key or an unmodified printable key', () => {
      const { shortcuts } = rig();
      const outside = [
        'Mod+K',
        'Mod+/',
        'Alt+Shift+G',
        'Alt+Shift+7',
        'Mod+Alt+Shift+D',
        'Mod+Alt+J',
        'Ctrl+J',
        'Alt+J',
        'Escape',
        'Enter',
        'ArrowDown',
        'PageUp',
        'G',
        'F3',
      ];
      for (const keys of outside) {
        expect(refusalOf(shortcuts, counted(keys)), keys).toBe('family');
      }
    });

    it('refuses the keys a focused grid handles with a modifier, but not Mod+Enter', () => {
      const { shortcuts } = rig();
      for (const keys of ['Mod+D', 'Mod+C', 'Mod+Z', 'Mod+Shift+Z', 'Mod+ArrowUp', 'Mod+Shift+Home', 'Mod+F']) {
        expect(refusalOf(shortcuts, counted(keys)), keys).toBe('grid');
      }
      // The grid binds Ctrl+Space, which Mod+Space is everywhere but on Apple platforms.
      expect(refusalOf(shortcuts, counted('Mod+Space'))).toBe('grid');
      expect(refusalOf(shortcuts, counted('Mod+Enter'))).toBeNull();
    });

    it('refuses the keys the browser keeps', () => {
      const { shortcuts } = rig();
      for (const keys of TM_BROWSER_KEYS) {
        expect(refusalOf(shortcuts, counted(keys)), keys).toBe('browser');
      }
    });

    it('refuses the keys of a live shortcut, on either platform, naming its holder', () => {
      const { shortcuts } = rig();
      shortcuts.register(counted('Mod+J', { id: 'acme.first' }), lifetime().get(DestroyRef));

      expect(refusalOf(shortcuts, counted('Mod+J', { id: 'acme.second' }))).toBe('live');
      expect(() => shortcuts.register(counted('Mod+J', { id: 'acme.third' }), lifetime().get(DestroyRef))).toThrow(
        'the live shortcut "acme.first" already takes these keys.',
      );
      expect(shortcuts.active().map((shortcut) => shortcut.id)).toEqual(['acme.first']);
    });

    it('drops a refused registration in production and reports the conflict, unsampled', () => {
      const { shortcuts, telemetry } = rig({ dev: false });
      shortcuts.register(counted('Mod+J', { id: 'acme.first' }), lifetime().get(DestroyRef));
      const refused: [TmShortcut, TmShortcutRefusal][] = [
        [counted('Hyper+K', { id: 'acme.invalid' }), 'invalid'],
        [counted('Alt+Shift+G', { id: 'tm.custom.open' }), 'family'],
        [counted('Mod+D', { id: 'acme.grid' }), 'grid'],
        [counted('Mod+T', { id: 'acme.browser' }), 'browser'],
        [counted('Mod+J', { id: 'acme.live' }), 'live'],
      ];

      for (const [shortcut] of refused) {
        expect(() => shortcuts.register(shortcut, lifetime().get(DestroyRef))).not.toThrow();
      }

      expect(shortcuts.active().map((shortcut) => shortcut.id)).toEqual(['acme.first']);
      expect(telemetry.events).toEqual(
        refused.map(([shortcut, reason]) => ({
          name: 'tm.shortcut.conflict',
          props: { id: shortcut.id, reason },
          options: undefined,
        })),
      );
    });
  });

  describe('the reserved and browser keys', () => {
    it('reserve the platform shortcuts', () => {
      expect(TM_RESERVED_SHORTCUTS.map(({ id, keys, owner }) => `${id} ${keys} ${owner}`)).toEqual([
        'tm.launcher.open Mod+K shell',
        'tm.shortcuts.open Mod+/ shell',
        'tm.pins.toggle Alt+Shift+P shell',
        'tm.toasts.focus Alt+Shift+J shell',
        'tm.attention.notifications Alt+Shift+U attention',
        'tm.attention.assignments Alt+Shift+Q attention',
        'tm.help.open Alt+Shift+H help',
        'tm.assistant.open Alt+Shift+I assistant',
        'tm.support.copy-diagnostics Mod+Alt+Shift+D support',
      ]);
    });

    it('avoid the Alt+Shift letters the system, its accessibility features and Chrome take first', () => {
      const avoided = new Set(['S', 'M', 'N', 'L', 'T', 'A', 'B', 'R', 'C', 'W', 'X', 'Z']);
      const letters = TM_RESERVED_SHORTCUTS.flatMap(({ keys }) => (keys.startsWith('Alt+Shift+') ? [keys.slice(-1)] : []));
      expect(letters).toEqual(['P', 'J', 'U', 'Q', 'H', 'I']);
      expect(letters.filter((letter) => avoided.has(letter))).toEqual([]);
    });

    it('label each reserved shortcut for both platforms', () => {
      expect(TM_RESERVED_SHORTCUTS.map(({ keys }) => tmKeysText(keys, false))).toEqual([
        'Ctrl+K',
        'Ctrl+/',
        'Alt+Shift+P',
        'Alt+Shift+J',
        'Alt+Shift+U',
        'Alt+Shift+Q',
        'Alt+Shift+H',
        'Alt+Shift+I',
        'Ctrl+Alt+Shift+D',
      ]);
      expect(TM_RESERVED_SHORTCUTS.map(({ keys }) => tmKeysText(keys, true))).toEqual([
        '⌘K',
        '⌘/',
        '⌥⇧P',
        '⌥⇧J',
        '⌥⇧U',
        '⌥⇧Q',
        '⌥⇧H',
        '⌥⇧I',
        '⌥⇧⌘D',
      ]);
      expect(tmAriaKeyShortcuts('Mod+K', false)).toBe('Control+K');
      expect(tmAriaKeyShortcuts('Mod+K', true)).toBe('Meta+K');
      expect(tmAriaKeyShortcuts('Alt+Shift+P', true)).toBe('Alt+Shift+P');
    });

    it("hold the registry's copy of the grid's modified keys equal to the grid's own list", () => {
      const modified = TM_GRID_KEYS.map(({ keys }) => keys).filter((keys) => keys.includes('+') && keys !== 'Mod+Enter');
      expect([...new Set(TM_GRID_MODIFIED_KEYS)].sort()).toEqual([...new Set(modified)].sort());
    });
  });
});
