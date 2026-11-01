import { Component, OnInit, inject, signal, computed } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterLink, Router, ActivatedRoute } from '@angular/router';
import { FormsModule } from '@angular/forms';
import { forkJoin } from 'rxjs';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSnackBar } from '@angular/material/snack-bar';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { ConfirmService } from '../../core/services/confirm.service';
import { HtmlTemplateService } from '../../core/services/html-template.service';
import { PdfTemplateService } from '../../core/services/pdf-template.service';
import { AuthService } from '../../core/auth/auth.service';
import { TemplateCardComponent } from '../../shared/template-card/template-card.component';
import { TemplateKind, UnifiedTemplate, fromHtmlTemplate, fromPdfTemplate } from '../../core/models/template-gallery.model';
import { TemplateVersionService } from '../../core/services/template-version.service';
import { problemText } from '../../core/utils/problem-text';

@Component({
  selector: 'sf-templates',
  standalone: true,
  imports: [
    CommonModule, RouterLink, FormsModule,
    MatButtonModule, MatIconModule, MatProgressSpinnerModule, TranslatePipe,
    TemplateCardComponent
  ],
  template: `
    <div class="templates-page">
      <div class="page-header">
        <div>
          <h1 class="page-title">{{ headerTitleKey() | translate }}</h1>
          <p class="page-subtitle">{{ headerSubtitleKey() | translate }}</p>
        </div>
        @if (canManage()) {
          <div class="manage-links">
            <!-- Filtered to one kind, the page offers exactly one way to add a template: that kind's. -->
            @if (kindFilter() !== 'pdf') {
              <a mat-stroked-button routerLink="/admin/html-templates/new">
                <mat-icon>article</mat-icon> {{ 'templates.createHtml' | translate }}
              </a>
            }
            @if (kindFilter() !== 'html') {
              <a mat-stroked-button routerLink="/templates/pdf/new">
                <mat-icon>picture_as_pdf</mat-icon> {{ 'templates.createPdf' | translate }}
              </a>
            }
          </div>
        }
      </div>

      <div class="toolbar">
        <div class="search-wrap">
          <mat-icon class="search-icon">search</mat-icon>
          <input class="search-input" [(ngModel)]="search" (input)="onSearch()" [placeholder]="'templates.searchPlaceholder' | translate" />
        </div>
        <div class="filter-pills">
          <button class="pill" [class.active]="kindFilter() === null" (click)="setKindFilter(null)">{{ 'templates.filterAll' | translate }}</button>
          <button class="pill" [class.active]="kindFilter() === 'html'" (click)="setKindFilter('html')">{{ 'templates.kindHtml' | translate }}</button>
          <button class="pill" [class.active]="kindFilter() === 'pdf'" (click)="setKindFilter('pdf')">{{ 'templates.kindPdf' | translate }}</button>
        </div>
        @if (canManage()) {
          <div class="filter-pills">
            <button class="pill" [class.active]="activeFilter() === null" (click)="setActiveFilter(null)">{{ 'templates.filterAllStatus' | translate }}</button>
            <button class="pill" [class.active]="activeFilter() === true" (click)="setActiveFilter(true)">{{ 'templates.filterActive' | translate }}</button>
            <button class="pill" [class.active]="activeFilter() === false" (click)="setActiveFilter(false)">{{ 'templates.filterInactive' | translate }}</button>
          </div>
        }
      </div>

      @if (loading()) {
        <div class="loading"><mat-spinner diameter="40"></mat-spinner></div>
      } @else if (templates().length === 0) {
        <div class="empty-state">
          <mat-icon class="empty-icon">description</mat-icon>
          <p>{{ 'templates.empty' | translate }}</p>
        </div>
      } @else {
        <div class="list-surface">
          @for (t of templates(); track t.kind + t.id) {
            <sf-template-card [template]="t" [canManage]="canManage()" (use)="useTemplate($event)" (manage)="manageTemplate($event)" (remove)="deleteTemplate($event)" (lifecycle)="runLifecycle($event.template, $event.action)" />
          }
        </div>
      }
    </div>
  `,
  styles: [`
    .templates-page { padding: 0; }
    .manage-links { display: flex; gap: 8px; flex-wrap: wrap; }

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
  `]
})
export class TemplatesComponent implements OnInit {
  private readonly htmlSvc = inject(HtmlTemplateService);
  private readonly pdfSvc = inject(PdfTemplateService);
  private readonly auth = inject(AuthService);
  private readonly confirm = inject(ConfirmService);
  private readonly snack = inject(MatSnackBar);
  private readonly translate = inject(TranslateService);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  private readonly versions = inject(TemplateVersionService);

  private readonly allTemplates = signal<UnifiedTemplate[]>([]);
  readonly loading = signal(true);
  readonly kindFilter = signal<TemplateKind | null>(null);
  readonly activeFilter = signal<boolean | null>(null);
  search = '';
  private searchTimer: ReturnType<typeof setTimeout> | null = null;

