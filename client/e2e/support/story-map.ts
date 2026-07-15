// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

/**
 * Single indirection between the specs and the page host serving the stories.
 * Today the host is the internal showcase app (/story/:id); if the host ever
 * changes, only this function changes, not the specs.
 */
export interface StoryUrlOptions {
  readonly dir?: 'ltr' | 'rtl';
  readonly theme?: 'light' | 'dark';
}

export function storyUrl(id: string, options: StoryUrlOptions = {}): string {
  const query = new URLSearchParams();
  if (options.dir) {
    query.set('dir', options.dir);
  }
  if (options.theme) {
    query.set('theme', options.theme);
  }
  const suffix = query.size > 0 ? `?${query.toString()}` : '';
  return `/story/${id}${suffix}`;
}
