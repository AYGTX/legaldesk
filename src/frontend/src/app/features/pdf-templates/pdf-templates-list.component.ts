import { Component, OnInit, inject, signal, computed } from '@angular/core';
import { CommonModule, DatePipe } from '@angular/common';
import { Router } from '@angular/router';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatMenuModule } from '@angular/material/menu';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatTooltipModule } from '@angular/material/tooltip';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { PdfTemplateService } from '../../core/services/pdf-template.service';
import { PdfTemplateDto } from '../../core/models/pdf-template.models';
import { AuthService } from '../../core/auth/auth.service';

@Component({
  selector: 'sf-pdf-templates-list',
  standalone: true,
  imports: [
    CommonModule, DatePipe, FormsModule,
    MatButtonModule, MatIconModule, MatMenuModule,
    MatProgressSpinnerModule, MatTooltipModule, MatSnackBarModule, TranslatePipe
  ],
  template: `
    <div class="pt-page">
      <div class="page-header">
        <div>
          <h1 class="page-title">{{ 'pdfTemplates.list.title' | translate }}</h1>
          <p class="page-sub">{{ 'pdfTemplates.list.subtitle' | translate }}</p>
        </div>
        @if (canManage()) {
          <button mat-flat-button class="create-btn" (click)="showUpload.set(!showUpload())">
            <mat-icon>upload_file</mat-icon> {{ 'pdfTemplates.list.newTemplate' | translate }}
          </button>
        }
      </div>

      @if (canManage() && showUpload()) {
        <div class="upload-panel">
          <h3 class="upload-title">{{ 'pdfTemplates.upload.title' | translate }}</h3>
          <label class="file-drop" [class.has-file]="selectedFile()">
            <input type="file" accept="application/pdf" hidden (change)="onFileSelected($event)" />
            <mat-icon>picture_as_pdf</mat-icon>
            <span>{{ selectedFile()?.name ?? ('pdfTemplates.upload.choosePdf' | translate) }}</span>
          </label>
          <input class="text-input" [(ngModel)]="uploadName" [placeholder]="'pdfTemplates.upload.namePlaceholder' | translate" />
          <input class="text-input" [(ngModel)]="uploadDescription" [placeholder]="'pdfTemplates.upload.descriptionPlaceholder' | translate" />
          <div class="upload-actions">
            <button mat-button (click)="cancelUpload()">{{ 'common.cancel' | translate }}</button>
            <button mat-flat-button class="create-btn" [disabled]="!selectedFile() || !uploadName.trim() || uploading()" (click)="upload()">
              @if (uploading()) { <mat-spinner diameter="18"></mat-spinner> }
              {{ 'pdfTemplates.upload.uploadAndConfigure' | translate }}
            </button>
          </div>
        </div>
      }

      <div class="toolbar">
        <div class="search-wrap">
          <mat-icon class="search-icon">search</mat-icon>
          <input class="search-input" [(ngModel)]="search" (input)="onSearch()"
                 [placeholder]="'pdfTemplates.list.searchPlaceholder' | translate" />
        </div>
        @if (canManage()) {
          <div class="filter-pills">
            <button class="pill" [class.active]="activeFilter() === null" (click)="setFilter(null)">{{ 'pdfTemplates.list.filterAll' | translate }}</button>
            <button class="pill" [class.active]="activeFilter() === true" (click)="setFilter(true)">{{ 'pdfTemplates.list.filterActive' | translate }}</button>
            <button class="pill" [class.active]="activeFilter() === false" (click)="setFilter(false)">{{ 'pdfTemplates.list.filterInactive' | translate }}</button>
          </div>
        }
      </div>

      @if (loading()) {
        <div class="loading"><mat-spinner diameter="40"></mat-spinner></div>
      } @else if (templates().length === 0) {
        <div class="empty-state">
          <mat-icon class="empty-icon">picture_as_pdf</mat-icon>
          <p>{{ 'pdfTemplates.list.empty' | translate }}</p>
        </div>
      } @else {
        <div class="list-surface">
          @for (t of templates(); track t.id; let last = $last) {
            <div class="template-row" [class.last]="last">
              <div class="row-icon" [class.inactive]="!t.isActive"><mat-icon>picture_as_pdf</mat-icon></div>
              <div class="row-body">
                <div class="row-top">
                  <span class="tpl-name">{{ t.name }}</span>
                  @if (!t.isActive) { <span class="badge-inactive">{{ 'pdfTemplates.list.inactive' | translate }}</span> }
                </div>
                @if (t.description) { <p class="tpl-desc">{{ t.description }}</p> }
                <div class="row-meta">
                  <span>{{ 'pdfTemplates.list.pages' | translate: { n: t.pageCount } }}</span>
                  <span class="sep">·</span>
                  <span>{{ 'pdfTemplates.list.fields' | translate: { n: t.fieldCount } }}</span>
                  @if (t.defaultApproverName) {
                    <span class="sep">·</span>
                    <span>{{ 'pdfTemplates.list.defaultApprover' | translate: { name: t.defaultApproverName } }}</span>
                  }
                  <span class="sep">·</span>
                  <span>{{ t.createdAt | date:'mediumDate' }}</span>
                </div>
              </div>
              <div class="row-actions">
                @if (t.isActive && t.fieldCount > 0) {
                  <button mat-flat-button class="use-btn" (click)="startRequest(t)">
                    <mat-icon>send</mat-icon> {{ 'pdfTemplates.list.startRequest' | translate }}
                  </button>
                }
                @if (canManage()) {
                  <button mat-icon-button (click)="configure(t)" [matTooltip]="'pdfTemplates.list.configure' | translate">
                    <mat-icon>tune</mat-icon>
                  </button>
                  <button mat-icon-button [matMenuTriggerFor]="menu" [matTooltip]="'common.more' | translate">
                    <mat-icon>more_vert</mat-icon>
                  </button>
                  <mat-menu #menu="matMenu">
                    <button mat-menu-item (click)="configure(t)">
                      <mat-icon>tune</mat-icon><span>{{ 'pdfTemplates.list.configure' | translate }}</span>
                    </button>
                    <button mat-menu-item (click)="deleteTemplate(t)">
                      <mat-icon color="warn">delete</mat-icon><span>{{ 'common.delete' | translate }}</span>
                    </button>
                  </mat-menu>
                }
              </div>
            </div>
          }
        </div>
      }
    </div>
  `,
  styles: [`
    .pt-page { padding: 0; }
    .page-header { display: flex; align-items: flex-start; justify-content: space-between; margin-bottom: 24px; gap: 16px; }
    .page-title { font-size: 22px; font-weight: 500; margin: 0 0 4px; letter-spacing: -0.2px; }
    .page-sub { font-size: 13px; color: #5f6368; margin: 0; }
    .create-btn { background: #0b57d0 !important; color: #fff !important; border-radius: 20px !important; }

    .upload-panel { border: 1px solid #c7d0dc; border-radius: 12px; background: #fff; padding: 20px; margin-bottom: 20px; display: flex; flex-direction: column; gap: 12px; }
    .upload-title { margin: 0; font-size: 15px; font-weight: 500; }
    .file-drop { display: flex; align-items: center; gap: 10px; padding: 16px; border: 2px dashed #c7d0dc; border-radius: 10px; cursor: pointer; color: #5f6368; }
    .file-drop.has-file { border-color: #0b57d0; color: #0b57d0; background: #f0f6ff; }
    .text-input { height: 40px; padding: 0 14px; border-radius: 8px; border: 1px solid #c7d0dc; font-size: 14px; font-family: inherit; outline: none; box-sizing: border-box; }
    .text-input:focus { border-color: #0b57d0; }
    .upload-actions { display: flex; justify-content: flex-end; gap: 8px; align-items: center; }

    .toolbar { display: flex; align-items: center; gap: 12px; margin-bottom: 16px; flex-wrap: wrap; }
    .search-wrap { position: relative; flex: 1; min-width: 200px; }
    .search-icon { position: absolute; left: 10px; top: 50%; transform: translateY(-50%); font-size: 18px; height: 18px; width: 18px; color: #5f6368; pointer-events: none; }
    .search-input { width: 100%; height: 40px; padding: 0 16px 0 38px; border-radius: 20px; border: 1px solid #c7d0dc; background: #fff; font-size: 14px; font-family: inherit; box-sizing: border-box; outline: none; }
    .search-input:focus { border-color: #0b57d0; box-shadow: 0 0 0 2px rgba(11,87,208,0.15); }
    .filter-pills { display: flex; gap: 6px; }
    .pill { height: 32px; padding: 0 14px; border-radius: 16px; border: 1px solid #c7d0dc; background: #fff; font-size: 13px; font-family: inherit; cursor: pointer; color: #444; }
    .pill.active { background: #d3e3fd; border-color: #0b57d0; color: #0b57d0; font-weight: 500; }

    .loading { display: flex; justify-content: center; padding: 64px; }
    .empty-state { display: flex; flex-direction: column; align-items: center; gap: 12px; padding: 64px; color: #5f6368; }
    .empty-icon { font-size: 48px; height: 48px; width: 48px; opacity: 0.4; }

    .list-surface { border: 1px solid #c7d0dc; border-radius: 12px; background: #fff; overflow: hidden; box-shadow: 0 1px 2px rgba(0,0,0,0.06); }
    .template-row { display: flex; align-items: center; gap: 16px; padding: 16px 20px; border-bottom: 1px solid #e8edf2; }
    .template-row.last { border-bottom: none; }
    .template-row:hover { background: #f8faff; }
    .row-icon { width: 40px; height: 40px; border-radius: 10px; background: #fce8e6; color: #c5221f; display: flex; align-items: center; justify-content: center; flex-shrink: 0; }
    .row-icon.inactive { background: #f1f3f4; color: #9aa0a6; }
    .row-body { flex: 1; min-width: 0; }
    .row-top { display: flex; align-items: center; gap: 8px; margin-bottom: 2px; }
    .tpl-name { font-size: 14px; font-weight: 500; }
    .badge-inactive { font-size: 11px; font-weight: 500; background: #f1f3f4; color: #5f6368; padding: 2px 8px; border-radius: 10px; }
    .tpl-desc { font-size: 13px; color: #5f6368; margin: 0 0 4px; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
    .row-meta { font-size: 12px; color: #80868b; display: flex; align-items: center; gap: 4px; flex-wrap: wrap; }
    .sep { color: #c7d0dc; }
    .row-actions { display: flex; align-items: center; gap: 4px; }
    .use-btn { background: #0b57d0 !important; color: #fff !important; border-radius: 18px !important; height: 34px; }
  `]
})
export class PdfTemplatesListComponent implements OnInit {
  private readonly svc = inject(PdfTemplateService);
  private readonly snack = inject(MatSnackBar);
  private readonly router = inject(Router);
  private readonly auth = inject(AuthService);
  private readonly translate = inject(TranslateService);

