import { Injectable, inject, signal } from '@angular/core';
import { TranslateService } from '@ngx-translate/core';

export type AppLanguage = 'en' | 'fr';

@Injectable({ providedIn: 'root' })
export class LanguageService {
  private readonly KEY = 'sf_lang';
  private readonly translate = inject(TranslateService);
  readonly supported: AppLanguage[] = ['en', 'fr'];

  readonly current = signal<AppLanguage>(this.loadInitial());

  constructor() {
    this.translate.use(this.current());
  }

  setLanguage(lang: AppLanguage): void {
    this.current.set(lang);
    this.translate.use(lang);
    try { localStorage.setItem(this.KEY, lang); } catch { /* ignore */ }
  }

  private loadInitial(): AppLanguage {
    try {
      const stored = localStorage.getItem(this.KEY) as AppLanguage | null;
      if (stored && this.supported.includes(stored)) return stored;
      const browserLang = navigator.language?.slice(0, 2) as AppLanguage;
      if (this.supported.includes(browserLang)) return browserLang;
    } catch { /* ignore */ }
    return 'en';
  }
}
