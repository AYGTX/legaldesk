import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { filter } from 'rxjs';
import { RouterOutlet, RouterLink, Router, NavigationEnd } from '@angular/router';
import { CommonModule } from '@angular/common';
import { MatIconModule } from '@angular/material/icon';
import { MatButtonModule } from '@angular/material/button';
import { MatMenuModule } from '@angular/material/menu';
import { MatDividerModule } from '@angular/material/divider';
import { MatTooltipModule } from '@angular/material/tooltip';
import { TranslatePipe } from '@ngx-translate/core';
import { AuthService } from '../../core/auth/auth.service';
import { HubService } from '../../core/services/hub.service';
import { resolveFileUrl } from '../../core/utils/file-url';
import { NotificationBellComponent } from '../notification-bell/notification-bell.component';
import { CookieConsentBannerComponent } from '../cookie-consent/cookie-consent-banner.component';
import { ThemeService } from '../../core/services/theme.service';
import { LanguageService, AppLanguage } from '../../core/services/language.service';
import { environment } from '../../../environments/environment';

/**
 * One menu entry: a link, or a group of links when `children` is set. Active state is computed from the
 * current URL (see isLeafActive) rather than by routerLinkActive, because several entries share a path and
 * differ only by query string (`/templates?kind=pdf`, `/admin?tab=users`).
 */
interface NavEntry {
  id: string;
  labelKey: string;
  icon?: string;
  path?: string;
  query?: Record<string, string>;
  /** Query values assumed when the URL carries none, so `/admin` counts as `/admin?tab=users`. */
  queryDefault?: Record<string, string>;
  /** The entry is not active while the URL has any of these query keys (plain /templates vs ?kind=pdf). */
  withoutQuery?: string[];
  /** Also active on sub-paths (`/documents/:id`). */
  prefix?: boolean;
  /** Sub-paths that belong to a sibling entry instead. */
  exclude?: string[];
  /** Further path prefixes that count as this entry. */
  alsoPrefixes?: string[];
  roles?: string[];
  children?: NavEntry[];
}

const MANAGERS = ['Administrator', 'Manager'];

const NAV: NavEntry[] = [
  { id: 'dashboard', labelKey: 'nav.dashboard', icon: 'dashboard', path: '/dashboard' },
  {
    id: 'contracts', labelKey: 'nav.documents', icon: 'description',
    children: [
      { id: 'all', labelKey: 'nav.allContracts', path: '/documents', prefix: true, exclude: ['/documents/upload', '/documents/new-request'] },
      { id: 'review', labelKey: 'nav.review', path: '/review', prefix: true },
      { id: 'schedule', labelKey: 'nav.schedule', path: '/schedule' },
      { id: 'upload', labelKey: 'nav.uploadFile', path: '/documents/new-request' },
      { id: 'use-pdf', labelKey: 'nav.usePdfTemplate', path: '/templates', query: { kind: 'pdf' }, alsoPrefixes: ['/pdf-templates'] },
      { id: 'use-html', labelKey: 'nav.useHtmlTemplate', path: '/requests/from-html-template' }
    ]
  },
  { id: 'templates', labelKey: 'nav.templates', icon: 'article', path: '/templates', withoutQuery: ['kind'] },
  {
    id: 'insights', labelKey: 'nav.insights', icon: 'monitoring', roles: MANAGERS,
    children: [
      { id: 'reports', labelKey: 'nav.reports', path: '/reports', prefix: true },
      { id: 'audit', labelKey: 'nav.auditTrail', path: '/audit', prefix: true }
    ]
  },
  // Capability map hidden from the menu; the page is still reachable at /capabilities.
  {
    id: 'admin', labelKey: 'nav.administration', icon: 'admin_panel_settings', roles: ['Administrator'],
    children: [
      { id: 'users', labelKey: 'admin.tabs.users', path: '/admin', query: { tab: 'users' }, queryDefault: { tab: 'users' } },
      { id: 'types', labelKey: 'admin.tabs.requestTypes', path: '/admin', query: { tab: 'types' } },
      { id: 'manage-templates', labelKey: 'admin.tabs.htmlTemplates', path: '/admin/html-templates', prefix: true },
      { id: 'workflows', labelKey: 'admin.tabs.workflows', path: '/admin/workflows', prefix: true },
      { id: 'clauses', labelKey: 'admin.tabs.clauses', path: '/admin/clauses', prefix: true },
      { id: 'settings', labelKey: 'admin.tabs.systemSettings', path: '/admin', query: { tab: 'settings' } }
    ]
  }
];

