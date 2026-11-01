import { Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import { MatSnackBar } from '@angular/material/snack-bar';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { AssistantService } from '../../core/services/assistant.service';
import { OrganisationSettings } from '../../core/models/assistant.models';

/**
 * The organisation's settings: whether contract text may be sent to the Harvey assistant's model (off by default, D3),
 * and the days before a deadline the contract alerts go out (R-M10-3).
 */
@Component({
  selector: 'sf-organisation-settings',
  standalone: true,
  imports: [FormsModule, MatButtonModule, MatFormFieldModule, MatIconModule, MatInputModule, MatProgressSpinnerModule, MatSlideToggleModule, TranslatePipe],
  template: `
    <div class="os-page">
      @if (!settings()) {
        <mat-spinner diameter="32"></mat-spinner>
      } @else {
        @let s = settings()!;
        <section class="os-block">
          <h3><mat-icon>auto_awesome</mat-icon> {{ 'admin.orgSettings.assistantTitle' | translate }}</h3>
          <p class="muted">{{ 'admin.orgSettings.assistantIntro' | translate }}</p>
          @if (s.modelAvailable) {
            <mat-slide-toggle [(ngModel)]="modelEnabled">{{ 'admin.orgSettings.assistantToggle' | translate }}</mat-slide-toggle>
            <p class="muted">{{ 'admin.orgSettings.assistantModel' | translate: { provider: s.provider, model: s.model } }}</p>
          } @else {
            <p class="warn"><mat-icon>info</mat-icon> {{ 'admin.orgSettings.assistantUnavailable' | translate }}</p>
          }
        </section>
        <section class="os-block">
          <h3><mat-icon>notifications_active</mat-icon> {{ 'admin.orgSettings.alertsTitle' | translate }}</h3>
          <p class="muted">{{ 'admin.orgSettings.alertsIntro' | translate }}</p>
          <mat-form-field appearance="outline" class="os-field">
            <mat-label>{{ 'admin.orgSettings.alertsLabel' | translate }}</mat-label>
            <input matInput [(ngModel)]="alertOffsets" placeholder="60,30,14,7,0" />
          </mat-form-field>
        </section>
        <button mat-flat-button color="primary" (click)="save()" [disabled]="saving()">
          {{ 'common.save' | translate }}
        </button>
      }
    </div>
  `,
  styles: [`
    .os-page { display: flex; flex-direction: column; gap: 16px; max-width: 720px; }
    .os-block h3 { display: flex; align-items: center; gap: 8px; margin: 0 0 4px; font-size: 16px; }
    .os-block h3 mat-icon { color: #7c4dff; }
    .os-field { width: 100%; max-width: 320px; }
    .muted { color: #6b7280; font-size: 13px; }
    .warn { display: flex; gap: 6px; align-items: center; color: #9a3412; font-size: 13px; }
  `]
})
export class OrganisationSettingsComponent implements OnInit {
  private readonly assistant = inject(AssistantService);
  private readonly snack = inject(MatSnackBar);
  private readonly translate = inject(TranslateService);

  readonly settings = signal<OrganisationSettings | null>(null);
  readonly saving = signal(false);
  modelEnabled = false;
  alertOffsets = '';

  ngOnInit(): void {
    this.assistant.settings().subscribe(s => this.apply(s));
  }

  save(): void {
    this.saving.set(true);
    this.assistant.saveSettings(this.modelEnabled, this.alertOffsets).subscribe({
      next: s => {
        this.apply(s);
        this.saving.set(false);
        this.snack.open(this.translate.instant('admin.orgSettings.saved'), 'OK', { duration: 3000 });
      },
      error: err => {
        this.saving.set(false);
        this.snack.open(err?.error?.detail ?? this.translate.instant('admin.orgSettings.failed'), 'OK', { duration: 5000 });
      }
    });
  }

  private apply(s: OrganisationSettings): void {
    this.settings.set(s);
    this.modelEnabled = s.modelEnabled;
    this.alertOffsets = s.alertOffsets;
  }
}
