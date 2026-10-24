import { Component, OnInit, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatSelectModule } from '@angular/material/select';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { MatTooltipModule } from '@angular/material/tooltip';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { PdfTemplateService } from '../../core/services/pdf-template.service';
import { RequestTypeService } from '../../core/services/request-type.service';
import { RequestType } from '../../core/models';
import { formatSla } from '../../core/utils/sla';

@Component({
  selector: 'sf-pdf-template-upload',
  standalone: true,
  imports: [
    CommonModule, FormsModule, RouterLink,
    MatButtonModule, MatIconModule, MatFormFieldModule, MatSelectModule, MatProgressSpinnerModule,
    MatSnackBarModule, MatTooltipModule, TranslatePipe
  ],
  template: `
    <div class="upload-page">
      <div class="top-bar">
        <a mat-icon-button routerLink="/pdf-templates" [matTooltip]="'common.back' | translate">
          <mat-icon>arrow_back</mat-icon>
        </a>
        <div>
          <h1 class="top-title">{{ 'pdfTemplates.upload.title' | translate }}</h1>
          <p class="top-sub">{{ 'pdfTemplates.upload.subtitle' | translate }}</p>
        </div>
      </div>

      <div class="upload-panel">
        <div class="up-left">
        <label class="file-drop" [class.has-file]="selectedFile()">
          <input type="file" accept="application/pdf" hidden (change)="onFileSelected($event)" />
          <div class="drop-icon"><mat-icon>{{ selectedFile() ? 'picture_as_pdf' : 'cloud_upload' }}</mat-icon></div>
          <div class="drop-text">
            <span class="drop-title">{{ selectedFile()?.name ?? ('pdfTemplates.upload.choosePdf' | translate) }}</span>
            @if (!selectedFile()) { <span class="drop-hint">{{ 'pdfTemplates.upload.dropHint' | translate }}</span> }
          </div>
          @if (selectedFile()) { <span class="file-check"><mat-icon>check_circle</mat-icon></span> }
        </label>
        </div>

        <div class="up-right">
        <div class="field-group">
          <label class="field-label">{{ 'pdfTemplates.upload.namePlaceholder' | translate }}</label>
          <input class="text-input" [(ngModel)]="uploadName" [placeholder]="'pdfTemplates.upload.namePlaceholder' | translate" />
        </div>

        <div class="field-group">
          <label class="field-label">{{ 'pdfTemplates.upload.descriptionPlaceholder' | translate }}</label>
          <input class="text-input" [(ngModel)]="uploadDescription" [placeholder]="'pdfTemplates.upload.descriptionPlaceholder' | translate" />
        </div>

        <div class="field-group">
          <label class="field-label">{{ 'pdfTemplates.upload.requestTypePlaceholder' | translate }}</label>
          <mat-form-field appearance="outline" class="ff" subscriptSizing="dynamic">
            <mat-select [(ngModel)]="requestTypeId" [placeholder]="'pdfTemplates.upload.requestTypePlaceholder' | translate">
              @for (rt of requestTypes(); track rt.id) { <mat-option [value]="rt.id">{{ rt.name }} ({{ formatSla(rt.slaHours) }})</mat-option> }
            </mat-select>
          </mat-form-field>
        </div>
        </div>

        <div class="upload-actions">
          <a mat-button routerLink="/pdf-templates">{{ 'common.cancel' | translate }}</a>
          <button mat-flat-button class="create-btn" [disabled]="!selectedFile() || !uploadName.trim() || !requestTypeId || uploading()" (click)="upload()">
            @if (uploading()) { <mat-spinner diameter="18"></mat-spinner> }
            <mat-icon>arrow_forward</mat-icon>
            {{ 'pdfTemplates.upload.uploadAndConfigure' | translate }}
          </button>
        </div>
      </div>
    </div>
  `,
  styles: [`
    .upload-page { padding: 0; width: 100%; }
    .top-bar { display: flex; align-items: flex-start; gap: 8px; margin-bottom: 24px; }
    .top-title { font-size: 22px; font-weight: 500; margin: 4px 0 2px; letter-spacing: -0.2px; }
    .top-sub { font-size: 13px; color: #5f6368; margin: 0 0 0 0; }
    .upload-panel {
      border: 1px solid #c7d0dc; border-radius: 12px; background: #fff; padding: 28px; box-shadow: 0 1px 2px rgba(0,0,0,0.06);
      display: grid; grid-template-columns: minmax(0, 1.1fr) minmax(0, 1fr); gap: 24px 36px;
    }
    @media (max-width: 900px) { .upload-panel { grid-template-columns: 1fr; } }
    .up-left { display: flex; }
    .up-left .file-drop { flex: 1; flex-direction: column; justify-content: center; text-align: center; min-height: 300px; }
    .up-left .drop-text { align-items: center; }
    .up-left .file-check { margin-left: 0; }
    .up-right { display: flex; flex-direction: column; gap: 20px; min-width: 0; }

    .file-drop {
      display: flex; align-items: center; gap: 14px; padding: 22px;
      border: 2px dashed #c7d0dc; border-radius: 12px; cursor: pointer;
      transition: border-color 0.15s, background 0.15s;
    }
    .file-drop:hover { border-color: #0b57d0; background: #f8faff; }
    .file-drop.has-file { border-color: #0b57d0; border-style: solid; background: #f0f6ff; }
    .drop-icon {
      width: 44px; height: 44px; border-radius: 10px; flex-shrink: 0;
      background: #e8f0fe; color: #0b57d0;
      display: flex; align-items: center; justify-content: center;
    }
    .drop-icon mat-icon { font-size: 24px; width: 24px; height: 24px; }
    .drop-text { display: flex; flex-direction: column; min-width: 0; }
    .drop-title { font-size: 14px; font-weight: 500; color: #1f1f1f; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
    .drop-hint { font-size: 12px; color: #80868b; margin-top: 2px; }
    .file-check { margin-left: auto; color: #1e6b2e; display: flex; }

    .field-group { display: flex; flex-direction: column; gap: 6px; }
    .field-label { font-size: 12px; font-weight: 600; color: #5f6368; text-transform: uppercase; letter-spacing: 0.04em; }
    .text-input { height: 44px; padding: 0 14px; border-radius: 8px; border: 1px solid #c7d0dc; font-size: 14px; font-family: inherit; outline: none; box-sizing: border-box; transition: border-color 0.15s, box-shadow 0.15s; }
    .text-input:focus { border-color: #0b57d0; box-shadow: 0 0 0 2px rgba(11,87,208,0.15); }
    .ff { width: 100%; }
    .ff ::ng-deep .mat-mdc-form-field-infix { min-height: 44px; padding-top: 10px !important; padding-bottom: 10px !important; font-size: 14px; }

    .upload-actions { grid-column: 1 / -1; display: flex; justify-content: flex-end; gap: 8px; align-items: center; margin-top: 4px; padding-top: 12px; border-top: 1px solid #e8edf2; }
    .create-btn { background: #0b57d0 !important; color: #fff !important; border-radius: 20px !important; }
    .create-btn mat-icon { margin-right: 4px; }
  `]
})
export class PdfTemplateUploadComponent implements OnInit {
  private readonly svc = inject(PdfTemplateService);
  private readonly requestTypeSvc = inject(RequestTypeService);
  private readonly snack = inject(MatSnackBar);
  private readonly router = inject(Router);
  private readonly translate = inject(TranslateService);

