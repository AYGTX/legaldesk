export interface KeyTerm {
  field: string;
  value: string;
  quote: string | null;
  verified: boolean;
}

export interface SuggestedTerm {
  startDate: string | null;
  endDate: string | null;
  autoRenews: boolean | null;
  noticePeriodDays: number | null;
}

export interface KeyTerms {
  terms: KeyTerm[];
  suggested: SuggestedTerm;
  language: string;
}

export interface RenewalNote {
  recommendation: 'Renew' | 'Renegotiate' | 'Terminate' | 'Review';
  summary: string;
  reasons: string[];
  citations: { quote: string; verified: boolean }[];
  stage: string;
  endDate: string | null;
  noticeDeadline: string | null;
  autoRenews: boolean;
  language: string;
}

export interface ContractScheduleRow {
  documentId: string;
  title: string;
  status: string;
  requestTypeName: string | null;
  startDate: string | null;
  endDate: string | null;
  noticeDeadline: string | null;
  daysToEnd: number | null;
  daysToNoticeDeadline: number | null;
  autoRenews: boolean;
  noticePeriodDays: number | null;
  termConfirmed: boolean;
  /** A lifecycle stage, or "MissingDates" when no end date is recorded. */
  stage: string;
  createdAt: string;
}
