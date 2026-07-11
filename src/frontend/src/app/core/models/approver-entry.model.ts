/** One approver picker's local state: an existing system user, or an external signer (name + email, no account). */
export interface ApproverEntry {
  mode: 'user' | 'external';
  userId: string;
  externalName: string;
  externalEmail: string;
}

export function createEmptyApproverEntry(): ApproverEntry {
  return { mode: 'user', userId: '', externalName: '', externalEmail: '' };
}

const EMAIL_RE = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;

export function isApproverEntryValid(e: ApproverEntry): boolean {
  return e.mode === 'user' ? !!e.userId : !!(e.externalName.trim() && EMAIL_RE.test(e.externalEmail.trim()));
}