  readonly canManage = computed(() => {
    const roles = this.auth.user()?.roles ?? [];
    return roles.includes('Administrator') || roles.includes('Manager');
  });

  readonly templates = computed(() => {
    const kind = this.kindFilter();
    return kind ? this.allTemplates().filter(t => t.kind === kind) : this.allTemplates();
  });

  readonly headerTitleKey = computed(() => {
    const kind = this.kindFilter();
    return kind === 'html' ? 'templates.titleHtml' : kind === 'pdf' ? 'templates.titlePdf' : 'templates.title';
  });
  readonly headerSubtitleKey = computed(() => {
    const kind = this.kindFilter();
    return kind === 'html' ? 'templates.hintHtml' : kind === 'pdf' ? 'templates.hintPdf' : 'templates.subtitle';
  });

  ngOnInit() {
    // /contracts/from-pdf-template shows only PDF templates (route data); /templates?kind=html|pdf still filters.
    this.route.queryParamMap.subscribe(params => {
      const kind = this.route.snapshot.data['kind'] ?? params.get('kind');
      this.kindFilter.set(kind === 'html' || kind === 'pdf' ? kind : null);
    });
    this.load();
  }

  setKindFilter(kind: TemplateKind | null) { this.kindFilter.set(kind); }
  setActiveFilter(val: boolean | null) { this.activeFilter.set(val); this.load(); }

  onSearch() {
    if (this.searchTimer) clearTimeout(this.searchTimer);
    this.searchTimer = setTimeout(() => this.load(), 300);
  }

  load() {
    this.loading.set(true);
    const activeOnly = this.canManage() ? (this.activeFilter() ?? undefined) : true;
    forkJoin({
      html: this.htmlSvc.getAll({ search: this.search || undefined, activeOnly, pageSize: 100 }),
      pdf: this.pdfSvc.getAll({ search: this.search || undefined, activeOnly, pageSize: 100 })
    }).subscribe({
      next: ({ html, pdf }) => {
        const merged = [
          ...html.items.map(fromHtmlTemplate),
          ...pdf.items.map(fromPdfTemplate)
        ].sort((a, b) => new Date(b.createdAt).getTime() - new Date(a.createdAt).getTime());
        this.allTemplates.set(merged);
        this.loading.set(false);
      },
      error: () => this.loading.set(false)
    });
  }

  useTemplate(t: UnifiedTemplate) {
    if (t.kind === 'html') {
      this.router.navigate(['/contracts/from-html-template'], { queryParams: { templateId: t.id } });
    } else {
      this.router.navigate(['/contracts/from-pdf-template', t.id]);
    }
  }

  deleteTemplate(t: UnifiedTemplate) {
    this.confirm.confirm({
      message: this.translate.instant('pdfTemplates.list.confirmDelete', { name: t.name }),
      confirmLabel: this.translate.instant('common.delete'),
      danger: true
    }).subscribe(ok => {
      if (!ok) return;
      this.pdfSvc.delete(t.id).subscribe({
        next: () => {
          this.snack.open(this.translate.instant('pdfTemplates.list.deleted'), 'OK', { duration: 3000 });
          this.load();
        },
        error: () => this.snack.open(this.translate.instant('pdfTemplates.list.deleteFailed'), 'OK', { duration: 3000 })
      });
    });
  }

  /** Send to legal review, approve (Harvey reviews first), retire, or draft the next version (R-M6-5). */
  runLifecycle(t: UnifiedTemplate, action: 'submit' | 'approve' | 'retire' | 'next') {
    const run = () => {
      const call = action === 'submit' ? this.versions.submit(t.kind, t.id)
        : action === 'approve' ? this.versions.approve(t.kind, t.id)
        : action === 'retire' ? this.versions.retire(t.kind, t.id)
        : this.versions.next(t.kind, t.id);
      call.subscribe({
        next: result => {
          this.snack.open(this.translate.instant('templates.lifecycle.done.' + action, { version: result.version }), 'OK', { duration: 4000 });
          if (action === 'next') this.manageTemplate({ ...t, id: result.id, status: result.status, version: result.version });
          else this.load();
        },
        error: (err: unknown) => this.snack.open(problemText(this.translate, err, 'templates.lifecycle.failed'), 'OK', { duration: 7000 })
      });
    };
    if (action !== 'retire') { run(); return; }
    this.confirm.confirm({
      message: this.translate.instant('templates.lifecycle.confirmRetire', { name: t.name, version: t.version }),
      confirmLabel: this.translate.instant('templates.lifecycle.retire'),
      danger: true
    }).subscribe(ok => { if (ok) run(); });
  }

  manageTemplate(t: UnifiedTemplate) {
    if (t.kind === 'html') {
      this.router.navigate(['/admin/html-templates', t.id, 'edit']);
    } else {
      this.router.navigate(['/templates/pdf', t.id, 'designer']);
    }
  }
}
