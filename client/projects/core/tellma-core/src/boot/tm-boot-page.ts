// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import type { TmBootConfig, TmBootLanguage, TmBootPageReason, TmBootStrings } from './tm-boot-types';

// The boot page (spec 0024 §4.2): one page for every reason the app cannot
// start. It is built with DOM calls and no HTML strings, so Trusted Types
// need nothing, inside `<tm-app>`, and `.tm-boot-page` styles it. It may show
// in a browser too old for the app, so it sticks to DOM calls every browser
// has had for a decade.

/** The string key of each reason's sentence. */
const SENTENCES: Readonly<Record<TmBootPageReason, keyof TmBootStrings>> = {
  unsupported: 'Boot_Unsupported',
  cookies: 'Boot_Cookies',
  'sign-in-failed': 'Boot_SignInFailed',
  'load-failed': 'Boot_LoadFailed',
};

/** The placeholder the heading carries for the display name. */
const DISPLAY_NAME = '{displayName}';

/**
 * Builds the boot page inside `<tm-app>` (or the body, when the page has no
 * `<tm-app>`), replacing what is there, and moves focus to its heading. Each
 * string comes from the language's boot strings, else from English's; every
 * element carries the language and direction of its own text.
 *
 * @param document - The document.
 * @param config - The boot configuration.
 * @param language - The boot language.
 * @param name - The display name in the boot language, for the heading's placeholder.
 * @param reason - Why the page shows.
 * @param retry - What the Try again button does.
 */
export function tmShowBootPage(
  document: Document,
  config: TmBootConfig,
  { code, dir }: TmBootLanguage,
  name: string,
  reason: TmBootPageReason,
  retry: () => void,
): void {
  const own = config.strings[code] || {};
  const english = config.strings['en'] || {};

  // One element holding one string, with its language and direction: the
  // language's own string, or English's marked as English, so that a screen
  // reader voices it right.
  const element = <K extends keyof HTMLElementTagNameMap>(
    parent: HTMLElement | null,
    tag: K,
    key: keyof TmBootStrings | null,
  ): HTMLElementTagNameMap[K] => {
    const node = document.createElement(tag);
    const text = key && own[key];
    node.lang = text || !key ? code : 'en';
    node.dir = text || !key ? dir : 'ltr';
    if (key) {
      node.textContent = (text || english[key] || '').split(DISPLAY_NAME).join(name);
    }
    if (parent) {
      parent.appendChild(node);
    }
    return node;
  };

  // A main landmark: the heading, one sentence and the Try again button.
  const main = element(null, 'main', null);
  main.className = 'tm-boot-page';
  const heading = element(main, 'h1', 'Boot_Title');
  heading.tabIndex = -1;
  element(main, 'p', SENTENCES[reason]);
  const button = element(main, 'button', 'Boot_TryAgain');
  button.type = 'button';
  button.className = 'tm-button tm-button--primary';
  button.addEventListener('click', retry);

  // Into <tm-app>, emptied first; the app never renders over the page.
  const host = document.querySelector('tm-app') || document.body;
  host.textContent = '';
  host.appendChild(main);
  heading.focus();
}
