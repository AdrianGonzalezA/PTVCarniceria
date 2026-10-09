import { HttpErrorResponse } from '@angular/common/http';
import { Component, inject, OnInit, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { AdminPaymentGatewayClient, MercadoPagoRegister, MercadoPagoSettings } from '../../core/admin/admin-payment-gateway-client';
import { CurrentSession, SessionClient } from '../../core/session/session-client';
import { AdminAreaTabs } from './admin-area-tabs';

@Component({
  selector: 'app-payment-gateways-page',
  imports: [RouterLink, ReactiveFormsModule, AdminAreaTabs],
  templateUrl: './payment-gateways-page.html',
  styleUrls: ['./admin-page.scss', './categories-page.scss', './arca-settings-page.scss', './payment-gateways-page.scss'],
})
export class PaymentGatewaysPage implements OnInit {
  private readonly sessions = inject(SessionClient);
  private readonly client = inject(AdminPaymentGatewayClient);
  private readonly router = inject(Router);
  private readonly formBuilder = inject(FormBuilder);

  protected readonly session = signal<CurrentSession | null>(null);
  protected readonly settings = signal<MercadoPagoSettings | null>(null);
  protected readonly loading = signal(true);
  protected readonly saving = signal(false);
  protected readonly registerSaving = signal(false);
  protected readonly editing = signal<MercadoPagoRegister | null>(null);
  protected readonly notice = signal<string | null>(null);
  protected readonly error = signal<string | null>(null);
  protected readonly registerError = signal<string | null>(null);
  protected readonly form = this.formBuilder.nonNullable.group({
    sellerUserId: ['', [Validators.required, Validators.pattern(/^\d{1,30}$/)]],
    accessToken: ['', Validators.maxLength(4096)],
    webhookSecret: ['', Validators.maxLength(4096)],
  });
  protected readonly registerForm = this.formBuilder.nonNullable.group({
    qrExternalPosId: ['', Validators.pattern(/^[A-Za-z0-9_-]{0,40}$/)],
    pointTerminalId: ['', Validators.pattern(/^[A-Za-z0-9_-]{0,100}$/)],
  });

  ngOnInit(): void {
    this.sessions.current().subscribe({
      next: (session) => {
        if (!session.context?.permissions.includes('organization.manage')) {
          void this.router.navigateByUrl('/');
          return;
        }
        this.session.set(session);
        this.reload();
      },
      error: () => void this.router.navigateByUrl('/'),
    });
  }

  protected reload(): void {
    this.loading.set(true);
    this.client.get().subscribe({
      next: (settings) => {
        this.settings.set(settings);
        this.form.patchValue({ sellerUserId: settings.sellerUserId });
        this.loading.set(false);
      },
      error: () => {
        this.loading.set(false);
        this.error.set('No se pudo cargar Mercado Pago.');
      },
    });
  }

  protected save(): void {
    if (this.form.invalid || this.saving()) {
      this.form.markAllAsTouched();
      return;
    }
    this.saving.set(true);
    this.error.set(null);
    this.notice.set(null);
    const value = this.form.getRawValue();
    this.client.save(value.sellerUserId, value.accessToken, value.webhookSecret).subscribe({
      next: (saved) => {
        this.settings.update((current) => current && { ...current, ...saved });
        this.form.patchValue({ accessToken: '', webhookSecret: '' });
        this.saving.set(false);
        this.notice.set('Credenciales de prueba guardadas. Los secretos no se vuelven a mostrar.');
      },
      error: (response: HttpErrorResponse) => {
        this.saving.set(false);
        this.error.set(response.error?.error?.code === 'VALIDATION_ERROR'
          ? 'Revisá el User ID y las credenciales.' : 'No se pudo guardar la configuración.');
      },
    });
  }

  protected editRegister(register: MercadoPagoRegister): void {
    this.editing.set(register);
    this.registerForm.setValue({ qrExternalPosId: register.qrExternalPosId ?? '',
      pointTerminalId: register.pointTerminalId ?? '' });
    this.error.set(null);
    this.registerError.set(null);
  }

  protected closeRegister(): void {
    if (!this.registerSaving()) this.editing.set(null);
  }

  protected saveRegister(): void {
    const editing = this.editing();
    if (!editing || this.registerForm.invalid || this.registerSaving()) {
      this.registerForm.markAllAsTouched();
      return;
    }
    this.registerSaving.set(true);
    this.registerError.set(null);
    const value = this.registerForm.getRawValue();
    this.client.saveRegister(editing.id, value.qrExternalPosId.trim(), value.pointTerminalId.trim()).subscribe({
      next: (saved) => {
        this.settings.update((current) => current && { ...current,
          registers: current.registers.map((register) => register.id === editing.id
            ? { ...register, ...saved } : register) });
        this.registerSaving.set(false);
        this.editing.set(null);
        this.notice.set(`Caja ${editing.name} actualizada.`);
      },
      error: (response: HttpErrorResponse) => {
        this.registerSaving.set(false);
        this.registerError.set(response.error?.error?.code === 'REGISTER_IDENTIFIER_CONFLICT'
          ? 'Ese identificador ya está asignado a otra caja.' : 'No se pudo guardar la caja.');
      },
    });
  }
}
