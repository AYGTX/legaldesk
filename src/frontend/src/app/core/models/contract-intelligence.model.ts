export type RiskSeverity = 'Critical' | 'High' | 'Medium' | 'Low';

export const RISK_SEVERITY_ORDER: RiskSeverity[] = ['Critical', 'High', 'Medium', 'Low'];

// ── Contract term ───────────────────────────────────────────────────────────────

/**
 * Where an executed contract sits in its own term. Derived from the recorded dates rather than
 * stored, so it cannot go stale — mirrors the backend LifecycleStage enum.
 */
export type LifecycleStage =
  | 'Unknown' | 'NotStarted' | 'InForce' | 'NoticeDeadlinePassed' | 'ExpiringSoon' | 'Expired';

/** Payload for PATCH /documents/{id}/term. */
export interface ContractTermUpdate {
  startDate: string | null;
  endDate: string | null;
  autoRenews: boolean;
  noticePeriodDays: number | null;
}

// ── Pre-flight review (M8) ───────────────────────────────────────────────────────

export type ReadinessVerdict = 'NotEvaluated' | 'Ready' | 'NeedsWork' | 'Blocked';

/** Open; Fixed (a review of a newer version no longer finds it); Accepted (risk taken, with a reason);
 *  NotApplicable (false positive, with a reason, reported to Harvey). */
export type ReviewItemStatus = 'Open' | 'Fixed' | 'Accepted' | 'NotApplicable';

export type ReviewRunStatus = 'Queued' | 'Running' | 'Completed' | 'Failed';

/** CodeCheck: a check in code; MeaningMatch: close in meaning to the rule's examples; Phrase: a lawyer's wording. */
export type DetectionKind = 'CodeCheck' | 'MeaningMatch' | 'Phrase';

/** A verified article a rule links, quoted word for word. */
export interface ReviewArticle {
  articleId: string;
  citation: string;
  quote: string;
  why?: string | null;
  /** A lawyer-approved plain-language reading: shown after the quote, never instead of it. */
  explanation?: string | null;
}

/** One risk Harvey found in one clause, with where it comes from. */
export interface ReviewItem {
  id: string;
  ruleKey?: string | null;
  ruleVersion?: number | null;
  ruleApprovedBy?: string | null;
  ruleApprovedAt?: string | null;
  severity: RiskSeverity;
  title: string;
  detail: string;
  clauseReference?: string | null;
  quotes: string[];
  detectionKind?: DetectionKind | null;
  confidence?: number | null;
  articles: ReviewArticle[];
  /** Items from before the review rework: the law as text. */
  whyItMatters?: string | null;
  recommendedAction?: string | null;
  proposedLanguage?: string | null;
  status: ReviewItemStatus;
  reason?: string | null;
  decidedBy?: string | null;
  decidedAt?: string | null;
  firstFoundInVersion?: number | null;
  fixedInVersion?: number | null;
}

export interface ReviewRun {
  id: string;
  versionNumber: number;
  status: ReviewRunStatus;
  queuedAt: string;
  startedAt?: string | null;
  finishedAt?: string | null;
  asOfDate: string;
  gridVersion?: string | null;
  findingCount: number;
  /** review.unavailable, review.timeout, review.no_text, review.stale */
  errorCode?: string | null;
}

/**
 * The review of a contract's current version. Counts rather than prose: the sentence is built here, in the reader's
 * language. Items are the current findings; fixedItems are the ones earlier revisions fixed.
 */
export interface Review {
  documentId: string;
  documentVersion: number;
  verdict: ReadinessVerdict;
  readinessScore?: number | null;
  riskScore?: number | null;
  evaluatedAt?: string | null;
  openCount: number;
  criticalCount: number;
  highCount: number;
  handledCount: number;
  latestRun?: ReviewRun | null;
  items: ReviewItem[];
  fixedItems: ReviewItem[];
}

/** A Harvey rule in service and the organisation's setting for it. */
export interface ReviewRuleSetting {
  ruleKey: string;
  version?: number | null;
  approvedBy?: string | null;
  enabled: boolean;
  severityOverride?: RiskSeverity | null;
}
