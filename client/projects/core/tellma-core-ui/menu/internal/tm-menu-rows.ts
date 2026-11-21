// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { NgTemplateOutlet } from '@angular/common';
import {
  afterRenderEffect,
  Component,
  computed,
  Directive,
  forwardRef,
  inject,
  input,
  isDevMode,
  type Signal,
  TemplateRef,
  untracked,
  viewChild,
  ViewEncapsulation,
} from '@angular/core';
import { Menu, MenuItem } from '@angular/aria/menu';
import { CdkConnectedOverlay, OverlayModule } from '@angular/cdk/overlay';
import { Check, ChevronRight, ExternalLink } from 'lucide';

import { TM_UI_TRANSLATE } from '@tellma/core-ui';
import { TmIcon, type TmIconSource } from '@tellma/core-ui/icon';
import { TmKbd } from '@tellma/core-ui/kbd';
import { tmCreateAnchoredOverlay, tmLogicalPositions } from '@tellma/core-ui/overlay';

import type {
  TmMenuEntry,
  TmMenuGroup,
  TmMenuItem,
  TmMenuLink,
  TmMenuSeparator,
  TmMenuSubmenu,
} from '../tm-menu-entries';

/** An entry that renders as one focusable row. */
export type ɵTmMenuRow = TmMenuItem | TmMenuLink | TmMenuSubmenu;

/** Whether an entry is a separator. */
export function ɵtmIsMenuSeparator(entry: TmMenuEntry): entry is TmMenuSeparator {
  return 'separator' in entry;
}

/** Whether an entry is a group of rows. */
export function ɵtmIsMenuGroup(entry: TmMenuEntry): entry is TmMenuGroup {
  return 'group' in entry;
}

/** Whether a row is a link. */
export function ɵtmIsMenuLink(row: ɵTmMenuRow): row is TmMenuLink {
  return 'href' in row;
}

/**
 * What a row needs from the menu it belongs to, whichever level it sits
 * on: the one place an activation is carried out.
 */
export abstract class ɵTmMenuHost {
  /**
   * Carries out the activation of an item or a link: runs its action,
   * reports an item, and closes the whole menu unless the item keeps it
   * open.
   */
  abstract activate(entry: TmMenuItem | TmMenuLink): void;
}

/** Everything the row template reads about one row, derived once per entry. */
interface TmMenuRowVm {
  /** The entry itself; also the value the row carries in the aria menu. */
  readonly entry: ɵTmMenuRow;
  /** The entry when it is a plain or checkable item. */
  readonly item?: TmMenuItem;
  /** The entry when it is a link. */
  readonly link?: TmMenuLink;
  /** The entry when it opens a submenu. */
  readonly submenu?: TmMenuSubmenu;
  /** The label, literal or translated. */
  readonly label: Signal<string>;
  /** Whether activation does nothing: disabled, with or without a reason. */
  readonly inert: boolean;
  /** The muted second line: the reason when there is one, else the description. */
  readonly secondLine?: string;
  /** The id of the second line's element, which describes the row; absent without one. */
  readonly descriptionId?: string;
  /** The row's ARIA role. */
  readonly role: 'menuitem' | 'menuitemcheckbox' | 'menuitemradio';
  /** `aria-checked` of a checkable row; `null` on every other row. */
  readonly checked: 'true' | 'false' | null;
  /** A registry glyph name, when the icon is one. */
  readonly iconName?: string;
  /** Icon data drawn directly, when the icon is that. */
  readonly iconSvg?: TmIconSource;
  /** An icon template, when the icon is one. */
  readonly iconTemplate?: TemplateRef<void>;
}

/** Counts row sets, so the ids each one mints stay unique in the document. */
let nextRowsId = 0;

/** Whether a key or pointer event carries any modifier. */
function hasModifier(event: KeyboardEvent | MouseEvent): boolean {
  return event.ctrlKey || event.metaKey || event.altKey || event.shiftKey;
}

