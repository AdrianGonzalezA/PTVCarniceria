import { HttpErrorResponse } from '@angular/common/http';
import { Component, inject, OnInit, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import {
  CentralManagedAdministrator,
  CentralManagedCompany,
  CentralManufacturerClient,
  CentralManufacturerSession,
} from './central-manufacturer-client';

type ManufacturerView = 'companies' | 'create' | 'administrators';

@Component({
  selector: 'app-central-manufacturer-page',
  standalone: true,
  imports: [ReactiveFormsModule],
  templateUrl: './central-manufacturer-page.html',
  styleUrls: ['./app.scss', './central-manufacturer-page.scss'],
})
export class CentralManufacturerPage implements OnInit {
  private readonly client = inject(CentralManufacturerClient);
  private readonly formBuilder = inject(FormBuilder);
  private sessionEpoch = 0;

  protected readonly checked = signal(false);
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly message = signal<string | null>(null);
  protected readonly session = signal<CentralManufacturerSession | null>(null);
  protected readonly view = signal<ManufacturerView>('companies');
  protected readonly companies = signal<readonly CentralManagedCompany[]>([]);
  protected readonly companiesBusy = signal(false);
  protected readonly companiesError = signal<string | null>(null);
  protected readonly companyPage = signal(1);
  protected readonly companyPages = signal(0);
  protected readonly companyCount = signal(0);
  protected readonly selectedCompany = signal<CentralManagedCompany | null>(null);
  protected readonly administrators = signal<readonly CentralManagedAdministrator[]>([]);
  protected readonly administratorsBusy = signal(false);
  protected readonly administratorsError = signal<string | null>(null);
  protected readonly administratorPage = signal(1);
  protected readonly administratorPages = signal(0);
  protected readonly administratorCount = signal(0);

  protected readonly loginForm = this.formBuilder.nonNullable.group({
    credential: ['', [Validators.required, Validators.maxLength(100)]],
    password: ['', Validators.required],
  });
  protected readonly createForm = this.formBuilder.nonNullable.group({
    code: ['', [Validators.required, Validators.pattern(/^[a-zA-Z0-9-]{2,40}$/)]],
    name: ['', [Validators.required, Validators.maxLength(200)]],
    initialAdministratorUsername: ['', [Validators.required, Validators.maxLength(100)]],
    initialAdministratorPassword: ['', [Validators.required, Validators.minLength(6)]],
  });
  protected readonly administratorForm = this.formBuilder.nonNullable.group({
    username: ['', [Validators.required, Validators.maxLength(100)]],
    password: ['', [Validators.required, Validators.minLength(6)]],
  });

  ngOnInit(): void {
    this.client.current().subscribe({
      next: session => {
        this.sessionEpoch++;
        this.session.set(session);
        this.checked.set(true);
        this.loadCompanies(1);
      },
      error: (error: HttpErrorResponse) => {
        this.checked.set(true);
        if (error.status !== 401) {
          this.error.set('No se pudo verificar la sesion de SuperAdmin central. Reintenta.');
        }
      },
    });
  }

  protected login(): void {
    if (this.loginForm.invalid || this.busy()) {
      this.loginForm.markAllAsTouched();
      return;
    }
    this.busy.set(true);
    this.error.set(null);
    const { credential, password } = this.loginForm.getRawValue();
    this.client.login(credential.trim(), password).subscribe({
      next: session => {
        this.sessionEpoch++;
        this.session.set(session);
        this.loginForm.reset();
        this.busy.set(false);
        this.loadCompanies(1);
      },
      error: (error: HttpErrorResponse) => {
        this.loginForm.controls.password.setValue('');
        this.error.set(error.status === 429
          ? 'Demasiados intentos. Espera unos minutos antes de reintentar.'
          : error.status === 401
            ? 'Credenciales invalidas. Revisa los datos e intenta nuevamente.'
            : 'No se pudo ingresar al portal central. Reintenta.');
        this.busy.set(false);
      },
    });
  }

  protected logout(): void {
    if (this.busy()) return;
    this.busy.set(true);
    this.error.set(null);
    this.client.logout().subscribe({
      next: () => {
        this.clearSession();
        this.busy.set(false);
      },
      error: () => {
        this.error.set('No se pudo cerrar la sesion. Reintenta.');
        this.busy.set(false);
      },
    });
  }

  protected loadCompanies(page: number): void {
    if (!this.session() || this.companiesBusy()) return;
    this.companiesBusy.set(true);
    this.companiesError.set(null);
    const epoch = this.sessionEpoch;
    this.client.companies(page).subscribe({
      next: result => {
        if (epoch !== this.sessionEpoch) return;
        this.companies.set(result.data);
        this.companyPage.set(result.pagination.page);
        this.companyPages.set(result.pagination.totalPages);
        this.companyCount.set(result.pagination.totalCount);
        this.companiesBusy.set(false);
      },
      error: (error: HttpErrorResponse) => {
        if (epoch !== this.sessionEpoch) return;
        this.companiesBusy.set(false);
        if (error.status === 401) this.expireSession();
        else this.companiesError.set('No se pudo cargar el listado. Reintenta.');
      },
    });
  }

  protected openCreate(): void {
    this.createForm.reset();
    this.message.set(null);
    this.error.set(null);
    this.view.set('create');
  }

  protected createCompany(): void {
    if (this.createForm.invalid || this.busy()) {
      this.createForm.markAllAsTouched();
      return;
    }
    this.busy.set(true);
    this.error.set(null);
    const values = this.createForm.getRawValue();
    this.client.createCompany({
      code: values.code.trim().toUpperCase(),
      name: values.name.trim(),
      initialAdministratorUsername: values.initialAdministratorUsername.trim(),
      initialAdministratorPassword: values.initialAdministratorPassword,
    }).subscribe({
      next: created => {
        this.createForm.reset();
        this.view.set('companies');
        this.message.set(`Empresa central ${created.code} creada con su primer administrador.`);
        this.busy.set(false);
        this.loadCompanies(1);
      },
      error: (error: HttpErrorResponse) => {
        this.createForm.controls.initialAdministratorPassword.setValue('');
        this.error.set(error.status === 401 ? 'La sesion vencio. Ingresa nuevamente.'
          : error.status === 409 ? 'El codigo de empresa ya existe.'
            : error.status === 400 ? 'Revisa los datos y la politica de contrasenas.'
              : 'No se pudo crear la empresa. Reintenta.');
        this.busy.set(false);
        if (error.status === 401) this.expireSession();
      },
    });
  }

  protected openAdministrators(company: CentralManagedCompany): void {
    this.selectedCompany.set(company);
    this.administratorForm.reset();
    this.administrators.set([]);
    this.administratorsBusy.set(false);
    this.administratorCount.set(0);
    this.error.set(null);
    this.message.set(null);
    this.view.set('administrators');
    this.loadAdministrators(1);
  }

  protected loadAdministrators(page: number): void {
    const company = this.selectedCompany();
    if (!this.session() || !company || this.administratorsBusy()) return;
    this.administratorsBusy.set(true);
    this.administratorsError.set(null);
    const epoch = this.sessionEpoch;
    this.client.administrators(company.id, page).subscribe({
      next: result => {
        if (epoch !== this.sessionEpoch || this.selectedCompany()?.id !== company.id) return;
        this.administrators.set(result.data);
        this.administratorPage.set(result.pagination.page);
        this.administratorPages.set(result.pagination.totalPages);
        this.administratorCount.set(result.pagination.totalCount);
        this.administratorsBusy.set(false);
      },
      error: (error: HttpErrorResponse) => {
        if (epoch !== this.sessionEpoch || this.selectedCompany()?.id !== company.id) return;
        this.administratorsBusy.set(false);
        if (error.status === 401) this.expireSession();
        else this.administratorsError.set('No se pudieron cargar los administradores. Reintenta.');
      },
    });
  }

  protected addAdministrator(): void {
    const company = this.selectedCompany();
    if (!company || this.administratorForm.invalid || this.busy() || this.administratorsBusy()) {
      this.administratorForm.markAllAsTouched();
      return;
    }
    this.busy.set(true);
    this.error.set(null);
    const { username, password } = this.administratorForm.getRawValue();
    this.client.addAdministrator(company.id, { username: username.trim(), password }).subscribe({
      next: created => {
        this.administratorForm.reset();
        this.message.set(`Administrador ${created.username} agregado a ${company.name}.`);
        this.busy.set(false);
        this.loadAdministrators(1);
      },
      error: (error: HttpErrorResponse) => {
        this.administratorForm.controls.password.setValue('');
        this.error.set(error.status === 401 ? 'La sesion vencio. Ingresa nuevamente.'
          : error.status === 409 ? 'Ese usuario ya existe en la empresa.'
            : error.status === 400 ? 'Revisa los datos y la politica de contrasenas.'
              : 'No se pudo agregar el administrador. Reintenta.');
        this.busy.set(false);
        if (error.status === 401) this.expireSession();
      },
    });
  }

  protected showCompanies(): void {
    this.view.set('companies');
    this.selectedCompany.set(null);
    this.administratorsBusy.set(false);
    this.createForm.reset();
    this.administratorForm.reset();
    this.error.set(null);
    this.message.set(null);
    this.loadCompanies(this.companyPage());
  }

  private expireSession(): void {
    this.clearSession();
    this.error.set('La sesion de SuperAdmin central vencio. Ingresa nuevamente.');
  }

  private clearSession(): void {
    this.sessionEpoch++;
    this.session.set(null);
    this.view.set('companies');
    this.companies.set([]);
    this.companiesBusy.set(false);
    this.selectedCompany.set(null);
    this.administrators.set([]);
    this.administratorsBusy.set(false);
    this.createForm.reset();
    this.administratorForm.reset();
    this.message.set(null);
  }
}
