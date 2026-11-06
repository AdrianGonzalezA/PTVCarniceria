import { Component, inject, OnInit, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { ProductionClassifier, ProductionClient } from '../../core/production/production-client';
import { AdminDetailDialog } from './admin-detail-dialog';

@Component({
  selector: 'app-production-classifiers-page',
  imports: [RouterLink, AdminDetailDialog],
  template: `
    <main class="admin-shell">
      <header class="app-header"><a class="brand" routerLink="/admin/configuracion">
        <span class="brand-mark" aria-hidden="true"></span>
        <span>Carnicerías <small>· Configuración</small></span></a></header>
      <div class="workspace">
        <a class="back-link" routerLink="/admin/configuracion">← Volver</a>
        <p class="eyebrow">Configuración · Producción</p>
        <h1>Tipos de producción</h1>
        <p class="intro">Clasificá las órdenes sin imponer una receta ni un rendimiento fijo.</p>
        @if (error(); as message) { <p class="error-message" role="alert">{{ message }}</p> }
        @if (notice(); as message) { <p class="success-message" role="status">{{ message }}</p> }
        <section class="category-panel" aria-labelledby="classifier-title">
          <header class="panel-heading"><h2 id="classifier-title">Clasificadores</h2></header>
          <form class="editor-form" (submit)="$event.preventDefault(); create()">
            <label for="classifier-name">Nuevo tipo</label>
            <input id="classifier-name" maxlength="120" required [value]="name()"
              (input)="name.set($any($event.target).value)" />
            <div class="editor-actions"><button type="submit" [disabled]="saving()">Crear tipo</button></div>
          </form>
          @if (loading()) { <p class="table-note" role="status">Cargando tipos…</p> }
          @else { <div class="table-scroll"><table><thead><tr>
            <th>Nombre</th><th>Estado</th><th>Acción</th>
          </tr></thead><tbody>
            @for (item of classifiers(); track item.id) {
              <tr><td>{{ item.name }}</td><td>{{ item.isActive ? 'Activo' : 'Inactivo' }}</td>
                <td><button type="button" class="secondary-button" (click)="edit(item)">Editar</button></td></tr>
            } @empty { <tr><td colspan="3">Todavía no hay tipos de producción.</td></tr> }
          </tbody></table></div> }
        </section>
      </div>
    </main>
    @if (selected(); as item) {
      <app-admin-detail-dialog labelledBy="classifier-edit-title" [compact]="true"
        [canClose]="!saving()" (closed)="selected.set(null)">
        <div class="classifier-dialog"><h2 id="classifier-edit-title">Editar tipo de producción</h2>
          <label for="classifier-edit-name">Nombre</label>
          <input id="classifier-edit-name" maxlength="120" required [value]="editName()"
            (input)="editName.set($any($event.target).value)" />
          <label><input type="checkbox" [checked]="editActive()"
            (change)="editActive.set($any($event.target).checked)" /> Activo</label>
          <div class="editor-actions"><button type="button" [disabled]="saving()" (click)="save()">Guardar</button>
            <button type="button" class="secondary-button" [disabled]="saving()"
              (click)="selected.set(null)">Cancelar</button></div>
        </div>
      </app-admin-detail-dialog>
    }
  `,
  styles: [`
    .classifier-dialog { display: grid; gap: .75rem; min-width: min(30rem, 80vw); padding: 1rem; }
    .classifier-dialog h2 { margin: 0; }
    .classifier-dialog input[type=text] { width: 100%; box-sizing: border-box; }
  `],
  styleUrls: ['./admin-page.scss', './categories-page.scss'],
})
export class ProductionClassifiersPage implements OnInit {
  private readonly client = inject(ProductionClient);
  protected readonly classifiers = signal<readonly ProductionClassifier[]>([]);
  protected readonly loading = signal(true);
  protected readonly saving = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly notice = signal<string | null>(null);
  protected readonly name = signal('');
  protected readonly selected = signal<ProductionClassifier | null>(null);
  protected readonly editName = signal('');
  protected readonly editActive = signal(true);

  ngOnInit(): void { this.reload(); }

  protected reload(): void {
    this.loading.set(true);
    this.client.classifiers(true).subscribe({
      next: rows => { this.classifiers.set(rows); this.loading.set(false); },
      error: () => { this.error.set('No se pudieron cargar los tipos.'); this.loading.set(false); },
    });
  }

  protected create(): void {
    const name = this.name().trim();
    if (!name || name.length > 120 || this.saving()) return;
    this.saving.set(true); this.error.set(null); this.notice.set(null);
    this.client.createClassifier(name).subscribe({
      next: () => { this.name.set(''); this.saving.set(false);
        this.notice.set('Tipo creado.'); this.reload(); },
      error: () => { this.saving.set(false);
        this.error.set('No se pudo crear. Revisá si ya existe un tipo con ese nombre.'); },
    });
  }

  protected edit(item: ProductionClassifier): void {
    this.selected.set(item); this.editName.set(item.name); this.editActive.set(item.isActive);
  }

  protected save(): void {
    const item = this.selected(); const name = this.editName().trim();
    if (!item || !name || name.length > 120 || this.saving()) return;
    this.saving.set(true); this.error.set(null); this.notice.set(null);
    this.client.updateClassifier(item.id, { name, isActive: this.editActive() }).subscribe({
      next: () => { this.selected.set(null); this.saving.set(false);
        this.notice.set('Tipo actualizado.'); this.reload(); },
      error: () => { this.saving.set(false);
        this.error.set('No se pudo guardar. Revisá si el nombre ya está en uso.'); },
    });
  }
}
