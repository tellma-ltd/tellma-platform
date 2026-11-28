// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import type { TmEntitiesChunk, TmEntitySummary, TmJsonObject } from '@tellma/core';
import { tmTestEntities } from '@tellma/core-testing';

// A sales invoice for the child grid and child files tests: the recorded
// entity model, patched with an invoice whose lines carry a center, a
// description and an amount, and whose attachments carry a caption and a
// file, as a distribution's invoice would. The invoice copies the recorded
// customer's members, so its own fields render too.

/** The invoice's English labels, beside the rig's own. */
export const TM_SALES_INVOICE_LABELS: Readonly<Record<string, string>> = {
  Acme_SalesInvoice: 'Sales invoice',
  Acme_SalesInvoice_Plural: 'Sales invoices',
  Acme_SalesInvoiceLine: 'Line',
  Acme_SalesInvoiceLine_Plural: 'Lines',
  Acme_SalesInvoiceLine_Center: 'Center',
  Acme_SalesInvoiceLine_Description: 'Description',
  Acme_SalesInvoiceLine_Amount: 'Amount',
  Acme_SalesInvoiceAttachment: 'Attachment',
  Acme_SalesInvoiceAttachment_Plural: 'Attachments',
  Acme_SalesInvoiceAttachment_Caption: 'Caption',
  Acme_SalesInvoiceAttachment_File: 'File',
};

/** An invoice as `sales-invoices/get` returns it: two lines and one attachment. */
export const TM_SALES_INVOICE: TmJsonObject = {
  id: 7,
  code: 'INV-007',
  name: 'March retainer',
  isActive: true,
  lines: [
    { id: 21, salesInvoiceId: 7, centerId: 4, description: 'Consulting', amount: '1000.10' },
    { id: 22, salesInvoiceId: 7, centerId: null, description: 'Travel', amount: '0.20' },
  ],
  attachments: [{ id: 31, salesInvoiceId: 7, caption: 'Signed copy', fileId: 501 }],
};

/** The rows the read relates to the invoice: the line's center and the attachment's blob. */
export const TM_SALES_INVOICE_RELATED: Readonly<Record<string, readonly TmJsonObject[]>> = {
  'gl.Center': [{ id: 4, code: 'C-4', name: 'Sales', isActive: true }],
  'core.Blob': [{ id: 501, kind: 'invoice-attachment', fileName: 'invoice.pdf', contentType: 'application/pdf', size: '2048', variants: '' }],
};

/** The invoice attachments' blob kind: PDFs, PNGs and text, up to 1 MB. */
export const TM_INVOICE_ATTACHMENT_POLICY = {
  kind: 'invoice-attachment',
  accept: ['application/pdf', 'image/png', 'text/plain'],
  maxSize: 1048576,
  image: null,
  thumbnail: false,
  readAccess: 'owner-read',
} as const;

/**
 * The Core chunk's summaries of the invoice, its lines and its attachments.
 *
 * @returns The summaries.
 */
export function tmSalesInvoiceSummaries(): readonly TmEntitySummary[] {
  const summary = (name: string, segment: string, kind: string, operations: readonly string[]): TmEntitySummary =>
    ({
      name: `acme.${name}`,
      segment,
      labelKey: `Acme_${name}`,
      pluralLabelKey: `Acme_${name}_Plural`,
      newLabelKey: 'Entity_New',
      operations,
      capabilities: [],
      displayName: { source: 'Id', multilingual: false },
      kind,
    }) as unknown as TmEntitySummary;
  return [
    { ...summary('SalesInvoice', 'sales-invoices', 'stack', ['query', 'details', 'save', 'delete']), displayName: { source: 'Name', multilingual: true, prefix: 'Code' } } as TmEntitySummary,
    summary('SalesInvoiceLine', 'sales-invoice-lines', 'child', ['query']),
    summary('SalesInvoiceAttachment', 'sales-invoice-attachments', 'child', ['query']),
  ];
}

/**
 * A scalar property of the patched entities.
 *
 * @param entity - The entity's label prefix, such as `SalesInvoiceLine`.
 * @param name - The property's name.
 * @param type - Its type.
 * @param extra - Its other members.
 * @returns The property's metadata.
 */
