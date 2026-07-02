/**
 * Cross-cutting contracts of the Tellma UI library (spec 0002 §2.1).
 *
 * This entry point is types + pure helpers ONLY: no Angular, no other
 * @tellma packages, no i18n (enforced by lint) — so the future data grid can
 * depend on it without pulling in the components.
 */

/**
 * Read channel a host uses to drive a control it owns — an Angular signal
 * satisfies it structurally.
 */
export type SignalLike<T> = () => T;

/** Read + write channel (the grid owns a cell's value and passes it in). */
export interface WritableSignalLike<T> extends SignalLike<T> {
  set(value: T): void;
  update(fn: (prev: T) => T): void;
}

/**
 * A field error as surfaced to `tm-form-field`: `kind` mirrors the framework
 * ValidationError's `kind` one-for-one ('required', 'minLength', 'email', …
 * camelCase per Signal Forms — NOT reactive forms' 'minlength'); `message` is
 * the human-readable, ALREADY-LOCALIZED text (resolved by the control through
 * the message resolver — the wrapper only decides whether to show it).
 */
export interface TmFieldError {
  readonly kind: string;
  readonly message: string;
}

/**
 * What `tm-form-field` needs to do its job (the MatFormFieldControl seam
 * adapted to Signal Forms — spec §2.1/§3.1). The control re-surfaces the
 * Signal Forms field state it receives via [formField] so the wrapper can
 * apply the display policy and render the localized error text — the FULL
 * state set, not just `invalid`.
 */
export interface TmFormFieldControl {
  /**
   * Id of the actual control element (the <input>), so <label for> targets
   * it and aria wiring resolves.
   */
  readonly controlId: SignalLike<string>;
  /**
   * true = the control renders its own adornment chrome (tm-checkbox,
   * tm-select); false = the field wraps the control in the shared bordered
   * box (tmInput) — see §3.
   */
  readonly ownsChrome: boolean;
  /**
   * Control currently holds no value — drives the field's empty/placeholder
   * styling and "show hint vs error" logic.
   */
  readonly empty: SignalLike<boolean>;
  /**
   * Ids the control currently exposes via aria-describedby (read so the
   * field can merge, not clobber, existing ones).
   */
  readonly describedByIds: SignalLike<readonly string[]>;
  /**
   * Field pushes its hint/error element ids; the control writes them into
   * aria-describedby.
   */
  setDescribedByIds(ids: readonly string[]): void;
  /**
   * For controls whose focusable host is NOT labelable (tm-select's <div>
   * trigger): the field passes its <label> id, the control binds
   * aria-labelledby; native-input hosts omit this — <label for> does the
   * job (§3.1).
   */
  setLabelId?(id: string | null): void;
  // Field state, mirrored from the bound Field (all read-only to the wrapper):
  readonly required: SignalLike<boolean>;
  readonly disabled: SignalLike<boolean>;
  readonly readonly: SignalLike<boolean>;
  readonly touched: SignalLike<boolean>;
  readonly dirty: SignalLike<boolean>;
  readonly invalid: SignalLike<boolean>;
  /** Async validation in progress. */
  readonly pending: SignalLike<boolean>;
  /** Already-localized messages (resolved through the message resolver, §5). */
  readonly errors: SignalLike<readonly TmFieldError[]>;
  /**
   * Optional: field calls this when the user clicks the container chrome
   * (padding/border, not the input itself) so the control focuses itself.
   */
  onContainerClick?(): void;
}

// DRAFT / STUB — TmCellEditor and TmCellDisplay below are forward-compat
// placeholders, not a finished design. They exist only to keep rule 6
// (grid-embeddability) from being foreclosed and to shape the controls'
// internal separation of edit-path vs. display-path. They are properly
// designed and hardened when the actual data grid is built (its real
// requirements will reshape them). Phase 1 does not test-harden them (§9).

/**
 * DRAFT / STUB (see note above). Every grid-embeddable control implements
 * this, so the grid drives them uniformly. The control itself implements it
 * (no separate pattern class); commit/cancel mutate through the write
 * channel.
 */
export interface TmCellEditor<T> {
  /** Host (grid) owns this; commit/cancel write through it. */
  readonly value: WritableSignalLike<T>;
  /** Accept the edit (Enter/Tab in a grid; blur standalone). */
  commit(): void;
  /** Revert to last committed (Esc). */
  cancel(): void;
  focus(): void;
  /** Host forwards; the editor consumes only its own keys. */
  onKeydown(e: KeyboardEvent): void;
}

/**
 * DRAFT / STUB (see note above). Pure display path, no Angular instance
 * required — lets the grid paint thousands of non-edited cells as plain
 * readonly DOM (§9). A grid-facing capability, NOT what the standalone
 * control uses to render its own trigger (§3.4).
 */
export interface TmCellDisplay<T> {
  /** e.g. select → resolved label; text → the string. */
  formatValue(value: T): string;
  /** Optional token-driven class for non-text glyphs (checkbox box). */
  readonlyClass?(value: T): string;
}
