import { Routes } from '@angular/router';

export const pdfTemplatesRoutes: Routes = [
  {
    path: '',
    loadComponent: () => import('./pdf-templates-list.component').then(m => m.PdfTemplatesListComponent),
    title: 'PDF Contract Templates — LegalDesk'
  },
  {
    path: 'new',
    loadComponent: () => import('./pdf-template-upload.component').then(m => m.PdfTemplateUploadComponent),
    title: 'New PDF Contract Template — LegalDesk'
  },
  {
    path: ':id/designer',
    loadComponent: () => import('./pdf-template-designer.component').then(m => m.PdfTemplateDesignerComponent),
    title: 'Configure PDF Contract Template — LegalDesk'
  },
  {
    path: ':id/new',
    loadComponent: () => import('./pdf-template-request.component').then(m => m.PdfTemplateRequestComponent),
    title: 'New Validation Request — LegalDesk'
  }
];
