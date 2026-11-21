// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';

import { provideTellmaUi } from '@tellma/core-ui';
import { TmFormField } from '@tellma/core-ui/form-field';

import { TmOption } from './tm-option';
import { TmSelect } from './tm-select';

/**
 * Serializes an element tree for comparison: one line per element (its
 * attributes sorted) or text node, comments dropped, generated ids
 * numbered in order of appearance, and the per-component encapsulation
 * attributes reduced to their kind.
 *
 * A glyph — an inline `<svg>`, or a `tm-icon` drawing one — serializes as
 * `<glyph>` with its own classes (`tm-icon` bookkeeping aside) and its
 * shapes' geometry, so the comparison sees what is drawn where, not which
 * element draws it.
 */
function serialize(root: Element): string {
  const ids = new Map<string, string>();
  const idOf = (raw: string) =>
    raw
      .split(/\s+/)
      .filter(Boolean)
      .map((id) => {
        if (!ids.has(id)) {
          ids.set(id, `id${ids.size}`);
        }
        return ids.get(id)!;
      })
      .join(' ');
  const idAttributes = new Set([
    'id',
    'for',
    'aria-controls',
    'aria-activedescendant',
    'aria-labelledby',
    'aria-describedby',
  ]);
  const geometry = ['d', 'cx', 'cy', 'r', 'x1', 'x2', 'y1', 'y2'];
  const glyph = (host: Element, pad: string): string => {
    const svg = host.localName === 'svg' ? host : host.querySelector(':scope > svg');
    const classes = (host.getAttribute('class') ?? '')
      .split(/\s+/)
      .filter((name) => name !== '' && !name.startsWith('tm-icon'))
      .join(' ');
    let out = `${pad}<glyph aria-hidden="${host.getAttribute('aria-hidden')}" class="${classes}">\n`;
    for (const shape of svg?.children ?? []) {
      const attrs = geometry
        .filter((name) => shape.hasAttribute(name))
        .map((name) => `${name}="${shape.getAttribute(name)}"`)
        .join(' ');
      out += `${pad}  <${shape.localName} ${attrs}>\n`;
    }
    return out;
  };
  const walk = (node: Node, depth: number): string => {
    const pad = '  '.repeat(depth);
    if (node.nodeType === Node.TEXT_NODE) {
      const text = node.textContent!.trim();
      return text === '' ? '' : `${pad}${text}\n`;
    }
    if (node.nodeType !== Node.ELEMENT_NODE) {
      return '';
    }
    const element = node as Element;
    if (element.localName === 'svg' || element.localName === 'tm-icon') {
      return glyph(element, pad);
    }
    const attrs = [...element.attributes]
      .map((a) => {
        const name = /^_ng(content|host)-/.test(a.name) ? a.name.replace(/-.*$/, '') : a.name;
        const value = idAttributes.has(a.name) ? idOf(a.value) : a.value;
        return `${name}="${value}"`;
      })
      .sort()
      .join(' ');
    let out = `${pad}<${element.localName}${attrs ? ' ' + attrs : ''}>\n`;
    for (const child of element.childNodes) {
      out += walk(child, depth + 1);
    }
    return out;
  };
  return walk(root, 0);
}

// The markup a select without groups rendered before option groups existed:
// a closed trigger, then the open panel with a selected and a disabled row.
const CLOSED = `<tm-select _nghost="" class="tm-select">
  <div _ngcontent="" aria-autocomplete="none" aria-describedby="id2" aria-disabled="false" aria-expanded="false" aria-labelledby="id1" class="tm-select__trigger" id="id0" ngcombobox="" role="combobox" tabindex="0">
    <span _ngcontent="" class="tm-select__value">
      Ethiopia
    <glyph aria-hidden="true" class="tm-select__caret">
      <path d="m6 9 6 6 6-6">
`;

const PANEL = `<div _ngcontent="" class="tm-select__panel">
  <ul _ngcontent="" aria-activedescendant="id1" aria-disabled="false" aria-multiselectable="false" aria-orientation="vertical" aria-readonly="false" class="tm-select__listbox" focusmode="activedescendant" id="id0" ngcomboboxwidget="" nglistbox="" role="listbox" selectionmode="explicit" tabindex="-1">
    <li _ngcontent="" aria-disabled="false" aria-selected="false" class="tm-option__row" data-active="false" id="id2" ngoption="" role="option" tabindex="-1">
      <span _ngcontent="" class="tm-option__content">
        Saudi Arabia
      <glyph aria-hidden="true" class="tm-option__check">
        <path d="M20 6 9 17l-5-5">
    <li _ngcontent="" aria-disabled="false" aria-selected="true" class="tm-option__row" data-active="true" id="id1" ngoption="" role="option" tabindex="-1">
      <span _ngcontent="" class="tm-option__content">
        Ethiopia
      <glyph aria-hidden="true" class="tm-option__check">
        <path d="M20 6 9 17l-5-5">
    <li _ngcontent="" aria-disabled="true" aria-selected="false" class="tm-option__row" data-active="false" id="id3" ngoption="" role="option" tabindex="-1">
      <span _ngcontent="" class="tm-option__content">
        Jordan
      <glyph aria-hidden="true" class="tm-option__check">
        <path d="M20 6 9 17l-5-5">
`;

@Component({
  imports: [TmSelect, TmOption, TmFormField],
  template: `
    <tm-form-field label="Country" hint="Where the tenant operates">
      <tm-select [(value)]="value">
        <tm-option [value]="1" label="Saudi Arabia">Saudi Arabia</tm-option>
        <tm-option [value]="2" label="Ethiopia">Ethiopia</tm-option>
        <tm-option [value]="3" label="Jordan" disabled>Jordan</tm-option>
      </tm-select>
    </tm-form-field>
  `,
})
class Host {
  readonly value = signal<number | undefined>(2);
}

describe('tm-select markup without groups', () => {
  it('renders the trigger and the open panel exactly as a flat list always has', async () => {
    TestBed.configureTestingModule({ providers: [provideTellmaUi()] });
    const fixture = TestBed.createComponent(Host);
    await fixture.whenStable();
    const host = fixture.nativeElement.querySelector('tm-select') as HTMLElement;
    expect(serialize(host)).toBe(CLOSED);

    const trigger = host.querySelector('.tm-select__trigger') as HTMLElement;
    trigger.focus();
    trigger.click();
    await fixture.whenStable();
    // The panel content inserts one render pass after the overlay attaches.
    await new Promise((resolve) => setTimeout(resolve, 0));
    await fixture.whenStable();
    expect(serialize(document.querySelector('.tm-select__panel')!)).toBe(PANEL);
  });
});
