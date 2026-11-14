// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { Component, Type } from '@angular/core';

import { AlertStory } from './alert/alert-story';
import { AvatarStory } from './avatar/avatar-story';
import { BadgeStory } from './badge/badge-story';
import { BannerStory } from './banner/banner-story';
import { BusyOverlayStory } from './busy-overlay/busy-overlay-story';
import { ButtonStory } from './button/button-story';
import { CardStory } from './card/card-story';
import { CheckboxStory } from './checkbox/checkbox-story';
import { ConfirmStory } from './confirm/confirm-story';
import { ChipStory } from './chip/chip-story';
import { CopyStory } from './copy/copy-story';
import { DatePickerStory } from './date-picker/date-picker-story';
import { DateRangeStory } from './date-range/date-range-story';
import { DateTimeStory } from './date-time/date-time-story';
import { DescriptionListStory } from './description-list/description-list-story';
import { EmptyStateStory } from './empty-state/empty-state-story';
import { EntityPickerStory } from './entity-picker/entity-picker-story';
import { ErrorDialogStory } from './error-dialog/error-dialog-story';
import { GridCustomEditorStory } from './grid/grid-custom-editor-story';
import { GridEditableStory } from './grid/grid-editable-story';
import { GridEntityStory } from './grid/grid-entity-story';
import { GridLazyEditingStory } from './grid/grid-lazy-editing-story';
import { GridListScreenStory } from './grid/grid-list-screen-story';
import { GridSearchPageStory } from './grid/grid-search-page-story';
import { GridReadonlyStory } from './grid/grid-readonly-story';
import { FilePreviewStory } from './file-preview/file-preview-story';
import { FilesStory } from './files/files-story';
import { FormFieldStory } from './form-field/form-field-story';
import { BasicsGlyphsStory } from './glyphs/basics-glyphs-story';
import { GlyphsStory } from './glyphs/glyphs-story';
import { GridStatesStory } from './grid/grid-states-story';
import { TreeGridLargeStory } from './grid/tree-grid-large-story';
import { TreeGridRestoreStory } from './grid/tree-grid-restore-story';
import { TreeGridStory } from './grid/tree-grid-story';
import { I18nStory } from './i18n/i18n-story';
import { IconStory } from './icon/icon-story';
import { ImageStory } from './image/image-story';
import { InputStory } from './input/input-story';
import { KbdStory } from './kbd/kbd-story';
import { MenuRichStory } from './menu/menu-rich-story';
import { MenuStory } from './menu/menu-story';
import { ModalStory } from './modal/modal-story';
import { NumberStory } from './number/number-story';
import { PageHeaderStory } from './page-header/page-header-story';
import { PopoverStory } from './popover/popover-story';
import { ProgressStory } from './progress/progress-story';
import { RadioStory } from './radio/radio-story';
import { RelativeTimeStory } from './relative-time/relative-time-story';
import { SearchFieldStory } from './search-field/search-field-story';
import { SegmentedStory } from './segmented/segmented-story';
import { TooltipStory } from './tooltip/tooltip-story';
import { SelectStory } from './select/select-story';
import { SkeletonStory } from './skeleton/skeleton-story';
import { SplitterStory } from './splitter/splitter-story';
import { SwitchStory } from './switch/switch-story';
import { TabsStory } from './tabs/tabs-story';
import { ThemingStory } from './theming/theming-story';
import { ToastStory } from './toast/toast-story';

/**
 * The showcase story registry.
 *
 * Each entry is a demo page the Playwright suite (and a human) can address as
 * /story/<id>, with ?dir=rtl|ltr and ?theme=light|dark|system applied to <html>.
 * Component stages register their demo hosts here.
 */
export interface ShowcaseStory {
  readonly id: string;
  readonly title: string;
  readonly component: Type<unknown>;
  /** Lifts the story shell's inline-size cap (data-dense stories like grids). */
  readonly wide?: boolean;
}

@Component({
  template: `
    <h2>Welcome</h2>
    <p>
      Tellma UI showcase — the internal host the component library's browser tests run against. Pick
      a story from the index.
    </p>
  `,
})
export class WelcomeStory {}