function property(entity: string, name: string, type: string, extra: Readonly<Record<string, unknown>> = {}): Readonly<Record<string, unknown>> {
  return {
    name,
    json: name[0]!.toLowerCase() + name.slice(1),
    type,
    queryable: true,
    nullable: true,
    labelKey: `Acme_${entity}_${name.replace(/Id$/, '')}`,
    excel: true,
    ownership: 'editable',
    ...extra,
  };
}

/**
 * The recorded entity model with the sales invoice, its lines and its attachments.
 *
 * @returns The Entities chunk.
 */
export function tmSalesInvoiceChunk(): TmEntitiesChunk {
  const chunk = tmTestEntities().chunk;
  const entities = chunk.entities as unknown as Record<string, unknown>[];
  const customer = entities.find((entity) => entity['name'] === 'acme.Customer')!;
  const contact = entities.find((entity) => entity['name'] === 'acme.CustomerContact')!;

  // --- The invoice: the customer's members, with lines and attachments for children ---
  const invoice = {
    ...structuredClone(customer),
    name: 'acme.SalesInvoice',
    segment: 'sales-invoices',
    labelKey: 'Acme_SalesInvoice',
    pluralLabelKey: 'Acme_SalesInvoice_Plural',
    newLabelKey: 'Entity_New',
    actions: [],
    extras: [],
    children: [
      { name: 'Lines', json: 'lines', entity: 'acme.SalesInvoiceLine', parentKey: 'SalesInvoiceId', maxCount: 1000, labelKey: 'Acme_SalesInvoiceLine_Plural' },
      { name: 'Attachments', json: 'attachments', entity: 'acme.SalesInvoiceAttachment', parentKey: 'SalesInvoiceId', maxCount: 100, labelKey: 'Acme_SalesInvoiceAttachment_Plural' },
    ],
  };

  // --- A child of the invoice: the recorded contact's shape, with its own members ---
  const child = (name: string, label: string, collection: string, properties: Readonly<Record<string, unknown>>[], navigations: Readonly<Record<string, unknown>>[]): Record<string, unknown> => ({
    ...structuredClone(contact),
    name: `acme.${name}`,
    segment: `sales-invoice-${collection.toLowerCase()}`,
    labelKey: `Acme_${label}`,
    pluralLabelKey: `Acme_${label}_Plural`,
    owner: { entity: 'acme.SalesInvoice', navigation: 'SalesInvoice', collection },
    actions: [],
    properties: [
      property(label, 'Id', 'integer', { nullable: false, unique: true, ownership: 'server-owned' }),
      property(label, 'SalesInvoiceId', 'integer', { nullable: false, foreignKeyOf: 'SalesInvoice' }),
      ...properties,
    ],
    navigations: [
      { name: 'SalesInvoice', json: 'salesInvoice', foreignKey: 'SalesInvoiceId', target: 'acme.SalesInvoice', kind: 'owner', nullable: false, labelKey: `Acme_${label}_SalesInvoice` },
      ...navigations,
    ],
  });
  const line = child(
    'SalesInvoiceLine',
    'SalesInvoiceLine',
    'Lines',
    [
      property('SalesInvoiceLine', 'CenterId', 'integer', { foreignKeyOf: 'Center' }),
      property('SalesInvoiceLine', 'Description', 'string', { maxLength: 255 }),
      property('SalesInvoiceLine', 'Amount', 'decimal', { nullable: false, precision: 19, scale: 2 }),
    ],
    [{ name: 'Center', json: 'center', foreignKey: 'CenterId', target: 'gl.Center', kind: 'reference', nullable: true, labelKey: 'Acme_SalesInvoiceLine_Center' }],
  );
  const attachment = child(
    'SalesInvoiceAttachment',
    'SalesInvoiceAttachment',
    'Attachments',
    [
      property('SalesInvoiceAttachment', 'Caption', 'string', { maxLength: 100 }),
      property('SalesInvoiceAttachment', 'FileId', 'integer', { unique: true, foreignKeyOf: 'File', blob: TM_INVOICE_ATTACHMENT_POLICY }),
    ],
    [{ name: 'File', json: 'file', foreignKey: 'FileId', target: 'core.Blob', kind: 'reference', nullable: true, labelKey: 'Acme_SalesInvoiceAttachment_File' }],
  );
  entities.push(invoice, line, attachment);
  return chunk;
}
