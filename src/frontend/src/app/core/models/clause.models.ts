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

export interface Clause {
  id: string;
  title: string;
  category: ClauseCategory;
  body: string;
  guidance?: string;
  language: ClauseLanguage;
  status: ClauseStatus;
  riskLevel?: 'Low' | 'Medium' | 'High';
  version: number;
  usageCount: number;
  requestTypeId?: string;
  requestTypeName?: string;
  createdByName?: string;
  createdAt: string;
  updatedAt?: string;
}

export interface ClauseInput {
  title: string;
  category: ClauseCategory;
  body: string;
  language: ClauseLanguage;
  guidance?: string | null;
  riskLevel?: string | null;
  requestTypeId?: string | null;
}