@Component({
  selector: 'sf-layout',
  standalone: true,
  imports: [
    CommonModule, RouterOutlet, RouterLink,
    MatIconModule, MatButtonModule, MatMenuModule, MatDividerModule, MatTooltipModule,
    TranslatePipe,
    NotificationBellComponent, CookieConsentBannerComponent
  ],
  template: `
    <div class="shell">
      <!-- Left sidebar -->
      <nav class="sidenav" [class.collapsed]="sidenavCollapsed()">
        <!-- Logo -->
        <div class="logo-row" [class.collapsed]="sidenavCollapsed()">
          <div class="logo-icon-wrap">
            <mat-icon class="logo-icon">gavel</mat-icon>
          </div>
          @if (!sidenavCollapsed()) {
            <div class="logo-text-wrap">
              <span class="logo-text">{{ appName }}</span>
              <span class="logo-sub">{{ 'layout.tagline' | translate }}</span>
            </div>
          }
        </div>

        <!-- Nav items -->
        <div class="nav-items">
          @for (entry of visibleNav(); track entry.id) {
            @if (entry.children) {
              @if (sidenavCollapsed()) {
                <!-- Collapsed rail: a group is one icon that opens its links in a flyout. -->
                <button type="button" class="nav-item" [class.nav-active]="isGroupActive(entry)"
                        [matMenuTriggerFor]="groupMenu"
                        [matTooltip]="entry.labelKey | translate" matTooltipPosition="right">
                  <mat-icon class="nav-icon">{{ entry.icon }}</mat-icon>
                </button>
                <mat-menu #groupMenu="matMenu" xPosition="after">
                  @for (child of entry.children; track child.id) {
                    <a mat-menu-item [routerLink]="child.path" [queryParams]="child.query"
                       [class.nav-active]="isLeafActive(child)">{{ child.labelKey | translate }}</a>
                  }
                </mat-menu>
              } @else {
                <button type="button" class="nav-item nav-group"
                        [class.nav-active]="isGroupActive(entry) && !isOpen(entry)"
                        [attr.aria-expanded]="isOpen(entry)" (click)="toggleGroup(entry)">
                  <mat-icon class="nav-icon">{{ entry.icon }}</mat-icon>
                  <span class="nav-label">{{ entry.labelKey | translate }}</span>
                  <mat-icon class="nav-chevron">{{ isOpen(entry) ? 'expand_less' : 'expand_more' }}</mat-icon>
                </button>
                @if (isOpen(entry)) {
                  <div class="nav-children">
                    @for (child of entry.children; track child.id) {
                      <a class="nav-child" [routerLink]="child.path" [queryParams]="child.query"
                         [class.nav-active]="isLeafActive(child)">{{ child.labelKey | translate }}</a>
                    }
                  </div>
                }
              }
            } @else {
              <a class="nav-item" [routerLink]="entry.path" [queryParams]="entry.query"
                 [class.nav-active]="isLeafActive(entry)"
                 [matTooltip]="sidenavCollapsed() ? (entry.labelKey | translate) : ''"
                 matTooltipPosition="right">
                <mat-icon class="nav-icon">{{ entry.icon }}</mat-icon>
                @if (!sidenavCollapsed()) {
                  <span class="nav-label">{{ entry.labelKey | translate }}</span>
                }
              </a>
            }
          }
        </div>

        <!-- Sidenav footer: user + status + controls -->
        <div class="sidenav-footer" [class.collapsed]="sidenavCollapsed()">

          <!-- User button -->
          <button class="footer-user-btn" [matMenuTriggerFor]="userMenu"
                  [matTooltip]="sidenavCollapsed() ? userInitials() : ''"
                  matTooltipPosition="right">
            @if (avatarSrc(); as src) {
              <img [src]="src" class="user-avatar-img" alt="Avatar" />
            } @else {
              <span class="user-avatar">{{ userInitials() }}</span>
            }
            @if (!sidenavCollapsed()) {
              @if (auth.user(); as u) {
                <span class="footer-user-name">{{ u.firstName }} {{ u.lastName }}</span>
              }
              <mat-icon class="chevron">expand_more</mat-icon>
            }
          </button>

          <mat-menu #userMenu="matMenu" yPosition="above">
            <div class="menu-header" (click)="$event.stopPropagation()">
              @if (auth.user(); as u) {
                <p class="menu-name">{{ u.firstName }} {{ u.lastName }}</p>
                <p class="menu-email">{{ u.email }}</p>
              }
            </div>
            <mat-divider></mat-divider>
            <button mat-menu-item routerLink="/profile">
              <mat-icon>manage_accounts</mat-icon>
              <span>{{ 'layout.manageAccount' | translate }}</span>
            </button>
            <button mat-menu-item (click)="logout()">
              <mat-icon>logout</mat-icon>
              <span>{{ 'layout.signOut' | translate }}</span>
            </button>
          </mat-menu>

          <!-- Controls row: conn dot · notifications · language · collapse toggle -->
          <div class="footer-controls">
            <span class="conn-dot" [class.online]="hub.isConnected()"
                  [matTooltip]="hub.isConnected() ? ('layout.realtimeConnected' | translate) : ('layout.realtimeDisconnected' | translate)"
                  matTooltipPosition="right"></span>

            <sf-notification-bell></sf-notification-bell>

            <button class="icon-btn" [matMenuTriggerFor]="langMenu"
                    [matTooltip]="'common.language' | translate"
                    matTooltipPosition="right">
              <mat-icon>translate</mat-icon>
            </button>
            <mat-menu #langMenu="matMenu">
              <button mat-menu-item (click)="setLanguage('en')" [class.active-lang]="language.current() === 'en'">
                {{ 'common.english' | translate }}
              </button>
              <button mat-menu-item (click)="setLanguage('fr')" [class.active-lang]="language.current() === 'fr'">
                {{ 'common.french' | translate }}
              </button>
            </mat-menu>

            <button class="icon-btn" (click)="theme.toggle()"
                    [matTooltip]="theme.dark() ? ('layout.switchToLight' | translate) : ('layout.switchToDark' | translate)"
                    matTooltipPosition="right">
              <mat-icon>{{ theme.dark() ? 'light_mode' : 'dark_mode' }}</mat-icon>
            </button>

            <button class="icon-btn" (click)="toggleSidenav()"
                    [matTooltip]="sidenavCollapsed() ? ('layout.expandSidebar' | translate) : ('layout.collapseSidebar' | translate)"
                    matTooltipPosition="right">
              <mat-icon>{{ sidenavCollapsed() ? 'menu_open' : 'menu' }}</mat-icon>
            </button>
          </div>
        </div>
      </nav>

      <!-- Content area (no topbar) -->
      <div class="main-area">
        <main class="page-content">
          <router-outlet></router-outlet>
        </main>
        <footer class="legal-footer">
          <a routerLink="/legal/privacy">{{ 'layout.privacyPolicy' | translate }}</a>
          <span class="dot">·</span>
          <a routerLink="/legal/terms">{{ 'layout.termsOfService' | translate }}</a>
          <span class="dot">·</span>
          <a routerLink="/legal/cookies">{{ 'layout.cookiePolicy' | translate }}</a>
          <span class="dot">·</span>
          <span class="copy">&copy; {{ year }} {{ appName }}. {{ 'layout.allRightsReserved' | translate }}</span>
        </footer>
      </div>
    </div>

    <!-- GDPR cookie consent banner (renders only until user decides) -->
    <sf-cookie-consent-banner></sf-cookie-consent-banner>
  `,
  styleUrl: './layout.component.scss'
})
export class LayoutComponent implements OnInit {
  readonly auth = inject(AuthService);
  readonly hub = inject(HubService);
  readonly theme = inject(ThemeService);
  readonly language = inject(LanguageService);
  private readonly router = inject(Router);
  readonly year = new Date().getFullYear();
  readonly appName = environment.appName;

