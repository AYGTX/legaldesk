import { Injectable, signal, computed } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { environment } from '../../../environments/environment';

export interface CookiePreferences {
  functional: boolean;
  analytics: boolean;
  decidedAt: string;
}

@Injectable({ providedIn: 'root' })
export class CookieConsentService {
  private readonly STORAGE_KEY = 'sf_cookie_prefs';
  private readonly API = `${environment.apiUrl}/v1/gdpr/consent`;

  readonly preferences = signal<CookiePreferences | null>(this.loadFromStorage());
  readonly bannerVisible = computed(() => this.preferences() === null);

  constructor(private http: HttpClient) {}

  acceptAll(): void {
    this.save({ functional: true, analytics: true });
  }

  acceptNecessaryOnly(): void {
    this.save({ functional: false, analytics: false });
  }

  saveCustom(functional: boolean, analytics: boolean): void {
    this.save({ functional, analytics });
  }

  private save(prefs: Omit<CookiePreferences, 'decidedAt'>): void {
    const full: CookiePreferences = { ...prefs, decidedAt: new Date().toISOString() };
    localStorage.setItem(this.STORAGE_KEY, JSON.stringify(full));
    this.preferences.set(full);

    // Fire-and-forget consent logs to backend for GDPR audit trail
    this.http.post(this.API, { tier: 2, granted: prefs.functional, source: 'cookie-banner' }).subscribe();
    this.http.post(this.API, { tier: 3, granted: prefs.analytics, source: 'cookie-banner' }).subscribe();
  }

  private loadFromStorage(): CookiePreferences | null {
    try {
      const raw = localStorage.getItem(this.STORAGE_KEY);
      return raw ? JSON.parse(raw) : null;
    } catch {
      return null;
    }
  }
}
