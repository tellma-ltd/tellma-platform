// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

/**
 * The chart's built-in English strings, under the `chart` key of the
 * library's strings (`tmUi.chart.description` in a translation table). They
 * ship with the chart, not in the core table, and a locale pack or the
 * app's own table translates or overrides them as it does the core's.
 */
export const TM_CHART_STRINGS_EN = {
  // The plot's accessible name, per type; the title is the spec's.
  name: {
    column: '{title}, column chart',
    bar: '{title}, bar chart',
    line: '{title}, line chart',
    area: '{title}, area chart',
    pie: '{title}, pie chart',
    doughnut: '{title}, doughnut chart',
  },
  // The plot's accessible description; min and max arrive formatted.
  description:
    '{count, plural, one {# point} other {# points}}, from {min} to {max}. Use Show as table to read every value.',
  // A tooltip's value as a share of its category (a normalized stack) or of the whole (a pie).
  share: '{share} ({value})',
  // A labelled value: a tooltip line (a series or the measure, and its value),
  // or a pie or doughnut slice's label (the member and its share).
  point: '{label}: {value}',
} as const;