  readonly formatSla = formatSla;
  readonly uploading = signal(false);
  readonly selectedFile = signal<File | null>(null);
  readonly requestTypes = signal<RequestType[]>([]);
  uploadName = '';
  uploadDescription = '';
  requestTypeId = '';

  ngOnInit() {
    this.requestTypeSvc.getActive().subscribe(types => this.requestTypes.set(types));
  }

  onFileSelected(event: Event) {
    const file = (event.target as HTMLInputElement).files?.[0] ?? null;
    this.selectedFile.set(file);
    if (file && !this.uploadName.trim()) this.uploadName = file.name.replace(/\.pdf$/i, '');
  }

  upload() {
    const file = this.selectedFile();
    if (!file || !this.uploadName.trim() || !this.requestTypeId) return;
    this.uploading.set(true);
    this.svc.create(file, this.uploadName.trim(), this.requestTypeId, this.uploadDescription.trim() || undefined).subscribe({
      next: res => {
        this.uploading.set(false);
        this.router.navigate(['/pdf-templates', res.id, 'designer']);
      },
      error: err => {
        this.uploading.set(false);
        this.snack.open(err?.error?.detail ?? this.translate.instant('pdfTemplates.upload.failed'), 'OK', { duration: 5000 });
      }
    });
  }
}
