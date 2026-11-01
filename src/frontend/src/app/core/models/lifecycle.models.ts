import { LifecycleStage } from './contract-intelligence.model';

export type RenewalMode = 'None' | 'Automatic' | 'Option';
export type NoticeUnit = 'Days' | 'Months';
export type NoticeForm = 'Letter' | 'Email' | 'RegisteredMail';
export type TermDecision = 'None' | 'Renew' | 'Renegotiate' | 'Terminate' | 'LetExpire';
export type ObligationKind = 'Payment' | 'Report' | 'InsuranceCertificate' | 'Audit' | 'Renewal' | 'Other';
export type ObligationRecurrence = 'None' | 'Monthly' | 'Quarterly' | 'Yearly';
export type ObligationSource = 'Person' | 'Harvey';
export type ObligationStatus = 'Upcoming' | 'Done' | 'Missed' | 'Waived';
export type ObligationAction = 'Confirm' | 'Done' | 'Waive' | 'Delete';

/** A contract's term (R-M10-1), with what the server computes from it. */
export interface ContractTerm {
  startDate?: string | null;
  initialEndDate?: string | null;
  currentEndDate?: string | null;
  renewalMode: RenewalMode;
  renewalMonths?: number | null;
  noticeAmount?: number | null;
  noticeUnit: NoticeUnit;
  noticeForm?: NoticeForm | null;
  confirmed: boolean;
  confirmedByName?: string | null;
  confirmedAt?: string | null;
  noticeDeadline?: string | null;
  stage: LifecycleStage;
  daysToEnd?: number | null;
  daysToNoticeDeadline?: number | null;
  decision: TermDecision;
  decisionAt?: string | null;
  noticeSentAt?: string | null;
  hasNoticeProof: boolean;
}

export interface Obligation {
  id: string;
  title: string;
  kind: ObligationKind;
  dueDate?: string | null;
  recurrence: ObligationRecurrence;
  ownerId?: string | null;
  ownerName?: string | null;
  quote?: string | null;
  source: ObligationSource;
  status: ObligationStatus;
  confirmed: boolean;
}

export interface ContractEvent {
  kind: 'Rollover' | 'Decision' | 'NoticeSent';
  at: string;
  byName?: string | null;
  details?: string | null;
}

export interface ContractLifecycle {
  term?: ContractTerm | null;
  obligations: Obligation[];
  events: ContractEvent[];
}

export interface SetTermRequest {
  startDate: string | null;
  endDate: string | null;
  renewalMode: RenewalMode;
  renewalMonths: number | null;
  noticeAmount: number | null;
  noticeUnit: NoticeUnit;
  noticeForm: NoticeForm | null;
}

export interface ObligationRequest {
  title: string;
  kind: ObligationKind;
  dueDate: string | null;
  recurrence: ObligationRecurrence;
  ownerId?: string | null;
  quote?: string | null;
  source?: ObligationSource;
}
