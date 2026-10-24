import { Component, OnInit, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { CommonModule } from '@angular/common';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatCardModule } from '@angular/material/card';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatSelectModule } from '@angular/material/select';
import { MatTooltipModule } from '@angular/material/tooltip';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { DocumentService } from '../../core/services/document.service';
import { RequestTypeService } from '../../core/services/request-type.service';
import { RequestType } from '../../core/models';
import { formatSla } from '../../core/utils/sla';

@Component({
  selector: 'sf-upload-document',
  standalone: true,
  imports: [
    CommonModule, ReactiveFormsModule,
    MatCardModule, MatButtonModule, MatIconModule,
    MatFormFieldModule, MatInputModule, MatProgressBarModule, MatSelectModule,
    MatTooltipModule, TranslatePipe
  ],
  template: `
    <div class="upload-page">
      <div class="page-header">
        <div>
          <h1 class="page-title">{{ 'documents.upload.title' | translate }}</h1>
          <p class="page-subtitle">{{ 'documents.upload.subtitle' | translate }}</p>
        </div>
      </div>

      <div class="upload-layout">
        <mat-card class="upload-card">
          <!-- Drop Zone -->
          @if (!selectedFile()) {
            <div class="drop-zone" [class.drag-over]="isDragOver()"
                 (dragover)="onDragOver($event)" (dragleave)="isDragOver.set(false)"
                 (drop)="onDrop($event)" (click)="fileInput.click()">
              <mat-icon class="drop-icon">cloud_upload</mat-icon>
              <h3>{{ 'documents.upload.dropHere' | translate }}</h3>
              <p>{{ 'documents.upload.orBrowse' | translate }}</p>
              <p class="file-hint">{{ 'documents.upload.hint' | translate }}</p>
              <input #fileInput type="file" accept=".pdf" hidden (change)="onFileSelected($event)" />
            </div>
          } @else {
            <div class="file-preview">
              <mat-icon class="pdf-icon">picture_as_pdf</mat-icon>
              <div class="file-info">
                <span class="file-name">{{ selectedFile()!.name }}</span>
                <span class="file-size">{{ formatSize(selectedFile()!.size) }}</span>
              </div>
              <button mat-icon-button (click)="selectedFile.set(null)" [matTooltip]="'documents.upload.remove' | translate">
                <mat-icon>close</mat-icon>
              </button>
            </div>

            <form [formGroup]="form" class="upload-form">
              <mat-form-field appearance="outline" class="full-width">
                <mat-label>{{ 'documents.upload.documentTitle' | translate }}</mat-label>
                <input matInput formControlName="title" [placeholder]="'documents.upload.titlePlaceholder' | translate" />
                @if (form.get('title')?.hasError('required') && form.get('title')?.touched) {
                  <mat-error>{{ 'documents.upload.titleRequired' | translate }}</mat-error>
                }
              </mat-form-field>

              @if (requestTypes().length > 0) {
                <mat-form-field appearance="outline" class="full-width">
                  <mat-label>{{ 'documents.upload.requestType' | translate }}</mat-label>
                  <mat-select formControlName="requestTypeId">
                    <mat-option [value]="null">{{ 'documents.upload.none' | translate }}</mat-option>
                    @for (rt of requestTypes(); track rt.id) {
                      <mat-option [value]="rt.id">
                        <mat-icon class="opt-icon" [style.color]="rt.color || '#6366f1'">{{ rt.icon || 'description' }}</mat-icon>
                        {{ rt.name }}
                      </mat-option>
                    }
                  </mat-select>
                </mat-form-field>

                @if (selectedType(); as t) {
                  <div class="rt-info" [style.border-color]="(t.color || '#6366f1')">
                    @if (t.description) { <p class="rt-info-desc">{{ t.description }}</p> }
                    <div class="rt-info-sla">
                      <mat-icon>schedule</mat-icon>
                      {{ 'documents.upload.mustBeSignedWithinPrefix' | translate }} <strong>{{ formatSla(t.slaHours) }}</strong> {{ 'documents.upload.mustBeSignedWithinSuffix' | translate }}
                    </div>
                  </div>
                }
              }

              <mat-form-field appearance="outline" class="full-width">
                <mat-label>{{ 'documents.upload.messageLabel' | translate }}</mat-label>
                <textarea matInput formControlName="message" rows="3"
                          [placeholder]="'documents.upload.messagePlaceholder' | translate"></textarea>
              </mat-form-field>
            </form>

            @if (uploading()) {
              <div class="upload-progress">
                <mat-progress-bar mode="indeterminate"></mat-progress-bar>
                <span>{{ 'documents.upload.uploadingProgress' | translate }}</span>
              </div>
            }

            @if (error()) {
              <div class="error-banner">
                <mat-icon>error_outline</mat-icon>
                {{ error() }}
              </div>
            }

            <div class="upload-actions">
              <button mat-stroked-button (click)="selectedFile.set(null)" [disabled]="uploading()">{{ 'common.cancel' | translate }}</button>
              <button mat-flat-button color="primary" (click)="upload()"
                      [disabled]="uploading() || form.invalid">
                @if (uploading()) { {{ 'documents.upload.processing' | translate }} } @else { {{ 'documents.upload.uploadAndContinue' | translate }} }
              </button>
            </div>
          }
        </mat-card>

        <div class="upload-tips">
          <h3>{{ 'documents.upload.nextSteps' | translate }}</h3>
          <ol>
            <li>
              <mat-icon>touch_app</mat-icon>
              <div>
                <strong>{{ 'documents.upload.steps.placeFields.title' | translate }}</strong>
                <p>{{ 'documents.upload.steps.placeFields.desc' | translate }}</p>
              </div>
            </li>
            <li>
              <mat-icon>people</mat-icon>
              <div>
                <strong>{{ 'documents.upload.steps.assignRecipients.title' | translate }}</strong>
                <p>{{ 'documents.upload.steps.assignRecipients.desc' | translate }}</p>
              </div>
            </li>
            <li>
              <mat-icon>send</mat-icon>
              <div>
                <strong>{{ 'documents.upload.steps.sendForSignatures.title' | translate }}</strong>
                <p>{{ 'documents.upload.steps.sendForSignatures.desc' | translate }}</p>
              </div>
            </li>
          </ol>
        </div>
      </div>
    </div>
  `,
  styleUrl: './upload-document.component.scss'
})
export class UploadDocumentComponent implements OnInit {
  private readonly documentService = inject(DocumentService);
  private readonly requestTypeService = inject(RequestTypeService);
  private readonly router = inject(Router);
  private readonly fb = inject(FormBuilder);
  private readonly translate = inject(TranslateService);

