// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { Directionality } from '@angular/cdk/bidi';
import { NgTemplateOutlet } from '@angular/common';
import {
  afterRenderEffect,
  booleanAttribute,
  Component,
  computed,
  DestroyRef,
  effect,
  type ElementRef,
  inject,
  input,
  model,
  output,
  signal,
  untracked,
  viewChild,
} from '@angular/core';
import { TM_ACTIVE_LOCALE, TM_UI_TRANSLATE, ɵtmRegisterUiStrings } from '@tellma/core-ui';
import { TmBadge, type TmBadgeTone } from '@tellma/core-ui/badge';
import { TmGrid, TmGridColumn, type TmGridRowActivation } from '@tellma/core-ui/grid';
import { tmIsApplePlatform } from '@tellma/core-ui/kbd';
import { tmFormatNumber } from '@tellma/core-ui/l10n';
import { ɵTmChartEnvironment } from './internal/chart-environment';
import {
  ɵTM_CHART_LABEL_SLOT,
  ɵTM_CHART_SHARE_SLOT,
  ɵTM_CHART_VALUE_SLOT,
  type ɵTmChartEngine,
  type ɵTmChartPointerModifiers,
  type ɵTmChartRenderRequest,
} from './internal/chart-plot';
import { TM_CHART_STRINGS_EN } from './tm-chart-strings';
import type {
  TmChartMember,
  TmChartPointActivation,
  TmChartSpec,
  TmChartThreshold,
  TmChartThresholdTone,
} from './tm-chart-types';

/** What the chart shows: the host's no-data state, the card, the table view, or the plot. */
type ChartView = 'empty' | 'card' | 'table' | 'plot';

/** One row of the table view: a point's members and value. */
interface TableRow {
  /** The row's identity: the point's position among the points. */
  readonly id: number;
  /** The point's member indexes, as a drill reports them. */
  readonly members: readonly [number | null, number | null];
  /** The first dimension's member label. */
  readonly d0: string;
  /** The second dimension's member label. */
  readonly d1: string;
  /** The value. */
  readonly value: number;
}

/** The types the engine draws. */
const PLOT_TYPES: ReadonlySet<string> = new Set(['column', 'bar', 'line', 'area', 'pie', 'doughnut']);

/** The badge tone of each threshold tone. */
const BADGE_TONES: Readonly<Record<TmChartThresholdTone, TmBadgeTone>> = {
  positive: 'success',
  warning: 'warning',
  negative: 'error',
};

/** Numbers the per-instance ids, so two charts never share a description or a grid's state. */
let nextInstance = 0;

/** A member's parts, each its own run in the table view, or `null` for the label alone. */
function partsOf(member: TmChartMember | undefined): readonly string[] | null {
  return member?.parts !== undefined && member.parts.length > 0 ? member.parts : null;
}

/**
 * The threshold a card's value reaches: one on the far side of zero that
 * the value meets or passes, the farthest from zero when several are.
 */
function reachedThreshold(
  value: number,
  thresholds: readonly TmChartThreshold[],
): TmChartThreshold | null {
  let reached: TmChartThreshold | null = null;
  for (const threshold of thresholds) {
    const meets = threshold.value >= 0 ? value >= threshold.value : value <= threshold.value;
    if (meets && (reached === null || Math.abs(threshold.value) > Math.abs(reached.value))) {
      reached = threshold;
    }
  }
  return reached;
}

/**
 * A chart from a JSON spec: columns, bars, lines, areas, a pie, a doughnut,
 * or a card showing one value. Every type but the card is drawn on a canvas
 * by an engine that loads on the first such spec the page shows; the card
 * and the table view are markup and never load it.
 *
 * The plot never mirrors: its value axis stays at the physical left and its
 * categories run left to right (bars top to bottom); its text, the legend's
 * side and order, and the tooltip follow the page's direction. Colours and
 * the font come from the `chart` tokens, read again whenever the theme,
 * the direction or the locale changes. A spec without a value shows the
 * content projected into the chart (the host's no-data state); a spec the
 * engine cannot draw shows as a table.
 *
 * Give the chart a block size; it defaults to the `chart` height token (a
 * card takes its content's height). Validate a spec from outside the code
 * with {@link tmValidateChartSpec} before showing it.
 *
 * @tmGroup collection
 * @tmA11yNotes The plot is a `figure` holding an element with `role="img"`,
 *   out of the tab order, named "{title}, {type} chart" and described by
 *   its point count and value range, which ends by pointing at the table
 *   view. `asTable` swaps the plot for a readonly `tm-grid` with one row
 *   per point, its members and formatted value: the keyboard path, where
 *   Enter on a row drills (with Ctrl or ⌘: a new tab). Pointer users hover
 *   a point for its tooltip and click it to drill (Ctrl or ⌘, or the middle
 *   button: a new tab). The card is a button when it drills. Marks meet
 *   3:1 against the plot's ground, text uses the text tokens, and series
 *   that colour alone would separate carry patterns, which every chart
 *   shows under forced colours or a request for more contrast.
 */
