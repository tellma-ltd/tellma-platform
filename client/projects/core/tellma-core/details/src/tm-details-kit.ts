// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

import { TmDetailsAside } from './tm-details-aside';
import { TmDetailsAudit } from './tm-details-audit';
import { TmDetailsCard, TmDetailsCardSection } from './tm-details-card';
import { TmDetailsExtra, TmDetailsTitleExtra } from './tm-details-extra';
import { TmDetailsTab, TmDetailsTabContent, TmDetailsTabs } from './tm-details-tabs';
import { TmField, TmFieldEdit, TmFieldView } from './tm-field';
import { TmFieldGroup } from './tm-field-group';

// The layout kit as one import (spec 0031 §6): a layout's `imports` takes
// the array, and the compiler keeps only the parts its template uses.

/**
 * Every part of the details layout kit, for a layout's `imports`: cards
 * and their sections, fields and their own templates, the field group,
 * tabs, the aside, the audit card and the record's extras.
 *
 * @example
 * ```ts
 * @Component({
 *   imports: [TmDetailsKit],
 *   template: '<tm-details-card><tm-field path="Name" /></tm-details-card><tm-details-audit />',
 * })
 * export class AcmeCustomerDetails {}
 * ```
 */
export const TmDetailsKit = [
  TmDetailsCard,
  TmDetailsCardSection,
  TmField,
  TmFieldEdit,
  TmFieldView,
  TmFieldGroup,
  TmDetailsTabs,
  TmDetailsTab,
  TmDetailsTabContent,
  TmDetailsAside,
  TmDetailsAudit,
  TmDetailsExtra,
  TmDetailsTitleExtra,
] as const;
