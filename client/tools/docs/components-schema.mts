/**
 * The components.json contract (spec 0002 §11) — the single source of truth
 * feeding the showcase, llms.txt, and the @tellma/core-ui-mcp
 * server. Consumers pin/validate `schemaVersion`.
 */
import { z } from 'zod';

export const COMPONENTS_JSON_SCHEMA_VERSION = '1.0.0';

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
});

export const componentDoc = z.object({
  name: z.string(),
  kind: z.enum(['component', 'directive']),
  group: z.string(),
  selector: z.string(),
  entryPoint: z.string(),
  formControl: z.enum(['FormValueControl', 'FormCheckboxControl']).nullable(),
  description: z.string(),
  inputs: z.array(propDoc),
  outputs: z.array(propDoc),
  slots: z.array(slotDoc),
  tokens: z.array(z.string()),
  a11y: z.object({
    roles: z.array(z.string()),
    keyboard: z.array(z.string()),
    notes: z.string(),
  }),
  examples: z.array(exampleDoc),
  harness: z.string(),
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
