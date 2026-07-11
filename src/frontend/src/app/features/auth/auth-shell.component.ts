import { Component } from '@angular/core';
import { RouterOutlet } from '@angular/router';
import { MatIconModule } from '@angular/material/icon';
import { TranslatePipe } from '@ngx-translate/core';

@Component({
  selector: 'sf-auth-shell',
  standalone: true,
  imports: [RouterOutlet, MatIconModule, TranslatePipe],
  template: `
    <div class="auth-layout">
      <div class="auth-brand">
        <div class="brand-content">
          <div class="brand-logo">
            <mat-icon>draw</mat-icon>
          </div>
          <h1>SignFlow</h1>
          <p>{{ 'auth.shell.tagline' | translate }}</p>
          <div class="brand-features">
            <div class="feature">
              <mat-icon>verified_user</mat-icon>
              <span>{{ 'auth.shell.feature1' | translate }}</span>
            </div>
            <div class="feature">
              <mat-icon>speed</mat-icon>
              <span>{{ 'auth.shell.feature2' | translate }}</span>
            </div>
            <div class="feature">
              <mat-icon>lock</mat-icon>
              <span>{{ 'auth.shell.feature3' | translate }}</span>
            </div>
          </div>
        </div>
      </div>
      <div class="auth-form-area">
        <router-outlet></router-outlet>
      </div>
    </div>
  `,
  styleUrl: './auth-shell.component.scss'
})
export class AuthShellComponent {}
