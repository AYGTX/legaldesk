import { Routes } from '@angular/router';

export const adminRoutes: Routes = [
  {
    path: '',
    loadComponent: () => import('./admin.component').then(m => m.AdminComponent)
  },
  {
    path: 'html-templates',
    loadChildren: () => import('../html-templates/html-templates.routes').then(m => m.htmlTemplatesRoutes),
    title: 'Clause Templates — LegalDesk'
  }
];