  readonly templates = signal<PdfTemplateDto[]>([]);
  readonly loading = signal(true);
  readonly activeFilter = signal<boolean | null>(null);
  readonly showUpload = signal(false);
  readonly uploading = signal(false);
  readonly selectedFile = signal<File | null>(null);
  search = '';
  uploadName = '';
  uploadDescription = '';
  private searchTimer: ReturnType<typeof setTimeout> | null = null;

  readonly canManage = computed(() => {
    const roles = this.auth.user()?.roles ?? [];
    return roles.includes('Administrator') || roles.includes('Manager');
  });

  ngOnInit() { this.load(); }

  setFilter(val: boolean | null) { this.activeFilter.set(val); this.load(); }

  onSearch() {
    if (this.searchTimer) clearTimeout(this.searchTimer);
    this.searchTimer = setTimeout(() => this.load(), 300);
  }

  load() {
    this.loading.set(true);
    this.svc.getAll({
      search: this.search || undefined,
      activeOnly: this.activeFilter() === null ? undefined : this.activeFilter()!
    }).subscribe({
      next: r => { this.templates.set(r.items); this.loading.set(false); },
      error: () => this.loading.set(false)
    });
  }

  onFileSelected(event: Event) {
    const file = (event.target as HTMLInputElement).files?.[0] ?? null;
    this.selectedFile.set(file);
    if (file && !this.uploadName.trim()) this.uploadName = file.name.replace(/\.pdf$/i, '');
  }

