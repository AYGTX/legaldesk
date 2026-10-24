/** One structural problem found in a template, before any request exists. `code` picks the translated message. */
export interface TemplateLintIssue {
  severity: 'error' | 'warning';
  code:
    | 'emptyContent'
    | 'undefinedPlaceholder'
    | 'unusedField'
    | 'signatureNotPlaced'
    | 'noApprover'
    | 'unclosedCondition'
    | 'duplicateKey'
    | 'unsafeMarkup';
  /** Pre-formatted values for the translated message. */
  params: Record<string, string>;
}

/** The slice of a field the checks need. The editor's own field type fits structurally. */
export interface LintField {
  fieldKey: string;
  type: string;
  role: string;
}

/**
 * Deterministic checks on a template, instant and free: the mistakes an administrator makes between writing the text
 * and the first request failing. The AI review (`/html-templates/review`) looks at what the contract says; this looks
 * at whether the template hangs together.
 *
 * `{{#if …}}` / `{{#else}}` / `{{/if}}` markers are not placeholders; only `{{word}}` is, the same rule the
 * editor's field scan and the backend renderer use.
 */
export function lintTemplate(html: string, fields: readonly LintField[]): TemplateLintIssue[] {
  const issues: TemplateLintIssue[] = [];

  if (!html.trim()) {
    return [{ severity: 'error', code: 'emptyContent', params: {} }];
  }

  const used = new Set<string>();
  for (const m of html.matchAll(/\{\{(\w+)\}\}/g)) used.add(m[1]);

  const known = new Map<string, LintField>();
  const seen = new Set<string>();
  for (const f of fields) {
    if (seen.has(f.fieldKey)) issues.push({ severity: 'error', code: 'duplicateKey', params: { key: f.fieldKey } });
    seen.add(f.fieldKey);
    known.set(f.fieldKey, f);
  }

  for (const key of used) {
    if (!known.has(key)) issues.push({ severity: 'error', code: 'undefinedPlaceholder', params: { key } });
  }

  for (const f of fields) {
    if (used.has(f.fieldKey)) continue;
    issues.push(f.type === 'Signature'
      ? { severity: 'warning', code: 'signatureNotPlaced', params: { key: f.fieldKey } }
      : { severity: 'warning', code: 'unusedField', params: { key: f.fieldKey } });
  }

  if (!fields.some(f => f.role === 'Approver' && f.type === 'Signature')) {
    issues.push({ severity: 'warning', code: 'noApprover', params: {} });
  }

  const opens = (html.match(/\{\{\s*#if\b/gi) ?? []).length;
  const closes = (html.match(/\{\{\s*\/if\s*\}\}/gi) ?? []).length;
  if (opens !== closes) {
    issues.push({ severity: 'error', code: 'unclosedCondition', params: { opens: String(opens), closes: String(closes) } });
  }

  if (/<\s*(script|iframe|object|embed)\b|\son\w+\s*=/i.test(html)) {
    issues.push({ severity: 'warning', code: 'unsafeMarkup', params: {} });
  }

  return issues;
}
