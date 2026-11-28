// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { TestBed } from '@angular/core/testing';
import { TmEntities, tmRowsToObjects, type TmColumnInput, type TmJson, type TmQueryRowSet, type TmSelectPlan } from '@tellma/core';

import { tmEntityRig, type TmEntityRigOptions } from '../../src/entity/tm-entity.testing.util';
import { tmSelectPlan, type TmSelectPlanOptions } from './tm-select-plan';

// Select plans over the recorded entity model: each column kind's terms,
// order and cell, the root key first, each term once, the dropped keys with
// their telemetry, and the rows read back from a row set.

/** A user who reads everything. */
const SYSTEM: TmEntityRigOptions = { access: { isSystem: true } };

/**
 * A plan made in the rig's app.
 *
 * @param root - The entity.
 * @param keys - The column inputs.
 * @param extra - The extra paths.
 * @param options - The plan's options.
 * @returns The plan.
 */
function plan(root: string, keys: readonly TmColumnInput<string>[], extra?: readonly string[], options?: TmSelectPlanOptions): TmSelectPlan {
  return TestBed.runInInjectionContext(() => tmSelectPlan(TestBed.inject(TmEntities).get(root), keys, extra, options));
}

/**
 * A row set answering a plan's select: one path column per term, in order.
 *
 * @param terms - The selected terms.
 * @param rows - The rows' cells.
 * @param types - The Queryex type of a term, by term.
 * @returns The row set.
 */
function rowSet(terms: readonly string[], rows: readonly (readonly TmJson[])[], types: Readonly<Record<string, string>> = {}): TmQueryRowSet {
  return {
    columns: terms.map((term) => ({
      name: term,
      type: types[term] ?? 'String',
      nullable: true,
      groupingKey: false,
      ...(!(term in types) || /^[A-Za-z.]+$/.test(term) ? { path: term.split('.') } : {}),
    })),
    rows,
  };
}

