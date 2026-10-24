import { signal } from '@angular/core';

/**
 * Undo/redo for an editor whose state is a snapshot.
 *
 * Two kinds of change reach it: a typed burst (many keystrokes that should undo as one step — {@link typed}) and a
 * deliberate action such as inserting a clause or generating with AI ({@link record}, one step each). The browser's
 * own undo cannot do this: any script that sets a textarea's value wipes its undo stack, and a whole-contract AI
 * generation would then have no way back.
 *
 * Plain TypeScript apart from the two signals, so the stepping rules can be tested without a component.
 */
export class EditHistory<T> {
  private undoStack: T[] = [];
  private redoStack: T[] = [];
  /** The state as of the last committed step: what undo returns to. */
  private stable: T;
  private typingTimer: ReturnType<typeof setTimeout> | null = null;

  readonly canUndo = signal(false);
  readonly canRedo = signal(false);

  constructor(
    initial: T,
    /** The state right now. Snapshots are always taken through this, so it must return a fresh copy. */
    private readonly capture: () => T,
    private readonly restore: (snapshot: T) => void,
    private readonly sameState: (a: T, b: T) => boolean,
    private readonly limit = 100,
    private readonly typingPauseMs = 700
  ) {
    this.stable = initial;
  }

  /** Forgets all history and takes the current state as the starting point (after loading a template). */
  reset(initial: T): void {
    this.clearTimer();
    this.undoStack = [];
    this.redoStack = [];
    this.stable = initial;
    this.sync();
  }

  /** Call after the state changed by typing. A burst of calls becomes one undo step. */
  typed(): void {
    if (this.typingTimer === null) {
      this.pushUndo(this.stable);
      this.redoStack = [];
    } else {
      clearTimeout(this.typingTimer);
    }
    this.typingTimer = setTimeout(() => this.settle(), this.typingPauseMs);
    this.sync();
  }

  /** Runs a deliberate change as its own undo step. */
  record(change: () => void): void {
    this.settle();
    this.pushUndo(this.stable);
    this.redoStack = [];
    change();
    this.stable = this.capture();
    this.sync();
  }

  undo(): void {
    this.settle();
    const previous = this.undoStack.pop();
    if (previous === undefined) return;
    this.redoStack.push(this.stable);
    this.apply(previous);
  }

  redo(): void {
    this.settle();
    const next = this.redoStack.pop();
    if (next === undefined) return;
    this.undoStack.push(this.stable);
    this.apply(next);
  }

  /** Ends a typing burst now: whatever is on screen becomes the state undo will step back from. */
  private settle(): void {
    if (this.typingTimer === null) return;
    this.clearTimer();
    this.stable = this.capture();
  }

  private apply(snapshot: T): void {
    this.restore(snapshot);
    this.stable = snapshot;
    this.sync();
  }

  private pushUndo(snapshot: T): void {
    const top = this.undoStack[this.undoStack.length - 1];
    if (top !== undefined && this.sameState(top, snapshot)) return;
    this.undoStack.push(snapshot);
    if (this.undoStack.length > this.limit) this.undoStack.shift();
  }

  private clearTimer(): void {
    if (this.typingTimer !== null) { clearTimeout(this.typingTimer); this.typingTimer = null; }
  }

  private sync(): void {
    this.canUndo.set(this.undoStack.length > 0 || this.typingTimer !== null);
    this.canRedo.set(this.redoStack.length > 0);
  }
}
