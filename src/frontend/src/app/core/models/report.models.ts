import { LifecycleStage } from './contract-intelligence.model';

/** Organization-wide reporting overview — mirrors AdminReportDto (admin/manager only). */
export interface AdminReport {
  totalRequests: number;
  activeRequests: number;
  completedRequests: number;
  rejectedRequests: number;
  expiredRequests: number;
  changesRequestedNow: number;
  completionRatePercent: number;
  avgCompletionHours: number;
  slaCompliancePercent: number;
  slaTrackedCompleted: number;
  totalChangeRequests: number;
  totalRevisionsUploaded: number;
  documentsWithRevisions: number;
  monthlyVolume: MonthlyVolume[];
  byRequestType: TypeBreakdown[];
  byDepartment: DepartmentBreakdown[];
}

export interface MonthlyVolume {
  year: number;
  month: number;
  created: number;
  completed: number;
}

export interface TypeBreakdown {
  name: string;
  color?: string;
  icon?: string;
  count: number;
  completed: number;
  avgCompletionHours: number;
  slaCompliancePercent: number;
}

export interface DepartmentBreakdown {
  department: string;
  count: number;
  completed: number;
  avgCompletionHours: number;
}

/** Portfolio-level risk map aggregated from existing contract risk analyses — mirrors RiskMapDto (admin/manager only). */
export interface RiskMap {
  totalAnalyzed: number;
  avgRiskScore: number;
  bySeverity: SeverityCount[];
  topClauseTypes: ClauseTypeFrequency[];
  byRequestType: ContractFamilyRisk[];
}

export interface SeverityCount {
  severity: string;
  count: number;
}

export interface ClauseTypeFrequency {
  clauseType: string;
  count: number;
}

export interface ContractFamilyRisk {
  requestTypeName: string;
  count: number;
  avgRiskScore: number;
}

/** Portfolio view of where executed contracts sit in their terms — mirrors ContractLifecycleDto. */
export interface ContractLifecycle {
  totalTracked: number;
  expiringIn30Days: number;
  expiringIn60Days: number;
  expiringIn90Days: number;
  noticeDeadlinePassed: number;
  byStage: LifecycleStageCount[];
  upcoming: UpcomingContract[];
  byRequestType: ContractFamilyLifecycle[];
}

export interface LifecycleStageCount {
  stage: LifecycleStage;
  count: number;
}

export interface UpcomingContract {
  documentId: string;
  title: string;
  requestTypeName?: string;
  endDate: string;
  noticeDeadline?: string;
  autoRenews: boolean;
  daysToEnd: number;
  stage: LifecycleStage;
}

export interface ContractFamilyLifecycle {
  requestTypeName: string;
  count: number;
  expiringIn90Days: number;
}

/** A single entry in the organization-wide audit log — mirrors OrgAuditLogDto (admin/manager only). */
export interface OrgAuditLogEntry {
  id: string;
  documentId: string;
  documentTitle: string;
  action: string;
  description?: string;
  ipAddress: string;
  actorName?: string;
  actorEmail?: string;
  timestamp: string;
}
