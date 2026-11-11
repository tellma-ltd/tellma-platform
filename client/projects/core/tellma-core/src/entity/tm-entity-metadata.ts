// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import type { TmJsonSchema } from '../json/tm-json';

// The entity model the client metadata carries (spec 0029 §2.2): a summary of
// every entity in the eager Core chunk, and its full metadata, with the named
// enums, in the Entities chunk. The emit writes them from the server's model
// at build time; nothing here is tenant-specific. Label keys arrive resolved:
// a `labelFallback` beside a key means no resource declares the key, and the
// fallback is the humanized English the client shows in every language.

/** An operation an entity's API offers. */
export type TmEntityOperation = 'query' | 'details' | 'save' | 'delete' | 'import' | 'export';

/**
 * A value's type as the client sees it. `integer` covers every integer width
 * (a C# `long` also carries `long`); `date` is a calendar date, `datetime` a
 * wall-clock date-time and `instant` a point in time with its offset.
 * `float`, `time`, `json` and `binary` values are never queryable.
 */
export type TmDataType =
  | 'string'
  | 'integer'
  | 'decimal'
  | 'float'
  | 'bool'
  | 'date'
  | 'datetime'
  | 'instant'
  | 'time'
  | 'guid'
  | 'enum'
  | 'json'
  | 'binary';

/** How an entity's display name is built: from which property, multilingual or not, with which prefix. */
export interface TmDisplayNameRule {
  /** The property the name reads, such as `Name`. */
  readonly source: string;
  /** Whether the source is a multilingual group, read in the active slot. */
  readonly multilingual: boolean;
  /** A property shown before the name, such as `Code`. */
  readonly prefix?: string;
}

/**
 * The eager facts about one entity, in the Core chunk: enough for route
 * matching, the launcher and display names before the Entities chunk loads.
 */
export interface TmEntitySummary {
  /** The entity's name, such as `gl.Center`. */
  readonly name: string;
  /** The entity's API resource segment, such as `centers`; `null` for a read-only root. */
  readonly segment: string | null;
  /** The string key of the entity's singular label. */
  readonly labelKey: string;
  /** The string key of the entity's plural label. */
  readonly pluralLabelKey: string;
  /** The string key of the entity's create label, such as "New customer". */
  readonly newLabelKey: string;
  /** The humanized English the emit wrote because no resource declares the label key. */
  readonly labelFallback?: string;
  /** The operations the entity's API offers. */
  readonly operations: readonly TmEntityOperation[];
  /** The capabilities the entity's stack declares. */
  readonly capabilities: readonly string[];
  /** How a row's display name is built. */
  readonly displayName: TmDisplayNameRule;
  /** Whether the entity is a stack, a child of a stack, or a read-only root. */
  readonly kind: 'stack' | 'child' | 'readonly';
  /** The icon a merged declaration sets; never emitted. */
  readonly icon?: string;
}

/** A blob kind's upload policy, carried by every blob reference property. */
export interface TmBlobKindPolicy {
  /** The blob kind, such as `customer-image`. */
  readonly kind: string;
  /** The content types a file picker offers. */
  readonly accept: readonly string[];
  /** The largest file accepted, in bytes. */
  readonly maxSize: number;
  /** How images are re-encoded on upload; `null` when the kind keeps files as they are. */
  readonly image: {
    readonly maxDimension: number;
    readonly fit: 'contain' | 'cover-square';
    readonly format: 'webp' | 'jpeg' | 'png';
  } | null;
  /** Whether a thumbnail variant renders. */
  readonly thumbnail: boolean;
  /** Who may read the blob. */
  readonly readAccess: 'owner-read' | 'any-member';
}

