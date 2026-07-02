/*
 * Public API Surface of @tellma/core-ui/contracts.
 *
 * Zero-/low-runtime cross-cutting contract types (SignalLike,
 * TmFormFieldControl, TmFieldError, draft TmCellEditor/TmCellDisplay) plus a
 * couple of pure helpers. This entry point must stay free of Angular and
 * component imports (enforced by lint) so the future data grid can depend on
 * it without pulling in the components.
 */

/** Entry-point marker (replaced by the real contracts in a later stage). */
export const TM_CONTRACTS_ENTRY_POINT = '@tellma/core-ui/contracts';
