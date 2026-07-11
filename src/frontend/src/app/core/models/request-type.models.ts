export interface RequestType {
  id: string;
  name: string;
  description?: string;
  slaHours: number;
  isActive: boolean;
  icon?: string;
  color?: string;
  createdAt: string;
}

export interface CreateRequestTypeRequest {
  name: string;
  description?: string;
  slaHours: number;
  icon?: string;
  color?: string;
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