  readonly sidenavCollapsed = signal(false);

  /** Menu entries the signed-in user may see; a group with nothing left to show disappears. */
  readonly visibleNav = computed(() => {
    const roles = this.auth.user()?.roles ?? [];
    const allowed = (r?: string[]) => !r || r.some(x => roles.includes(x));
    return NAV
      .filter(e => allowed(e.roles))
      .map(e => e.children ? { ...e, children: e.children.filter(c => allowed(c.roles)) } : e)
      .filter(e => !e.children || e.children.length > 0);
  });

  private readonly url = signal(this.router.url);
  private readonly openGroups = signal<ReadonlySet<string>>(new Set());

  /** The current URL as a path plus query values, for matching menu entries. */
  private readonly location = computed(() => {
    const tree = this.router.parseUrl(this.url());
    const segments = tree.root.children['primary']?.segments.map(s => s.path) ?? [];
    return { path: '/' + segments.join('/'), query: tree.queryParams as Record<string, string> };
  });

  constructor() {
    this.router.events.pipe(
      filter(e => e instanceof NavigationEnd),
      takeUntilDestroyed()
    ).subscribe(() => {
      this.url.set(this.router.url);
      this.openActiveGroups();
    });
  }

  isOpen(group: NavEntry): boolean { return this.openGroups().has(group.id); }

