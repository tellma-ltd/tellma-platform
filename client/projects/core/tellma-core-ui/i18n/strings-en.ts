// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

/**
 * The built-in ENGLISH library strings — the only locale that ships in the
 * core. Every other locale (Arabic included) ships as an optional
 * per-distribution locale pack contributing its strings as a Transloco
 * scope. Plurals and selects use ICU MessageFormat.
 *
 * Error keys mirror Signal Forms' camelCase error `kind`s one-for-one.
 */
export const TM_UI_STRINGS_EN = {
  errors: {
    required: 'This field is required',
    email: 'Enter a valid email address',
    minLength: 'Enter at least {minLength, plural, one {# character} other {# characters}}',
    maxLength: 'Enter no more than {maxLength, plural, one {# character} other {# characters}}',
    min: 'Enter a value of at least {min, number}',
    max: 'Enter a value of at most {max, number}',
    pattern: 'The value does not match the expected format',
    minDate: 'Enter a date on or after {minDate}',
    maxDate: 'Enter a date on or before {maxDate}',
    parse: 'Enter a valid number, like {example}',
    numberPrecision:
      'Enter a number with at most {maxDigits, plural, one {# digit} other {# digits}}',
    parseDate: 'Enter a date like {example}',
  },
  datePicker: {
    chooseDate: 'Choose date',
    dialogLabel: 'Choose date',
    previous:
      '{view, select, day {Previous month} month {Previous year} other {Previous years}}',
    next: '{view, select, day {Next month} month {Next year} other {Next years}}',
    switchView:
      '{view, select, day {Choose month} month {Choose year} other {Back to day view}}',
    today: 'Today',
    clear: 'Clear',
  },
  select: {
    placeholder: 'Select an option',
  },
  multiSelect: {
    placeholder: 'Select options',
  },
  l10n: {
    quarter: 'Q{quarter} {year}',
  },
  entityPicker: {
    advancedSearch: 'Advanced search…',
    create: 'Create…',
    edit: 'Edit…',
    advancedTitle: 'Advanced search',
    createTitle: 'Create',
    editTitle: 'Edit',
    magnifier: 'Advanced search',
    noResults: 'No results',
    searchFailed: 'Search failed',
    moreResults:
      '{count, plural, one {Showing 1 match. Type to refine.} other {Showing # matches. Type to refine.}}',
    announce: {
      results: '{count, plural, one {1 result} other {# results}}',
      resultsMore: '{count, number}+ results — more available',
      noResults: 'No results',
      searchFailed: 'Search failed',
      picked: '{label} selected',
    },
    errors: {
      unresolved: 'Select an item from the list',
      noMatch: 'No match for ‘{text}’',
      ambiguous: '‘{text}’ matches more than one item',
      searchFailed: 'Search failed',
    },
  },
  grid: {
    loading: 'Loading…',
    empty: 'No records to display',
    newRow: 'New row',
    selectAll: 'Select all rows',
    selectRow: 'Select row',
    rowActivationHint: 'Press Enter to open a row. Press F2 to reach a link in a cell.',
    // A navigable column header's description: a sortable header, with or
    // without an action (filterKeys is the action's key, such as Alt+↓), and
    // a header whose only interaction is its action.
    headerNavHint:
      '{filter, select, true {Enter to sort. {filterKeys} to filter.} other {Enter to sort.}}',
    headerActionHint: 'Press Enter to open.',
    // A header action's description while its state is on (a filter applied).
    headerActionActive: 'Active',
    menu: {
      cut: 'Cut',
      copy: 'Copy',
      copyWithHeaders: 'Copy with headers',
      paste: 'Paste',
      pasteHint: 'Press {shortcut} to paste',
      insertAbove: 'Insert {count, plural, one {1 row} other {# rows}} above',
      insertBelow: 'Insert {count, plural, one {1 row} other {# rows}} below',
      insertChild: 'Insert child row',
      deleteRows: 'Delete {count, plural, one {1 row} other {# rows}}',
    },
    op: {
      cellEdit: 'cell edit',
      clear: 'clear',
      paste: 'paste',
      fillDown: 'fill down',
      cutMove: 'move',
      rowInsert: 'row insert',
      rowDelete: 'row delete',
      rowMove: 'row move',
      transaction: 'change',
    },
    announce: {
      selection: '{rows, number} × {cols, number} selected',
      selectionAll: 'All cells selected',
      copied: '{cells, plural, one {1 cell} other {# cells}} copied',
      copyRefused: 'Cannot copy a multi-range selection of this shape',
      copyFailed: 'Copy failed — select the cells and copy again',
      cutCancelled: 'Cut cancelled',
      marqueeCleared: 'Marquee cleared',
      pasted:
        '{cells, plural, =0 {Nothing} one {1 cell} other {# cells}} pasted{errors, plural, =0 {} one {, 1 error} other {, # errors}}{pending, plural, =0 {} one {, 1 resolving} other {, # resolving}}',
      pasteRowsDropped:
        '{count, plural, one {1 row} other {# rows}} could not be added — the grid does not create rows',
      undone:
        '{skipped, plural, =0 {Undid {action}} one {Undid {action} — 1 row no longer exists} other {Undid {action} — # rows no longer exist}}',
      redone:
        '{skipped, plural, =0 {Redid {action}} one {Redid {action} — 1 row no longer exists} other {Redid {action} — # rows no longer exist}}',
      undoSkipped: 'Undo skipped — the affected rows no longer exist',
      redoSkipped: 'Redo skipped — the affected rows no longer exist',
      rowsInserted: '{count, plural, one {1 row} other {# rows}} inserted',
      rowsDeleted: '{count, plural, one {1 row} other {# rows}} deleted',
      rowsMoved: '{count, plural, one {1 row} other {# rows}} moved',
      moveRejected: 'Cannot move a row into its own subtree',
      editorCancelledRowRemoved: 'Editing cancelled — the row was removed',
      resolved:
        '{count, plural, one {1 label} other {# labels}} resolved{errors, plural, =0 {} one {, 1 not matched} other {, # not matched}}',
      lazyLoadFailed: 'Could not load child rows',
      errorJump: 'Error {index, number} of {count, number}',
      checkedCount: '{selected, number} of {total, number} selected',
      checkedCountAcross:
        '{onPage, number} of {total, number} selected here, {selected, number} in all',
      sorted: 'Sorted by {column}, {direction, select, asc {ascending} other {descending}}',
      sortCleared: 'Sort cleared',
      loaded: '{count, plural, =0 {No records} one {1 record} other {# records}} loaded',
      loading: 'Loading',
    },
    cellErrors: {
      invalidInput: '‘{text}’ is not a valid {column}.',
      precision:
        '‘{text}’ has too many digits — enter a number with at most {maxDigits, plural, one {# digit} other {# digits}}.',
      notFound: 'No {collection} named ‘{label}’',
      ambiguous: '‘{label}’ matches more than one {collection}',
      resolutionFailed: 'Could not check ‘{label}’ in {collection} — paste it again to retry',
      tally: '{count, plural, one {1 error} other {# errors}}',
      pending: '{count, plural, one {1 cell} other {# cells}} resolving',
      next: 'Next error',
      previous: 'Previous error',
    },
    find: {
      label: 'Find in grid',
      counter: '{index, number} of {count, number}',
      noMatches: 'No matches',
      next: 'Next match',
      previous: 'Previous match',
      close: 'Close find',
    },
    // The keyboard help labels of the grid's key combinations, one per help row.
    keys: {
      move: 'Move to the adjacent cell',
      jump: 'Jump to the edge of the data',
      page: 'Move up or down one page',
      rowEdge: 'Move to the start or end of the row',
      gridEdge: 'Move to the first or last cell',
      extend: 'Extend the selection',
      selectRows: 'Select entire rows',
      selectColumns: 'Select entire columns',
      selectAll: 'Select all cells',
      checkRow: 'Check or uncheck the row',
      checkAll: 'Check or uncheck all rows',
      tab: 'Move to the next or previous cell',
      enter: 'Open the row, edit the cell, or move down',
      openRowNewTab: 'Open the row in a new tab',
      moveUp: 'Move up',
      edit: 'Edit the cell, or reach the link in it',
      openDropdown: 'Open the cell’s dropdown',
      clear: 'Clear the selected cells',
      escape: 'Cancel the cut or copy, or leave the grid',
      menu: 'Open the context menu',
      find: 'Find in the grid',
      copy: 'Copy',
      cut: 'Cut',
      paste: 'Paste',
      fillDown: 'Fill down',
      undo: 'Undo',
      redo: 'Redo',
      insertRows: 'Insert rows above',
      deleteRows: 'Delete the selected rows',
      expand: 'Expand the row',
      collapse: 'Collapse the row',
      headerAction: 'Open the column header’s action, such as its filter',
    },
  },
  menu: {
    opensInNewTab: '(opens in a new tab)',
  },
  alert: {
    info: 'Info:',
    success: 'Success:',
    warning: 'Warning:',
    error: 'Error:',
  },
  modal: {
    close: 'Close',
  },
  preview: {
    download: 'Download',
    print: 'Print',
    unsupported: 'Preview not available',
    unsupportedHint: 'Download the file to view it',
    loadError: 'The file could not be loaded',
    truncated: 'Showing the first 1 MB — download the file for the rest',
  },
  filePicker: {
    hint: 'Drag and drop here, paste, or',
    browse: 'browse',
    acceptedTypes: 'Accepted: {types}',
    maxSize: 'Up to {maxMb, number} MB each',
    maxSizeSingle: 'Up to {maxMb, number} MB',
    announce:
      '{accepted, plural, =0 {No files added} one {1 file added} other {# files added}}{rejectedCount, plural, =0 {} one {, 1 file rejected} other {, # files rejected}}',
    rejected: {
      size: '{name} is larger than {maxMb, number} MB',
      type: '{name} is not an accepted file type',
      count: 'Only {maxFiles, plural, one {# file} other {# files}} can be added',
      folder: 'Folders cannot be dropped — drop files only',
    },
  },
  image: {
    add: 'Add image',
    replace: 'Replace image',
    adjust: 'Adjust crop',
    remove: 'Remove image',
    error: 'The image could not be loaded',
    cropSurface: 'Crop area — arrow keys pan, plus and minus zoom',
    zoom: 'Zoom',
    done: 'Done',
    tooLarge: 'The file is larger than {maxMb, number} MB',
    unsupported: 'The file is not a supported image',
  },
  copy: {
    copy: 'Copy',
    copied: 'Copied',
    // {keys} is the platform's copy shortcut, written as text: "Ctrl+C" or "⌘C".
    manual: 'Press {keys} to copy',
  },
  reference: {
    label: 'Reference',
    copy: 'Copy reference',
  },
  counter: {
    // {max} arrives formatted in the active locale's digits.
    overflow: '{max}+',
  },
  chip: {
    remove: 'Remove {label}',
    // {labels} is the hidden chips' labels, already joined as a list.
    more: '{count, number} more: {labels}',
  },
  toast: {
    region: 'Recent messages',
    close: 'Close',
    // The default overflow pill, when the app supplies none.
    more: '{count, number} more',
  },
  banner: {
    dismiss: 'Dismiss',
    previous: 'Previous message',
    next: 'Next message',
    position: '{index, number} of {count, number}',
  },
  busyOverlay: {
    label: 'Please wait',
  },
  confirm: {
    // The confirm button's label when a caller passes none.
    ok: 'OK',
    cancel: 'Cancel',
  },
  errorDialog: {
    close: 'Close',
    details: 'Details',
    // After the first ten listed items: how many more there are.
    more: 'and {count, number} more',
  },
  card: {
    // The name of a card heading (or its disclosure button) while the card holds errors.
    hasErrors: '{heading}, has errors',
  },
  splitter: {
    label: 'Resize panels',
  },
} as const;
