import { Routes } from '@angular/router';

export const htmlTemplatesRoutes: Routes = [
  {
    path: '',
    loadComponent: () => import('./html-templates-list.component').then(m => m.HtmlTemplatesListComponent),
    title: 'HTML Templates — SignFlow'
  },
  {
    path: 'new',
    loadComponent: () => import('./html-template-editor.component').then(m => m.HtmlTemplateEditorComponent),
    title: 'New Template — SignFlow'
  },
  {
    path: ':id/edit',
    loadComponent: () => import('./html-template-editor.component').then(m => m.HtmlTemplateEditorComponent),
    title: 'Edit Template — SignFlow'
  }
];
