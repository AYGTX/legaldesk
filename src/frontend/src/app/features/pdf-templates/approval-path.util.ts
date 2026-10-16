import { WorkflowType } from '../../core/models';

/** The slice of a template field the approval path needs. Structural, so the designer's own field type fits. */
export interface PathField {
  tempId: string;
  label: string;
  type: string;
  fillRole: string;
  workflowStage: number;
  preAssignedApproverId?: string;
}

export interface PathSigner {
  tempId: string;
  label: string;
  /** The fixed approver for this signature, if the template names one (field-level first, then template-wide). */
  approverId?: string;
  /** True when the template does not say who signs — the requester is asked when creating the request. */
  atRequestTime: boolean;
}

export interface PathStep {
  /** 1-based position in the approval path. */
  index: number;
  signers: PathSigner[];
}

export type PathWarningCode = 'noApprover' | 'stageGap' | 'stagesIgnored' | 'stageNoSignature';

export interface PathWarning {
  code: PathWarningCode;
  /** Pre-formatted values for the translated message. */
  params: Record<string, string>;
}

export interface ApprovalPath {
  steps: PathStep[];
  warnings: PathWarning[];
  /** How many approvers the person creating a request will be asked to choose. */
  requestTimeCount: number;
  /** field tempId → 1-based step, for every approver field that belongs to a step. */
  stepOf: Map<string, number>;
}

const isApprover = (f: PathField) => f.fillRole === 'Approver';
const isSignature = (f: PathField) => f.type === 'Signature';

/**
 * Derives the approval path the server will build for a template — the same rules as
 * <c>CreateRequestFromPdfTemplateCommand</c>: one recipient per approver signature field, in field order;
 * Sequential runs them one after another, Parallel all at once, and Mixed groups them by workflow stage
 * (same stage = parallel, stages run in ascending order). An approver field that is not a signature
 * follows the recipient of the first signature field that shares its stage.
 *
 * Kept free of Angular so the rules can be read, and tested, on their own.
 */
export function buildApprovalPath(
  fields: readonly PathField[],
  workflowType: WorkflowType,
  defaultApproverId: string | undefined
): ApprovalPath {
  const signatures = fields.filter(f => isApprover(f) && isSignature(f));
  const otherApproverFields = fields.filter(f => isApprover(f) && !isSignature(f));

  const toSigner = (f: PathField): PathSigner => {
    const approverId = f.preAssignedApproverId || defaultApproverId || undefined;
    return { tempId: f.tempId, label: f.label, approverId, atRequestTime: !approverId };
  };

  let groups: PathField[][];
  if (workflowType === 'Parallel') {
    groups = signatures.length ? [signatures] : [];
  } else if (workflowType === 'Mixed') {
    const stages = [...new Set(signatures.map(f => f.workflowStage || 1))].sort((a, b) => a - b);
    groups = stages.map(stage => signatures.filter(f => (f.workflowStage || 1) === stage));
  } else {
    groups = signatures.map(f => [f]);
  }

  const steps: PathStep[] = groups.map((group, i) => ({ index: i + 1, signers: group.map(toSigner) }));

  const stepOf = new Map<string, number>();
  steps.forEach(step => step.signers.forEach(s => stepOf.set(s.tempId, step.index)));

  // A non-signature approver field is filled by the recipient of the first signature sharing its stage,
  // or the first recipient overall when no signature has that stage.
  for (const f of otherApproverFields) {
    const owner = signatures.find(s => (s.workflowStage || 1) === (f.workflowStage || 1)) ?? signatures[0];
    const step = owner ? stepOf.get(owner.tempId) : undefined;
    if (step) stepOf.set(f.tempId, step);
  }

  const warnings: PathWarning[] = [];

  if (signatures.length === 0) {
    warnings.push({ code: 'noApprover', params: {} });
  }

  if (workflowType === 'Mixed' && signatures.length > 0) {
    const used = [...new Set(signatures.map(f => f.workflowStage || 1))].sort((a, b) => a - b);
    const contiguous = used.every((stage, i) => stage === i + 1);
    if (!contiguous) warnings.push({ code: 'stageGap', params: { stages: used.join(', ') } });
  }

  if (workflowType !== 'Mixed') {
    const ignored = [...signatures, ...otherApproverFields].filter(f => (f.workflowStage || 1) !== 1);
    if (ignored.length > 0) warnings.push({ code: 'stagesIgnored', params: { count: String(ignored.length) } });
  }

  const signatureStages = new Set(signatures.map(f => f.workflowStage || 1));
  const orphaned = otherApproverFields.filter(f => !signatureStages.has(f.workflowStage || 1));
  if (signatures.length > 0 && orphaned.length > 0) {
    warnings.push({ code: 'stageNoSignature', params: { fields: orphaned.map(f => f.label).join(', ') } });
  }

  const requestTimeCount = signatures.filter(f => !f.preAssignedApproverId && !defaultApproverId).length;

  return { steps, warnings, requestTimeCount, stepOf };
}

/** Renumbers workflow stages to 1..n in their current order, closing any gaps. Returns the new stage per field id. */
export function compactStages(fields: readonly PathField[]): Map<string, number> {
  const approvers = fields.filter(isApprover);
  const stages = [...new Set(approvers.map(f => f.workflowStage || 1))].sort((a, b) => a - b);
  const renumber = new Map(stages.map((stage, i) => [stage, i + 1]));
  return new Map(approvers.map(f => [f.tempId, renumber.get(f.workflowStage || 1)!]));
}
