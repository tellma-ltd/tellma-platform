// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

/**
 * Usage examples: the docs extractor reads each exported
 * const's `template` into components.json (→ llms.txt → the MCP `example`
 * tool), titled by the export name. Keep every template copy-pasteable.
 */

/** A details page: the crumb link back, the title with a status, and the toolbar. */
export const DetailsPage = {
  template: `
    <tm-page-header>
      <a tmPageBack href="/centers">Centers</a>
      <h1 tmPageTitle>Jeddah sales</h1>
      <tm-badge tmPageTitleExtra tone="success">Active</tm-badge>
      <button tmButton tmPageHeaderEnd variant="ghost">Share</button>
      <button tmButton tmPageToolbar variant="primary">Edit</button>
      <button tmButton tmPageToolbar>Duplicate</button>
      <button tmButton tmPageToolbarEnd variant="ghost">Previous</button>
    </tm-page-header>
  `,
};

/** A report page whose parameters wrap in the band below the rows. */
export const ReportWithBand = {
  template: `
    <tm-page-header>
      <h1 tmPageTitle>Trial balance</h1>
      <div tmPageHeaderBand>
        <tm-chip-set><tm-chip label="Period: January" /><tm-chip label="Center: Jeddah" /></tm-chip-set>
      </div>
    </tm-page-header>
  `,
};