/**
 * Marks the element that carries one level's aria menu, and keeps that
 * level navigable when every row in it is disabled.
 *
 * @internal Used by `TmMenu` and its submenus only.
 */
@Directive({ selector: '[tmMenuList]' })
export class ɵTmMenuList {
  constructor() {
    // The aria list treats a level whose rows are ALL disabled as a disabled
    // list: nothing in it takes focus and the arrow keys do nothing. A menu
    // whose every action is unavailable must still be readable row by row
    // (each row may carry the reason it is unavailable), so the list is
    // never reported disabled. This runs as the directive is created, before
    // aria computes a single tabindex from the answer.
    const menu = inject<Menu<ɵTmMenuRow>>(Menu, { self: true });
    menu._pattern.listBehavior.focusBehavior.isListDisabled = (): boolean => false;
  }
}

/**
 * The rows of one menu level: items, links, submenu parents, groups and
 * separators, each row an aria menu item of the `ngMenu` element this
 * component sits in.
 *
 * @internal Rendered by `TmMenu` and its submenus only.
 */
@Component({
  selector: 'tm-menu-rows',
  imports: [MenuItem, NgTemplateOutlet, TmIcon, TmKbd, forwardRef(() => ɵTmMenuSubmenu)],
  // Section notes (kept out of the markup, which ships in the bundle):
  // - The @for walks the entries: a separator, a group (its divider, its
  //   heading and its rows) or a single row.
  // - #row stamps one row in one of three shapes: a link (<a>), a submenu
  //   parent (followed by its submenu), or an item.
  // - #content is what every shape holds: the icon box, the label over its
  //   second line, then the trailing detail, new-tab glyph, shortcut caps
  //   and submenu chevron. A template's context is untyped, so it passes
  //   the row's view model through typed() to be checked like the rest.
  template: `
    @for (entry of entries(); track $index; let i = $index) {
      @if (isSeparator(entry)) {
        <div class="tm-menu__separator" role="separator"></div>
      } @else if (isGroup(entry)) {
        @if (needsDivider(i)) {
          <div class="tm-menu__separator" role="separator"></div>
        }
        @let heading = headingOf(entry)();
        @let headingId = heading ? uid + '-g' + i : null;
        <div class="tm-menu__group" role="group" [attr.aria-labelledby]="headingId">
          @if (heading) {
            <div class="tm-menu__heading" [id]="headingId">{{ heading }}</div>
          }
          @for (child of entry.items; track $index) {
            <ng-container
              [ngTemplateOutlet]="row"
              [ngTemplateOutletContext]="{ $implicit: child, grouped: true }"
            />
          }
        </div>
      } @else {
        <ng-container [ngTemplateOutlet]="row" [ngTemplateOutletContext]="{ $implicit: entry }" />
      }
    }

    <ng-template #row let-entry let-grouped="grouped">
      @let vm = vmOf(entry, grouped);
      @if (vm.link; as link) {
        <a
          ngMenuItem
          class="tm-menu__item"
          rel="noopener"
          [value]="vm.entry"
          [disabled]="vm.inert"
          [searchTerm]="vm.label()"
          [attr.href]="vm.inert ? null : link.href"
          [attr.target]="link.target ?? null"
          [attr.aria-label]="nameOf(vm)"
          [attr.aria-describedby]="vm.descriptionId"
          (click)="onLinkClick(vm, $event)"
          (keydown)="onLinkKeydown(vm, $event)"
        >
          <ng-container
            [ngTemplateOutlet]="content"
            [ngTemplateOutletContext]="{ $implicit: vm }"
          />
        </a>
      } @else if (vm.submenu; as submenu) {
        <div
          ngMenuItem
          #parent="ngMenuItem"
          class="tm-menu__item"
          [value]="vm.entry"
          [disabled]="vm.inert"
          [searchTerm]="vm.label()"
          [submenu]="sub.menu()"
          [attr.aria-label]="nameOf(vm)"
          [attr.aria-describedby]="vm.descriptionId"
          (keydown)="onParentKeydown(parent, sub, $event)"
        >
          <ng-container
            [ngTemplateOutlet]="content"
            [ngTemplateOutletContext]="{ $implicit: vm }"
          />
        </div>
        <tm-menu-submenu #sub [entries]="submenu.items" [parent]="parent" [label]="vm.label()" />
      } @else {
        <div
          ngMenuItem
          #item="ngMenuItem"
          class="tm-menu__item"
          [value]="vm.entry"
          [disabled]="vm.inert"
          [searchTerm]="vm.label()"
          [role]="vm.role"
          [attr.aria-checked]="vm.checked"
          [attr.lang]="vm.item?.lang ?? null"
          [attr.aria-label]="nameOf(vm)"
          [attr.aria-describedby]="vm.descriptionId"
          (click)="onItemClick(vm, item, $event)"
          (keydown)="onItemKeydown(vm, item, $event)"
        >
          <ng-container
            [ngTemplateOutlet]="content"
            [ngTemplateOutletContext]="{ $implicit: vm }"
          />
        </div>
      }
    </ng-template>

    <ng-template #content let-row>
      @let vm = typed(row);
      <span class="tm-menu__icon" aria-hidden="true">
        @if (vm.checked !== null) {
          @if (vm.checked === 'true') {
            <tm-icon [svg]="checkGlyph" />
          }
        } @else if (vm.iconTemplate; as template) {
          <ng-container [ngTemplateOutlet]="template" />
        } @else if (vm.iconName || vm.iconSvg) {
          <tm-icon [name]="vm.iconName" [svg]="vm.iconSvg" />
        }
      </span>
      <span class="tm-menu__text">
        <span class="tm-menu__label">{{ vm.label() }}</span>
        @if (vm.secondLine; as secondLine) {
          <span class="tm-menu__description" aria-hidden="true" [id]="vm.descriptionId">{{
            secondLine
          }}</span>
        }
      </span>
      @if (vm.entry.detail; as detail) {
        <span class="tm-menu__detail">{{ detail }}</span>
      }
      @if (vm.link?.target) {
        <tm-icon class="tm-menu__external" [svg]="externalGlyph" />
      }
      @if (vm.entry.shortcut; as shortcut) {
        <tm-kbd class="tm-menu__shortcut" aria-hidden="true" [keys]="shortcut" />
      }
      @if (vm.submenu) {
        <tm-icon class="tm-menu__chevron" [svg]="chevronGlyph" />
      }
    </ng-template>
  `,
  // One stylesheet (tm-menu.css, loaded by TmMenu) styles every level, so
  // this component carries none and must not scope its markup.
  encapsulation: ViewEncapsulation.None,
  host: { class: 'tm-menu-rows' },
})
export class ɵTmMenuRows {
  private readonly translate = inject(TM_UI_TRANSLATE);
  private readonly host = inject(ɵTmMenuHost);
  /** The aria menu these rows belong to: the root's or a submenu's. */
  private readonly menu = inject<Menu<ɵTmMenuRow>>(Menu);