  toggleGroup(group: NavEntry): void {
    const next = new Set(this.openGroups());
    if (!next.delete(group.id)) next.add(group.id);
    this.openGroups.set(next);
  }

  /** Opens the group holding the current page, leaving any group the user opened themselves as it is. */
  private openActiveGroups(): void {
    const active = this.visibleNav().filter(e => e.children && this.isGroupActive(e)).map(e => e.id);
    if (active.length === 0) return;
    this.openGroups.update(open => new Set([...open, ...active]));
  }

  isGroupActive(group: NavEntry): boolean {
    return (group.children ?? []).some(c => this.isLeafActive(c));
  }

  isLeafActive(entry: NavEntry): boolean {
    if (!entry.path) return false;
    const { path, query } = this.location();
    const under = (prefix: string) => path === prefix || path.startsWith(prefix + '/');

    if ((entry.alsoPrefixes ?? []).some(under)) return true;

    const pathMatches = entry.prefix ? under(entry.path) : path === entry.path;
    if (!pathMatches) return false;
    if ((entry.exclude ?? []).some(under)) return false;
    if ((entry.withoutQuery ?? []).some(k => k in query)) return false;

    return Object.entries(entry.query ?? {})
      .every(([k, v]) => (query[k] ?? entry.queryDefault?.[k]) === v);
  }

  readonly userInitials = () => {
    const u = this.auth.user();
    if (!u) return '?';
    return `${u.firstName[0]}${u.lastName[0]}`.toUpperCase();
  };

  readonly avatarSrc = () => resolveFileUrl(this.auth.user()?.avatarUrl);

  ngOnInit(): void { this.hub.connect(); }

  toggleSidenav(): void { this.sidenavCollapsed.update(v => !v); }

  setLanguage(lang: AppLanguage): void { this.language.setLanguage(lang); }

  logout(): void {
    this.hub.disconnect();
    this.auth.logout();
  }
}
