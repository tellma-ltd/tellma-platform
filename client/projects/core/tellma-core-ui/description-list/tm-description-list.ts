// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { Directive, input } from '@angular/core';

/**
 * How many columns of pairs a {@link TmDescriptionList} lays out. `'auto'`
 * is two while the list is at least the `sm` breakpoint (576 px) wide, else
 * one.
 */
export type TmDescriptionListColumns = 1 | 2 | 'auto';

/**
 * Where a {@link TmDescriptionList} puts each label: `'stacked'` above its
 * value, `'inline'` beside it.
 */
export type TmDescriptionListLayout = 'stacked' | 'inline';

/**
 * Lays out label–value pairs on a native `<dl>`, so the list keeps its
 * term and definition roles. Each pair sits in a `<div>` holding its `<dt>`
 * and its `<dd>`:
 *
 * ```html
 * <dl tmDescriptionList>
 *   <div><dt>Version</dt><dd>2.14.0</dd></div>
 *   <div><dt>Build</dt><dd>8f2c41a</dd></div>
 * </dl>
 * ```
 *
 * Labels are 12 px secondary text and values 13 px strong text, with pairs
 * 10 px apart down and 18 px across. The directive has no view: its look
 * ships as the global stylesheet `styles/tm-description-list.css`, which
 * the application adds to its styles.
 *
 * With `columns` set to `'auto'` the list measures its own width (a
 * container query), so it must take its width from its container, as a
 * block-level list does; inside a row that sizes items to their content,
 * give it a width.
 *
 * @tmGroup layout
 * @tmA11yNotes A native description list: each `<dt>` is read as a term
 *   and its `<dd>` as the definition. The `<div>` wrappers are allowed by
 *   HTML and do not change the roles. No keyboard interaction.
 */
@Directive({
  selector: 'dl[tmDescriptionList]',
  host: {
    class: 'tm-description-list',
    // The stylesheet keys on these; attribute bindings rather than a class
    // per value keep the directive small.
    '[attr.data-columns]': 'columns()',
    '[attr.data-layout]': 'layout()',
  },
})
export class TmDescriptionList {
  /** One column (default), two, or `'auto'`: two while the list is at least 576 px wide. */
  readonly columns = input<TmDescriptionListColumns>(1);

  /** `'stacked'` (default): each label above its value; `'inline'`: beside it. */
  readonly layout = input<TmDescriptionListLayout>('stacked');
}
