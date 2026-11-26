// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

/// <reference types="@tellma/core-tools/lucide-icons" />

import { LocationStrategy, NgComponentOutlet } from '@angular/common';
import {
  afterNextRender,
  ChangeDetectionStrategy,
  Component,
  computed,
  DestroyRef,
  effect,
  ElementRef,
  inject,
  Injector,
  type OnInit,
  signal,
  untracked,
  viewChild,
  ViewContainerRef,
} from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { TM_BREAKPOINTS } from '@tellma/core-ui';
import { TmBadge } from '@tellma/core-ui/badge';
import { TmButton } from '@tellma/core-ui/button';
import { TmIcon, TmIconRegistry } from '@tellma/core-ui/icon';
import { TmPageHeader } from '@tellma/core-ui/page-header';
import { TmProgressBar } from '@tellma/core-ui/progress';
import { TmSkeleton } from '@tellma/core-ui/skeleton';
import { TmTooltip } from '@tellma/core-ui/tooltip';
import {
  type TmApiError,
  TmDisplayName,
  TmEntities,
  type TmJsonObject,
  TmLocalization,
  TmPageActions,
  type TmPageActionInput,
  TmTitleStrategy,
  TmUrls,
} from '@tellma/core';
import ChevronDown from 'lucide/dist/esm/icons/chevron-down.mjs';
import CircleCheck from 'lucide/dist/esm/icons/circle-check.mjs';
import CircleOff from 'lucide/dist/esm/icons/circle-off.mjs';
import Pencil from 'lucide/dist/esm/icons/pencil.mjs';
import Plus from 'lucide/dist/esm/icons/plus.mjs';
import RefreshCw from 'lucide/dist/esm/icons/refresh-cw.mjs';
import X from 'lucide/dist/esm/icons/x.mjs';

import { TmDetailsController } from './tm-details-controller';

// The details page's frame (spec 0031 §5.1–§5.3, §10.1): the header's two
// rows (the crumb, the title and the badges; the toolbar), the body band
// with its progress bar and status strip, the layout in the record's own
// injector or the read's error state, and the one polite status region. It
// registers the page actions the toolbar's buttons are views of.

/** The icons the frame draws, registered when it first renders. */
const ICONS = {
  'chevron-down': ChevronDown,
  'circle-check': CircleCheck,
  'circle-off': CircleOff,
  pencil: Pencil,
  plus: Plus,
  'refresh-cw': RefreshCw,
  x: X,
};

/** Counts the frames, so each title gets its own id. */
let nextFrame = 0;

