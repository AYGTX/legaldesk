import { Component, OnInit, inject, signal } from '@angular/core';
import { RouterOutlet, RouterLink, RouterLinkActive, Router } from '@angular/router';
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

interface NavItem {
  labelKey: string;
  icon: string;
  path: string;
  roles?: string[];
}

@Component({
  selector: 'sf-layout',
  standalone: true,
  imports: [
    CommonModule, RouterOutlet, RouterLink, RouterLinkActive,
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
            <mat-icon class="logo-icon">draw</mat-icon>
          </div>
          @if (!sidenavCollapsed()) {
            <div class="logo-text-wrap">
              <span class="logo-text">SignFlow</span>
              <span class="logo-sub">e-Signature</span>
            </div>
          }
        </div>

        <!-- Nav items -->
        <div class="nav-items">
          @for (item of visibleNavItems(); track item.path) {
            <a class="nav-item" [routerLink]="item.path" routerLinkActive="nav-active"
               [matTooltip]="sidenavCollapsed() ? (item.labelKey | translate) : ''"
               matTooltipPosition="right">
              <mat-icon class="nav-icon">{{ item.icon }}</mat-icon>
              @if (!sidenavCollapsed()) {
                <span class="nav-label">{{ item.labelKey | translate }}</span>
              }
            </a>
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
          <span class="copy">&copy; {{ year }} SignFlow. {{ 'layout.allRightsReserved' | translate }}</span>
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

  readonly sidenavCollapsed = signal(false);

  private readonly navItems: NavItem[] = [
    { labelKey: 'nav.dashboard',      icon: 'dashboard',           path: '/dashboard' },
    { labelKey: 'nav.myRequests',     icon: 'move_to_inbox',       path: '/requests' },
    { labelKey: 'nav.documents',      icon: 'description',         path: '/documents' },
    { labelKey: 'nav.templates',      icon: 'article',             path: '/templates' },
    { labelKey: 'nav.auditTrail',     icon: 'history',             path: '/audit' },
    { labelKey: 'nav.administration', icon: 'admin_panel_settings', path: '/admin', roles: ['Administrator'] }
  ];

  readonly visibleNavItems = () => this.navItems.filter(item =>
    !item.roles || item.roles.some(r => this.auth.user()?.roles.includes(r))
  );

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
