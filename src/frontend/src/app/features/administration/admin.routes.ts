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
  },
  {
    path: 'workflows',
    loadComponent: () => import('./workflow-presets.component').then(m => m.WorkflowPresetsComponent),
    title: 'Workflows — LegalDesk'
  },
  {
    path: 'clauses',
    loadComponent: () => import('../clauses/clause-library.component').then(m => m.ClauseLibraryComponent),
    title: 'Clause Library — LegalDesk'
  },
  {
    path: 'regulatory-watch',
    loadComponent: () => import('../regulatory-watch/regulatory-watch.component').then(m => m.RegulatoryWatchComponent),
    title: 'Regulatory Watch — LegalDesk'
  }
];
