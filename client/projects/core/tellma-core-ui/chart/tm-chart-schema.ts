// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import type { TmChartJsonSchema, TmChartSpecProblem } from './tm-chart-types';

// The chart spec's JSON Schema and its validator (spec 0034 §7.1). The
// validator evaluates the schema itself, through the small subset of
// JSON Schema the schema uses, so the two can never disagree; it then adds
// what a schema cannot say: §7.2's counts per type, member indexes within
// their lists, and no bidi control in any text.

/** The numbers formats allow for fraction digits, as `tmFormatNumber` does. */
const MAX_DECIMALS = 20;

/**
 * The JSON Schema (draft 2020-12) of {@link TmChartSpec}, for agents and
 * tools that write chart specs. {@link tmValidateChartSpec} checks the
 * schema and the rules it cannot express.
 */
export const TM_CHART_SPEC_SCHEMA: TmChartJsonSchema = {
  $schema: 'https://json-schema.org/draft/2020-12/schema',
  title: 'TmChartSpec',
  description:
    'A chart as JSON: rows index the dimensions’ member lists, and every label is text, never markup.',
  type: 'object',
  required: ['type', 'title', 'dimensions', 'measure', 'rows'],
  additionalProperties: false,
  properties: {
    type: { enum: ['column', 'bar', 'line', 'area', 'pie', 'doughnut', 'card'] },
    title: { type: 'string', description: 'The accessible name; hosts draw visible titles.' },
    dimensions: {
      type: 'array',
      maxItems: 2,
      items: { $ref: '#/$defs/dimension' },
      description: 'In display order: the categories, then the series.',
    },
    measure: {
      type: 'object',
      required: ['label'],
      additionalProperties: false,
      properties: {
        label: { type: 'string' },
        format: { $ref: '#/$defs/format' },
      },
    },
    rows: {
      type: 'array',
      description: 'One per point: the member index in each dimension, then the value.',
      items: {
        type: 'array',
        minItems: 3,
        prefixItems: [
          { type: ['integer', 'null'], minimum: 0 },
          { type: ['integer', 'null'], minimum: 0 },
          { type: ['number', 'null'] },
        ],
        items: false,
      },
    },
    options: {
      type: 'object',
      additionalProperties: false,
      properties: {
        stack: { enum: ['none', 'stacked', 'normalized'] },
        legend: { enum: ['auto', 'none', 'top', 'bottom', 'start', 'end'] },
        markers: { type: 'boolean' },
        labels: { type: 'boolean' },
        palette: { enum: ['categorical', 'sequential', 'diverging'] },
        min: { type: 'number' },
        max: { type: 'number' },
        thresholds: {
          type: 'array',
          items: {
            type: 'object',
            required: ['value', 'tone'],
            additionalProperties: false,
            properties: {
              value: { type: 'number' },
              tone: { enum: ['positive', 'warning', 'negative'] },
            },
          },
        },
      },
    },
  },
  $defs: {
    dimension: {
      type: 'object',
      required: ['label', 'members'],
      additionalProperties: false,
      properties: {
        label: { type: 'string' },
        members: { type: 'array', items: { $ref: '#/$defs/member' } },
      },
    },
    member: {
      type: 'object',
      required: ['label'],
      additionalProperties: false,
      properties: {
        label: { type: 'string' },
        parts: {
          type: 'array',
          items: { type: 'string' },
          description: 'A display name’s parts; joined, they equal the label.',
        },
      },
    },
    format: {
      type: 'object',
      additionalProperties: false,
      properties: {
        minDecimals: { type: 'integer', minimum: 0, maximum: MAX_DECIMALS },
        maxDecimals: { type: 'integer', minimum: 0, maximum: MAX_DECIMALS },
        grouping: { type: 'boolean' },
        percent: { type: 'boolean' },
      },
    },
  },
};

// The dimension counts of spec 0034 §7.2.
/** The fewest and the most dimensions each type takes. */
const DIMENSIONS: Readonly<Record<string, readonly [min: number, max: number]>> = {
  column: [1, 2],
  bar: [1, 2],
  line: [1, 2],
  area: [1, 2],
  pie: [1, 1],
  doughnut: [1, 1],
  card: [0, 0],
};