/** The details page's header, band and status, over the host's state. Internal: the routed host and the source page render it. */
@Component({
  selector: 'tm-details-frame',
  imports: [NgComponentOutlet, RouterLink, TmBadge, TmButton, TmDisplayName, TmIcon, TmPageHeader, TmProgressBar, TmSkeleton, TmTooltip],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <!-- Rows 1 and 2: the header. -->
    <tm-page-header>
      <!-- The crumb: the entity's list. -->
      @if (crumb(); as back) {
        <a tmPageBack class="tm-details__crumb" [routerLink]="back.url">{{ back.label }}</a>
      }
      <!-- The title: the record's display name, the new record's label, or a skeleton while unknown. -->
      <h1 tmPageTitle class="tm-details__title" [id]="titleId">
        @if (controller.title().parts; as parts) {
          <tm-display-name [parts]="parts" />
        } @else if (controller.title().text) {
          {{ controller.title().text }}
        } @else {
          <tm-skeleton class="tm-details__title-skeleton" width="14rem" />
        }
      </h1>
      <!-- The badges, each in a box of its own: Active or Inactive, then Unsaved changes. -->
      @if (activity(); as state) {
        <span tmPageTitleExtra class="tm-details__badge">
          @if (state === 'loading') {
            <tm-skeleton width="4.5rem" />
          } @else if (state === 'active') {
            <tm-badge tone="success" icon="circle-check">{{ text('active') }}</tm-badge>
          } @else {
            <tm-badge tone="neutral" icon="circle-off">{{ text('inactive') }}</tm-badge>
          }
        </span>
      }
      @if (mode() !== 'view') {
        <!-- Kept in edit and create mode and shown while dirty, so the first keystroke moves nothing. -->
        <span tmPageTitleExtra class="tm-details__badge" [class.tm-details__badge--hidden]="!dirty()">
          <tm-badge tone="warning" dot>{{ text('unsaved') }}</tm-badge>
        </span>
      }

      <!-- Row 2, leading: Edit, New and the Actions menu in view mode; Save and Cancel in edit and create mode. -->
      <div tmPageToolbar class="tm-details__group">
        @if (writes()) {
          @if (mode() === 'view') {
            <button
              #editButton
              tmButton
              class="tm-details__edit"
              type="button"
              variant="primary"
              [disabled]="!editEnabled() && !controller.editReason()"
              [disabledReason]="controller.editReason()"
              [pending]="running().has(ids().edit)"
              (click)="invoke(ids().edit)"
            >
              <tm-icon name="pencil" />{{ text('edit') }}
            </button>
            @if (creates()) {
              @if (controller.createReason(); as reason) {
                <!-- A refused New is a button that says why: a link cannot be disabled. -->
                <button tmButton class="tm-details__create" type="button" [disabledReason]="reason" [attr.aria-label]="wide() ? null : text('create')">
                  <tm-icon name="plus" />
                  @if (wide()) {
                    {{ text('create') }}
                  }
                </button>
              } @else if (wide()) {
                <a tmButton class="tm-details__create" [attr.href]="createHref()" (click)="create($event)"><tm-icon name="plus" />{{ text('create') }}</a>
              } @else {
                <a tmButton class="tm-details__create" [attr.href]="createHref()" [attr.aria-label]="text('create')" [tmTooltip]="text('create')" (click)="create($event)">
                  <tm-icon name="plus" />
                </a>
              }
            }
          } @else {
            <button
              tmButton
              class="tm-details__save"
              type="button"
              variant="primary"
              [disabled]="!saveEnabled() && !running().has(ids().save)"
              [pending]="running().has(ids().save)"
              (click)="invoke(ids().save)"
            >
              {{ text('save') }}
            </button>
            @if (wide()) {
              <button tmButton class="tm-details__cancel" type="button" [disabled]="controller.busy()" (click)="invoke(ids().cancel)">
                <tm-icon name="x" />{{ text('cancel') }}
              </button>
            } @else {
              <button
                tmButton
                class="tm-details__cancel"
                type="button"
                [disabled]="controller.busy()"
                [attr.aria-label]="text('cancel')"
                [tmTooltip]="text('cancel')"
                (click)="invoke(ids().cancel)"
              >
                <tm-icon name="x" />
              </button>
            }
          }
        }
        @if (mode() === 'view') {
          <!-- The Actions menu's place; its items arrive with the record's actions. -->
          <button tmButton class="tm-details__actions" type="button" disabled>{{ text('actions') }}<tm-icon name="chevron-down" /></button>
        }
      </div>
      <!-- Row 2, trailing: Refresh in view mode, then the pager's place, packed to the inline end. -->
      <div tmPageToolbarEnd class="tm-details__group">
        @if (mode() === 'view') {
          <button
            tmButton
            class="tm-details__refresh"
            type="button"
            variant="ghost"
            [attr.aria-label]="text('refresh')"
            [tmTooltip]="text('refresh')"
            (click)="invoke(ids().refresh)"
          >
            <tm-icon name="refresh-cw" />
          </button>
        }
        <span class="tm-details__pager"></span>
      </div>
    </tm-page-header>

    <!-- Row 3: the band, under the progress bar and the status strip, which overlay its top edge. -->
    <div class="tm-details__body">
      <tm-progress-bar class="tm-details__bar" [active]="controller.holding()" [label]="text('loading')" />
      <div class="tm-details__strip">
        @if (controller.refreshFailure()) {
          <!-- A refresh that failed under the edit: the edit stays, and the strip says so. -->
          <p class="tm-details__notice" role="alert">{{ text('refreshFailed') }}</p>
          <button tmButton class="tm-details__retry" type="button" variant="ghost" size="sm" (click)="controller.refresh()">{{ text('retry') }}</button>
        }
      </div>
      <section class="tm-details__band" [attr.aria-labelledby]="titleId" [attr.aria-busy]="controller.loud() ? 'true' : null">
        @if (controller.status() === 'error') {
          <!-- The read's error state, with its way out to the list. -->
          <ng-container #errorSlot />
        } @else if (controller.record(); as record) {
          <!-- The layout, in the record's own injector: a re-key renders a fresh one. -->
          @if (controller.layout(); as layout) {
            <ng-container *ngComponentOutlet="layout; injector: record.injector" />
          }
        }
      </section>
    </div>

    <!-- The page's one polite status region. -->
    <div class="tm-details__live" role="status">{{ live() }}</div>
  `,
  styles: `
    /* The frame fills the shell's main and splits it into the header and the band. */
    :host {
      display: flex;
      flex-direction: column;
      block-size: 100%;
      min-block-size: 0;
    }

    .tm-details__title-skeleton {
      display: inline-block;
      vertical-align: middle;
    }

    /* A badge's box keeps its place while its badge hides, so the title row never moves. */
    .tm-details__badge {
      display: inline-flex;
      align-items: center;
    }

    .tm-details__badge--hidden {
      visibility: hidden;
    }

    /* Each toolbar group's buttons are items of its slot's own row. */
    .tm-details__group {
      display: contents;
    }

    .tm-details__pager:empty {
      display: none;
    }

    /* The band fills the rest of the page and scrolls on its own. */
    .tm-details__body {
      position: relative;
      display: flex;
      flex: 1 1 auto;
      flex-direction: column;
      min-block-size: 0;
    }

    .tm-details__bar {
      position: absolute;
      z-index: 2;
      inset-block-start: 0;
      inset-inline: 0;
    }

    /* The status strip: one row over the band's top, taking no space; empty, it is not drawn. */
    .tm-details__strip {
      position: absolute;
      z-index: 1;
      inset-block-start: 0;
      inset-inline: 0;
      display: flex;
      align-items: center;
      block-size: 36px;
      background: var(--surface-card);
      box-shadow: var(--shadow-xs);
    }

    .tm-details__strip:empty {
      display: none;
    }

    .tm-details__notice {
      flex: 1 1 auto;
      margin: 0;
      padding-inline: var(--page-header-padding-x);
      overflow: hidden;
      color: var(--text-default);
      text-overflow: ellipsis;
      white-space: nowrap;
    }

    .tm-details__band {
      display: flex;
      flex: 1 1 auto;
      flex-direction: column;
      gap: 10px;
      min-block-size: 0;
      padding: var(--page-header-padding-x);
      overflow: auto;
      border-block-start: var(--border-width) solid var(--border-subtle);
      background: var(--surface-subtle);
      scroll-padding-block-start: 36px;
      scrollbar-gutter: stable;
    }

    /* Narrower than sm: a 44 px strip and 10 px padding. */
    @media (width < 576px) {
      .tm-details__strip {
        block-size: 44px;
      }

      .tm-details__band {
        padding: var(--page-header-padding-narrow);
        scroll-padding-block-start: 44px;
      }
    }

    /* Read by assistive technology, never drawn. */
    .tm-details__live {
      position: absolute;
      inline-size: 1px;
      block-size: 1px;
      overflow: hidden;
      clip-path: inset(50%);
      white-space: nowrap;
    }
  `,
  host: { class: 'tm-details' },
})
export class TmDetailsFrame implements OnInit {
  readonly #injector = inject(Injector);
  readonly #l10n = inject(TmLocalization);
  readonly #urls = inject(TmUrls);
  readonly #entities = inject(TmEntities);
  readonly #actions = inject(TmPageActions);

  /** The host's state. */
  protected readonly controller: TmDetailsController<TmJsonObject> = inject(TmDetailsController);

  /** The title's id, which labels the band. */
  protected readonly titleId = `tm-details-title-${nextFrame++}`;

  /** The page actions' ids: `details.*` on an entity's page, `settings.*` on a settings page. */
  protected readonly ids = computed(() => {
    const prefix = this.controller.source().kind;
    return { edit: `${prefix}.edit`, create: `${prefix}.create`, save: `${prefix}.save`, cancel: `${prefix}.cancel`, refresh: `${prefix}.refresh` };
  });

  /** The ids being invoked, which the buttons show busy. */
  protected readonly running = this.#actions.running;

  /** The header's width, which labels the buttons; wide until measured. */
  readonly #width = signal(Number.POSITIVE_INFINITY);

  /** Whether every button is labelled: the header reaches `xl`. */
  protected readonly wide = computed(() => this.#width() >= TM_BREAKPOINTS.xl);

  /** The read's error state's slot. */
  protected readonly errorSlot = viewChild('errorSlot', { read: ViewContainerRef });

  /** The Edit button, which Cancel and Save focus. */
  protected readonly editButton = viewChild<ElementRef<HTMLButtonElement>>('editButton');

  /** Counts the error state's renders, so a superseded one is dropped. */
  #errorRun = 0;

  /** The strings the frame shows, by key under `tm.details`. */
  protected readonly text: (key: string) => string = (key) => this.#l10n.ɵtext(`tm.details.${key}`)();

  /** The record's mode. */
  protected readonly mode = this.controller.mode;

  /** Whether the working copy has changes. */
  protected readonly dirty = computed(() => this.controller.record()?.dirty() ?? false);

  /** The crumb: the entity's plural label, linking to its search page; none on a source page. */
  protected readonly crumb = computed(() => {
    const source = this.controller.source();
    return this.controller.routed && source.kind === 'details'
      ? { label: this.#entities.label(source.entity, true)(), url: this.#urls.search(source.entity) }
      : null;
  });

  /**
   * The Active or Inactive badge's state, on an activatable entity outside
   * create mode: `loading` holds its box while the record loads.
   */
  protected readonly activity = computed<'active' | 'inactive' | 'loading' | null>(() => {
    const descriptor = this.controller.descriptor();
    const record = this.controller.record();
    if (!descriptor.capabilities.has('activatable') || record?.mode() === 'create' || this.controller.status() === 'error') {
      return null;
    }
    const entity = record?.entity();
    if (entity === undefined) {
      return 'loading';
    }
    const json = descriptor.property('IsActive')?.json ?? 'isActive';
    return entity[json] === false ? 'inactive' : 'active';
  });

  /** Whether the record is in view mode. */
  readonly #viewing = computed(() => this.controller.mode() === 'view');

  /** Whether Edit may run now: in view mode, over a record that has landed and may be edited. */
  protected readonly editEnabled = computed(
    () => this.#viewing() && this.controller.status() === 'ready' && (this.controller.record()?.can.edit() ?? false),
  );

  /** Whether Save may run now: in edit or create mode, over a record that has landed, with no write in flight. */
  protected readonly saveEnabled = computed(() => !this.#viewing() && !this.controller.busy() && (this.controller.record()?.landed() ?? false));

  /** Whether New may run now. */
  protected readonly createEnabled = computed(() => this.#viewing() && this.controller.source().can.create());

  /** New's link. */
  protected readonly createHref = computed(() => this.#href(this.#urls.create(this.controller.source().entity)));

  /** Whether the toolbar offers Edit, New and Save: the source writes; an operation the stack lacks offers none. */
  protected readonly writes = computed(() => this.controller.source().kind === 'settings' || this.controller.descriptor().operations.has('save'));

  /** Whether the toolbar offers New: an entity's routed page that writes. */
  protected readonly creates = computed(() => this.writes() && this.controller.routed && this.controller.source().kind === 'details');

  /** The frame's lifetime, which ends its page actions. */
  readonly #destroyRef = inject(DestroyRef);

  /** What the polite status region says: an announcement, else "Loading" while the layout shows skeletons. */
  protected readonly live = computed(() => {
    const said = this.controller.announcement();
    if (said !== '') {
      return said;
    }
    return this.controller.status() === 'loading' && !this.controller.record()?.landed() ? this.text('loading') : '';
  });

  /** Sets the frame up: its icons, the document title, the width and the error state. */
  constructor() {
    const controller = this.controller;
    const icons = inject(TmIconRegistry);
    const missing = Object.entries(ICONS).filter(([name]) => icons.get(name) === undefined);
    if (missing.length > 0) {
      icons.register(Object.fromEntries(missing));
    }

    // --- The document title names the record (spec 0031 §5.1) ---
    const titles = inject(TmTitleStrategy);
    titles.ɵsetPageTitle(this, computed(() => controller.title().text));
    inject(DestroyRef).onDestroy(() => titles.ɵsetPageTitle(this, null));

    // --- Cancel and Save return focus to Edit ---
    controller.ɵfocusEdit = () => this.editButton()?.nativeElement.focus();

    // --- The header's width labels the buttons, as a container query would ---
    const element = inject<ElementRef<HTMLElement>>(ElementRef).nativeElement;
    const destroyRef = inject(DestroyRef);
    afterNextRender(() => {
      const observer = new ResizeObserver(([entry]) => this.#width.set(entry?.contentRect.width ?? element.clientWidth));
      observer.observe(element);
      destroyRef.onDestroy(() => observer.disconnect());
    });

    // --- The read's error state, from the error presenter, loaded on first use ---
    // A page gone before the presenter loads makes its pending run stale.
    destroyRef.onDestroy(() => this.#errorRun++);
    effect(() => {
      const slot = this.errorSlot();
      const error = controller.error();
      untracked(() => void this.#showError(slot, error));
    });
  }

  /**
   * Registers the page actions the toolbar's buttons are views of, once the
   * host's inputs, which name the source, are set.
   */
  ngOnInit(): void {
    // Spec 0031 §10.1: edit, create, save, cancel and refresh; a source that does not write registers Refresh alone.
    const controller = this.controller;
    const ids = untracked(this.ids);
    const viewing = this.#viewing;
    const editing = computed(() => !viewing());
    const register = (action: TmPageActionInput): void => this.#actions.register(action, this.#destroyRef);
    if (untracked(this.writes)) {
      register({
        id: ids.edit,
        labelKey: 'tm.details.edit',
        icon: 'pencil',
        group: 'primary',
        order: 10,
        shortcut: 'F2',
        enabled: this.editEnabled,
        disabledReason: computed(() => (viewing() ? controller.editReason() : null)),
        invoke: () => {
          controller.edit();
          return Promise.resolve({ outcome: 'done' });
        },
      });
      if (untracked(this.creates)) {
        register({
          id: ids.create,
          labelKey: 'tm.details.create',
          icon: 'plus',
          group: 'primary',
          order: 20,
          enabled: this.createEnabled,
          disabledReason: computed(() => (viewing() ? controller.createReason() : null)),
          invoke: async () => {
            await this.#injector.get(Router).navigateByUrl(this.#urls.create(untracked(controller.source).entity));
            return { outcome: 'done' };
          },
        });
      }
      register({
        id: ids.save,
        labelKey: 'tm.details.save',
        group: 'primary',
        order: 10,
        shortcut: 'Mod+S',
        enabled: this.saveEnabled,
        invoke: async () => ((await controller.save()) ? { outcome: 'done' } : { outcome: 'cancelled' }),
      });
      register({
        id: ids.cancel,
        labelKey: 'tm.details.cancel',
        icon: 'x',
        group: 'primary',
        order: 20,
        enabled: computed(() => editing() && !controller.busy()),
        invoke: async () => ((await controller.cancel()) ? { outcome: 'done' } : { outcome: 'cancelled' }),
      });
    }
    register({
      id: ids.refresh,
      labelKey: 'tm.details.refresh',
      icon: 'refresh-cw',
      group: 'primary',
      order: 25,
      enabled: viewing,
      invoke: () => {
        controller.refresh();
        return Promise.resolve({ outcome: 'done' });
      },
    });
  }

  /**
   * Invokes a page action, as every view of one does.
   *
   * @param id - The action's id.
   */
  protected invoke(id: string): void {
    void this.#actions.invoke(id, {}, 'user');
  }

  /**
   * New's click: a plain click runs the action; a click that opens a new tab
   * or window stays the browser's.
   *
   * @param event - The click.
   */
  protected create(event: MouseEvent): void {
    if (event.button !== 0 || event.ctrlKey || event.metaKey || event.shiftKey || event.altKey) {
      return;
    }
    event.preventDefault();
    this.invoke(untracked(this.ids).create);
  }

  /**
   * The link's address of an app URL, as the router writes it.
   *
   * @param url - The app URL.
   * @returns The address.
   */
  #href(url: string): string {
    return this.#injector.get(LocationStrategy).prepareExternalUrl(url);
  }

  /**
   * Renders the error state of a failed read: its view from the presenter,
   * a primary link to the entity's list on `not-found` and `forbidden`, and
   * Try again, which reads again.
   *
   * @param slot - The state's slot, while it renders.
   * @param error - The failure, or `null`.
   */
  async #showError(slot: ViewContainerRef | undefined, error: TmApiError | null): Promise<void> {
    const run = ++this.#errorRun;
    slot?.clear();
    if (slot === undefined || error === null) {
      return;
    }
    const { TmErrorPresenter, TmErrorState } = await import('@tellma/core/errors');
    if (run !== this.#errorRun) {
      return;
    }
    const entity = untracked(this.controller.source).entity;
    const view = this.#injector.get(TmErrorPresenter).describe(error, { kind: 'read', entity });
    const state = slot.createComponent(TmErrorState, { injector: this.#injector });
    state.setInput('view', view);
    // Spec 0031 §3.5: a way out to the list; New and the pager stay live beside it.
    if (untracked(this.crumb) !== null && (error.kind === 'not-found' || error.kind === 'forbidden')) {
      state.setInput('link', { label: this.#entities.label(entity, true), url: this.#urls.search(entity) });
    }
    state.instance.retry.subscribe(() => this.controller.refresh());
  }
}
