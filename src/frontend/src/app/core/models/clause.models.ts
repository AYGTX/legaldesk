export type ClauseCategory =
  | 'Confidentiality' | 'Liability' | 'Termination' | 'DataProtection'
  | 'IntellectualProperty' | 'Payment' | 'ForceMajeure' | 'GoverningLaw'
  | 'Compliance' | 'Warranty' | 'NonCompete' | 'Other';

export type ClauseStatus = 'Draft' | 'Approved' | 'Deprecated';

export type ClauseLanguage = 'en' | 'fr';

export const CLAUSE_CATEGORIES: ClauseCategory[] = [
  'Confidentiality', 'Liability', 'Termination', 'DataProtection',
  'IntellectualProperty', 'Payment', 'ForceMajeure', 'GoverningLaw',
  'Compliance', 'Warranty', 'NonCompete', 'Other'
];

export type ClauseRiskLevel = 'Low' | 'Medium' | 'High';

/** An approved wording, as a drafter or a reviewer is offered it (M9). */
export interface Clause {
  id: string;
  title: string;
  category: ClauseCategory;
  guidance?: string;
  riskLevel?: ClauseRiskLevel;
  /** The number of the approved wording: contracts and templates cite it. */
  version: number;
  body: string;
  language: ClauseLanguage;
  usageCount: number;
  requestTypeId?: string;
  requestTypeName?: string;
  /** Harvey rules this clause answers. */
  ruleKeys: string[];
}

/** One wording of a clause. An approved wording never changes (R-M9-1). */
export interface ClauseVersion {
  number: number;
  body: string;
  language: ClauseLanguage;
  status: ClauseStatus;
  authorName?: string;
  authorId: string;
  approvedByName?: string;
  approvedAt?: string;
  createdAt: string;
}

/** A clause with every wording it has had, newest first, as the curators see it. */
export interface LibraryClause {
  id: string;
  title: string;
  category: ClauseCategory;
  guidance?: string;
  riskLevel?: ClauseRiskLevel;
  usageCount: number;
  requestTypeId?: string;
  requestTypeName?: string;
  createdByName?: string;
  createdAt: string;
  updatedAt?: string;
  ruleKeys: string[];
  versions: ClauseVersion[];
  approved?: ClauseVersion | null;
  latest?: ClauseVersion | null;
}

export interface ClauseInput {
  title: string;
  category: ClauseCategory;
  body: string;
  language: ClauseLanguage;
  guidance?: string | null;
  riskLevel?: ClauseRiskLevel | null;
  requestTypeId?: string | null;
  /** Added from a finding: the Harvey rule it answers (R-M9-4). */
  ruleKey?: string | null;
}

export interface ClauseDescription {
  title: string;
  category: ClauseCategory;
  guidance?: string | null;
  riskLevel?: ClauseRiskLevel | null;
  requestTypeId?: string | null;
}
