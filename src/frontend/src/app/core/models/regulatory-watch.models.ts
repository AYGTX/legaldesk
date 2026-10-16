export type RegulatoryCategory =
  | 'DataProtection' | 'Labor' | 'Consumer' | 'Commercial' | 'Tax' | 'Telecom' | 'Other';

export type RegulatoryJurisdiction = 'Tunisia' | 'EU' | 'Sectoral';

export type RegulatoryTextStatus = 'Draft' | 'Published' | 'Archived';

export type RegulatoryLanguage = 'en' | 'fr' | 'ar';

export const REGULATORY_CATEGORIES: RegulatoryCategory[] = [
  'DataProtection', 'Labor', 'Consumer', 'Commercial', 'Tax', 'Telecom', 'Other'
];

export const REGULATORY_JURISDICTIONS: RegulatoryJurisdiction[] = ['Tunisia', 'EU', 'Sectoral'];

export interface RegulatoryText {
  id: string;
  title: string;
  reference?: string;
  summary: string;
  keywords: string;
  language: RegulatoryLanguage;
  category: RegulatoryCategory;
  jurisdiction: RegulatoryJurisdiction;
  status: RegulatoryTextStatus;
  effectiveDate?: string;
  requestTypeId?: string;
  requestTypeName?: string;
  createdByName?: string;
  createdAt: string;
  updatedAt?: string;
  /** Where the official text can be read. */
  sourceUrl?: string;
  /** How many articles are stored word for word. */
  articleCount: number;
  /** Set when a person signed off the stored articles against the official source. */
  verifiedAt?: string;
  verifiedByName?: string;
}

export interface RegulatoryTextInput {
  title: string;
  category: RegulatoryCategory;
  jurisdiction: RegulatoryJurisdiction;
  summary: string;
  keywords: string;
  language: RegulatoryLanguage;
  reference?: string | null;
  effectiveDate?: string | null;
  requestTypeId?: string | null;
  sourceUrl?: string | null;
}

export interface RegulatoryArticle {
  id: string;
  number: string;
  text: string;
  order: number;
}

/** How pasted law text would be cut into articles; nothing is saved by asking. */
export interface ArticleSplit {
  preamble: string;
  articles: { number: string; text: string }[];
  warnings: string[];
}
