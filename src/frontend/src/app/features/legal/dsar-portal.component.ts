import { Component, signal, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { HttpClient } from '@angular/common/http';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatIconModule } from '@angular/material/icon';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { environment } from '../../../environments/environment';

const DSAR_TYPE_KEYS = [
  { value: 1, key: 'legal.dsar.type.access' },
  { value: 2, key: 'legal.dsar.type.erasure' },
  { value: 3, key: 'legal.dsar.type.rectification' },
  { value: 4, key: 'legal.dsar.type.portability' },
  { value: 5, key: 'legal.dsar.type.objection' },
  { value: 6, key: 'legal.dsar.type.restriction' },
];

@Component({
  selector: 'sf-dsar-portal',
  standalone: true,
  imports: [
    RouterLink, ReactiveFormsModule,
    MatButtonModule, MatFormFieldModule, MatInputModule, MatSelectModule, MatIconModule,
    TranslatePipe
  ],
  template: `
    <div class="legal-page">
      <a class="back-link" routerLink="/dashboard">&larr; {{ 'legal.dsar.backToDashboard' | translate }}</a>
      <h1>{{ 'legal.dsar.title' | translate }}</h1>
      <p class="subtitle" [innerHTML]="'legal.dsar.subtitle' | translate"></p>

      @if (!submitted()) {
        <form [formGroup]="form" (ngSubmit)="submit()" class="dsar-form">
          <mat-form-field appearance="outline">
            <mat-label>{{ 'legal.dsar.emailLabel' | translate }}</mat-label>
            <input matInput formControlName="email" type="email" [placeholder]="'legal.dsar.emailPlaceholder' | translate" />
            @if (form.get('email')?.invalid && form.get('email')?.touched) {
              <mat-error>{{ 'legal.dsar.emailError' | translate }}</mat-error>
            }
          </mat-form-field>

          <mat-form-field appearance="outline">
            <mat-label>{{ 'legal.dsar.nameLabel' | translate }}</mat-label>
            <input matInput formControlName="name" [placeholder]="'legal.dsar.namePlaceholder' | translate" />
          </mat-form-field>

          <mat-form-field appearance="outline">
            <mat-label>{{ 'legal.dsar.requestTypeLabel' | translate }}</mat-label>
            <mat-select formControlName="requestType">
              @for (t of dsarTypes; track t.value) {
                <mat-option [value]="t.value">{{ t.label }}</mat-option>
              }
            </mat-select>
          </mat-form-field>

          <mat-form-field appearance="outline">
            <mat-label>{{ 'legal.dsar.detailsLabel' | translate }}</mat-label>
            <textarea matInput formControlName="details" rows="4"
              [placeholder]="'legal.dsar.detailsPlaceholder' | translate"></textarea>
          </mat-form-field>

          <div class="form-actions">
            <button mat-flat-button color="primary" type="submit"
              [disabled]="form.invalid || submitting()">
              @if (submitting()) {
                {{ 'legal.dsar.submitting' | translate }}
              } @else {
                {{ 'legal.dsar.submitRequest' | translate }}
              }
            </button>
          </div>

          @if (error()) {
            <p class="form-error">
              <mat-icon>error_outline</mat-icon>
              {{ error() }}
            </p>
          }
        </form>
      } @else {
        <div class="success-card">
          <mat-icon class="success-icon">check_circle</mat-icon>
          <h2>{{ 'legal.dsar.successTitle' | translate }}</h2>
          <p [innerHTML]="'legal.dsar.successBody' | translate: { id: requestId() }"></p>
          <p>{{ 'legal.dsar.successContact' | translate }} <a href="mailto:privacy@signflow.app">privacy&#64;signflow.app</a>.</p>
          <a routerLink="/dashboard" mat-flat-button color="primary">{{ 'legal.dsar.backToDashboard' | translate }}</a>
        </div>
      }
    </div>
  `,
  styleUrl: './dsar-portal.component.scss'
})
export class DsarPortalComponent {
  private fb = inject(FormBuilder);
  private http = inject(HttpClient);
  private translate = inject(TranslateService);

  readonly dsarTypes = DSAR_TYPE_KEYS.map(t => ({ value: t.value, label: this.translate.instant(t.key) }));
  readonly submitted = signal(false);
  readonly submitting = signal(false);
  readonly error = signal<string | null>(null);
  readonly requestId = signal<string>('');

  readonly form = this.fb.group({
    email: ['', [Validators.required, Validators.email, Validators.maxLength(256)]],
    name: [''],
    requestType: [null as number | null, Validators.required],
    details: ['']
  });

  submit(): void {
    if (this.form.invalid) return;
    this.submitting.set(true);
    this.error.set(null);

    this.http.post<{ requestId: string }>(`${environment.apiUrl}/v1/gdpr/dsar`, this.form.value)
      .subscribe({
        next: (res) => {
          this.requestId.set(res.requestId);
          this.submitted.set(true);
          this.submitting.set(false);
        },
        error: () => {
          this.error.set(this.translate.instant('legal.dsar.genericError'));
          this.submitting.set(false);
        }
      });
  }
}
