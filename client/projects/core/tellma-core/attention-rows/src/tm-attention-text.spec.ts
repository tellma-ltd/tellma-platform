// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { TestBed } from '@angular/core/testing';
import { TmLocalization } from '@tellma/core';

import { TmAttentionText } from './tm-attention-text';
import { tmAttentionRig, tmSettleUntil, tmTestAssignment } from '../../src/attention/tm-attention.testing.util';

// Server text on the client, in a whole test app entered into tenant 17,
// whose content slots are English, Arabic and French: notification titles
// and details by key, the job label's order and its entity argument, job
// messages, and assignment captions with their isolated code, each
// following a language switch with no request.

/** Starts the app and returns the service with the localization. */
async function setUp(): Promise<{ readonly text: TmAttentionText; readonly l10n: TmLocalization }> {
  await tmAttentionRig();
  return { text: TestBed.inject(TmAttentionText), l10n: TestBed.inject(TmLocalization) };
}

/** Switches the language and waits until its strings have loaded and it applies. */
async function switchTo(l10n: TmLocalization, code: string): Promise<void> {
  l10n.setLanguage(code);
  // The language applies once its strings load through a lazily loaded chunk, which no count of turns bounds.
  await tmSettleUntil(() => l10n.language() === code, `the language is ${code}`);
}

