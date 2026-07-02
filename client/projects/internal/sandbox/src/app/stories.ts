import { Component, Type } from '@angular/core';

import { CheckboxStory } from './checkbox/checkbox-story';
import { InputStory } from './input/input-story';
import { SelectStory } from './select/select-story';
import { ThemingStory } from './theming/theming-story';

/**
 * The sandbox story registry.
 *
 * Each entry is a demo page the Playwright suite (and a human) can address as
 * /story/<id>, with ?dir=rtl|ltr and ?theme=light|dark applied to <html>.
 * Component stages register their demo hosts here; the same components back
 * the CSF stories so the two showcases cannot drift apart.
 */
export interface SandboxStory {
  readonly id: string;
  readonly title: string;
  readonly component: Type<unknown>;
}

@Component({
  template: `
    <h2>Welcome</h2>
    <p>
      Tellma UI sandbox — the internal host the component library's browser
      tests run against. Pick a story from the index.
    </p>
  `,
})
export class WelcomeStory {}

export const SANDBOX_STORIES: readonly SandboxStory[] = [
  { id: 'welcome', title: 'Welcome', component: WelcomeStory },
  { id: 'theming', title: 'Tokens & theming', component: ThemingStory },
  { id: 'input', title: 'Text input (tmInput + tm-form-field)', component: InputStory },
  { id: 'checkbox', title: 'Checkbox (tm-checkbox)', component: CheckboxStory },
  { id: 'select', title: 'Select (tm-select)', component: SelectStory },
];
