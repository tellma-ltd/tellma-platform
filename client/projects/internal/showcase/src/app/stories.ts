// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { Component, Type } from '@angular/core';

import { CheckboxStory } from './checkbox/checkbox-story';
import { GridCustomEditorStory } from './grid/grid-custom-editor-story';
import { GridEditableStory } from './grid/grid-editable-story';
import { GridListScreenStory } from './grid/grid-list-screen-story';
import { GridReadonlyStory } from './grid/grid-readonly-story';
import { GridStatesStory } from './grid/grid-states-story';
import { TreeGridStory } from './grid/tree-grid-story';
import { I18nStory } from './i18n/i18n-story';
import { InputStory } from './input/input-story';
import { MenuStory } from './menu/menu-story';
import { SelectStory } from './select/select-story';
import { ThemingStory } from './theming/theming-story';

/**
 * The showcase story registry.
 *
 * Each entry is a demo page the Playwright suite (and a human) can address as
 * /story/<id>, with ?dir=rtl|ltr and ?theme=light|dark applied to <html>.
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
      Tellma UI showcase — the internal host the component library's browser
      tests run against. Pick a story from the index.
    </p>
  `,
})
export class WelcomeStory {}

export const SHOWCASE_STORIES: readonly ShowcaseStory[] = [
  { id: 'welcome', title: 'Welcome', component: WelcomeStory },
  { id: 'theming', title: 'Tokens & theming', component: ThemingStory },
  { id: 'input', title: 'Text input (tmInput + tm-form-field)', component: InputStory },
  { id: 'checkbox', title: 'Checkbox (tm-checkbox)', component: CheckboxStory },
  { id: 'select', title: 'Select (tm-select)', component: SelectStory },
  { id: 'menu', title: 'Menu', component: MenuStory },
  { id: 'grid-readonly', title: 'Grid (readonly)', component: GridReadonlyStory, wide: true },
  {
    id: 'grid-list-screen',
    title: 'Grid (list screen: selectable + searchable)',
    component: GridListScreenStory,
    wide: true,
  },
  { id: 'grid-states', title: 'Grid (states)', component: GridStatesStory, wide: true },
  { id: 'grid-editable', title: 'Grid (editable)', component: GridEditableStory, wide: true },
  {
    id: 'grid-custom-editor',
    title: 'Grid (custom editor)',
    component: GridCustomEditorStory,
    wide: true,
  },
  { id: 'tree-grid', title: 'Tree grid', component: TreeGridStory, wide: true },
  { id: 'i18n', title: 'i18n / locale packs', component: I18nStory },
];
