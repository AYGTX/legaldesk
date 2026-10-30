import { Component, OnInit, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { MatIconModule } from '@angular/material/icon';
import { MatSelectModule } from '@angular/material/select';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { DocumentService } from '../../core/services/document.service';
import { ReviewService } from '../../core/services/review.service';
import { RISK_SEVERITY_ORDER, ReviewRuleSetting, RiskSeverity } from '../../core/models';

/**
 * The organisation's choices about Harvey's rules (rework spec R-M0-7): which rules in service its contracts are
 * reviewed with, and at which severity. The rules themselves are kept and approved by the lawyers in Harvey; a
 * change here applies to the next review of each contract.
 */
@Component({
  selector: 'sf-review-settings',
  standalone: true,
  imports: [CommonModule, MatIconModule, MatSelectModule, MatSlideToggleModule, MatProgressSpinnerModule, MatSnackBarModule, TranslatePipe],
  template: `
    <div class="rs-page">
      <h2>{{ 'admin.reviewRules.title' | translate }}</h2>
      <p class="muted">{{ 'admin.reviewRules.intro' | translate }}</p>

      @if (loading()) {
        <mat-spinner diameter="32"></mat-spinner>
      } @else if (error()) {
        <div class="rs-error"><mat-icon>error_outline</mat-icon> {{ error() }}</div>
      } @else if (rules().length === 0) {
        <p class="muted">{{ 'admin.reviewRules.none' | translate }}</p>
      } @else {
        <table class="rs-table">
          <thead>
            <tr>
              <th>{{ 'admin.reviewRules.rule' | translate }}</th>
              <th>{{ 'admin.reviewRules.version' | translate }}</th>
              <th>{{ 'admin.reviewRules.enabled' | translate }}</th>
              <th>{{ 'admin.reviewRules.severity' | translate }}</th>
            </tr>
          </thead>
          <tbody>
            @for (r of rules(); track r.ruleKey) {
              <tr [class.off]="!r.enabled">
                <td><code>{{ r.ruleKey }}</code></td>
                <td>
                  @if (r.version != null) {
                    v{{ r.version }} @if (r.approvedBy) { <span class="muted">· {{ r.approvedBy }}</span> }
                  } @else {
                    <span class="muted">{{ 'admin.reviewRules.notInService' | translate }}</span>
                  }
                </td>
                <td>
                  <mat-slide-toggle [checked]="r.enabled" [disabled]="saving() === r.ruleKey"
                                    (change)="save(r, $event.checked, r.severityOverride ?? null)"></mat-slide-toggle>
                </td>
                <td>
                  <mat-select class="rs-select" [value]="r.severityOverride ?? ''" [disabled]="!r.enabled || saving() === r.ruleKey"
                              (selectionChange)="save(r, r.enabled, $event.value || null)">
                    <mat-option value="">{{ 'admin.reviewRules.ruleOwn' | translate }}</mat-option>
                    @for (s of severities; track s) {
                      <mat-option [value]="s">{{ ('documents.detail.severity' + s) | translate }}</mat-option>
                    }
                  </mat-select>
                </td>
              </tr>
            }
          </tbody>
        </table>
      }
    </div>
  `,
  styles: [`
    .rs-page { max-width: 900px; padding: 8px 4px; }
    .rs-page h2 { margin: 0 0 4px; font-size: 20px; }
    .muted { color: var(--g-text-muted); font-size: 13px; }
    .rs-table { width: 100%; border-collapse: collapse; margin-top: 16px; font-size: 13.5px; }
    .rs-table th { text-align: left; font-size: 11.5px; text-transform: uppercase; letter-spacing: 0.04em; color: var(--g-text-muted); padding: 8px; border-bottom: 1px solid var(--g-border); }
    .rs-table td { padding: 8px; border-bottom: 1px solid var(--g-border-light); vertical-align: middle; }
    .rs-table tr.off td:first-child { opacity: 0.55; }
    .rs-select { width: 180px; }
    .rs-error { display: flex; gap: 6px; align-items: center; color: var(--g-red); margin-top: 12px; }
  `]
})
export class ReviewSettingsComponent implements OnInit {
  private readonly documents = inject(DocumentService);
  private readonly reviews = inject(ReviewService);
  private readonly snack = inject(MatSnackBar);
  private readonly translate = inject(TranslateService);

  readonly severities = RISK_SEVERITY_ORDER;
  readonly rules = signal<ReviewRuleSetting[]>([]);
  readonly loading = signal(true);
  readonly error = signal<string | null>(null);
  readonly saving = signal<string | null>(null);

  ngOnInit(): void {
    this.documents.getReviewSettings().subscribe({
      next: rules => { this.rules.set(rules); this.loading.set(false); },
      error: err => { this.error.set(this.reviews.toError(err).message); this.loading.set(false); }
    });
  }

  save(rule: ReviewRuleSetting, enabled: boolean, severity: RiskSeverity | null): void {
    this.saving.set(rule.ruleKey);
    this.documents.setReviewSetting(rule.ruleKey, enabled, severity).subscribe({
      next: () => {
        this.saving.set(null);
        this.rules.update(list => list.map(r => r.ruleKey === rule.ruleKey ? { ...r, enabled, severityOverride: severity } : r));
        this.snack.open(this.translate.instant('admin.reviewRules.saved'), 'OK', { duration: 2500 });
      },
      error: () => {
        this.saving.set(null);
        this.snack.open(this.translate.instant('admin.reviewRules.saveFailed'), 'OK', { duration: 4000 });
      }
    });
  }
}
