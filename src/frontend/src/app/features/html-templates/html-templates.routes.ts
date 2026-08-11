import { Routes } from '@angular/router';

export const htmlTemplatesRoutes: Routes = [
  {
    path: '',
    loadComponent: () => import('./html-templates-list.component').then(m => m.HtmlTemplatesListComponent),
    title: 'Clause Templates — LegalDesk'
  },
  {
    path: 'new',
    loadComponent: () => import('./html-template-editor.component').then(m => m.HtmlTemplateEditorComponent),
    title: 'New Contract Template — LegalDesk'
  },
  {
    path: ':id/edit',
    loadComponent: () => import('./html-template-editor.component').then(m => m.HtmlTemplateEditorComponent),
    title: 'Edit Contract Template — LegalDesk'
  }
];
