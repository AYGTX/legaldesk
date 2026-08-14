import { LifecycleStage } from '../models';

/** Expiry inside this many days counts as "expiring soon" — matches ContractTermEvaluator. */
export const EXPIRING_SOON_DAYS = 30;

export interface ContractTermStatus {
  stage: LifecycleStage;
  /** Derived from the end date and notice period, never stored. */
  noticeDeadline: Date | null;
  daysToEnd: number | null;
  daysToNoticeDeadline: number | null;
}

/** Midnight-anchored day difference, so the result never depends on the time of day. */
function daysBetween(from: Date, to: Date): number {
  const a = Date.UTC(from.getFullYear(), from.getMonth(), from.getDate());
  const b = Date.UTC(to.getFullYear(), to.getMonth(), to.getDate());
  return Math.round((b - a) / 86_400_000);
}

/**
 * Mirrors the backend `ContractTermEvaluator` so the detail page and the alert sweep agree on what
 * stage a contract is in. Kept deliberately small and rule-for-rule identical; if the ladder or the
 * stage precedence changes on the server, it must change here in the same commit.
 */
export function evaluateContractTerm(
  startDate: string | null | undefined,
  endDate: string | null | undefined,
  noticePeriodDays: number | null | undefined,
  asOf: Date = new Date()
): ContractTermStatus {
  if (!endDate) {
    return { stage: 'Unknown', noticeDeadline: null, daysToEnd: null, daysToNoticeDeadline: null };
  }

  const end = new Date(endDate);
  const daysToEnd = daysBetween(asOf, end);

  let noticeDeadline: Date | null = null;
  let daysToNoticeDeadline: number | null = null;
  if (noticePeriodDays && noticePeriodDays > 0) {
    noticeDeadline = new Date(end);
    noticeDeadline.setDate(noticeDeadline.getDate() - noticePeriodDays);
    daysToNoticeDeadline = daysBetween(asOf, noticeDeadline);
  }

  return {
    stage: determineStage(startDate, asOf, daysToEnd, daysToNoticeDeadline),
    noticeDeadline,
    daysToEnd,
    daysToNoticeDeadline
  };
}

function determineStage(
  startDate: string | null | undefined,
  asOf: Date,
  daysToEnd: number,
  daysToNoticeDeadline: number | null
): LifecycleStage {
  if (daysToEnd < 0) return 'Expired';
  if (startDate && daysBetween(asOf, new Date(startDate)) > 0) return 'NotStarted';
  // A closed notice window outranks a near expiry — it is the more actionable fact.
  if (daysToNoticeDeadline !== null && daysToNoticeDeadline <= 0) return 'NoticeDeadlinePassed';
  return daysToEnd <= EXPIRING_SOON_DAYS ? 'ExpiringSoon' : 'InForce';
}

/** Severity ramp used for the stage chip's colour. */
export function lifecycleStageTone(stage: LifecycleStage): 'neutral' | 'ok' | 'warn' | 'danger' {
  switch (stage) {
    case 'Expired':
    case 'NoticeDeadlinePassed':
      return 'danger';
    case 'ExpiringSoon':
      return 'warn';
    case 'InForce':
      return 'ok';
    default:
      return 'neutral';
  }
}
