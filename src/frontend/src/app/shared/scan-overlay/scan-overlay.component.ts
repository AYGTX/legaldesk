import { Component, OnDestroy, OnInit, signal } from '@angular/core';
import { MatIconModule } from '@angular/material/icon';
import { TranslatePipe } from '@ngx-translate/core';

const MESSAGES = [
  'shared.scanOverlay.reading',
  'shared.scanOverlay.blanks',
  'shared.scanOverlay.checkboxes',
  'shared.scanOverlay.signatures',
  'shared.scanOverlay.labels',
  'shared.scanOverlay.almost'
];

/**
 * Shown over a PDF page while Harvey looks for its fields, which takes several seconds: a light sweeps down the page
 * and a status line changes every couple of seconds, so the wait reads as work in progress rather than a frozen
 * screen. Place it inside a `position: relative` page wrapper; it covers the wrapper and blocks clicks on it.
 */
@Component({
  selector: 'sf-scan-overlay',
  standalone: true,
  imports: [MatIconModule, TranslatePipe],
  template: `
    <div class="so" role="status" aria-live="polite">
      <div class="so-beam"></div>
      <span class="so-corner so-tl"></span><span class="so-corner so-tr"></span>
      <span class="so-corner so-bl"></span><span class="so-corner so-br"></span>
      <div class="so-pill">
        <mat-icon class="so-icon">auto_awesome</mat-icon>
        @for (i of [index()]; track i) {
          <span class="so-text">{{ messages[i] | translate }}</span>
        }
        <span class="so-dots"><i></i><i></i><i></i></span>
      </div>
    </div>
  `,
  styles: [`
    :host { position: absolute; inset: 0; z-index: 20; display: block; }
    .so { position: absolute; inset: 0; overflow: hidden; background: rgba(11, 87, 208, 0.06); cursor: progress; }

    .so-beam {
      position: absolute; left: 0; right: 0; height: 90px; top: -90px;
      background: linear-gradient(to bottom, rgba(11, 87, 208, 0) 0%, rgba(11, 87, 208, 0.22) 85%, rgba(11, 87, 208, 0.75) 100%);
      border-bottom: 2px solid #0b57d0;
      box-shadow: 0 6px 18px rgba(11, 87, 208, 0.45);
      animation: so-sweep 2.6s ease-in-out infinite;
    }
    @keyframes so-sweep { 0% { top: -90px; } 100% { top: 100%; } }

    .so-corner { position: absolute; width: 22px; height: 22px; border: 0 solid #0b57d0; opacity: 0.85; animation: so-pulse 1.6s ease-in-out infinite; }
    .so-tl { top: 10px; left: 10px; border-top-width: 3px; border-left-width: 3px; border-top-left-radius: 4px; }
    .so-tr { top: 10px; right: 10px; border-top-width: 3px; border-right-width: 3px; border-top-right-radius: 4px; }
    .so-bl { bottom: 10px; left: 10px; border-bottom-width: 3px; border-left-width: 3px; border-bottom-left-radius: 4px; }
    .so-br { bottom: 10px; right: 10px; border-bottom-width: 3px; border-right-width: 3px; border-bottom-right-radius: 4px; }
    @keyframes so-pulse { 0%, 100% { opacity: 0.35; transform: scale(0.94); } 50% { opacity: 1; transform: scale(1); } }

    .so-pill {
      position: absolute; left: 50%; top: 42%; transform: translate(-50%, -50%);
      display: flex; align-items: center; gap: 10px; padding: 12px 20px;
      background: #fff; color: #174ea6; border-radius: 999px; font-size: 14px; font-weight: 500;
      box-shadow: 0 6px 24px rgba(11, 87, 208, 0.28); white-space: nowrap;
    }
    .so-icon { color: #0b57d0; animation: so-spin 3s linear infinite; }
    @keyframes so-spin { to { transform: rotate(360deg); } }
    .so-text { animation: so-fade 0.45s ease; }
    @keyframes so-fade { from { opacity: 0; transform: translateY(5px); } to { opacity: 1; transform: none; } }

    .so-dots { display: inline-flex; gap: 3px; }
    .so-dots i { width: 5px; height: 5px; border-radius: 50%; background: #0b57d0; animation: so-bounce 1.1s infinite ease-in-out; }
    .so-dots i:nth-child(2) { animation-delay: 0.15s; }
    .so-dots i:nth-child(3) { animation-delay: 0.3s; }
    @keyframes so-bounce { 0%, 80%, 100% { transform: scale(0.5); opacity: 0.4; } 40% { transform: scale(1); opacity: 1; } }

    @media (prefers-reduced-motion: reduce) {
      .so-beam, .so-corner, .so-icon, .so-dots i, .so-text { animation: none; }
      .so-beam { top: 0; height: 100%; background: rgba(11, 87, 208, 0.08); border-bottom: 0; box-shadow: none; }
    }
  `]
})
export class ScanOverlayComponent implements OnInit, OnDestroy {
  readonly messages = MESSAGES;
  readonly index = signal(0);
  private timer?: ReturnType<typeof setInterval>;

  ngOnInit(): void {
    // Stays on the last message ("almost there") rather than looping back to the start if the wait is long.
    this.timer = setInterval(() => this.index.update(i => Math.min(i + 1, MESSAGES.length - 1)), 2400);
  }

  ngOnDestroy(): void {
    clearInterval(this.timer);
  }
}