// The set of spec 0024 §7.9, as the l10n entry point strips it.
/** One bidi control character; not global, so `test` holds no state. */
const BIDI_CONTROL = /[\u061C\u200E\u200F\u202A-\u202E\u2066-\u2069]/;

/** A schema node as the evaluator reads it. */
type Node = { readonly [key: string]: unknown };

/**
 * Checks a chart spec: against {@link TM_CHART_SPEC_SCHEMA}, against the
 * dimension counts of its type and the indexes its rows hold, and for a bidi
 * control character in any text (code `bidi-control`). Returns every
 * problem found, each with a JSON Pointer; an empty list means the spec is
 * valid.
 *
 * The codes: `type`, `required`, `unknown-property`, `enum`,
 * `too-few-items`, `too-many-items`, `minimum` and `maximum` from the
 * schema; `dimension-count` (the type takes another number of dimensions),
 * `member-index` (a row's index is missing, out of its member list, or
 * given for a dimension the chart lacks), `duplicate-point` (two rows at
 * the same members), `row-count` (a card holds more than one row),
 * `parts-mismatch` (a member's parts do not join to its label),
 * `min-max` (the axis minimum is not below its maximum) and `bidi-control`.
 *
 * @example
 * ```ts
 * tmValidateChartSpec({ type: 'pie', title: 'Sales', dimensions: [], measure: { label: 'Sales' }, rows: [] });
 * // [{ pointer: '/dimensions', code: 'dimension-count' }]
 * ```
 */
export function tmValidateChartSpec(spec: unknown): readonly TmChartSpecProblem[] {
  const problems: TmChartSpecProblem[] = [];
  evaluate(TM_CHART_SPEC_SCHEMA, spec, '', problems);
  // The rules beyond the schema need the shape the schema guarantees.
  if (problems.length === 0) {
    checkRules(spec as ValidSpec, problems);
  }
  checkBidi(spec, '', problems);
  return problems;
}

// ---- the schema subset ----