@Component({
  selector: 'tm-chart',
  imports: [NgTemplateOutlet, TmBadge, TmGrid, TmGridColumn],
  template: `
    @switch (view()) {
      @case ('empty') {
        <!-- No value: the host's no-data state. -->
        <ng-content />
      }
      @case ('card') {
        <!-- The card: the value over the measure's label, a button when it drills. -->
        <div class="tm-chart__card" role="group" [attr.aria-label]="spec().title">
          @if (activatable()) {
            <button
              type="button"
              class="tm-chart__card-body tm-chart__card-body--action"
              (click)="onCardClick($event)"
              (auxclick)="onCardAuxClick($event)"
            >
              <ng-container [ngTemplateOutlet]="cardBody" />
            </button>
          } @else {
            <div class="tm-chart__card-body">
              <ng-container [ngTemplateOutlet]="cardBody" />
            </div>
          }
        </div>
      }
      @case ('table') {
        <!-- The table view: a readonly grid, one row per point. -->
        <div class="tm-chart__table" role="group" [attr.aria-label]="spec().title">
          <tm-grid
            [gridId]="tableGridId()"
            [data]="tableRows()"
            [rowId]="rowId"
            [rowActivation]="activatable() ? 'row' : 'none'"
            (rowActivate)="onRowActivate($event)"
          >
            @if (spec().dimensions[0]; as dimension) {
              <tm-grid-column
                key="d0"
                [header]="dimension.label"
                [flex]="1"
                [formatParts]="firstParts"
              />
            }
            @if (spec().dimensions[1]; as dimension) {
              <tm-grid-column
                key="d1"
                [header]="dimension.label"
                [flex]="1"
                [formatParts]="secondParts"
              />
            }
            <tm-grid-column
              key="value"
              type="number"
              [header]="spec().measure.label"
              [format]="formatValue"
              [width]="140"
            />
          </tm-grid>
        </div>
      }
    }

    <!-- The plot: kept while the table view shows, so the engine lives until destroy. -->
    @if (drawable()) {
      <figure class="tm-chart__figure" dir="ltr" [hidden]="view() !== 'plot'">
        <div
          #plot
          class="tm-chart__plot"
          role="img"
          [class.tm-chart__plot--rtl]="direction() === 'rtl'"
          [attr.aria-label]="name()"
          [attr.aria-describedby]="descriptionId"
        ></div>
        <span hidden [id]="descriptionId">{{ description() }}</span>
      </figure>
    }

    <!-- The card's content, inside its button or its plain box. -->
    <ng-template #cardBody>
      <span class="tm-chart__card-value" dir="ltr">{{ cardValue() }}</span>
      <span class="tm-chart__card-label">{{ spec().measure.label }}</span>
      @if (cardThreshold(); as threshold) {
        <tm-badge class="tm-chart__card-badge" [tone]="threshold.tone">
          <span dir="ltr">{{ threshold.text }}</span>
        </tm-badge>
      }
    </ng-template>
  `,
  styleUrl: './tm-chart.css',
  host: {
    class: 'tm-chart',
    '[class.tm-chart--card]': 'view() === "card"',
  },
})
export class TmChart {
  // ---- inputs and outputs ----

  /** The chart to show, as JSON. */
  readonly spec = input.required<TmChartSpec>();

  /** Whether the table view replaces the plot; the host's toggle writes it. */
  readonly asTable = model(false);

  /**
   * The table view's grid id, under which the grid remembers its column
   * widths. Without one, each chart takes its own, and widths are not
   * remembered across instances.
   */
  readonly gridId = input<string | undefined>(undefined);

  /** Whether points, the card and table rows drill, emitting `pointActivate`. Defaults to `true`. */
  readonly activatable = input(true, { transform: booleanAttribute });

  /** Emits when the user opens a point, the card or a table row. */
  readonly pointActivate = output<TmChartPointActivation>();

  // ---- services and state ----

