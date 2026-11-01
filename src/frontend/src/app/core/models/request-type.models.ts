export interface RequestType {
  id: string;
  name: string;
  description?: string;
  slaHours: number;
  isActive: boolean;
  icon?: string;
  color?: string;
  createdAt: string;
  /** The workflow a contract of this family follows when its template names none (R-M6-8). */
  defaultPresetId?: string | null;
  defaultPresetName?: string | null;
  /** Harvey rule sets that apply to the family. */
  ruleSets?: string[] | null;
}

/** The rule sets a contract family can name (R-M6-8). */
export const RULE_SETS = ['banking', 'competition', 'consumer', 'data-protection', 'employment', 'public-procurement'] as const;

export interface CreateRequestTypeRequest {
  name: string;
  description?: string;
  slaHours: number;
  icon?: string;
  color?: string;
  defaultPresetId?: string | null;
  ruleSets?: string[];
}

export type UpdateRequestTypeRequest = CreateRequestTypeRequest;

export interface AvgSigningTime {
  requestType: string;
  averageHours: number;
  count: number;
}

export interface SlaMetrics {
  nearingExpiration: number;
  expired: number;
  completedWithinSla: number;
  completedTotal: number;
  slaCompliancePercent: number;
  averageSigningTimeByType: AvgSigningTime[];
}
