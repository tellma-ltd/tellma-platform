// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import type { TmJson } from '../json/tm-json';

// The administration sections of the Core chunk, as camelCase mirrors of the
// server's records: the securables (spec 0014 §10), the setting keys and
// categories (spec 0013 §5.3) and the job handlers (spec 0020 §3.7). The
// stock admin pages read them (spec 0032 §7.3, §12.3, §12.4). Members the
// server leaves null are absent.

/** A securable's owner: a child entity's grants reach it through a navigation of its owner. */
export interface TmSecurableOwnerMetadata {
  /** The owner's resource. */
  readonly resource: string;
  /** The to-one path from the child to its owner. */
  readonly navigation: string;
}

/** One securable the composition registers: a resource and one of its actions. */
export interface TmSecurableMetadata {
  /** The resource, such as `acme.Customer`. */
  readonly resource: string;
  /** The action, such as `Read`. */
  readonly action: string;
  /** The entity a grant's filter applies to; absent when the securable takes no filter. */
  readonly filterRoot?: string;
  /** Whether the securable is sensitive, so its use asks for a recent sign-in. */
  readonly isSensitive: boolean;
  /** The key of the feature that registers it. */
  readonly feature: string;
  /** The owner of a child securable. */
  readonly owner?: TmSecurableOwnerMetadata;
  /** The string key of the resource's label. */
  readonly resourceLabelKey: string;
  /** The humanized English the emit wrote because no resource declares the label key. */
  readonly resourceLabelFallback?: string;
  /** The string key of the action's label. */
  readonly actionLabelKey: string;
  /** The humanized English the emit wrote because no resource declares the label key. */
  readonly actionLabelFallback?: string;
  /** The string key of the feature's label, which groups securables in the role editor. */
  readonly featureLabelKey: string;
  /** The humanized English the emit wrote because no resource declares the label key. */
  readonly featureLabelFallback?: string;
}

/** Who may read a setting's value: the server only, the client too, or nobody once stored. */
export type TmSettingVisibility = 'Server' | 'Client' | 'Secret';

/** The type of a setting's value. */
export type TmSettingValueType = 'Boolean' | 'Int32' | 'Int64' | 'Decimal' | 'String' | 'Date' | 'Time' | 'Guid' | 'Enum' | 'Json';

/** One setting key, as the settings page needs it. */
export interface TmSettingKeyMetadata {
  /** The key's dotted name, such as `gl.posting.autoNumberOnPost`. */
  readonly name: string;
  /** The settings tab the key belongs to. */
  readonly category: string;
  /** The card inside the tab. */
  readonly group?: string;
  /** Who may read the value. */
  readonly visibility: TmSettingVisibility;
  /** The type of the value. */
  readonly valueType: TmSettingValueType;
  /** Whether the value may be empty. */
  readonly isNullable: boolean;
  /** Whether the value is a list. */
  readonly isArray: boolean;
  /** The named enum of an `Enum` value. */
  readonly enumType?: string;
  /** The values an `Enum` value takes. */
  readonly enumValues?: readonly string[];
  /** The entity whose id the value holds. */
  readonly references?: string;
  /** The value a tenant has until it sets one. */
  readonly defaultJson: TmJson;
  /** The string key of the key's label. */
  readonly labelKey: string;
  /** The string key of the key's description, when a resource declares one. */
  readonly descriptionKey?: string;
}

/** One settings tab: a category of setting keys. */
export interface TmSettingCategoryMetadata {
  /** The category's name, such as `general`. */
  readonly name: string;
  /** The securable resource that gates reading and saving the category. */
  readonly resource: string;
  /** The tab's place among the tabs. */
  readonly order: number;
  /** The string key of the tab's label. */
  readonly labelKey: string;
}

/** The record a job handler's job produces, and the navigation that reaches it. */
export interface TmJobResultMetadata {
  /** The entity of the produced record. */
  readonly entity: string;
  /** The navigation from that record to the job. */
  readonly via: string;
}

/** One background job handler the composition registers. */
export interface TmJobHandlerMetadata {
  /** The handler's key. */
  readonly key: string;
  /** Whether a schedule may run the handler. */
  readonly schedulable: boolean;
  /** The string key of the handler's label. */
  readonly labelKey: string;
  /** The humanized English the emit wrote because no resource declares the label key. */
  readonly labelFallback?: string;
  /** The record the handler's job produces. */
  readonly result?: TmJobResultMetadata;
}
