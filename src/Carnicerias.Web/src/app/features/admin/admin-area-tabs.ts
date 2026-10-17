import { Component, input } from '@angular/core';
import { RouterLink } from '@angular/router';

@Component({
  selector: 'app-admin-area-tabs',
  imports: [RouterLink],
  template: `
    <nav class="area-tabs" aria-label="Áreas de administración">
      <a routerLink="/admin" [attr.aria-current]="area() === 'business' ? 'page' : null">Negocio</a>
      <a routerLink="/admin/configuracion" [attr.aria-current]="area() === 'configuration' ? 'page' : null">Configuración</a>
    </nav>
  `,
  styles: [`
    :host { --admin-tab-border: #6b7774; --admin-tab-ink: #dbe7e4; --admin-tab-active: #fff; --admin-tab-accent: #54c2bd; }
    :host-context(html[data-visual-theme='nocturno']) {
      --admin-tab-border: #414750; --admin-tab-ink: #bbb8b3;
      --admin-tab-active: #f3efe9; --admin-tab-accent: #e5b27f;
    }
    .area-tabs { display: flex; gap: .5rem; margin: 0 0 1.25rem; border-bottom: 1px solid var(--admin-tab-border); }
    .area-tabs a { display: inline-block; padding: .75rem 1rem; color: var(--admin-tab-ink); border-bottom: .25rem solid transparent; font-weight: 700; text-decoration: none; }
    .area-tabs a[aria-current='page'] { color: var(--admin-tab-active); border-bottom-color: var(--admin-tab-accent); }
    .area-tabs a:hover { color: var(--admin-tab-active); }
    .area-tabs a:focus-visible { outline: 2px solid var(--admin-tab-accent); outline-offset: 2px; }
  `],
})
export class AdminAreaTabs {
  readonly area = input.required<'business' | 'configuration'>();
}
