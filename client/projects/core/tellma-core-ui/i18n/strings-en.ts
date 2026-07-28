// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

/**
 * The built-in ENGLISH library strings — the only locale that ships in the
 * core. Every other locale (Arabic included) ships as an optional
 * per-distribution locale pack contributing its strings as a Transloco
 * scope. Plurals use ICU MessageFormat (via @jsverse/transloco-messageformat).
 *
 * Error keys mirror Signal Forms' camelCase error `kind`s one-for-one.
 */
export const TM_UI_STRINGS_EN = {
  errors: {
    required: 'This field is required',
    email: 'Enter a valid email address',
    minLength: 'Enter at least {minLength, plural, one {# character} other {# characters}}',
    maxLength: 'Enter no more than {maxLength, plural, one {# character} other {# characters}}',
    min: 'Enter a value of at least {min}',
    max: 'Enter a value of at most {max}',
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
  grid: {
    loading: 'Loading…',
    empty: 'No records to display',
    newRow: 'New row',
    selectAll: 'Select all rows',
    selectRow: 'Select row',
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
      selection: '{rows} × {cols} selected',
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
      errorJump: 'Error {index} of {count}',
      checkedCount: '{selected} of {total} selected',
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
      counter: '{index} of {count}',
      noMatches: 'No matches',
      next: 'Next match',
      previous: 'Previous match',
      close: 'Close find',
    },
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
    maxSize: 'Up to {maxMb} MB each',
    announce:
      '{accepted, plural, =0 {No files added} one {1 file added} other {# files added}}{rejectedCount, plural, =0 {} one {, 1 file rejected} other {, # files rejected}}',
    rejected: {
      size: '{name} is larger than {maxMb} MB',
      type: '{name} is not an accepted file type',
      count: 'Only {maxFiles, plural, one {# file} other {# files}} can be added',
      folder: 'Folders cannot be dropped — drop files only',
    },
  },
  image: {
    replace: 'Replace image',
    adjust: 'Adjust crop',
    remove: 'Remove image',
    error: 'The image could not be loaded',
    cropSurface: 'Crop area — arrow keys pan, plus and minus zoom',
    zoom: 'Zoom',
    done: 'Done',
    tooLarge: 'The file is larger than {maxMb} MB',
    unsupported: 'The file is not a supported image',
  },
} as const;
