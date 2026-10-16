/**
 * The clause-level redline between two versions of a contract, as returned by
 * `GET /v1/documents/{id}/versions/compare`.
 *
 * The comparison is computed server-side by `ContractDiffer` — deliberately not re-derived here, so
 * what a reviewer reads on screen is exactly what the audit trail recorded them consulting.
 */
export type ClauseChangeType = 'Unchanged' | 'Added' | 'Removed' | 'Modified';

/** One run of words inside a clause; `isChanged` marks it for striking or underlining. */
export interface WordMark {
  text: string;
  isChanged: boolean;
}

export interface ClauseDiff {
  change: ClauseChangeType;
  previousText?: string;
  currentText?: string;
  /** Populated for Removed and Modified clauses; empty otherwise. */
  previousWords: WordMark[];
  /** Populated for Added and Modified clauses; empty otherwise. */
  currentWords: WordMark[];
}

export interface VersionComparison {
  documentId: string;
  fromVersion: number;
  toVersion: number;
  addedCount: number;
  removedCount: number;
  modifiedCount: number;
  unchangedCount: number;
  clauses: ClauseDiff[];
}