  readonly selectedFile = signal<File | null>(null);
  readonly isDragOver = signal(false);
  readonly uploading = signal(false);
  readonly error = signal<string | null>(null);
  readonly requestTypes = signal<RequestType[]>([]);
  readonly formatSla = formatSla;

  readonly form = this.fb.group({
    title: ['', Validators.required],
    message: [''],
    requestTypeId: [null as string | null]
  });

  ngOnInit(): void {
    this.requestTypeService.getActive().subscribe({
      next: t => this.requestTypes.set(t),
      error: () => this.requestTypes.set([])
    });
  }

  selectedType(): RequestType | undefined {
    const id = this.form.value.requestTypeId;
    return id ? this.requestTypes().find(t => t.id === id) : undefined;
  }

  onDragOver(event: DragEvent): void {
    event.preventDefault();
    this.isDragOver.set(true);
  }

  onDrop(event: DragEvent): void {
    event.preventDefault();
    this.isDragOver.set(false);
    const file = event.dataTransfer?.files[0];
    if (file) this.setFile(file);
  }

  onFileSelected(event: Event): void {
    const file = (event.target as HTMLInputElement).files?.[0];
    if (file) this.setFile(file);
  }

  private setFile(file: File): void {
    if (file.type !== 'application/pdf') {
      this.error.set(this.translate.instant('documents.upload.onlyPdf'));
      return;
    }
    if (file.size > 50 * 1024 * 1024) {
      this.error.set(this.translate.instant('documents.upload.fileTooLarge'));
      return;
    }
    this.error.set(null);
    this.selectedFile.set(file);
    if (!this.form.value.title) {
      this.form.patchValue({ title: file.name.replace(/\.pdf$/i, '') });
    }
  }

  formatSize(bytes: number): string {
    if (bytes < 1024) return `${bytes} B`;
    if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`;
    return `${(bytes / 1024 / 1024).toFixed(1)} MB`;
  }

  upload(): void {
    if (!this.selectedFile() || this.form.invalid) return;
    this.uploading.set(true);
    this.error.set(null);

    const formData = new FormData();
    formData.append('file', this.selectedFile()!);
    formData.append('title', this.form.value.title!);
    if (this.form.value.message) {
      formData.append('message', this.form.value.message);
    }
    if (this.form.value.requestTypeId) {
      formData.append('requestTypeId', this.form.value.requestTypeId);
    }

    this.documentService.upload(formData).subscribe({
      next: result => this.router.navigate(['/documents', result.documentId, 'designer']),
      error: err => {
        this.uploading.set(false);
        this.error.set(err.error?.detail ?? this.translate.instant('documents.upload.uploadFailed'));
      }
    });
  }
}