  /** The library's strings in the active language. */
  private readonly translate = inject(TM_UI_TRANSLATE);
  /** The formatting locale. */
  private readonly locale = inject(TM_ACTIVE_LOCALE);
  /** The theme and contrast settings every chart redraws on. */
  private readonly environment = inject(ɵTmChartEnvironment);
  /** The plot element, while the plot is in the DOM. */
  private readonly plot = viewChild<ElementRef<HTMLElement>>('plot');
  /** The plot element's size, which a redraw follows. */
  private readonly size = signal({ width: 0, height: 0 }, {
    equal: (a, b) => a.width === b.width && a.height === b.height,
  });
  /** Set when the engine failed to load: the chart falls back to the table view. */
  private readonly engineFailed = signal(false);
  /** The engine instance, once loaded, and the element it draws in. */
  private engine: { readonly instance: ɵTmChartEngine; readonly element: HTMLElement } | null = null;
  /** The pending engine load, if any. */
  private loading: Promise<ɵTmChartEngine | null> | null = null;
  /** Set once the chart is destroyed, so a late engine load is released at once. */
  private destroyed = false;
  /** This instance's number. */
  private readonly instance = ++nextInstance;

  /** The page's direction. */
  protected readonly direction = inject(Directionality).valueSignal;
  /** The id of the plot's description. */
  protected readonly descriptionId = `tm-chart-${this.instance}-description`;
  /** The table rows' identity. */
  protected readonly rowId = (row: TableRow): number => row.id;

  // ---- derived state ----

  /** The points: the rows that hold a value. */
  private readonly points = computed(() =>
    this.spec().rows.filter(
      (row): row is readonly [number | null, number | null, number] =>
        typeof row[2] === 'number' && Number.isFinite(row[2]),
    ),
  );

  /** Whether the engine can draw the spec: a plot type with something to draw. */
  protected readonly drawable = computed(() => {
    const spec = this.spec();
    if (!PLOT_TYPES.has(spec.type)) {
      return false;
    }
    // A pie draws only its positive slices.
    const pie = spec.type === 'pie' || spec.type === 'doughnut';
    return this.points().some((row) => !pie || row[2] > 0);
  });

  /** What the chart shows now. */
  protected readonly view = computed<ChartView>(() => {
    if (this.points().length === 0) {
      return 'empty';
    }
    if (this.spec().type === 'card' && !this.asTable()) {
      return 'card';
    }
    return this.asTable() || this.engineFailed() || !this.drawable() ? 'table' : 'plot';
  });

  /** The plot's accessible name: "{title}, {type} chart". */
  protected readonly name = computed(() => {
    const spec = this.spec();
    return PLOT_TYPES.has(spec.type) ? this.translate(`chart.name.${spec.type}`, { title: spec.title })() : spec.title;
  });

  /** The plot's accessible description: the point count and the value range. */
  protected readonly description = computed(() => {
    const values = this.points().map((row) => row[2]);
    const format = this.spec().measure.format;
    const locale = this.locale();
    return this.translate('chart.description', {
      count: values.length,
      min: tmFormatNumber(Math.min(...values), locale, format),
      max: tmFormatNumber(Math.max(...values), locale, format),
    })();
  });

  /** The card's value, formatted. */
  protected readonly cardValue = computed(() =>
    tmFormatNumber(this.points()[0]?.[2], this.locale(), this.spec().measure.format),
  );

  /** The threshold the card's value reaches, as a badge's tone and text. */
  protected readonly cardThreshold = computed(() => {
    const value = this.points()[0]?.[2];
    const reached =
      value === undefined ? null : reachedThreshold(value, this.spec().options?.thresholds ?? []);
    return reached === null
      ? null
      : {
          tone: BADGE_TONES[reached.tone] ?? 'neutral',
          text: tmFormatNumber(reached.value, this.locale(), this.spec().measure.format),
        };
  });

  /** The table view's grid id: the input's, or this instance's own. */
  protected readonly tableGridId = computed(() => this.gridId() ?? `tm-chart-${this.instance}`);

  /** The table view's rows: one per point, its members' labels and its value. */
  protected readonly tableRows = computed<readonly TableRow[]>(() => {
    const [first, second] = this.spec().dimensions;
    return this.points().map(([d0, d1, value], id) => ({
      id,
      members: [first === undefined ? null : d0, second === undefined ? null : d1],
      d0: (d0 === null ? undefined : first?.members[d0]?.label) ?? '',
      d1: (d1 === null ? undefined : second?.members[d1]?.label) ?? '',
      value,
    }));
  });

  /** The first dimension's member parts for a table row. */
  protected readonly firstParts = (_: unknown, row: TableRow): readonly string[] | null =>
    partsOf(row.members[0] === null ? undefined : this.spec().dimensions[0]?.members[row.members[0]]);

  /** The second dimension's member parts for a table row. */
  protected readonly secondParts = (_: unknown, row: TableRow): readonly string[] | null =>
    partsOf(row.members[1] === null ? undefined : this.spec().dimensions[1]?.members[row.members[1]]);

  /** A value as the measure formats it. */
  protected readonly formatValue = (value: unknown): string =>
    tmFormatNumber(value, untracked(this.locale), untracked(this.spec).measure.format);