  /** The entries of this level, in display order. */
  readonly entries = input.required<readonly TmMenuEntry[]>();
  /** Whether these are a submenu's rows, which cannot open a submenu of their own. */
  readonly nested = input(false);

  /** Prefix of the ids this row set mints (descriptions and group headings). */
  protected readonly uid = `tm-menu-rows-${nextRowsId++}`;
  /** Counts the description ids minted so far. */
  private nextDescriptionId = 0;

  /** The check mark of a checked checkbox or radio item. */
  protected readonly checkGlyph: TmIconSource = Check;
  /** The glyph of a link that opens in a new tab. */
  protected readonly externalGlyph: TmIconSource = ExternalLink;
  /** The submenu chevron; it points along the reading direction. */
  protected readonly chevronGlyph: TmIconSource = { node: ChevronRight, mirrorInRtl: true };

  /** What assistive technology reads after the name of a new-tab link. */
  protected readonly opensInNewTab = this.translate('menu.opensInNewTab');

  /** Row view models, cached by entry reference (entries are immutable data). */
  private readonly vms = new WeakMap<ɵTmMenuRow, TmMenuRowVm>();
  /** Group heading signals, cached by group reference. */
  private readonly headings = new WeakMap<TmMenuGroup, Signal<string>>();

  /** Discriminates separator entries in the template. */
  protected readonly isSeparator = ɵtmIsMenuSeparator;
  /** Discriminates group entries in the template. */
  protected readonly isGroup = ɵtmIsMenuGroup;

