// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { NgTemplateOutlet } from '@angular/common';
import {
  type AfterViewInit,
  ChangeDetectionStrategy,
  Component,
  computed,
  contentChild,
  contentChildren,
  DestroyRef,
  Directive,
  effect,
  ElementRef,
  inject,
  input,
  type InputSignal,
  model,
  type ModelSignal,
  type OnInit,
  type Signal,
  TemplateRef,
  untracked,
  viewChild,
} from '@angular/core';
import type { FieldTree } from '@angular/forms/signals';
import { TmCounter } from '@tellma/core-ui/counter';
import { TmTab, TmTabContent, TmTabGroup, TmTabLabel } from '@tellma/core-ui/tabs';
import { type TmJson, TmLocalization } from '@tellma/core';

import { TmDetailsCard, TmDetailsCardSection } from './tm-details-card';
import { injectTmDetailsRecord, tmDevMode } from './tm-details-record';
import { TM_DETAILS_SCOPE, TmDetailsScope } from './tm-details-scope';

// The record's tabs (spec 0031 §6.6), on the component library's
// tm-tab-group: every tab keeps its content once visited, hidden and inert
// while unselected; a tab over a child collection takes its label, a live
// count and an error dot from it; a tab or its fields' error shows a dot
// and ", has errors" in its name, selected or not; and the record
// remembers the selection by the tabs' id. A field's focus() selects its
// tab first.

/**
 * The content of a `tm-details-tab`, rendered on the tab's first visit and
 * kept from then on.
 *
 * @example
 * ```html
 * <tm-details-tab id="lines" collection="Lines">
 *   <ng-template tmDetailsTabContent><tm-field path="Notes" /></ng-template>
 * </tm-details-tab>
 * ```
 */
@Directive({ selector: 'ng-template[tmDetailsTabContent]' })
export class TmDetailsTabContent {
  /** The content's template. */
  readonly template: TemplateRef<unknown> = inject<TemplateRef<unknown>>(TemplateRef);
}

/**
 * A tab of a `tm-details-tabs`: its id, its label and the child
 * collection it shows, and its content in a `tmDetailsTabContent` template.
 *
 * @example
 * ```html
 * <tm-details-tab id="attachments" collection="Attachments">
 *   <ng-template tmDetailsTabContent>…</ng-template>
 * </tm-details-tab>
 * ```
 */
@Component({
  selector: 'tm-details-tab',
  changeDetection: ChangeDetectionStrategy.OnPush,
  providers: [{ provide: TM_DETAILS_SCOPE, useFactory: () => inject(TmDetailsTab).scope }],
  template: ``,
})
export class TmDetailsTab {
  readonly #l10n = inject(TmLocalization);
  readonly #record = injectTmDetailsRecord();
  readonly #tabs = inject(TmDetailsTabs);

  /** The tab's id, unique within its tabs. */
  readonly id: InputSignal<string> = input.required<string>();

  /** The label's key; the collection's label when omitted. */
  readonly labelKey: InputSignal<string | undefined> = input<string | undefined>(undefined);

  /** The child collection the tab shows, such as `Lines`: its label, count, errors and deep-link target. */
  readonly collection: InputSignal<string | undefined> = input<string | undefined>(undefined);

  /** The tab's content. */
  readonly content: Signal<TmDetailsTabContent | undefined> = contentChild(TmDetailsTabContent);

  /** What the tab holds: its fields' errors, and the selection a focused field asks for. */
  readonly scope: TmDetailsScope = new TmDetailsScope(
    inject(TM_DETAILS_SCOPE, { optional: true, skipSelf: true }),
    () => this.#tabs.select(this.id()),
    inject(DestroyRef),
  );

  /** The collection's descriptor, or `undefined`. */
  readonly #child = computed(() => {
    const name = this.collection();
    return name === undefined ? undefined : this.#record.descriptor.child(name);
  });

  /** The label, in the active language. */
  readonly label: Signal<string> = computed(() => {
    const key = this.labelKey();
    return key !== undefined ? this.#l10n.ɵtext(key)() : (this.#child()?.label() ?? this.id());
  });

  /** The collection's row count, live in edit mode; `null` without a collection. */
  readonly count: Signal<number | null> = computed(() => {
    const child = this.#child();
    if (child === undefined) {
      return null;
    }
    const rows = (this.#record.model() as Readonly<Record<string, TmJson | undefined>>)[child.json];
    return Array.isArray(rows) ? rows.length : 0;
  });

  /** Whether the tab holds an error: a field beneath it, or a row of its collection. */
  readonly invalid: Signal<boolean> = computed(() => this.scope.invalid());

  /** Reports the collection's errors to the tab. */
  constructor() {
    // The collection's rows show their errors once touched or dirty, in edit and create mode.
    const rows = computed(() => {
      const child = this.#child();
      const form = this.#record.form as unknown as Readonly<Record<string, FieldTree<unknown> | undefined>>;
      const state = child === undefined ? undefined : form[child.json]?.();
      return this.#record.mode() !== 'view' && state !== undefined && state.invalid() && (state.touched() || state.dirty());
    });
    this.scope.hold(rows, inject(DestroyRef));
  }
}

/**
 * The record's tabs: `tm-details-tab`s on the component library's tab
 * group. Unique by `id` within a layout; inside a `tm-details-card` it is
 * the card's own child, or sits in a `tmDetailsCardSection`, and renders
 * below the field grid.
 *
 * @example
 * ```html
 * <tm-details-card flush>
 *   <tm-details-tabs>
 *     <tm-details-tab id="lines" collection="Lines"><ng-template tmDetailsTabContent>…</ng-template></tm-details-tab>
 *     <tm-details-tab id="attachments" collection="Attachments"><ng-template tmDetailsTabContent>…</ng-template></tm-details-tab>
 *   </tm-details-tabs>
 * </tm-details-card>
 * ```
 */