  /** What the engine draws: the spec, and every page setting it depends on. */
  private readonly request = computed<ɵTmChartRenderRequest>(() => {
    // The theme only says when to re-read the tokens.
    this.environment.theme();
    return {
      spec: this.spec(),
      locale: this.locale(),
      direction: this.direction() === 'rtl' ? 'rtl' : 'ltr',
      forceDecals: this.environment.forceDecals(),
      activatable: this.activatable(),
      templates: {
        share: this.translate('chart.share', {
          share: ɵTM_CHART_SHARE_SLOT,
          value: ɵTM_CHART_VALUE_SLOT,
        })(),
        point: this.translate('chart.point', {
          label: ɵTM_CHART_LABEL_SLOT,
          value: ɵTM_CHART_VALUE_SLOT,
        })(),
      },
    };
  }, { equal: () => false });

  /** Wires the strings, the size observer, the drawing and the teardown. */
  constructor() {
    // The chart's strings ship with it, beside the core table.
    ɵtmRegisterUiStrings('chart', TM_CHART_STRINGS_EN);

    // Section: the size: one observer, on whichever plot element is in the DOM.
    const observer =
      typeof ResizeObserver === 'function'
        ? new ResizeObserver((entries) => {
            const box = entries[entries.length - 1]?.contentRect;
            if (box !== undefined) {
              this.size.set({ width: Math.round(box.width), height: Math.round(box.height) });
            }
          })
        : null;
    effect((onCleanup) => {
      const element = this.plot()?.nativeElement;
      if (element === undefined) {
        // The plot left the DOM (the spec became a card or lost its points).
        this.releaseEngine();
        return;
      }
      observer?.observe(element);
      onCleanup(() => observer?.unobserve(element));
    });

    // Section: drawing: on the plot's first showing at a size, the engine
    // loads; every later change of the spec, the size or a page setting redraws.
    afterRenderEffect(() => {
      const element = this.plot()?.nativeElement;
      const request = this.request();
      const size = this.size();
      if (element === undefined || this.view() !== 'plot' || size.width === 0 || size.height === 0) {
        return;
      }
      this.engineFor(element)
        .then((engine) => engine?.render(request))
        .catch((error: unknown) => {
          // A plot that cannot draw leaves the data readable as a table.
          this.engineFailed.set(true);
          console.error(error);
        });
    });

    inject(DestroyRef).onDestroy(() => {
      this.destroyed = true;
      observer?.disconnect();
      this.releaseEngine();
    });
  }

  // ---- the engine ----

  /** The engine drawing in an element, loading the chunk and creating the instance on first use. */
  private engineFor(element: HTMLElement): Promise<ɵTmChartEngine | null> {
    if (this.engine?.element === element) {
      return Promise.resolve(this.engine.instance);
    }
    this.releaseEngine();
    const loading = import('./internal/chart-engine').then(
      ({ ɵtmCreateChartEngine }) => {
        if (this.destroyed || this.loading !== loading || this.plot()?.nativeElement !== element) {
          return null;
        }
        const instance = ɵtmCreateChartEngine(element, (members, modifiers) =>
          this.activate(members, modifiers),
        );
        this.engine = { instance, element };
        this.loading = null;
        return instance;
      },
      () => {
        // A chunk that fails to load leaves the data readable as a table.
        this.loading = null;
        this.engineFailed.set(true);
        return null;
      },
    );
    this.loading = loading;
    return loading;
  }

  /** Disposes the engine instance, if any, and forgets a pending load. */
  private releaseEngine(): void {
    this.engine?.instance.dispose();
    this.engine = null;
    this.loading = null;
  }

  // ---- activation ----

  /** Emits a point's activation, unless the chart does not drill. */
  private activate(
    members: readonly [number | null, number | null],
    modifiers: ɵTmChartPointerModifiers,
  ): void {
    if (!untracked(this.activatable)) {
      return;
    }
    const mod = tmIsApplePlatform() ? modifiers.metaKey : modifiers.ctrlKey;
    this.pointActivate.emit({ members, newTab: modifiers.middle || mod });
  }

  /** The card's click (or Enter, or Space): drills to the card's value. */
  protected onCardClick(event: MouseEvent): void {
    this.activate([null, null], { ctrlKey: event.ctrlKey, metaKey: event.metaKey, middle: false });
  }

  /** The card's middle click: drills to a new tab. */
  protected onCardAuxClick(event: MouseEvent): void {
    if (event.button === 1) {
      event.preventDefault();
      this.activate([null, null], { ctrlKey: event.ctrlKey, metaKey: event.metaKey, middle: true });
    }
  }

  /** A table row's activation: drills to the row's point. */
  protected onRowActivate(activation: TmGridRowActivation<TableRow>): void {
    if (untracked(this.activatable)) {
      this.pointActivate.emit({ members: activation.row.members, newTab: activation.newTab });
    }
  }
}
