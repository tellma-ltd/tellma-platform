/**
 * Single indirection between the specs and the page host serving the stories.
 * Today the host is the internal sandbox app (/story/:id); if the showcase
 * moves to Storybook, only this function changes (e.g. to
 * /iframe.html?id=<id>), not the specs.
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
