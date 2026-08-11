import { Routes } from '@angular/router';

export const pdfTemplatesRoutes: Routes = [
  {
    path: '',
    loadComponent: () => import('./pdf-templates-list.component').then(m => m.PdfTemplatesListComponent),
    title: 'PDF Forms — SignFlow'
  },
  {
    path: 'new',
    loadComponent: () => import('./pdf-template-upload.component').then(m => m.PdfTemplateUploadComponent),
    title: 'New PDF Form — SignFlow'
  },
  {
    path: ':id/designer',
    loadComponent: () => import('./pdf-template-designer.component').then(m => m.PdfTemplateDesignerComponent),
    title: 'Configure PDF Form — SignFlow'
  },
  {
    path: ':id/new',
    loadComponent: () => import('./pdf-template-request.component').then(m => m.PdfTemplateRequestComponent),
    title: 'New Request — SignFlow'
  }
];
