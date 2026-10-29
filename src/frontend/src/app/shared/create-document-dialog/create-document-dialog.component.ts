import { Component, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router } from '@angular/router';
import { MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';
import { TranslatePipe } from '@ngx-translate/core';

interface StartOption {
  key: 'pdfTemplate' | 'htmlTemplate' | 'uploadPdf';
  icon: string;
  color: string;
  bg: string;
  badgeKey: string;
  route: string[];
  queryParams?: Record<string, string>;
}

const OPTIONS: StartOption[] = [
  {
    key: 'pdfTemplate',
    icon: 'picture_as_pdf',
    color: '#c5221f',
    bg: '#fce8e6',
    badgeKey: 'createDocument.badgeReusable',
    route: ['/templates'],
    queryParams: { kind: 'pdf' }
  },
  {
    key: 'htmlTemplate',
    icon: 'article',
    color: '#0b57d0',
    bg: '#e8f0fe',
    badgeKey: 'createDocument.badgeReusable',
    route: ['/templates'],
    queryParams: { kind: 'html' }
  },
  {
    key: 'uploadPdf',
    icon: 'upload_file',
    color: '#188038',
    bg: '#e6f4ea',
    badgeKey: 'createDocument.badgeOneTime',
    route: ['/contracts/upload']
  }
];

@Component({
  selector: 'sf-create-document-dialog',
  standalone: true,
  imports: [CommonModule, MatDialogModule, MatIconModule, TranslatePipe],
  template: `
    <h2 mat-dialog-title>{{ 'createDocument.dialogTitle' | translate }}</h2>
    <p class="dialog-subtitle">{{ 'createDocument.dialogSubtitle' | translate }}</p>
    <mat-dialog-content>
      <div class="option-grid">
        @for (opt of options; track opt.key) {
          <button type="button" class="option-card" (click)="choose(opt)">
            <div class="option-icon" [style.background]="opt.bg" [style.color]="opt.color">
              <mat-icon>{{ opt.icon }}</mat-icon>
            </div>
            <span class="option-badge" [style.color]="opt.color" [style.background]="opt.bg">
              {{ opt.badgeKey | translate }}
            </span>
            <h3 class="option-title">{{ ('createDocument.' + opt.key + '.title') | translate }}</h3>
            <p class="option-desc">{{ ('createDocument.' + opt.key + '.description') | translate }}</p>
          </button>
        }
      </div>
    </mat-dialog-content>
  `,
  styles: [`
    :host { display: block; padding: 24px 24px 8px; max-width: 720px; }
    h2[mat-dialog-title] { margin: 0 0 4px; font-size: 20px; font-weight: 500; color: #1f1f1f; }
    .dialog-subtitle { margin: 0 0 20px; font-size: 13px; color: #5f6368; }
    mat-dialog-content { padding: 0; margin: 0; max-height: none; overflow: visible; }

    .option-grid { display: grid; grid-template-columns: repeat(3, 1fr); gap: 14px; }
    @media (max-width: 640px) { .option-grid { grid-template-columns: 1fr; } }

    .option-card {
      display: flex; flex-direction: column; align-items: flex-start; gap: 8px;
      text-align: left; padding: 18px; border: 1px solid #dadce0; border-radius: 12px;
      background: #fff; cursor: pointer; font-family: inherit; transition: box-shadow 0.15s, border-color 0.15s, transform 0.1s;
    }
    .option-card:hover { border-color: #0b57d0; box-shadow: 0 2px 10px rgba(0,0,0,0.1); transform: translateY(-1px); }
    .option-card:focus-visible { outline: 2px solid #0b57d0; outline-offset: 2px; }

    .option-icon {
      width: 44px; height: 44px; border-radius: 10px;
      display: flex; align-items: center; justify-content: center;
    }
    .option-icon mat-icon { font-size: 24px; height: 24px; width: 24px; }

    .option-badge {
      font-size: 11px; font-weight: 600; padding: 2px 8px; border-radius: 10px;
      letter-spacing: 0.2px; text-transform: uppercase;
    }

    .option-title { margin: 4px 0 0; font-size: 15px; font-weight: 500; color: #1f1f1f; }
    .option-desc { margin: 0; font-size: 12.5px; line-height: 1.5; color: #5f6368; }
  `]
})
export class CreateDocumentDialogComponent {
  private readonly router = inject(Router);
  readonly dialogRef = inject(MatDialogRef<CreateDocumentDialogComponent>);

  readonly options = OPTIONS;

  choose(opt: StartOption): void {
    this.dialogRef.close();
    this.router.navigate(opt.route, opt.queryParams ? { queryParams: opt.queryParams } : undefined);
  }
}