@Component({
  selector: 'tm-details-tabs',
  imports: [NgTemplateOutlet, TmCounter, TmTab, TmTabContent, TmTabGroup, TmTabLabel],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { class: 'tm-details-tabs' },
  styles: `
    :host {
      display: block;
    }

    .tm-details-tabs__label {
      display: inline-flex;
      align-items: center;
      gap: var(--space-1);
    }

    /* The error dot's reserved box: painted only while the tab holds an error, so it never moves the label. */
    .tm-details-tabs__dot {
      flex: none;
      inline-size: var(--card-dot-size);
      block-size: var(--card-dot-size);
      border-radius: var(--radius-full);
      background: var(--error);
      visibility: hidden;
    }

    .tm-details-tabs__dot--on {
      visibility: visible;
    }

    .tm-details-tabs__name {
      position: absolute;
      inline-size: 1px;
      block-size: 1px;
      overflow: hidden;
      clip-path: inset(50%);
      white-space: nowrap;
    }

    @media (forced-colors: active) {
      .tm-details-tabs__dot {
        forced-color-adjust: none;
        background: CanvasText;
      }
    }
  `,
  template: `
    <tm-tab-group [(selectedId)]="selected">
      @for (tab of tabs(); track tab.id()) {
        <tm-tab [id]="tab.id()" preserveContent>
          <!-- The label: the name (", has errors" for assistive technology while it holds one), the count and the dot. -->
          <ng-template tmTabLabel>
            <span class="tm-details-tabs__label">
              <span [attr.aria-hidden]="tab.invalid() ? 'true' : null">{{ tab.label() }}</span>
              @if (tab.invalid()) {
                <span class="tm-details-tabs__name">{{ errorName(tab) }}</span>
              }
              @if (tab.count() !== null) {
                <tm-counter [value]="tab.count()" />
              }
              <span class="tm-details-tabs__dot" [class.tm-details-tabs__dot--on]="tab.invalid()" aria-hidden="true"></span>
            </span>
          </ng-template>
          <!-- The content, rendered on the first visit and kept. -->
          <ng-template tmTabContent>
            @if (tab.content(); as content) {
              <ng-container [ngTemplateOutlet]="content.template" />
            }
          </ng-template>
        </tm-tab>
      }
    </tm-tab-group>
  `,
})
export class TmDetailsTabs implements OnInit, AfterViewInit {
  readonly #l10n = inject(TmLocalization);
  readonly #record = injectTmDetailsRecord();
  readonly #destroyRef = inject(DestroyRef);

  /** The tabs' id, unique within the layout, under which the record remembers the selection. */
  readonly id: InputSignal<string> = input('main');

  /** The selected tab's id; the remembered one, else the first, when unset. */
  readonly selected: ModelSignal<string | undefined> = model<string | undefined>(undefined);

  /** The tabs. */
  protected readonly tabs = contentChildren(TmDetailsTab);

  /** The tab group. */
  private readonly group = viewChild(TmTabGroup);

  /** The host element. */
  readonly #element: HTMLElement = inject(ElementRef).nativeElement;

  /** The card around the tabs, if any. */
  readonly #card = inject(TmDetailsCard, { optional: true });

  /** Whether the tabs sit in a card but outside its sections, where the card may have projected them into its field grid. */
  readonly #unsectioned: boolean = this.#card !== null && inject(TmDetailsCardSection, { optional: true })?.card !== this.#card;

  /** Remembers the selection. */
  constructor() {
    effect(() => {
      const selected = this.selected();
      const id = this.id();
      if (selected !== undefined) {
        untracked(() => this.#record.view.tabs.update((tabs) => (tabs[id] === selected ? tabs : { ...tabs, [id]: selected })));
      }
    });
  }

  /** Registers the tabs by id, and takes the remembered selection. */
  ngOnInit(): void {
    const id = this.id();
    this.#record.registerTabs(id, { instantiate: () => this.group()?.instantiate() }, this.#destroyRef);
    const remembered = untracked(this.#record.view.tabs)[id];
    if (this.selected() === undefined && remembered !== undefined) {
      this.selected.set(remembered);
    }
  }

  /**
   * Refuses tabs that render in a card's field grid: inside an element of
   * the card's content, or inside a component there. Tabs that are the
   * card's own child render below the grid.
   */
  ngAfterViewInit(): void {
    // The nearest field grid counts only when it is the tabs' own card's, not an outer card's.
    const grid = this.#element.parentElement?.closest('.tm-details-card__grid');
    if (this.#unsectioned && tmDevMode() && grid?.closest('tm-details-card') === this.#element.closest('tm-details-card')) {
      throw new Error(
        'A tm-details-tabs inside a tm-details-card renders in its field grid: make it a child of the card, or mark the element around it tmDetailsCardSection.',
      );
    }
  }

  /**
   * Selects a tab.
   *
   * @param id - The tab's id.
   */
  select(id: string): void {
    this.selected.set(id);
  }

  /**
   * A tab's name while it holds an error.
   *
   * @param tab - The tab.
   * @returns "{label}, has errors".
   */
  protected errorName(tab: TmDetailsTab): string {
    return this.#l10n.ɵtext('tm.details.hasErrors', { label: tab.label() })();
  }
}