export const SHOWCASE_STORIES: readonly ShowcaseStory[] = [
  { id: 'welcome', title: 'Welcome', component: WelcomeStory },
  { id: 'theming', title: 'Tokens & theming', component: ThemingStory },
  { id: 'icon', title: 'Icon (tm-icon)', component: IconStory },
  { id: 'kbd', title: 'Keyboard keys (tm-kbd)', component: KbdStory },
  { id: 'button', title: 'Button (tmButton)', component: ButtonStory },
  { id: 'copy', title: 'Copy (tm-copy-button + tm-reference)', component: CopyStory },
  { id: 'badge', title: 'Badges and counters (tm-badge + tm-counter)', component: BadgeStory },
  { id: 'avatar', title: 'Avatar (tm-avatar)', component: AvatarStory },
  {
    id: 'progress',
    title: 'Progress (tm-progress-bar + tm-progress-ring)',
    component: ProgressStory,
  },
  { id: 'skeleton', title: 'Skeleton (tm-skeleton)', component: SkeletonStory },
  { id: 'relative-time', title: 'Relative time (tm-relative-time)', component: RelativeTimeStory },
  { id: 'empty-state', title: 'Empty state (tm-empty-state)', component: EmptyStateStory },
  { id: 'chip', title: 'Chips (tm-chip + tm-chip-set)', component: ChipStory },
  { id: 'toast', title: 'Toasts (TmToasts)', component: ToastStory },
  { id: 'banner', title: 'Banner bar (tm-banner-bar)', component: BannerStory },
  { id: 'busy-overlay', title: 'Busy overlay (TmBusyOverlay)', component: BusyOverlayStory },
  { id: 'confirm', title: 'Confirm (TmConfirm)', component: ConfirmStory },
  { id: 'error-dialog', title: 'Error dialog (TmErrorDialog)', component: ErrorDialogStory },
  { id: 'card', title: 'Card (tm-card)', component: CardStory },
  {
    id: 'page-header',
    title: 'Page header (tm-page-header)',
    component: PageHeaderStory,
    wide: true,
  },
  {
    id: 'description-list',
    title: 'Description list (dl[tmDescriptionList])',
    component: DescriptionListStory,
  },
  { id: 'splitter', title: 'Splitter (tm-splitter)', component: SplitterStory, wide: true },
  { id: 'form-field', title: 'Form field (tm-form-field)', component: FormFieldStory },
  { id: 'alert', title: 'Alert (tm-alert)', component: AlertStory },
  { id: 'input', title: 'Text input (tmInput + tm-form-field)', component: InputStory },
  { id: 'number', title: 'Number input (tmNumber)', component: NumberStory },
  { id: 'date-picker', title: 'Date picker (tm-date-picker)', component: DatePickerStory },
  { id: 'checkbox', title: 'Checkbox (tm-checkbox)', component: CheckboxStory },
  { id: 'radio', title: 'Radio group (tm-radio-group)', component: RadioStory },
  { id: 'segmented', title: 'Segmented (tm-segmented)', component: SegmentedStory, wide: true },
  { id: 'switch', title: 'Switch (tm-switch)', component: SwitchStory },
  {
    id: 'search-field',
    title: 'Search field (tm-search-field + tmSearch)',
    component: SearchFieldStory,
  },
  { id: 'date-range', title: 'Date range (tm-date-range)', component: DateRangeStory },
  {
    id: 'date-time',
    title: 'Date and time (tm-time-field + tm-date-time-field)',
    component: DateTimeStory,
  },
  { id: 'image', title: 'Image (tm-image)', component: ImageStory },
  { id: 'files', title: 'Files (tmFilePicker + tm-dropzone)', component: FilesStory },
  { id: 'file-preview', title: 'File preview (TmFilePreview)', component: FilePreviewStory },
  { id: 'select', title: 'Select (tm-select)', component: SelectStory },
  { id: 'glyphs', title: 'Foundation glyphs (tm-icon)', component: GlyphsStory },
  { id: 'basics-glyphs', title: 'Basics glyphs (tm-icon)', component: BasicsGlyphsStory },
  {
    id: 'entity-picker',
    title: 'Entity picker (tm-entity-picker)',
    component: EntityPickerStory,
  },
  { id: 'tabs', title: 'Tabs (tm-tab-group)', component: TabsStory },
  { id: 'modal', title: 'Modal (TmModal)', component: ModalStory },
  { id: 'popover', title: 'Popover (tm-popover)', component: PopoverStory },
  { id: 'tooltip', title: 'Tooltip (tmTooltip)', component: TooltipStory },
  { id: 'menu', title: 'Menu', component: MenuStory },
  { id: 'menu-rich', title: 'Menu (rich entries)', component: MenuRichStory },
  { id: 'grid-readonly', title: 'Grid (readonly)', component: GridReadonlyStory, wide: true },
  {
    id: 'grid-list-screen',
    title: 'Grid (list screen: selectable + searchable)',
    component: GridListScreenStory,
    wide: true,
  },
  {
    id: 'grid-search-page',
    title: 'Grid (search page: driver, row activation, sort)',
    component: GridSearchPageStory,
    wide: true,
  },
  { id: 'grid-states', title: 'Grid (states)', component: GridStatesStory, wide: true },
  { id: 'grid-editable', title: 'Grid (editable)', component: GridEditableStory, wide: true },
  {
    id: 'grid-lazy-editing',
    title: 'Grid (editing code on its way)',
    component: GridLazyEditingStory,
    wide: true,
  },
  {
    id: 'grid-entity',
    title: 'Grid (entity picker editor)',
    component: GridEntityStory,
    wide: true,
  },
  {
    id: 'grid-custom-editor',
    title: 'Grid (custom editor)',
    component: GridCustomEditorStory,
    wide: true,
  },
  { id: 'tree-grid', title: 'Tree grid', component: TreeGridStory, wide: true },
  {
    id: 'tree-grid-large',
    title: 'Tree grid (large, expanded)',
    component: TreeGridLargeStory,
    wide: true,
  },
  {
    id: 'tree-grid-restore',
    title: 'Tree grid (expansion restore)',
    component: TreeGridRestoreStory,
    wide: true,
  },
  { id: 'i18n', title: 'i18n / locale packs', component: I18nStory },
];
