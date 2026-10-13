import { HttpErrorResponse } from '@angular/common/http';
import { Component, inject, OnInit, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { AdminDetailDialog } from '../admin/admin-detail-dialog';
import { ManufacturerClient, ManufacturerCompany, ManufacturerSession } from '../../core/manufacturer/manufacturer-client';

@Component({
  selector: 'app-manufacturer-page',
  imports: [ReactiveFormsModule, AdminDetailDialog],
  templateUrl: './manufacturer-page.html',
  styleUrls: ['../admin/admin-page.scss', './manufacturer-page.scss'],
})
export class ManufacturerPage implements OnInit {
  private readonly client = inject(ManufacturerClient);
  private readonly formBuilder = inject(FormBuilder);

  protected readonly session = signal<ManufacturerSession | null>(null);
  protected readonly checked = signal(false);
  protected readonly busy = signal(false);
  protected readonly companies = signal<readonly ManufacturerCompany[]>([]);
  protected readonly loadingCompanies = signal(false);
  protected readonly createOpen = signal(false);
  protected readonly editingCompany = signal<ManufacturerCompany | null>(null);
  protected readonly editName = signal('');
  protected readonly statusCompany = signal<ManufacturerCompany | null>(null);
  protected readonly error = signal<string | null>(null);
  protected readonly message = signal<string | null>(null);

  protected readonly loginForm = this.formBuilder.nonNullable.group({
    credential: ['', [Validators.required, Validators.maxLength(320)]],
    password: ['', Validators.required],
  });
  protected readonly createForm = this.formBuilder.nonNullable.group({
    name: ['', [Validators.required, Validators.maxLength(200)]],
    code: ['', [Validators.required, Validators.pattern(/^[A-Za-z0-9][A-Za-z0-9-]{0,38}[A-Za-z0-9]$/)]],
    initialBranchName: ['', [Validators.required, Validators.maxLength(200)]],
    adminUsername: ['', [Validators.required, Validators.maxLength(100), Validators.pattern(/^[^@]+$/)]],
    adminEmail: ['', [Validators.required, Validators.email, Validators.maxLength(320)]],
    adminPassword: ['', Validators.required],
  });

  ngOnInit(): void {
    this.client.current().subscribe({
      next: (session) => {
        this.session.set(session);
        this.checked.set(true);
        this.loadCompanies();
      },
      error: () => this.checked.set(true),
    });
  }

  protected login(): void {
    if (this.loginForm.invalid || this.busy()) return;
    this.busy.set(true);
    this.error.set(null);
    const { credential, password } = this.loginForm.getRawValue();
    this.client.login(credential, password).subscribe({
      next: (session) => {
        this.session.set(session);
        this.loginForm.reset();
        this.busy.set(false);
        this.loadCompanies();
      },
      error: () => {
        this.error.set('Usuario o contraseña incorrectos, o servicio no disponible.');
        this.busy.set(false);
      },
    });
  }

  protected logout(): void {
    if (this.busy()) return;
    this.busy.set(true);
    this.client.logout().subscribe({
      next: () => {
        this.session.set(null);
        this.companies.set([]);
        this.busy.set(false);
      },
      error: () => {
        this.error.set('No se pudo cerrar la sesión.');
        this.busy.set(false);
      },
    });
  }

  protected openCreate(): void {
    this.createForm.reset();
    this.error.set(null);
    this.createOpen.set(true);
  }

  protected closeCreate(): void {
    if (this.busy()) return;
    this.createOpen.set(false);
    this.createForm.reset();
  }

  protected createCompany(): void {
    if (this.createForm.invalid || this.busy()) {
      this.createForm.markAllAsTouched();
      return;
    }
    this.busy.set(true);
    this.error.set(null);
    const value = this.createForm.getRawValue();
    this.client.createCompany({
      name: value.name.trim(), code: value.code.trim().toUpperCase(),
      initialBranchName: value.initialBranchName.trim(),
      adminUsername: value.adminUsername.trim(), adminEmail: value.adminEmail.trim(),
      adminPassword: value.adminPassword,
    }).subscribe({
      next: () => {
        this.busy.set(false);
        this.closeCreate();
        this.message.set('Empresa, primera sucursal y administrador creados.');
        this.loadCompanies();
      },
      error: (response: HttpErrorResponse) => {
        this.busy.set(false);
        this.error.set(response.status === 409 ? 'El código o nombre de empresa ya existe.' :
          response.status === 400 ? 'Revisá los datos y la contraseña inicial.' :
            'No se pudo crear la empresa.');
      },
    });
  }

  protected startEdit(company: ManufacturerCompany): void {
    this.editingCompany.set(company);
    this.editName.set(company.name);
    this.error.set(null);
  }

  protected closeEdit(): void {
    if (!this.busy()) this.editingCompany.set(null);
  }

  protected changeEditName(event: Event): void {
    this.editName.set((event.target as HTMLInputElement).value.slice(0, 200));
  }

  protected saveEdit(): void {
    const company = this.editingCompany();
    const name = this.editName().trim();
    if (!company || !name || this.busy()) return;
    this.busy.set(true);
    this.client.updateCompany(company.id, { name }).subscribe({
      next: (updated) => {
        this.replaceCompany(updated);
        this.busy.set(false);
        this.editingCompany.set(null);
        this.message.set('Nombre de empresa actualizado; el código de acceso no cambió.');
      },
      error: () => {
        this.error.set('No se pudo actualizar la empresa.');
        this.busy.set(false);
      },
    });
  }

  protected confirmStatus(company: ManufacturerCompany): void {
    this.statusCompany.set(company);
    this.error.set(null);
  }

  protected closeStatus(): void {
    if (!this.busy()) this.statusCompany.set(null);
  }

  protected changeStatus(): void {
    const company = this.statusCompany();
    if (!company || this.busy()) return;
    this.busy.set(true);
    this.client.updateCompany(company.id, { isActive: !company.isActive }).subscribe({
      next: (updated) => {
        this.replaceCompany(updated);
        this.statusCompany.set(null);
        this.busy.set(false);
        this.message.set(updated.isActive ? 'Empresa activada.' : 'Empresa inactivada.');
      },
      error: (response: HttpErrorResponse) => {
        this.error.set(response.status === 409
          ? 'La empresa tiene turnos o tickets abiertos; no se puede inactivar.'
          : 'No se pudo cambiar el estado de la empresa.');
        this.busy.set(false);
      },
    });
  }

  private loadCompanies(): void {
    this.loadingCompanies.set(true);
    this.client.companies().subscribe({
      next: (companies) => {
        this.companies.set(companies);
        this.loadingCompanies.set(false);
      },
      error: () => {
        this.error.set('No se pudo cargar la lista de empresas.');
        this.loadingCompanies.set(false);
      },
    });
  }

  private replaceCompany(updated: ManufacturerCompany): void {
    this.companies.update((companies) => companies.map((company) =>
      company.id === updated.id ? updated : company));
  }
}
