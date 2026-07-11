import { Injectable, signal, effect } from '@angular/core';

@Injectable({ providedIn: 'root' })
export class ThemeService {
  private readonly KEY = 'sf_theme';

  readonly dark = signal<boolean>(this.loadInitial());

  constructor() {
    // Apply immediately so the class is on <html> before first render
    document.documentElement.classList.toggle('dark', this.dark());

    effect(() => {
      document.documentElement.classList.toggle('dark', this.dark());
      localStorage.setItem(this.KEY, this.dark() ? 'dark' : 'light');
    });
  }

  toggle(): void { this.dark.update(v => !v); }

  private loadInitial(): boolean {
    try {
      const stored = localStorage.getItem(this.KEY);
      if (stored !== null) return stored === 'dark';
      return window.matchMedia?.('(prefers-color-scheme: dark)').matches ?? false;
    } catch { return false; }
  }
}