  cancelUpload() {
    this.showUpload.set(false);
    this.selectedFile.set(null);
    this.uploadName = '';
    this.uploadDescription = '';
  }

  upload() {
    const file = this.selectedFile();
    if (!file || !this.uploadName.trim()) return;
    this.uploading.set(true);
    this.svc.create(file, this.uploadName.trim(), this.uploadDescription.trim() || undefined).subscribe({
      next: res => {
        this.uploading.set(false);
        this.cancelUpload();
        // Straight into the field designer to place fields on the freshly uploaded PDF.
        this.router.navigate(['/pdf-templates', res.id, 'designer']);
      },
      error: err => {
        this.uploading.set(false);
        this.snack.open(err?.error?.detail ?? this.translate.instant('pdfTemplates.upload.failed'), 'OK', { duration: 5000 });
      }
    });
  }

  configure(t: PdfTemplateDto) { this.router.navigate(['/pdf-templates', t.id, 'designer']); }
  startRequest(t: PdfTemplateDto) { this.router.navigate(['/pdf-templates', t.id, 'new']); }

  deleteTemplate(t: PdfTemplateDto) {
    if (!confirm(this.translate.instant('pdfTemplates.list.confirmDelete', { name: t.name }))) return;
    this.svc.delete(t.id).subscribe({
      next: () => { this.snack.open(this.translate.instant('pdfTemplates.list.deleted'), 'OK', { duration: 3000 }); this.load(); },
      error: () => this.snack.open(this.translate.instant('pdfTemplates.list.deleteFailed'), 'OK', { duration: 3000 })
    });
  }
}
