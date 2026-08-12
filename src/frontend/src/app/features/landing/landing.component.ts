import { Component } from '@angular/core';
import { RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { TranslatePipe } from '@ngx-translate/core';
import { environment } from '../../../environments/environment';

interface LandingFeature {
  icon: string;
  id: string;
}

@Component({
  selector: 'sf-landing',
  standalone: true,
  imports: [RouterLink, MatButtonModule, MatIconModule, TranslatePipe],
  template: `
    <div class="landing">
      <header class="landing-nav">
        <div class="brand">
          <mat-icon>gavel</mat-icon>
          <span>{{ appName }}</span>
        </div>
        <a routerLink="/auth/login" mat-stroked-button>{{ 'landing.nav.signIn' | translate }}</a>
      </header>

      <section class="hero">
        <h1>{{ 'landing.hero.title' | translate }}</h1>
        <p class="subtitle">{{ 'landing.hero.subtitle' | translate }}</p>
        <div class="hero-actions">
          <a routerLink="/auth/login" mat-flat-button color="primary">
            {{ 'landing.hero.cta' | translate }}
            <mat-icon>arrow_forward</mat-icon>
          </a>
        </div>
      </section>

      <section class="features">
        @for (f of features; track f.id) {
          <div class="feature-card">
            <mat-icon>{{ f.icon }}</mat-icon>
            <h3>{{ 'landing.features.' + f.id + '.title' | translate }}</h3>
            <p>{{ 'landing.features.' + f.id + '.desc' | translate }}</p>
          </div>
        }
      </section>

      <footer class="landing-footer">
        <span>&copy; {{ year }} {{ appName }}</span>
        <div class="footer-links">
          <a routerLink="/legal/privacy">{{ 'landing.footer.privacy' | translate }}</a>
          <a routerLink="/legal/terms">{{ 'landing.footer.terms' | translate }}</a>
        </div>
      </footer>
    </div>
  `,
  styleUrl: './landing.component.scss'
})
export class LandingComponent {
  readonly appName = environment.appName;
  readonly year = new Date().getFullYear();

  readonly features: LandingFeature[] = [
    { id: 'signatures', icon: 'verified_user' },
    { id: 'workflows', icon: 'account_tree' },
    { id: 'traceability', icon: 'history' }
  ];
}