  /**
   * Whether the group at `index` draws a divider above itself: whenever
   * something other than a separator precedes it.
   */
  protected needsDivider(index: number): boolean {
    return index > 0 && !ɵtmIsMenuSeparator(this.entries()[index - 1]);
  }

  /** A group's heading, literal or translated; empty when it has none. */
  protected headingOf(group: TmMenuGroup): Signal<string> {
    let heading = this.headings.get(group);
    if (heading === undefined) {
      heading = this.labelSignal(group);
      this.headings.set(group, heading);
    }
    return heading;
  }

  /**
   * The accessible name of a row whose name is more than its label: the
   * label, a pause and the detail ("Language, English"), then the note that
   * a link opens in a new tab ("Documentation (opens in a new tab)").
   * Spelled out because the name a browser builds from the row's content
   * runs the label and the detail together with no pause, and because the
   * note has no visible text. `null` leaves every other row to be named by
   * its content.
   */
  protected nameOf(vm: TmMenuRowVm): string | null {
    const detail = vm.entry.detail;
    const newTab = vm.link?.target !== undefined;
    if (!detail && !newTab) {
      return null;
    }
    const name = detail ? `${vm.label()}, ${detail}` : vm.label();
    return newTab ? `${name} ${this.opensInNewTab()}` : name;
  }

  /** Gives the content template's untyped context value its type back. */
  protected typed(vm: TmMenuRowVm): TmMenuRowVm {
    return vm;
  }

  /** The view model of one row; `grouped` says whether it sits in a group. */
  protected vmOf(entry: ɵTmMenuRow, grouped = false): TmMenuRowVm {
    let vm = this.vms.get(entry);
    if (vm === undefined) {
      vm = this.buildVm(entry, grouped);
      this.vms.set(entry, vm);
    }
    return vm;
  }

  /**
   * An item that keeps the menu open is activated here rather than by the
   * aria menu, whose own activation closes the submenu the row sits in.
   */
  protected onItemClick(vm: TmMenuRowVm, item: MenuItem<ɵTmMenuRow>, event: MouseEvent): void {
    if (vm.item?.keepOpen !== true || vm.inert) {
      return;
    }
    event.stopPropagation();
    // The aria click handler is what marks a clicked row active; a tap has
    // no hover to have done it already.
    this.menu._pattern.listBehavior.goto(item._pattern);
    this.host.activate(vm.item);
  }

  /** Enter and Space on an item that keeps the menu open (see `onItemClick`). */
  protected onItemKeydown(vm: TmMenuRowVm, item: MenuItem<ɵTmMenuRow>, event: KeyboardEvent): void {
    if (vm.item?.keepOpen !== true || vm.inert || !this.isActivationKey(event)) {
      return;
    }
    event.preventDefault();
    event.stopPropagation();
    // A held key repeats. Like every other row, this one answers the press
    // once: the repeats are consumed and nothing more.
    if (event.repeat) {
      return;
    }
    this.menu._pattern.listBehavior.goto(item._pattern);
    this.host.activate(vm.item);
  }