describe('TmAttentionText', () => {
  describe('notification', () => {
    it("renders the title and detail from the type's keys, with its arguments", async () => {
      const { text } = await setUp();
      const rendered = text.notification('core.export.ready', JSON.stringify({ fileName: 'Customers.xlsx', rowCount: 1284, fileId: 9 }));
      expect(rendered()).toEqual({ title: 'Export is ready', detail: 'Customers.xlsx · 1,284 rows' });
    });

    it("renders a missing title as the type's key, and a missing detail as null", async () => {
      const { text } = await setUp();
      expect(text.notification('acme.unknown.type', null)()).toEqual({ title: 'acme.unknown.type', detail: null });
      expect(text.notification('core.job.failed', '{}')()).toEqual({ title: 'A job failed', detail: null });
    });

    it('formats booleans as ICU select keys and other objects as their JSON text, as the server does', async () => {
      const { text } = await setUp();
      const args = { center: ['Head office', 'المكتب الرئيسي', null], urgent: true, tags: ['a', 'b'] };
      expect(text.notification('gl.center.flagged', JSON.stringify(args))()).toEqual({
        title: 'Head office was flagged',
        detail: 'Urgent: yes; tags ["a","b"]',
      });
    });

    it('renders without arguments when the stored text is not one JSON object', async () => {
      const { text } = await setUp();
      expect(text.notification('core.job.failed', 'not json')().title).toBe('A job failed');
      expect(text.notification('core.job.failed', '[1,2]')().title).toBe('A job failed');
    });

    it('follows a language switch with no request, falling back to English per key', async () => {
      const { text, l10n } = await setUp();
      const rendered = text.notification('core.export.ready', JSON.stringify({ fileName: 'C.xlsx', rowCount: 3 }));
      expect(rendered().title).toBe('Export is ready');
      await switchTo(l10n, 'ar');
      expect(rendered().title).toBe('التصدير جاهز');
      expect(rendered().detail).toContain('C.xlsx');
    });
  });

  describe('job', () => {
    it("renders the job's own label first, its entity argument as the entity's plural label", async () => {
      const { text, l10n } = await setUp();
      const label = text.job({ handlerKey: 'core.export', labelKey: 'Excel.Export.Label', labelArgumentsJson: '{"entity":"gl.Center"}' });
      expect(label()).toBe('Exporting Centers');
      await switchTo(l10n, 'ar');
      expect(label()).toBe('جارٍ تصدير المراكز');
    });

    it('leaves an entity argument that names no entity as it is', async () => {
      const { text } = await setUp();
      expect(text.job({ handlerKey: 'core.export', labelKey: 'Excel.Export.Label', labelArgumentsJson: '{"entity":"Leads"}' })()).toBe(
        'Exporting Leads',
      );
    });

    it("falls back to the handler's label, then its fallback, then its key", async () => {
      const { text } = await setUp();
      // The job's own key has no string: the handler's label renders.
      expect(text.job({ handlerKey: 'core.import', labelKey: 'Acme.Missing.Label' })()).toBe('Importing');
      // The handler's label has no string: its fallback renders.
      expect(text.job({ handlerKey: 'core.export' })()).toBe('core.export');
      // A handler the metadata does not list, with no string: its key.
      expect(text.job({ handlerKey: 'acme.reconcile' })()).toBe('acme.reconcile');
    });
  });

  describe('jobMessage', () => {
    it('renders a message with its arguments, a missing one as its key, and none as null', async () => {
      const { text } = await setUp();
      expect(text.jobMessage('Excel.Export.Progress', '{"done":5580,"total":9000}')()).toBe('5,580 of 9,000 rows');
      expect(text.jobMessage('Acme.Unknown', null)()).toBe('Acme.Unknown');
      expect(text.jobMessage(null, null)()).toBeNull();
    });
  });

  describe('caption', () => {
    it('renders the title, a localized-text detail and the amount with its currency code', async () => {
      const { text } = await setUp();
      const caption = { code: 'HQ-01', amount: '12400.00', currency: 'SAR', detail: { manager: ['Al Faris Group', 'مجموعة الفارس', null] } };
      const rendered = text.caption(tmTestAssignment(1874, { captionJson: JSON.stringify(caption) }));
      expect(rendered()).toEqual({
        title: 'Center HQ-01',
        titleSegments: [{ text: 'Center ' }, { text: 'HQ-01', arg: 'code' }],
        detail: 'Managed by Al Faris Group',
        // Intl keeps the code and the number together with a no-break space.
        amount: 'SAR 12,400.00',
      });
    });

    it("renders the target's id without a code, and no detail or amount without them", async () => {
      const { text } = await setUp();
      expect(text.caption(tmTestAssignment(1, { targetId: '9007199254740993' }))()).toEqual({
        title: 'Center #9007199254740993',
        titleSegments: [{ text: 'Center ' }, { text: '#9007199254740993', arg: 'code' }],
        detail: null,
        amount: null,
      });
    });

    it('renders an amount exactly at its own scale, plain without a currency', async () => {
      const { text } = await setUp();
      const amount = (caption: object): string | null =>
        text.caption(tmTestAssignment(1, { captionJson: JSON.stringify(caption) }))().amount;
      expect(amount({ amount: '12345678901234567.891' })).toBe('12,345,678,901,234,567.891');
      expect(amount({ amount: '5', currency: 'USD' })).toBe('USD 5');
      expect(amount({ amount: 'n/a' })).toBe('n/a');
    });

    it('renders no detail for a resource without a detail key, or a caption without a detail', async () => {
      const { text } = await setUp();
      const detail = { manager: 'Sara' };
      expect(text.caption(tmTestAssignment(1, { resource: 'acme.Customer', captionJson: JSON.stringify({ detail }) }))().detail).toBeNull();
      expect(text.caption(tmTestAssignment(1, { captionJson: JSON.stringify({ code: 'A' }) }))().detail).toBeNull();
      expect(text.caption(tmTestAssignment(1, { captionJson: JSON.stringify({ detail }) }))().detail).toBe('Managed by Sara');
      // An unreadable caption has no members.
      expect(text.caption(tmTestAssignment(1, { captionJson: '{oops' }))().title).toBe('Center #23');
    });

    it('renders a localized-text detail in each content slot, falling back to slot 1 when the slot is empty', async () => {
      const { text, l10n } = await setUp();
      const caption = { code: 'HQ', detail: { manager: ['Al Faris Group', 'مجموعة الفارس', null] } };
      const rendered = text.caption(tmTestAssignment(1, { captionJson: JSON.stringify(caption) }));
      expect(rendered().detail).toBe('Managed by Al Faris Group');

      await switchTo(l10n, 'ar');
      expect(l10n.contentSlot()).toBe(2);
      expect(rendered().detail).toBe('يديره مجموعة الفارس');
      expect(rendered().title).toBe('HQ · مركز');

      await switchTo(l10n, 'fr');
      expect(l10n.contentSlot()).toBe(3);
      expect(rendered().detail).toBe('Managed by Al Faris Group');
    });

    it("keeps the code an isolate in the translation's own order", async () => {
      const { text, l10n } = await setUp();
      const rendered = text.caption(tmTestAssignment(1, { captionJson: JSON.stringify({ code: 'INV-23' }) }));
      await switchTo(l10n, 'ar');
      expect(rendered().title).toBe('INV-23 · مركز');
      expect(rendered().titleSegments).toEqual([{ text: 'INV-23', arg: 'code' }, { text: ' · مركز' }]);
    });
  });

  describe('isolate', () => {
    it('splits a message around its isolated arguments, formatting the others in place', async () => {
      const { text } = await setUp();
      const segments = text.isolate('tm.attentionRows.assignment.title', { entity: 'Center', code: 'HQ-01' }, ['code']);
      expect(segments()).toEqual([{ text: 'Center ' }, { text: 'HQ-01', arg: 'code' }]);
      const both = text.isolate('tm.attentionRows.assignment.title', { entity: 'Center', code: 'HQ' }, ['entity', 'code']);
      expect(both()).toEqual([{ text: 'Center', arg: 'entity' }, { text: ' ' }, { text: 'HQ', arg: 'code' }]);
    });
  });
});
