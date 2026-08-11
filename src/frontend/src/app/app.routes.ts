import { Routes } from '@angular/router';
import { authGuard } from './core/guards/auth.guard';
import { guestGuard } from './core/guards/guest.guard';

export const routes: Routes = [
  {
    path: '',
    redirectTo: '/dashboard',
    pathMatch: 'full'
  },
  {
    path: 'auth',
    canActivate: [guestGuard],
    loadChildren: () => import('./features/auth/auth.routes').then(m => m.authRoutes)
  },
  {
    path: '',
    canActivate: [authGuard],
    loadComponent: () => import('./shared/layout/layout.component').then(m => m.LayoutComponent),
    children: [
      {
        path: 'dashboard',
        loadComponent: () => import('./features/dashboard/dashboard.component').then(m => m.DashboardComponent),
        title: 'Dashboard — SignFlow'
      },
      {
        path: 'documents',
        loadComponent: () => import('./features/documents/documents-list.component').then(m => m.DocumentsListComponent),
        title: 'Documents — SignFlow'
      },
      {
        path: 'requests',
        redirectTo: 'documents'
      },
      {
        path: 'requests/new',
        loadComponent: () => import('./features/requests/new-request.component').then(m => m.NewRequestComponent),
        title: 'New Request — SignFlow'
      },
      {
        path: 'documents/upload',
        loadComponent: () => import('./features/documents/upload-document.component').then(m => m.UploadDocumentComponent),
        title: 'Upload Document — SignFlow'
      },
      {
        path: 'documents/:id',
        loadComponent: () => import('./features/documents/document-detail.component').then(m => m.DocumentDetailComponent),
        title: 'Document Detail — SignFlow'
      },
      {
        path: 'documents/:id/designer',
        loadComponent: () => import('./features/designer/designer.component').then(m => m.DesignerComponent),
        title: 'PDF Designer — SignFlow'
      },
      {
        path: 'documents/:id/audit',
        loadComponent: () => import('./features/audit/audit.component').then(m => m.AuditComponent),
        title: 'Audit Trail — SignFlow'
      },
      {
        path: 'audit',
        loadComponent: () => import('./features/audit/org-audit.component').then(m => m.OrgAuditComponent),
        title: 'Audit Trail — SignFlow'
      },
      {
        path: 'templates',
        loadComponent: () => import('./features/templates/templates.component').then(m => m.TemplatesComponent),
        title: 'Templates — SignFlow'
      },
      {
        path: 'pdf-templates',
        loadChildren: () => import('./features/pdf-templates/pdf-templates.routes').then(m => m.pdfTemplatesRoutes),
        title: 'PDF Forms — SignFlow'
      },
      {
        path: 'reports',
        loadComponent: () => import('./features/reports/reports.component').then(m => m.ReportsComponent),
        title: 'Reports — SignFlow'
      },
      {
        path: 'profile',
        loadComponent: () => import('./features/profile/profile.component').then(m => m.ProfileComponent),
        title: 'My Profile — SignFlow'
      },
      {
        path: 'admin',
        loadChildren: () => import('./features/administration/admin.routes').then(m => m.adminRoutes),
        title: 'Administration — SignFlow'
      },
      {
        path: 'legal/privacy',
        loadComponent: () => import('./features/legal/privacy-policy.component').then(m => m.PrivacyPolicyComponent),
        title: 'Privacy Policy — SignFlow'
      },
      {
        path: 'legal/terms',
        loadComponent: () => import('./features/legal/terms-of-service.component').then(m => m.TermsOfServiceComponent),
        title: 'Terms of Service — SignFlow'
      },
      {
        path: 'legal/cookies',
        loadComponent: () => import('./features/legal/cookie-policy.component').then(m => m.CookiePolicyComponent),
        title: 'Cookie Policy — SignFlow'
      },
      {
        path: 'privacy/dsar',
        loadComponent: () => import('./features/legal/dsar-portal.component').then(m => m.DsarPortalComponent),
        title: 'Data Rights — SignFlow'
      }
    ]
  },
  {
    path: 'sign/:token',
    loadComponent: () => import('./features/signing/signing.component').then(m => m.SigningComponent),
    title: 'Sign Document — SignFlow'
  },
  {
    path: '**',
    loadComponent: () => import('./shared/not-found/not-found.component').then(m => m.NotFoundComponent)
  }
];