/** Escapes one JSON Pointer reference token. */
function token(key: string | number): string {
  return String(key).replace(/~/g, '~0').replace(/\//g, '~1');
}

/** Whether a value is of one JSON Schema type. */
function isType(value: unknown, type: string): boolean {
  switch (type) {
    case 'null':
      return value === null;
    case 'boolean':
      return typeof value === 'boolean';
    case 'string':
      return typeof value === 'string';
    case 'number':
      return typeof value === 'number' && Number.isFinite(value);
    case 'integer':
      return typeof value === 'number' && Number.isInteger(value);
    case 'array':
      return Array.isArray(value);
    case 'object':
      return typeof value === 'object' && value !== null && !Array.isArray(value);
    default:
      return false;
  }
}

/** Resolves a local `$ref` (`#/$defs/<name>`) against the root schema. */
function resolve(node: Node): Node {
  const ref = node['$ref'];
  if (typeof ref !== 'string') {
    return node;
  }
  const name = ref.replace(/^#\/\$defs\//, '');
  return (TM_CHART_SPEC_SCHEMA['$defs'] as Node)[name] as Node;
}

/**
 * Evaluates a value against a schema node, recording a problem per failed
 * keyword. A value of the wrong type is not looked into further.
 */
function evaluate(raw: Node, value: unknown, pointer: string, problems: TmChartSpecProblem[]): void {
  const node = resolve(raw);
  // Section: type and enum.
  const type = node['type'];
  if (type !== undefined) {
    const types = Array.isArray(type) ? (type as string[]) : [type as string];
    if (!types.some((candidate) => isType(value, candidate))) {
      problems.push({ pointer, code: 'type' });
      return;
    }
  }
  const choices = node['enum'];
  if (Array.isArray(choices) && !choices.includes(value)) {
    problems.push({ pointer, code: 'enum' });
    return;
  }
  // Section: numbers.
  if (typeof value === 'number') {
    if (typeof node['minimum'] === 'number' && value < node['minimum']) {
      problems.push({ pointer, code: 'minimum' });
    }
    if (typeof node['maximum'] === 'number' && value > node['maximum']) {
      problems.push({ pointer, code: 'maximum' });
    }
  }
  // Section: objects: required members, known members, and each member's schema.
  if (isType(value, 'object')) {
    const object = value as Record<string, unknown>;
    const properties = (node['properties'] ?? {}) as Record<string, Node>;
    for (const name of (node['required'] ?? []) as string[]) {
      if (!(name in object)) {
        problems.push({ pointer: `${pointer}/${token(name)}`, code: 'required' });
      }
    }
    for (const [name, member] of Object.entries(object)) {
      const schema = properties[name];
      if (schema !== undefined) {
        evaluate(schema, member, `${pointer}/${token(name)}`, problems);
      } else if (node['additionalProperties'] === false) {
        problems.push({ pointer: `${pointer}/${token(name)}`, code: 'unknown-property' });
      }
    }
  }
  // Section: arrays: counts, the leading positions, and the rest.
  if (Array.isArray(value)) {
    if (typeof node['minItems'] === 'number' && value.length < node['minItems']) {
      problems.push({ pointer, code: 'too-few-items' });
    }
    if (typeof node['maxItems'] === 'number' && value.length > node['maxItems']) {
      problems.push({ pointer, code: 'too-many-items' });
    }
    const prefix = (node['prefixItems'] ?? []) as Node[];
    const items = node['items'];
    value.forEach((item, index) => {
      const at = `${pointer}/${index}`;
      if (index < prefix.length) {
        evaluate(prefix[index]!, item, at, problems);
      } else if (items === false) {
        problems.push({ pointer: at, code: 'too-many-items' });
      } else if (items !== undefined) {
        evaluate(items as Node, item, at, problems);
      }
    });
  }
}

// ---- the rules beyond the schema ----

/** A spec the schema accepted, with optional members still optional. */
interface ValidSpec {
  readonly type: string;
  readonly dimensions: readonly { readonly members: readonly { readonly label: string; readonly parts?: readonly string[] }[] }[];
  readonly rows: readonly (readonly [number | null, number | null, number | null])[];
  readonly options?: { readonly min?: number; readonly max?: number };
}

/** Checks the dimension count of the type, the parts, the rows' indexes, and the axis range. */
function checkRules(spec: ValidSpec, problems: TmChartSpecProblem[]): void {
  // Section: the dimension count of the type.
  const [min, max] = DIMENSIONS[spec.type]!;
  const count = spec.dimensions.length;
  if (count < min || count > max) {
    problems.push({ pointer: '/dimensions', code: 'dimension-count' });
    return;
  }
  // Section: each member's parts join to its label.
  spec.dimensions.forEach((dimension, d) =>
    dimension.members.forEach((member, m) => {
      if (member.parts !== undefined && member.parts.join('') !== member.label) {
        problems.push({ pointer: `/dimensions/${d}/members/${m}/parts`, code: 'parts-mismatch' });
      }
    }),
  );
  // Section: each row's indexes, and one row per point.
  if (spec.type === 'card' && spec.rows.length > 1) {
    problems.push({ pointer: '/rows', code: 'row-count' });
  }
  const seen = new Set<string>();
  spec.rows.forEach((row, r) => {
    let valid = true;
    for (const d of [0, 1] as const) {
      const index = row[d];
      const dimension = spec.dimensions[d];
      const fits = dimension === undefined ? index === null : index !== null && index < dimension.members.length;
      if (!fits) {
        problems.push({ pointer: `/rows/${r}/${d}`, code: 'member-index' });
        valid = false;
      }
    }
    const key = `${row[0]}|${row[1]}`;
    if (valid && seen.has(key)) {
      problems.push({ pointer: `/rows/${r}`, code: 'duplicate-point' });
    }
    seen.add(key);
  });
  // Section: a fixed axis range runs low to high.
  const options = spec.options;
  if (options?.min !== undefined && options.max !== undefined && options.min >= options.max) {
    problems.push({ pointer: '/options/max', code: 'min-max' });
  }
}

/** Reports every string, at any depth, that holds a bidi control character. */
function checkBidi(value: unknown, pointer: string, problems: TmChartSpecProblem[]): void {
  if (typeof value === 'string') {
    if (BIDI_CONTROL.test(value)) {
      problems.push({ pointer, code: 'bidi-control' });
    }
  } else if (Array.isArray(value)) {
    value.forEach((item, index) => checkBidi(item, `${pointer}/${index}`, problems));
  } else if (typeof value === 'object' && value !== null) {
    for (const [name, member] of Object.entries(value)) {
      checkBidi(member, `${pointer}/${token(name)}`, problems);
    }
  }
}
