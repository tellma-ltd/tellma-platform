// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

// The components.json contract (spec 0002 §11) — the single source of truth
// feeding llms.txt and the @tellma/core-ui-mcp server. Consumers
// pin/validate `schemaVersion`.
import { z } from 'zod';

// 2.0.0: `harness` widened to string|null — breaking for readers that
// assumed a string, hence the major bump.
// 2.1.0: `group` is a closed set (DOCS_GROUPS); every 2.0.0 value is in it.
// 3.0.0: services join the record (`kind: 'service'`, `selector: null`),
// every entry lists its public `members`, and each example names its
// `language` — breaking for readers that assumed a selector string or a
// closed component|directive kind, hence the major bump.
export const COMPONENTS_JSON_SCHEMA_VERSION = '3.0.0';

/**
 * The docs groups a component's `@tmGroup` tag may name — the families
 * components.json, llms.txt and the MCP `list` tool sort by. A closed set,
 * so a misspelt tag fails docs:build instead of inventing a group.
 * `collection` holds the components that present many items (pagers,
 * trees, reorderable lists, tiles).
 */
export const DOCS_GROUPS = [
  'form-control',
  'control',
  'indicator',
  'media',
  'overlay',
  'layout',
  'collection',
  'grid',
  'files',
] as const;

const propDoc = z.object({
  name: z.string(),
  type: z.string(),
  default: z.string().optional(),
  required: z.boolean(),
  description: z.string(),
  signal: z.enum(['input', 'model', 'output']).optional(),
});

const slotDoc = z.object({
  name: z.string(),
  selector: z.string(),
  contextType: z.string().optional(),
  description: z.string(),
});

const exampleDoc = z.object({
  title: z.string(),
  code: z.string(),
  /** `html` for a template (the co-located *.examples.ts), `ts` for code from the TSDoc. */
  language: z.enum(['html', 'ts']),
});

/** A public method or property beyond the signal inputs and outputs. */
const memberDoc = z.object({
  name: z.string(),
  /** The declaration without its body: `show(toast: TmToast): TmToastRef`. */
  signature: z.string(),
  description: z.string(),
});

export const componentDoc = z.object({
  name: z.string(),
  kind: z.enum(['component', 'directive', 'service']),
  group: z.enum(DOCS_GROUPS),
  /** The element or attribute selector; `null` for a service, which has none. */
  selector: z.string().nullable(),
  entryPoint: z.string(),
  formControl: z.enum(['FormValueControl', 'FormCheckboxControl']).nullable(),
  description: z.string(),
  inputs: z.array(propDoc),
  outputs: z.array(propDoc),
  members: z.array(memberDoc),
  slots: z.array(slotDoc),
  tokens: z.array(z.string()),
  a11y: z.object({
    roles: z.array(z.string()),
    keyboard: z.array(z.string()),
    notes: z.string(),
  }),
  examples: z.array(exampleDoc),
  /** `<ClassName>Harness` in @tellma/core-ui-testing, or null when none exists. */
  harness: z.string().nullable(),
  status: z.enum(['stable', 'experimental', 'deprecated']),
  deprecation: z.object({ since: z.string(), replacement: z.string().optional() }).optional(),
});

export const componentsJson = z.object({
  schemaVersion: z.string(),
  libraryVersion: z.string(),
  components: z.array(componentDoc),
});

export type ComponentsJson = z.infer<typeof componentsJson>;
export type ComponentDoc = z.infer<typeof componentDoc>;
