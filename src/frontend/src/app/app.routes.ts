import { Routes } from '@angular/router';
import { authGuard } from './core/guards/auth.guard';
import { guestGuard } from './core/guards/guest.guard';

export const routes: Routes = [
  {
    path: '',
    pathMatch: 'full',
    canActivate: [guestGuard],
    loadComponent: () => import('./features/landing/landing.component').then(m => m.LandingComponent),
    title: 'LegalDesk — Contract Lifecycle Management'
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
        title: 'Dashboard — LegalDesk'
      },
      {
        path: 'documents',
        loadComponent: () => import('./features/documents/documents-list.component').then(m => m.DocumentsListComponent),
        title: 'Contracts — LegalDesk'
      },
      {
        path: 'requests',
        redirectTo: 'documents'
      },
      {
        path: 'requests/new',
        loadComponent: () => import('./features/requests/new-request.component').then(m => m.NewRequestComponent),
        title: 'New Validation Request — LegalDesk'
      },
      {
        path: 'review',
        loadComponent: () => import('./features/review/contract-review.component').then(m => m.ContractReviewComponent),
        title: 'Contract Review — LegalDesk'
      },
      {
        path: 'documents/upload',
        loadComponent: () => import('./features/documents/upload-document.component').then(m => m.UploadDocumentComponent),
        title: 'Upload Contract — LegalDesk'
      },
      {
        path: 'documents/:id',
        loadComponent: () => import('./features/documents/document-detail.component').then(m => m.DocumentDetailComponent),
        title: 'Contract Detail — LegalDesk'
      },
      {
        path: 'documents/:id/designer',
        loadComponent: () => import('./features/designer/designer.component').then(m => m.DesignerComponent),
        title: 'Contract Preparation — LegalDesk'
      },
      {
        path: 'documents/:id/audit',
        loadComponent: () => import('./features/audit/audit.component').then(m => m.AuditComponent),
        title: 'Traceability — LegalDesk'
      },
      {
        path: 'audit',
        loadComponent: () => import('./features/audit/org-audit.component').then(m => m.OrgAuditComponent),
        title: 'Traceability — LegalDesk'
      },
      {
        path: 'templates',
        loadComponent: () => import('./features/templates/templates.component').then(m => m.TemplatesComponent),
        title: 'Contract Templates — LegalDesk'
      },
      {
        path: 'pdf-templates',
        loadChildren: () => import('./features/pdf-templates/pdf-templates.routes').then(m => m.pdfTemplatesRoutes),
        title: 'PDF Contract Templates — LegalDesk'
      },
      {
        path: 'reports',
        loadComponent: () => import('./features/reports/reports.component').then(m => m.ReportsComponent),
        title: 'Reports — LegalDesk'
      },
      {
        path: 'capabilities',
        loadComponent: () => import('./features/capabilities/capabilities.component').then(m => m.CapabilitiesComponent),
        title: 'Capability Map — LegalDesk'
      },
      {
        path: 'profile',
        loadComponent: () => import('./features/profile/profile.component').then(m => m.ProfileComponent),
        title: 'My Profile — LegalDesk'
      },
      {
        path: 'admin',
        loadChildren: () => import('./features/administration/admin.routes').then(m => m.adminRoutes),
        title: 'Administration — LegalDesk'
      },
      {
        path: 'legal/privacy',
        loadComponent: () => import('./features/legal/privacy-policy.component').then(m => m.PrivacyPolicyComponent),
        title: 'Privacy Policy — LegalDesk'
      },
      {
        path: 'legal/terms',
        loadComponent: () => import('./features/legal/terms-of-service.component').then(m => m.TermsOfServiceComponent),
        title: 'Terms of Service — LegalDesk'
      },
      {
        path: 'legal/cookies',
        loadComponent: () => import('./features/legal/cookie-policy.component').then(m => m.CookiePolicyComponent),
        title: 'Cookie Policy — LegalDesk'
      },
      {
        path: 'privacy/dsar',
        loadComponent: () => import('./features/legal/dsar-portal.component').then(m => m.DsarPortalComponent),
        title: 'Data Rights — LegalDesk'
      }
    ]
  },
  {
    path: 'sign/:token',
    loadComponent: () => import('./features/signing/signing.component').then(m => m.SigningComponent),
    title: 'Sign Contract — LegalDesk'
  },
  {
    path: '**',
    loadComponent: () => import('./shared/not-found/not-found.component').then(m => m.NotFoundComponent)
  }
];
