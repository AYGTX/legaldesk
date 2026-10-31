import { Component, OnInit, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ActivatedRoute } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { TranslatePipe } from '@ngx-translate/core';
import { ApiService } from '../../core/services/api.service';

interface SealInfo { code: string; organisationName: string; sealedAt: string; }
interface SealCheck { code: string; matches: boolean; sealedAt: string; organisationName: string; }

/**
 * Public check of a sealed contract (R-M5-9), reached from the code printed on its evidence page: who sealed it and when,
 * and whether a PDF someone holds is that sealed file, byte for byte. Nothing of the contract is shown; the file is
 * compared on the server through its SHA-256 and not kept.
 */
@Component({
  selector: 'sf-verify',
  standalone: true,
  imports: [CommonModule, MatButtonModule, MatIconModule, MatProgressSpinnerModule, TranslatePipe],
  template: `
    <div class="vf-page">
      <div class="vf-card">
        <mat-icon class="vf-logo">verified_user</mat-icon>
        <h1>{{ 'verify.title' | translate }}</h1>
        <p class="vf-code">{{ 'verify.code' | translate }} <strong>{{ code }}</strong></p>

        @if (loading()) {
          <mat-spinner diameter="32"></mat-spinner>
        } @else if (info(); as i) {
          <p>{{ 'verify.sealed' | translate: { organisation: i.organisationName, date: (i.sealedAt | date: 'medium') } }}</p>

          <label class="vf-drop">
            <input type="file" accept="application/pdf" (change)="check($event)" hidden />
            <mat-icon>upload_file</mat-icon>
            <span>{{ 'verify.choose' | translate }}</span>
          </label>
          <p class="vf-note">{{ 'verify.privacy' | translate }}</p>

          @if (checking()) { <mat-spinner diameter="28"></mat-spinner> }
          @if (result(); as r) {
            <div class="vf-result" [class.ok]="r.matches" [class.ko]="!r.matches">
              <mat-icon>{{ r.matches ? 'check_circle' : 'cancel' }}</mat-icon>
              <span>{{ (r.matches ? 'verify.matches' : 'verify.differs') | translate: { name: fileName() } }}</span>
            </div>
          }
        } @else {
          <div class="vf-result ko"><mat-icon>help_outline</mat-icon><span>{{ 'verify.unknown' | translate }}</span></div>
        }
        <p class="vf-what">{{ 'verify.what' | translate }}</p>
      </div>
    </div>
  `,
  styles: [`
    .vf-page { min-height: 100vh; display: flex; align-items: center; justify-content: center; padding: 24px 16px; background: var(--g-surface-variant, #f4f6f8); }
    .vf-card { width: 100%; max-width: 560px; background: var(--g-surface, #fff); border-radius: 16px; padding: 32px 28px; text-align: center; box-shadow: 0 2px 12px rgba(0,0,0,.06); }
    .vf-logo { font-size: 44px; width: 44px; height: 44px; color: var(--g-blue, #1a73e8); }
    h1 { font-size: 22px; margin: 8px 0 4px; }
    .vf-code { color: var(--g-text-3, #5f6368); font-size: 14px; }
    .vf-code strong { font-family: Consolas, monospace; letter-spacing: .05em; }
    .vf-drop { display: inline-flex; align-items: center; gap: 8px; margin: 16px 0 6px; padding: 12px 18px; border: 1px dashed var(--g-border, #c7d0dc); border-radius: 10px; cursor: pointer; font-weight: 500; }
    .vf-note, .vf-what { font-size: 12px; color: var(--g-text-muted, #80868b); }
    .vf-what { margin-top: 20px; text-align: left; line-height: 1.5; }
    .vf-result { display: flex; gap: 8px; align-items: center; justify-content: center; margin-top: 14px; padding: 12px; border-radius: 10px; font-weight: 500; text-align: left; }
    .vf-result.ok { background: #e6f4ea; color: #1e6b2e; }
    .vf-result.ko { background: #fce8e6; color: #a50e0e; }
  `]
})
export class VerifyComponent implements OnInit {
  private readonly api = inject(ApiService);
  private readonly route = inject(ActivatedRoute);

  code = '';
  readonly loading = signal(true);
  readonly info = signal<SealInfo | null>(null);
  readonly checking = signal(false);
  readonly result = signal<SealCheck | null>(null);
  readonly fileName = signal('');

  ngOnInit(): void {
    this.code = (this.route.snapshot.paramMap.get('code') ?? '').toUpperCase();
    this.api.get<SealInfo>(`/v1/verify/${encodeURIComponent(this.code)}`).subscribe({
      next: i => { this.info.set(i); this.loading.set(false); },
      error: () => this.loading.set(false)
    });
  }

  check(event: Event): void {
    const file = (event.target as HTMLInputElement).files?.[0];
    if (!file) return;
    this.fileName.set(file.name);
    this.result.set(null);
    this.checking.set(true);
    const form = new FormData();
    form.append('file', file);
    this.api.upload<SealCheck>(`/v1/verify/${encodeURIComponent(this.code)}`, form).subscribe({
      next: r => { this.result.set(r); this.checking.set(false); },
      error: () => this.checking.set(false)
    });
  }
}
