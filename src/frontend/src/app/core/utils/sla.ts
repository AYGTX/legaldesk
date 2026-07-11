/** Formats an SLA duration in hours into a friendly label, e.g. 72 → "3 days", 24 → "1 day", 5 → "5 hours". */
export function formatSla(hours: number | null | undefined): string {
  if (!hours || hours <= 0) return 'No deadline';
  if (hours % 24 === 0) {
    const days = hours / 24;
    return `${days} day${days === 1 ? '' : 's'}`;
  }
  if (hours < 24) return `${hours} hour${hours === 1 ? '' : 's'}`;
  const days = Math.floor(hours / 24);
  const rem = hours % 24;
  return `${days}d ${rem}h`;
}

export interface RemainingTime {
  text: string;
  expired: boolean;
  /** 'critical' < 6h, 'warning' < 24h, 'ok' otherwise. */
  level: 'expired' | 'critical' | 'warning' | 'ok';
}

/** Computes a human-readable "time remaining" until the given deadline relative to now. */
export function remainingTime(expiresAt: string | null | undefined): RemainingTime | null {
  if (!expiresAt) return null;
  const end = new Date(expiresAt).getTime();
  const ms = end - Date.now();

  if (ms <= 0) return { text: 'Expired', expired: true, level: 'expired' };

  const totalMinutes = Math.floor(ms / 60000);
  const days = Math.floor(totalMinutes / (60 * 24));
  const hours = Math.floor((totalMinutes % (60 * 24)) / 60);
  const minutes = totalMinutes % 60;

  let text: string;
  if (days >= 1) {
    text = `${days} day${days === 1 ? '' : 's'} remaining`;
  } else if (hours >= 1) {
    text = `${hours} hour${hours === 1 ? '' : 's'} remaining`;
  } else {
    text = `${minutes} minute${minutes === 1 ? '' : 's'} remaining`;
  }

  const totalHours = ms / 3600000;
  const level = totalHours < 6 ? 'critical' : totalHours < 24 ? 'warning' : 'ok';
  return { text, expired: false, level };
}