/** The type facts that choose a value's format and control. */
export interface TmValueDescriptor {
  /** The value's type. */
  readonly type: TmDataType;
  /** Whether the value may be empty. */
  readonly nullable: boolean;
  /** The named enum the value takes its values from. */
  readonly enum?: string;
  /** The longest text accepted. */
  readonly maxLength?: number;
  /** The digits after the point of a decimal. */
  readonly scale?: number;
  /** Set on an integer that is a C# `long`, whose values travel as decimal text. */
  readonly long?: true;
  /** The entity whose id the value holds, such as `core.User`. */
  readonly target?: string;
  /** Set when the value is a list; only with `enum` or `target`. */
  readonly array?: true;
}

/** One scalar property of an entity. */
export interface TmPropertyMetadata {
  /** The property's name, as paths and declarations spell it: `CreditLimit`. */
  readonly name: string;
  /** The property's wire name: `creditLimit`. */
  readonly json: string;
  /** The property's type. */
  readonly type: TmDataType;
  /** Whether queries may filter, sort and select by it. */
  readonly queryable: boolean;
  /** Whether the value may be empty. */
  readonly nullable: boolean;
  /** Set when the value is unique across the entity's rows. */
  readonly unique?: true;
  /** Set on an integer that is a C# `long`, whose values travel as decimal text. */
  readonly long?: true;
  /** The string key of the property's label. */
  readonly labelKey: string;
  /** The humanized English the emit wrote because no resource declares the label key. */
  readonly labelFallback?: string;
  /** Whether Excel import and export include the property. */
  readonly excel: boolean;
  /** Who writes the value. */
  readonly ownership: 'editable' | 'write-once' | 'server-owned' | 'derived' | 'database-owned';
  /** Set when a save may leave the value empty and the server fills it. */
  readonly defaulted?: true;
  /** The longest text accepted. */
  readonly maxLength?: number;
  /** The total digits of a decimal. */
  readonly precision?: number;
  /** The digits after the point of a decimal. */
  readonly scale?: number;
  /** The named enum the value takes its values from. */
  readonly enum?: string;
  /** The multilingual group and slot the property fills. */
  readonly multilingual?: { readonly group: string; readonly slot: 1 | 2 | 3 };
  /** The navigation this property is the key of, such as `Parent`. */
  readonly foreignKeyOf?: string;
  /** The upload policy of a blob reference property. */
  readonly blob?: TmBlobKindPolicy;
}

/** One navigation of an entity: a property that points at another entity's row. */
export interface TmNavigationMetadata {
  /** The navigation's name: `Parent`. */
  readonly name: string;
  /** The navigation's wire name: `parent`. */
  readonly json: string;
  /** The property holding the target's id: `ParentId`. */
  readonly foreignKey: string;
  /** The target entity: `gl.Center`. */
  readonly target: string;
  /** Whether it is a plain reference, a child's link to its owner, or a sibling. */
  readonly kind: 'reference' | 'owner' | 'sibling';
  /** Whether the navigation may be empty. */
  readonly nullable: boolean;
  /** The string key of the navigation's label. */
  readonly labelKey: string;
  /** The humanized English the emit wrote because no resource declares the label key. */
  readonly labelFallback?: string;
}

/** One child collection of an entity. */
export interface TmChildMetadata {
  /** The collection's name: `Contacts`. */
  readonly name: string;
  /** The collection's wire name: `contacts`. */
  readonly json: string;
  /** The child entity. */
  readonly entity: string;
  /** The child's property holding its owner's id. */
  readonly parentKey: string;
  /** The most children one owner may have. */
  readonly maxCount: number;
  /** The string key of the collection's label. */
  readonly labelKey: string;
  /** The humanized English the emit wrote because no resource declares the label key. */
  readonly labelFallback?: string;
}

/** One argument of an action, as its generated dialog renders it. */
export interface TmActionField {
  /** The argument's wire name: `creditLimit`. */
  readonly name: string;
  /** The string key of the argument's label. */
  readonly labelKey: string;
  /** The humanized English the emit wrote because no resource declares the label key. */
  readonly labelFallback?: string;
  /** Whether the argument must be given. */
  readonly required: boolean;
  /** The argument's type facts. */
  readonly value: TmValueDescriptor;
}