  /**
   * ArrowDown and ArrowUp on a parent row whose submenu has opened but is
   * not on screen yet. Focus cannot enter a panel before the render that
   * displays it, so a key pressed in that moment still reaches the parent
   * row, where it would move the focus along this level and collapse the
   * submenu that was just opened. The key is dropped instead.
   */
  protected onParentKeydown(
    parent: MenuItem<ɵTmMenuRow>,
    sub: ɵTmMenuSubmenu,
    event: KeyboardEvent,
  ): void {
    if (
      parent.expanded() === true &&
      !sub.shown &&
      (event.key === 'ArrowDown' || event.key === 'ArrowUp')
    ) {
      event.preventDefault();
      event.stopPropagation();
    }
  }

  /**
   * A click on a link row. A plain click reaches the aria menu, which
   * reports the activation; the browser follows the link itself unless the
   * link has an action to run instead.
   */
  protected onLinkClick(vm: TmMenuRowVm, event: MouseEvent): void {
    if (vm.inert) {
      return;
    }
    if (hasModifier(event)) {
      // The browser's own gesture (a new tab, a new window, a download):
      // leave it alone and keep it from the menu, which would run the
      // in-place action and close.
      event.stopPropagation();
      return;
    }
    if (vm.link?.action !== undefined) {
      event.preventDefault();
    }
  }

  /** Enter and Space on a link row, both routed through the link's click. */
  protected onLinkKeydown(vm: TmMenuRowVm, event: KeyboardEvent): void {
    if (vm.inert || !this.isActivationKey(event)) {
      return;
    }
    // The aria menu would consume the key, and the link would never be
    // followed. Enter is left to the browser, which turns it into a click
    // on the link; Space, which a link does not answer natively, clicks it.
    event.stopPropagation();
    if (event.repeat) {
      // A held key follows the link once, on the press: cancelling the
      // repeat keeps the browser from clicking the link again.
      event.preventDefault();
    } else if (event.key === ' ') {
      event.preventDefault();
      (event.currentTarget as HTMLElement).click();
    }
  }

  /**
   * Whether a key press activates the focused row: an unmodified Enter, or
   * an unmodified Space that is not part of a typeahead search ("save as").
   */
  private isActivationKey(event: KeyboardEvent): boolean {
    if (hasModifier(event)) {
      return false;
    }
    return (
      event.key === 'Enter' || (event.key === ' ' && !this.menu._pattern.listBehavior.isTyping())
    );
  }

  /** The label signal of a row or a group: literal, or resolved through the i18n seam. */
  private labelSignal(entry: {
    readonly label?: string;
    readonly labelKey?: string;
  }): Signal<string> {
    return entry.labelKey !== undefined
      ? this.translate(entry.labelKey)
      : computed(() => entry.label ?? '');
  }

  /** Derives a row's view model and, in development, reports misuse of the entry. */
  private buildVm(entry: ɵTmMenuRow, grouped: boolean): TmMenuRowVm {
    let submenu = 'items' in entry ? entry : undefined;
    const link = ɵtmIsMenuLink(entry) ? entry : undefined;
    const item = 'items' in entry || ɵtmIsMenuLink(entry) ? undefined : entry;
    let inert = entry.disabled === true || entry.disabledReason !== undefined;

    // Submenus go one level deep. A submenu's row that asks for its own is
    // drawn as an unavailable row rather than opening a third level.
    if (submenu !== undefined && untracked(this.nested)) {
      if (isDevMode()) {
        console.error(
          `tm-menu: a submenu cannot hold another submenu; "${entry.id}" is shown disabled.`,
        );
      }
      submenu = undefined;
      inert = true;
    }

    if (isDevMode()) {
      if (entry.disabled === true && entry.disabledReason !== undefined) {
        console.warn(`tm-menu: "${entry.id}" sets both disabled and disabledReason; set one.`);
      }
      if (item?.role === 'radio' && !grouped) {
        console.warn(`tm-menu: the radio item "${entry.id}" must sit in a { group: true } entry.`);
      }
    }

    const icon = entry.icon;
    const secondLine = entry.disabledReason ?? item?.description;
    return {
      entry,
      item,
      link,
      submenu,
      label: this.labelSignal(entry),
      inert,
      secondLine,
      descriptionId: secondLine ? `${this.uid}-${this.nextDescriptionId++}` : undefined,
      role:
        item?.role === 'checkbox'
          ? 'menuitemcheckbox'
          : item?.role === 'radio'
            ? 'menuitemradio'
            : 'menuitem',
      checked: item?.role === undefined ? null : item.checked === true ? 'true' : 'false',
      iconName: typeof icon === 'string' ? icon : undefined,
      iconTemplate: icon instanceof TemplateRef ? icon : undefined,
      iconSvg:
        icon === undefined || typeof icon === 'string' || icon instanceof TemplateRef
          ? undefined
          : icon,
    };
  }
}

