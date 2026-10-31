import { Component } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterLink } from '@angular/router';
import { MatIconModule } from '@angular/material/icon';
import { TranslatePipe } from '@ngx-translate/core';

type CapabilityStatus = 'available' | 'partial' | 'roadmap';

interface Capability {
  /** i18n suffix under `capabilities.items.*` */
  id: string;
  icon: string;
  status: CapabilityStatus;
  /** Only set for capabilities that are live today — links to the screen that proves it. */
  route?: string;
}

/**
 * Answers the Legal Business Partner requirements brief point by point.
 *
 * Deliberately honest: each capability carries its real status, so a demo can
 * show what runs today and name what is still roadmap instead of improvising.
 */
@Component({
  selector: 'sf-capabilities',
  standalone: true,
  imports: [CommonModule, RouterLink, MatIconModule, TranslatePipe],
  template: `
    <div class="cap-page">
      <div class="page-header">
        <div>
          <h1 class="page-title">{{ 'capabilities.title' | translate }}</h1>
          <p class="page-subtitle">{{ 'capabilities.subtitle' | translate }}</p>
        </div>
      </div>

      <div class="legend">
        @for (s of statuses; track s) {
          <span class="legend-item">
            <span class="badge" [class]="'badge-' + s">{{ 'capabilities.status.' + s | translate }}</span>
            <span class="legend-count">{{ countOf(s) }}</span>
          </span>
        }
      </div>

      <div class="cap-grid">
        @for (c of capabilities; track c.id) {
          <div class="cap-card" [class]="'is-' + c.status">
            <div class="cap-head">
              <mat-icon class="cap-icon">{{ c.icon }}</mat-icon>
              <span class="badge" [class]="'badge-' + c.status">{{ 'capabilities.status.' + c.status | translate }}</span>
            </div>
            <h3 class="cap-title">{{ 'capabilities.items.' + c.id + '.title' | translate }}</h3>
            <p class="cap-desc">{{ 'capabilities.items.' + c.id + '.desc' | translate }}</p>
            @if (c.route) {
              <a class="cap-link" [routerLink]="c.route">
                {{ 'capabilities.seeItLive' | translate }}
                <mat-icon>arrow_forward</mat-icon>
              </a>
            }
          </div>
        }
      </div>

      <p class="cap-foot">{{ 'capabilities.footnote' | translate }}</p>
    </div>
  `,
  styles: [`
    .cap-page { padding: 0; }
    .page-header { margin-bottom: 20px; }
    .page-title { font-size: 22px; font-weight: 500; color: var(--g-text); margin: 0 0 4px; letter-spacing: -0.2px; }
    .page-subtitle { color: var(--g-text-3); margin: 0; font-size: 14px; max-width: 760px; line-height: 1.5; }

    .legend { display: flex; gap: 18px; flex-wrap: wrap; margin-bottom: 20px; }
    .legend-item { display: inline-flex; align-items: center; gap: 8px; }
    .legend-count { font-size: 13px; color: var(--g-text-3); }

    .badge { font-size: 11px; font-weight: 600; letter-spacing: .3px; text-transform: uppercase;
             padding: 3px 8px; border-radius: 999px; white-space: nowrap; }
    .badge-available { background: color-mix(in srgb, var(--g-green) 16%, transparent); color: var(--g-green); }
    .badge-partial   { background: color-mix(in srgb, var(--g-yellow) 20%, transparent); color: var(--g-yellow); }
    .badge-roadmap   { background: color-mix(in srgb, var(--g-text-3) 16%, transparent); color: var(--g-text-3); }

    .cap-grid { display: grid; grid-template-columns: repeat(3, 1fr); gap: 16px; }
    @media (max-width: 1100px) { .cap-grid { grid-template-columns: repeat(2, 1fr); } }
    @media (max-width: 720px)  { .cap-grid { grid-template-columns: 1fr; } }

    .cap-card { background: var(--g-surface); border: 1px solid var(--g-border); border-radius: 12px;
                padding: 18px; display: flex; flex-direction: column; gap: 8px; }
    .cap-card.is-available { border-left: 3px solid var(--g-green); }
    .cap-card.is-partial   { border-left: 3px solid var(--g-yellow); }
    .cap-card.is-roadmap   { border-left: 3px solid var(--g-border); }

    .cap-head { display: flex; align-items: center; justify-content: space-between; }
    .cap-icon { color: var(--g-text-3); }
    .cap-title { font-size: 15px; font-weight: 600; color: var(--g-text); margin: 0; line-height: 1.35; }
    .cap-desc { font-size: 13px; color: var(--g-text-3); margin: 0; line-height: 1.55; flex: 1; }

    .cap-link { display: inline-flex; align-items: center; gap: 4px; font-size: 13px; font-weight: 500;
                color: var(--g-blue); text-decoration: none; margin-top: 4px; }
    .cap-link:hover { text-decoration: underline; }
    .cap-link mat-icon { font-size: 16px; width: 16px; height: 16px; }

    .cap-foot { margin: 24px 0 0; font-size: 12px; color: var(--g-text-muted); max-width: 760px; line-height: 1.6; }
  `]
})
export class CapabilitiesComponent {
  readonly statuses: CapabilityStatus[] = ['available', 'partial', 'roadmap'];

  /** Mirrors the requirements brief one bullet at a time, in the order it was written. */
  readonly capabilities: Capability[] = [
    { id: 'generation',  icon: 'description',        status: 'available', route: '/templates' },
    { id: 'aiReview',    icon: 'auto_awesome',       status: 'available', route: '/contracts' },
    { id: 'workflow',    icon: 'account_tree',       status: 'available', route: '/contracts' },
    { id: 'versioning',  icon: 'history',            status: 'available', route: '/contracts' },
    { id: 'archive',     icon: 'inventory_2',        status: 'available', route: '/insights/audit' },
    { id: 'obligations', icon: 'event_repeat',       status: 'available', route: '/contracts' },
    { id: 'reporting',   icon: 'monitoring',         status: 'available', route: '/insights/reports' },
    { id: 'execution',   icon: 'notifications_active', status: 'available', route: '/insights/reports' },
    { id: 'regWatch',    icon: 'gavel',     status: 'available', route: '/admin/review-rules' },
    { id: 'riskMap',     icon: 'crisis_alert',       status: 'available', route: '/insights/reports' }
  ];

  countOf(status: CapabilityStatus): number {
    return this.capabilities.filter(c => c.status === status).length;
  }
}
