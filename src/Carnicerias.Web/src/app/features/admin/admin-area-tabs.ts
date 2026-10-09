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
    .area-tabs { display: flex; gap: .5rem; margin: 0 0 1.25rem; border-bottom: 1px solid #6b7774; }
    .area-tabs a { display: inline-block; padding: .75rem 1rem; color: #dbe7e4; border-bottom: .25rem solid transparent; font-weight: 700; text-decoration: none; }
    .area-tabs a[aria-current='page'] { color: #fff; border-bottom-color: #54c2bd; }
    .area-tabs a:hover { color: #fff; }
    .area-tabs a:focus-visible { outline: 2px solid #54c2bd; outline-offset: 2px; }
  `],
})
export class AdminAreaTabs {
  readonly area = input.required<'business' | 'configuration'>();
}
