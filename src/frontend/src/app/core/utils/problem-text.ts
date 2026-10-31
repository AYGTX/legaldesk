import { TranslateService } from '@ngx-translate/core';

interface Problem {
  error?: { code?: string; detail?: string; params?: Record<string, unknown>; errors?: Record<string, string[]> };
}

/**
 * The reader's wording for an API refusal: by its code ("errors.circuit.poolEmpty", with the values the server sent)
 * when one is translated, else the validation messages, else the server's sentence, else the fallback.
 */
export function problemText(translate: TranslateService, err: unknown, fallbackKey: string): string {
  const e = (err ?? {}) as Problem;
  const code = e.error?.code;
  if (code) {
    const key = `errors.${code}`;
    const text = translate.instant(key, e.error?.params ?? {});
    if (text !== key) return text;
  }
  const messages = e.error?.errors ? Object.values(e.error.errors).flat().join(' ') : '';
  return messages || e.error?.detail || translate.instant(fallbackKey);
}