/**
 * One submenu: a second overlay anchored at the inline end of its parent
 * row, holding its own aria menu. The overlay stays attached for as long
 * as the parent row exists, because the row needs the submenu's aria menu
 * to know it has one; the panel is simply not displayed until the row
 * expands.
 *
 * @internal Rendered by `ɵTmMenuRows` only.
 */
@Component({
  selector: 'tm-menu-submenu',
  imports: [Menu, OverlayModule, ɵTmMenuList, ɵTmMenuRows],
  template: `
    <ng-template
      [cdkConnectedOverlay]="anchored.overlayConfig()"
      [cdkConnectedOverlayOpen]="true"
      (attach)="anchored.handleAttach()"
      (detach)="anchored.handleDetach()"
    >
      <div class="tm-menu__panel tm-menu__panel--sub" [class.tm-menu__panel--closed]="!open()">
        <div ngMenu tmMenuList class="tm-menu__list" [attr.aria-label]="label()">
          <tm-menu-rows [nested]="true" [entries]="entries()" />
        </div>
      </div>
    </ng-template>
  `,
  encapsulation: ViewEncapsulation.None,
  host: { class: 'tm-menu-submenu' },
})
export class ɵTmMenuSubmenu {
  /** The submenu's entries, in display order. */
  readonly entries = input.required<readonly TmMenuEntry[]>();
  /** The row that opens this submenu. */
  readonly parent = input.required<MenuItem<ɵTmMenuRow>>();
  /** The parent row's label, which names the submenu. */
  readonly label = input.required<string>();

  /** The submenu's aria menu, once its overlay has rendered. */
  readonly menu = viewChild<Menu<ɵTmMenuRow>>(Menu);

  private readonly overlay = viewChild(CdkConnectedOverlay);

  /**
   * Whether the panel is on screen: whether the parent row was expanded as
   * of the last render. For one render after the row expands the panel is
   * open but not shown, and cannot take focus yet.
   */
  shown = false;

  /** Whether the parent row is expanded, which is when the panel shows. */
  protected readonly open = computed(() => this.parent().expanded() === true);

  /**
   * Inline-end of the parent row with the tops aligned, flipping to the
   * inline-start side and pushed back on screen where neither fits. The
   * CDK resolves the sides against the live direction on every measure.
   */
  protected readonly anchored = tmCreateAnchoredOverlay({
    overlay: () => this.overlay(),
    origin: () => this.parent().element,
    positions: tmLogicalPositions('inline-end', 'start'),
    keepOnScreen: true,
    viewportMargin: 8,
  });

  constructor() {
    // The overlay was placed while the panel was not displayed, so it is
    // measured again each time the panel shows: after the render that
    // displays it, before the frame paints.
    afterRenderEffect(() => {
      this.shown = this.open();
      if (this.shown) {
        this.anchored.reanchor();
      }
    });
  }
}
