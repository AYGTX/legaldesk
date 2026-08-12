export type RegulatoryCategory =
  | 'DataProtection' | 'Labor' | 'Consumer' | 'Commercial' | 'Tax' | 'Telecom' | 'Other';

export type RegulatoryJurisdiction = 'Tunisia' | 'EU' | 'Sectoral';

export type RegulatoryTextStatus = 'Draft' | 'Published' | 'Archived';

export type RegulatoryLanguage = 'en' | 'fr';

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
}