describe('tmSelectPlan', () => {
  describe('column kinds', () => {
    it('selects a scalar as itself, ordered and filtered by its type', async () => {
      await tmEntityRig(SYSTEM);
      const { select, columns } = plan('acme.Customer', ['CreditLimit', 'IsActive', 'CustomerType']);
      expect(select).toBe('Id,CreditLimit,IsActive,CustomerType');
      expect(columns.map((column) => [column.key, column.kind, column.type, column.filter])).toEqual([
        ['CreditLimit', 'value', 'decimal', 'number'],
        ['IsActive', 'value', 'bool', 'bool'],
        ['CustomerType', 'value', 'enum', 'enum'],
      ]);
      expect(columns[0]!.orderBy('asc')).toBe('CreditLimit');
      expect(columns[0]!.orderBy('desc')).toBe('CreditLimit desc');
    });

    it('keeps a key in canonical case', async () => {
      await tmEntityRig(SYSTEM);
      const { select, columns } = plan('acme.Customer', ['creditlimit', 'currency.code']);
      expect(select).toBe('Id,CreditLimit,Currency.Code');
      expect(columns.map((column) => column.key)).toEqual(['CreditLimit', 'Currency.Code']);
      expect(columns[1]!.label()).toBe('Currency / Code');
    });

    it('selects a twin alone, as a value', async () => {
      await tmEntityRig({ ...SYSTEM, languages: 2 });
      const { select, columns } = plan('acme.Customer', ['Name2']);
      expect(select).toBe('Id,Name2');
      expect(columns[0]!.kind).toBe('value');
      expect(columns[0]!.orderBy('asc')).toBe('Name2');
    });

    it('selects a group in every admitted slot and orders by the first slot in its language', async () => {
      await tmEntityRig({ ...SYSTEM, languages: 2 });
      const { select, columns } = plan('acme.Customer', ['Name']);
      expect(select).toBe('Id,Name,Name2');
      expect(columns[0]!.kind).toBe('text');
      expect(columns[0]!.filter).toBe('text');
      expect(columns[0]!.orderBy('asc')).toBe('Name');
    });

    it('orders a group by its active slot, falling back to the first', async () => {
      await tmEntityRig({ ...SYSTEM, languages: 2, language: 'ar' });
      const { columns } = plan('acme.Customer', ['Name']);
      expect(columns[0]!.orderBy('asc')).toBe('coalesce(Name2, Name)');
      expect(columns[0]!.orderBy('desc')).toBe('coalesce(Name2, Name) desc');
    });

    it('selects only the shape’s slots in a one-language tenant', async () => {
      await tmEntityRig({ ...SYSTEM, languages: 1, language: 'ar' });
      const { select, columns } = plan('acme.Customer', ['Name']);
      expect(select).toBe('Id,Name');
      expect(columns[0]!.orderBy('asc')).toBe('Name');
    });

    it('shows a group’s active slot, else its first', async () => {
      await tmEntityRig({ ...SYSTEM, languages: 2, language: 'ar' });
      const { columns, rows } = plan('acme.Customer', ['Name']);
      const [named, unnamed] = rows(rowSet(['Id', 'Name', 'Name2'], [[1, 'Acme', 'أكمي'], [2, 'Beta', null]]));
      expect(columns[0]!.value(named!)).toBe('أكمي');
      expect(columns[0]!.value(unnamed!)).toBe('Beta');
    });

    it('selects a navigation’s id and its display name, and orders by the name', async () => {
      await tmEntityRig({ ...SYSTEM, languages: 2 });
      const { select, columns } = plan('gl.Center', ['Code', 'Parent', 'Manager']);
      expect(select).toBe('Id,Code,Parent.Id,Parent.Code,Manager.Id,Manager.Name,Manager.Name2');
      expect(columns.map((column) => [column.kind, column.filter, column.descriptor?.target])).toEqual([
        ['value', 'text', undefined],
        ['navigation', 'navigation', 'gl.Center'],
        ['navigation', 'navigation', 'core.User'],
      ]);
      expect(columns[1]!.orderBy('asc')).toBe('Parent.Code');
      expect(columns[2]!.orderBy('desc')).toBe('Manager.Name desc');
    });

    it('orders a navigation by its prefix, then its name in the active slot', async () => {
      await tmEntityRig({ ...SYSTEM, languages: 2, language: 'ar' });
      const { select, columns } = plan('acme.CustomerContact', ['Customer']);
      expect(select).toBe('Id,Customer.Id,Customer.Name,Customer.Name2,Customer.Code');
      expect(columns[0]!.orderBy('asc')).toBe('Customer.Code, coalesce(Customer.Name2, Customer.Name)');
      expect(columns[0]!.orderBy('desc')).toBe('Customer.Code desc, coalesce(Customer.Name2, Customer.Name) desc');
    });

    it('reads a navigation’s value as the related row’s id', async () => {
      await tmEntityRig(SYSTEM);
      const { columns, rows } = plan('gl.Center', ['Parent']);
      const [row] = rows(rowSet(['Id', 'Parent.Id', 'Parent.Code'], [[5, 2, 'R-1']]));
      expect(columns[0]!.value(row!)).toBe(2);
      expect(row!.id).toBe(5);
    });

    it('selects an image’s id and variants, and never orders by it', async () => {
      await tmEntityRig(SYSTEM);
      const { select, columns, rows } = plan('acme.Customer', ['Image']);
      expect(select).toBe('Id,ImageId,Image.Variants');
      expect([columns[0]!.kind, columns[0]!.filter, columns[0]!.orderBy('asc')]).toEqual(['image', null, null]);
      const [row] = rows(rowSet(['Id', 'ImageId', 'Image.Variants'], [[1, 88, 'thumb']]));
      expect(columns[0]!.value(row!)).toBe(88);
    });

    it('selects a file’s id and name, and orders by its name', async () => {
      await tmEntityRig(SYSTEM);
      const { select, columns } = plan('core.OutgoingEmailAttachment', ['File']);
      expect(select).toBe('Id,FileId,File.FileName');
      expect(columns[0]!.kind).toBe('file');
      expect(columns[0]!.orderBy('asc')).toBe('File.FileName');
    });

    it('selects and orders an expression as written, typed by the response', async () => {
      await tmEntityRig(SYSTEM);
      const { select, columns, rows } = plan('acme.Customer', [{ expression: 'CreditLimit * 2', labelKey: 'Acme_Customer' }]);
      const [column] = columns;
      expect(select).toBe('Id,CreditLimit * 2');
      expect([column!.kind, column!.resolution, column!.type, column!.filter]).toEqual(['expression', null, null, null]);
      expect(column!.orderBy('desc')).toBe('CreditLimit * 2 desc');
      expect(column!.label()).toBe('Customer');

      const [row] = rows({
        columns: [
          { name: 'Id', type: 'Numeric', scale: 0, nullable: false, path: ['Id'], groupingKey: false },
          { name: 'CreditLimit * 2', type: 'Numeric', scale: 4, nullable: true, groupingKey: false },
        ],
        rows: [[1, '200.0000']],
      });
      expect(column!.type).toBe('decimal');
      expect(column!.descriptor).toEqual({ type: 'decimal', nullable: true, scale: 4 });
      expect(column!.value(row!)).toBe('200.0000');
    });

    it('takes a declared expression type before any response', async () => {
      await tmEntityRig(SYSTEM);
      const { columns } = plan('acme.Customer', [{ expression: 'Code', labelKey: 'Code', type: 'string' }]);
      expect(columns[0]!.type).toBe('string');
    });
  });

  describe('terms', () => {
    it('selects the root id first and each term once', async () => {
      await tmEntityRig({ ...SYSTEM, languages: 2 });
      const { select, columns } = plan('gl.Center', ['Parent.Code', 'Code', 'Parent', 'Id', 'Code']);
      expect(select).toBe('Id,Parent.Code,Code,Parent.Id');
      expect(columns.map((column) => column.key)).toEqual(['Parent.Code', 'Code', 'Parent', 'Id']);
    });

    it('selects the extra paths and a custom cell’s paths, never as columns', async () => {
      await tmEntityRig({ ...SYSTEM, languages: 2 });
      const { select, columns } = plan('acme.Customer', [{ path: 'Code', select: ['Rank'] }], ['Image', 'Name']);
      expect(select).toBe('Id,Code,Rank,ImageId,Image.Variants,Name,Name2');
      expect(columns.map((column) => column.key)).toEqual(['Code']);
      expect(columns[0]!.input).toEqual({ path: 'Code', select: ['Rank'] });
    });

    it('reads any selected term by path, ignoring case, and each column by its key', async () => {
      await tmEntityRig({ ...SYSTEM, languages: 2 });
      const { rows } = plan('gl.Center', ['Code', 'Parent.Code'], ['IsActive']);
      const [row] = rows(rowSet(['Id', 'Code', 'Parent.Code', 'IsActive'], [[5, 'E-01', 'R-1', true]]));
      expect(row!.get('Parent.Code')).toBe('R-1');
      expect(row!.get('parent.code')).toBe('R-1');
      expect(row!.get('IsActive')).toBe(true);
      expect(row!.get('Name')).toBeUndefined();
      expect((row as unknown as Record<string, unknown>)['Parent.Code']).toBe('R-1');
      expect((row as unknown as Record<string, unknown>)['IsActive']).toBeUndefined();
    });
  });

  describe('dropped keys', () => {
    /** A user who reads customers, and no currency. */
    const READER: TmEntityRigOptions = { access: { securables: [{ resource: 'acme.Customer', action: 'Read', hasFilter: false }] } };

    it('drops a key that does not resolve or that queries cannot read', async () => {
      const { telemetry } = await tmEntityRig(SYSTEM);
      const { select, columns, dropped } = plan('acme.Customer', ['Code', 'Nope', 'Notes', 'Currency.Nope'], [], { source: 'url' });
      expect(select).toBe('Id,Code');
      expect(columns.map((column) => column.key)).toEqual(['Code']);
      expect(dropped).toEqual([
        { key: 'Nope', reason: 'unknown' },
        { key: 'Notes', reason: 'unknown' },
        { key: 'Currency.Nope', reason: 'unknown' },
      ]);
      const events = telemetry.recent().filter((item) => item.name === 'tm.entity.column-dropped');
      expect(events.map((item) => item.props)).toEqual([
        { reason: 'unknown', source: 'url' },
        { reason: 'unknown', source: 'url' },
        { reason: 'unknown', source: 'url' },
      ]);
    });

    it('drops a key the user may not read, by the server’s traversal rule', async () => {
      const { telemetry } = await tmEntityRig(READER);
      const { select, columns, dropped } = plan('acme.Customer', ['Code', 'Currency', 'Currency.Name', 'Currency.Code'], ['Currency.DecimalPlaces'], {
        source: 'preference',
      });
      // A restricted currency projects Id and Code only: its display name is Code.
      expect(select).toBe('Id,Code,Currency.Id,Currency.Code');
      expect(columns.map((column) => column.key)).toEqual(['Code', 'Currency', 'Currency.Code']);
      expect(dropped).toEqual([
        { key: 'Currency.Name', reason: 'unreachable' },
        { key: 'Currency.DecimalPlaces', reason: 'unreachable' },
      ]);
      expect(telemetry.recent().filter((item) => item.name === 'tm.entity.column-dropped').map((item) => item.props)).toEqual([
        { reason: 'unreachable', source: 'preference' },
        { reason: 'unreachable', source: 'preference' },
      ]);
    });

    it('drops an expression that reads a path the user may not read', async () => {
      await tmEntityRig(READER);
      const { select, columns, dropped } = plan('acme.Customer', [
        { expression: "coalesce(Currency.Name, 'none')", labelKey: 'Acme_Customer_Currency' },
        { expression: 'CreditLimit * 2 + len(Currency.Code)', labelKey: 'Acme_Customer' },
      ]);
      expect(select).toBe('Id,CreditLimit * 2 + len(Currency.Code)');
      expect(columns.map((column) => column.key)).toEqual(['CreditLimit * 2 + len(Currency.Code)']);
      expect(dropped).toEqual([{ key: "coalesce(Currency.Name, 'none')", reason: 'unreachable' }]);
    });

    it('drops every key of an entity the user may not read', async () => {
      await tmEntityRig({ access: { securables: [] } });
      const { select, dropped } = plan('acme.Customer', ['Code']);
      expect(select).toBe('Id');
      expect(dropped).toEqual([{ key: 'Code', reason: 'unreachable' }]);
    });

    it('reports a declaration’s drops by default and none when nothing drops', async () => {
      const { telemetry } = await tmEntityRig(SYSTEM);
      plan('acme.Customer', ['Code']);
      expect(telemetry.recent().some((item) => item.name === 'tm.entity.column-dropped')).toBe(false);
      plan('acme.Customer', ['Nope']);
      expect(telemetry.recent().find((item) => item.name === 'tm.entity.column-dropped')?.props).toEqual({ reason: 'unknown', source: 'declaration' });
    });
  });

  describe('wire objects', () => {
    it('nests a plan’s navigation paths by their JSON names and leaves out its expressions', async () => {
      const { entities } = await tmEntityRig({ ...SYSTEM, languages: 2 });
      const { select } = plan('acme.Customer', ['Code', 'Currency', { expression: 'CreditLimit * 2', labelKey: 'Code' }]);
      expect(select).toBe('Id,Code,Currency.Id,Currency.Code,CreditLimit * 2');
      const set = rowSet(select.split(','), [[1, 'C-1', 3, 'SAR', '20.0000']], { 'CreditLimit * 2': 'Numeric' });
      expect(tmRowsToObjects(entities.get('acme.Customer'), set)).toEqual([{ id: 1, code: 'C-1', currency: { id: 3, code: 'SAR' } }]);
    });
  });
});
