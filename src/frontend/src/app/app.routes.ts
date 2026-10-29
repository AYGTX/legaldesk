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
        path: 'contracts',
        title: 'Contracts — LegalDesk',
        children: [
          { path: '', pathMatch: 'full', loadComponent: () => import('./features/documents/documents-list.component').then(m => m.DocumentsListComponent) },
          { path: 'review', loadComponent: () => import('./features/review/contract-review.component').then(m => m.ContractReviewComponent), title: 'Contract Review — LegalDesk' },
          { path: 'schedule', loadComponent: () => import('./features/schedule/contract-schedule.component').then(m => m.ContractScheduleComponent), title: 'Contract schedule — LegalDesk' },
          { path: 'new', loadComponent: () => import('./features/documents/new-contract-request.component').then(m => m.NewContractRequestComponent), title: 'New Contract Request — LegalDesk' },
          { path: 'upload', loadComponent: () => import('./features/documents/upload-document.component').then(m => m.UploadDocumentComponent), title: 'Upload Contract — LegalDesk' },
          {
            path: 'from-pdf-template',
            children: [
              {
                path: '', pathMatch: 'full', data: { kind: 'pdf' },
                loadComponent: () => import('./features/templates/templates.component').then(m => m.TemplatesComponent),
                title: 'PDF Contract Templates — LegalDesk'
              },
              {
                path: ':id',
                loadComponent: () => import('./features/pdf-templates/pdf-template-request.component').then(m => m.PdfTemplateRequestComponent),
                title: 'New Validation Request — LegalDesk'
              }
            ]
          },
          { path: 'from-html-template', loadComponent: () => import('./features/requests/new-request.component').then(m => m.NewRequestComponent), title: 'New Validation Request — LegalDesk' },
          { path: ':id', loadComponent: () => import('./features/documents/document-detail.component').then(m => m.DocumentDetailComponent), title: 'Contract Detail — LegalDesk' },
          { path: ':id/designer', loadComponent: () => import('./features/designer/designer.component').then(m => m.DesignerComponent), title: 'Contract Preparation — LegalDesk' },
          { path: ':id/audit', loadComponent: () => import('./features/audit/audit.component').then(m => m.AuditComponent), title: 'Traceability — LegalDesk' }
        ]
      },
      {
        path: 'templates',
        children: [
          {
            path: '', pathMatch: 'full',
            loadComponent: () => import('./features/templates/templates.component').then(m => m.TemplatesComponent),
            title: 'Contract Templates — LegalDesk'
          },
          {
            path: 'pdf',
            loadChildren: () => import('./features/pdf-templates/pdf-templates.routes').then(m => m.pdfTemplatesRoutes)
          }
        ]
      },
      {
        path: 'insights',
        children: [
          { path: '', pathMatch: 'full', redirectTo: 'reports' },
          { path: 'reports', loadComponent: () => import('./features/reports/reports.component').then(m => m.ReportsComponent), title: 'Reports — LegalDesk' },
          { path: 'audit', loadComponent: () => import('./features/audit/org-audit.component').then(m => m.OrgAuditComponent), title: 'Traceability — LegalDesk' }
        ]
      },

      // Old addresses (before the URLs followed the menu); links and bookmarks keep working.
      { path: 'documents', pathMatch: 'full', redirectTo: 'contracts' },
      { path: 'documents/new-request', redirectTo: 'contracts/new' },
      { path: 'documents/upload', redirectTo: 'contracts/upload' },
      { path: 'documents/:id', redirectTo: ({ params }) => `contracts/${params['id']}` },
      { path: 'documents/:id/designer', redirectTo: ({ params }) => `contracts/${params['id']}/designer` },
      { path: 'documents/:id/audit', redirectTo: ({ params }) => `contracts/${params['id']}/audit` },
      { path: 'requests', pathMatch: 'full', redirectTo: 'contracts' },
      { path: 'requests/new', redirectTo: 'contracts/from-html-template' },
      { path: 'requests/from-html-template', redirectTo: 'contracts/from-html-template' },
      { path: 'review', redirectTo: 'contracts/review' },
      { path: 'reports', redirectTo: 'insights/reports' },
      { path: 'audit', redirectTo: 'insights/audit' },
      { path: 'pdf-templates', pathMatch: 'full', redirectTo: 'templates/pdf' },
      { path: 'pdf-templates/new', redirectTo: 'templates/pdf/new' },
      { path: 'pdf-templates/:id/designer', redirectTo: ({ params }) => `templates/pdf/${params['id']}/designer` },
      { path: 'pdf-templates/:id/new', redirectTo: ({ params }) => `contracts/from-pdf-template/${params['id']}` },
      { path: 'schedule', redirectTo: 'contracts/schedule' },
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
