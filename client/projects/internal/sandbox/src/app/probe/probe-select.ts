import {
  afterRenderEffect,
  Component,
  computed,
  input,
  model,
  signal,
  viewChild,
} from '@angular/core';
import { Combobox, ComboboxPopup, ComboboxWidget } from '@angular/aria/combobox';
import { Listbox, Option } from '@angular/aria/listbox';
import { CdkConnectedOverlay, OverlayModule } from '@angular/cdk/overlay';
import type { ConnectedPosition } from '@angular/cdk/overlay';

/**
 * Risk-spike probe implementing the FULL spec §3.4 Select composition before
 * tm-select exists (stage 3 of the implementation plan):
 *
 *   ngCombobox on a non-input <div> trigger
 *   -> cdkConnectedOverlay (usePopover:'inline', matchWidth,
 *      [bottom-start, top-start], disableClose:true)
 *   -> ng-template ngComboboxPopup
 *   -> ngListbox + ngComboboxWidget (focusMode=activedescendant,
 *      selectionMode=explicit), activation-event commit,
 *      updatePosition()-on-attach macrotask.
 *
 * Kept only until the production tm-select (stages 10-11) supersedes it.
 */
@Component({
  selector: 'sandbox-probe-select',
  imports: [Combobox, ComboboxPopup, ComboboxWidget, Listbox, Option, OverlayModule],
  template: `
    <div
      ngCombobox
      #cb="ngCombobox"
      [(expanded)]="expanded"
      class="probe-trigger"
      [attr.data-testid]="testid()"
    >
      {{ display() }}
    </div>

    <ng-template
      [cdkConnectedOverlay]="{
        origin: cb.element,
        usePopover: 'inline',
        matchWidth: true,
        disableClose: true,
        positions: positions,
      }"
      [cdkConnectedOverlayOpen]="expanded()"
      (attach)="onAttach()"
    >
      <ng-template ngComboboxPopup [combobox]="cb">
        <div class="probe-panel" [attr.data-testid]="testid() + '-panel'">
          <ul
            #lb="ngListbox"
            ngListbox
            ngComboboxWidget
            [tabindex]="-1"
            focusMode="activedescendant"
            selectionMode="explicit"
            [(value)]="selected"
            [activeDescendant]="lb.activeDescendant()"
            (click)="commit()"
            (keydown.enter)="commit()"
            (keydown.space)="commit()"
          >
            @for (option of options(); track option) {
              <li ngOption [value]="option" [label]="option">{{ option }}</li>
            }
          </ul>
        </div>
      </ng-template>
    </ng-template>
  `,
  styles: `
    :host {
      /* An inline host wrapping the block trigger hit-tests ABOVE the trigger
         in Chromium (found by the stage-3 spike) — clicks would land on the
         host, never the trigger. Block display is mandatory. */
      display: block;
      inline-size: fit-content;
    }
    .probe-trigger {
      display: flex;
      align-items: center;
      inline-size: 220px;
      block-size: 38px;
      padding-inline: 12px;
      border: 1px solid #cbd6d9;
      border-radius: 6px;
      background: #fefefe;
      cursor: pointer;
      user-select: none;
    }
    .probe-trigger:focus-visible {
      outline: 2px solid #3e899d;
      outline-offset: 2px;
    }
    .probe-panel {
      box-sizing: border-box;
      inline-size: 100%;
      max-block-size: 200px;
      overflow: auto;
      border: 1px solid #cbd6d9;
      border-radius: 6px;
      background: #fefefe;
      box-shadow: 0 4px 12px rgba(0, 23, 34, 0.08);
    }
    .probe-panel ul {
      margin: 0;
      padding: 4px;
      list-style: none;
    }
    .probe-panel li {
      padding: 8px 12px;
      border-radius: 4px;
      cursor: pointer;
    }
    .probe-panel li[data-active='true'] {
      outline: 2px solid #3e899d;
      outline-offset: -2px;
    }
    .probe-panel li[aria-selected='true'] {
      background: #eaf4f7;
    }
  `,
})
export class ProbeSelect {
  readonly testid = input('probe');
  readonly options = input<readonly string[]>(['Alpha', 'Beta', 'Gamma', 'Delta', 'Epsilon']);

  protected readonly expanded = signal(false);
  protected readonly selected = model<string[]>([]);
  protected readonly display = computed(() => this.selected()[0] ?? 'Choose an option');

  protected readonly positions: ConnectedPosition[] = [
    { originX: 'start', originY: 'bottom', overlayX: 'start', overlayY: 'top' },
    { originX: 'start', originY: 'top', overlayX: 'start', overlayY: 'bottom' },
  ];

  private readonly overlay = viewChild(CdkConnectedOverlay);
  private readonly listbox = viewChild(Listbox);

  constructor() {
    afterRenderEffect(() => {
      this.listbox()?.scrollActiveItemIntoView();
    });
  }

  protected onAttach(): void {
    // DeferredContent inserts the panel one render pass after CDK attaches and
    // measures, so flip would otherwise measure a zero-height panel; a
    // macrotask (not afterNextRender/microtask) is required per the spike.
    setTimeout(() => this.overlay()?.overlayRef?.updatePosition());
  }

  protected commit(): void {
    this.expanded.set(false);
  }
}
