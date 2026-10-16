export type RiskSeverity = 'Critical' | 'High' | 'Medium' | 'Low';

export type RiskFindingType =
  | 'MissingClause'
  | 'WeakClause'
  | 'Ambiguity'
  | 'Contradiction'
  | 'Imbalance';

export interface RiskFinding {
  clauseType: string;
  severity: RiskSeverity;
  explanation: string;
  // Only the full legal review fills these in — the rules-based scan at upload time, and any
  // analysis stored before the review existed, leave them undefined.
  findingType?: RiskFindingType;
  whyItMatters?: string;
  recommendedAction?: string;
  proposedLanguage?: string;
  contractReference?: string;
}

/** What the analysis could determine about the contract itself. Fields are absent where the text does not say. */
export interface ContractProfile {
  contractType?: string;
  parties?: string;
  purpose?: string;
  jurisdiction?: string;
  governingLaw?: string;
  term?: string;
}

export interface ContractRiskAnalysis {
  riskScore: number;
  findings: RiskFinding[];
  analyzedAt: string;
  profile?: ContractProfile;
  /** What the analysis could not establish — shown to the reader rather than guessed at. */
  limitations?: string[];
}

/** Response of POST /documents/{id}/analyze. */
export interface ContractAnalysisResponse {
  riskAnalysisJson: string;
  generatedAt: string;
  obligationDueDate?: string;
  obligationType?: string;
  obligationRationale?: string;
  obligationConfirmed: boolean;
}

export const RISK_SEVERITY_ORDER: RiskSeverity[] = ['Critical', 'High', 'Medium', 'Low'];

export type ComplianceStatus = 'Addressed' | 'Gap' | 'Unclear';

export interface ComplianceFinding {
  regulationId: string;
  regulationTitle: string;
  status: ComplianceStatus;
  explanation: string;
  // Only the full model review fills these in — the keyword scan it falls back to, and any
  // analysis stored before the review existed, leave them undefined. Severity is set on gaps only.
  severity?: RiskSeverity;
  whyItMatters?: string;
  recommendedAction?: string;
  proposedLanguage?: string;
  contractReference?: string;
  // The article of law the finding rests on, as cited by the review and checked against the legal corpus.
  legalArticleId?: string;
  legalCitation?: string;
  legalQuote?: string;
  citationStatus?: CitationStatus;
}

/** Verified: quote found word for word in a verified article. Unverified: found, but not confirmed as law. Rejected: not in the article. */
export type CitationStatus = 'Verified' | 'Unverified' | 'Rejected' | 'Pending';

export interface LegalArticleRef {
  articleId: string;
  citation: string;
  text: string;
  score: number;
}

/** A contract clause and the verified articles of law the corpus search matched it to — no AI involved. */
export interface ClauseLawMatch {
  clause: string;
  articles: LegalArticleRef[];
}

export interface ComplianceAnalysis {
  complianceScore: number;
  findings: ComplianceFinding[];
  analyzedAt: string;
  /** What the review could not establish — shown to the reader rather than guessed at. */
  limitations?: string[];
  relevantLaw?: ClauseLawMatch[];
}

/** Response of POST /documents/{id}/compliance. */
export interface ComplianceAnalysisResponse {
  complianceAnalysisJson: string;
  generatedAt: string;
}

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

// ── Pre-flight readiness ────────────────────────────────────────────────────────

export type ReadinessVerdict = 'NotEvaluated' | 'Ready' | 'NeedsWork' | 'Blocked';
export type ReadinessItemCategory = 'Risk' | 'Compliance' | 'Obligation' | 'Structure';
export type ReadinessItemStatus = 'Open' | 'Resolved' | 'Dismissed';

/** One adjustment the pre-flight review says the contract needs. */
export interface ReadinessItem {
  id: string;
  category: ReadinessItemCategory;
  severity: RiskSeverity;
  sourceKey: string;
  title: string;
  detail: string;
  whyItMatters?: string;
  recommendedAction?: string;
  proposedLanguage?: string;
  status: ReadinessItemStatus;
  resolutionNote?: string;
  resolvedAt?: string;
}

/**
 * The pre-flight state of a contract. The API returns counts rather than a prose verdict so the
 * sentence is built here, in the reader's language.
 */
export interface Readiness {
  documentId: string;
  verdict: ReadinessVerdict;
  score?: number;
  evaluatedAt?: string;
  openCount: number;
  criticalCount: number;
  highCount: number;
  handledCount: number;
  /** Only populated by a fresh evaluation, not by the stored-state GET. */
  riskScore?: number;
  complianceScore?: number;
  items: ReadinessItem[];
}