/** One action an entity's API offers beyond the built-in operations. */
export interface TmActionMetadata {
  /** The action's name: `invite`, `activate`. */
  readonly name: string;
  /** Whether the action runs on records of the entity or is an API call of its own. */
  readonly kind: 'entity' | 'api';
  /** Whether the platform defines the action. */
  readonly builtIn: boolean;
  /** The securable action that gates it; `null` when any member may run it. */
  readonly action: string | null;
  /** The string key of the action's label. */
  readonly labelKey: string;
  /** The humanized English the emit wrote because no resource declares the label key. */
  readonly labelFallback?: string;
  /** Whether the action writes. */
  readonly mutation: boolean;
  /** Whether a retry of the action is safe. */
  readonly idempotent: boolean;
  /** Whether the action destroys data, so it asks for confirmation. */
  readonly destructive: boolean;
  /** Set when the action runs on a read-only tenant. */
  readonly allowWhenReadOnly?: true;
  /** Whether the action takes exactly one record. */
  readonly singleTarget: boolean;
  /** Whether the action accepts a filter in place of ids. */
  readonly supportsFilter: boolean;
  /** What the action returns: the entities it changed, its own result, or nothing. */
  readonly result: 'entities' | 'own' | 'none';
  /** The JSON Schema of the action's arguments. */
  readonly arguments?: TmJsonSchema;
  /** The action's arguments, one per top-level property. */
  readonly fields?: readonly TmActionField[];
}

/** The full metadata of one entity, in the Entities chunk. */
export interface TmEntityMetadata extends TmEntitySummary {
  /** The key property and its type: ids are numbers for `int` and decimal text for `long`. */
  readonly key: { readonly property: string; readonly type: 'int' | 'long' };
  /** The owner of a child entity, and the navigation and collection that link them. */
  readonly owner?: { readonly entity: string; readonly navigation: string; readonly collection: string };
  /** The entity's scalar properties. */
  readonly properties: readonly TmPropertyMetadata[];
  /** The entity's navigations. */
  readonly navigations: readonly TmNavigationMetadata[];
  /** The entity's child collections. */
  readonly children: readonly TmChildMetadata[];
  /** The entity's actions. */
  readonly actions: readonly TmActionMetadata[];
  /** The paths a related row's projection selects. */
  readonly relatedSelect: readonly string[];
  /** The order a query takes when it names none. */
  readonly defaultOrderBy: string;
  /** The properties search matches. */
  readonly searchable: readonly string[];
  /** The properties that identify a row to people. */
  readonly naturalKeys: readonly string[];
  /** The names of the extras a details read may ask for. */
  readonly extras: readonly string[];
  /** The entity's query limits. */
  readonly limits: {
    readonly maxTake: number;
    readonly countCap: number;
    readonly maxSkipWindow: number;
    readonly maxSearchLength: number;
    readonly maxIds: number;
    readonly maxExtras: number;
    readonly maxFilterLength: number;
  };
}

/** One value of a named enum, with its label: exactly one of `labelKey` and `label`. */
export interface TmEnumValueMetadata {
  /** The value as the wire spells it. */
  readonly value: string;
  /** The string key of the value's label. */
  readonly labelKey?: string;
  /** A label that reads the same in every language, such as a zone id. */
  readonly label?: string;
  /** The humanized English the emit wrote because no resource declares the label key. */
  readonly labelFallback?: string;
}

/** A named enum: a C# enum such as `gl.CenterType`, or a value set such as `core.TimeZone`. */
export interface TmEnumMetadata {
  /** The enum's name. */
  readonly name: string;
  /** The enum's values, in order. */
  readonly values: readonly TmEnumValueMetadata[];
  /** Other spellings a value set accepts, each mapped to its value. */
  readonly aliases?: Readonly<Record<string, string>>;
}
