import { Routes } from '@angular/router';

export const adminRoutes: Routes = [
  {
    path: '',
    pathMatch: 'full',
    // Old /admin?tab=types address: carry the tab over to its own path.
    redirectTo: ({ queryParams }) => (queryParams['tab'] === 'types' || queryParams['tab'] === 'settings') ? queryParams['tab'] : 'users'
  },
  ...['users', 'types', 'settings'].map(tab => ({
    path: tab,
    data: { tab },
    loadComponent: () => import('./admin.component').then(m => m.AdminComponent),
    title: 'Administration — LegalDesk'
  })),
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
    path: 'review-rules',
    loadComponent: () => import('./review-settings.component').then(m => m.ReviewSettingsComponent),
    title: 'Review rules — LegalDesk'
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
