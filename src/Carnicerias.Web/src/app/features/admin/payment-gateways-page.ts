import { HttpErrorResponse } from '@angular/common/http';
import { Component, inject, OnInit, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { AdminPaymentGatewayClient, MercadoPagoRegister, MercadoPagoSettings } from '../../core/admin/admin-payment-gateway-client';
import { AdminCloverClient, CloverAuthorization, CloverSettings } from '../../core/admin/admin-clover-client';
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
  private readonly cloverClient = inject(AdminCloverClient);
  private readonly router = inject(Router);
  private readonly formBuilder = inject(FormBuilder);

  protected readonly session = signal<CurrentSession | null>(null);
  protected readonly settings = signal<MercadoPagoSettings | null>(null);
  protected readonly cloverSettings = signal<CloverSettings | null>(null);
  protected readonly cloverAuthorization = signal<CloverAuthorization | null>(null);
  protected readonly cloverLoading = signal(true);
  protected readonly cloverSaving = signal(false);
  protected readonly cloverAuthorizing = signal(false);
  protected readonly cloverProbing = signal(false);
  protected readonly cloverProbeResult = signal<string | null>(null);
  protected readonly cloverNotice = signal<string | null>(null);
  protected readonly cloverError = signal<string | null>(null);
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
  protected readonly cloverForm = this.formBuilder.nonNullable.group({
    clientId: ['', [Validators.required, Validators.pattern(/^[A-Za-z0-9_-]{1,100}$/)]],
    merchantId: ['', [Validators.required, Validators.pattern(/^[A-Za-z0-9_-]{1,100}$/)]],
    clientSecret: ['', Validators.maxLength(4096)],
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
        this.reloadClover();
      },
      error: () => void this.router.navigateByUrl('/'),
    });
  }

  protected reloadClover(): void {
    this.cloverLoading.set(true);
    this.cloverProbeResult.set(null);
    this.cloverClient.get().subscribe({
      next: (settings) => {
        this.cloverSettings.set(settings);
        this.cloverForm.patchValue({ clientId: settings.clientId, merchantId: settings.merchantId });
        this.cloverForm.markAsPristine();
        if (settings.isConnected) this.cloverAuthorization.set(null);
        this.cloverError.set(null);
        this.cloverLoading.set(false);
      },
      error: () => {
        this.cloverLoading.set(false);
        this.cloverError.set('No se pudo cargar Clover.');
      },
    });
  }

  protected saveClover(): void {
    if (this.cloverForm.invalid || this.cloverSaving()) {
      this.cloverForm.markAllAsTouched();
      return;
    }
    this.cloverSaving.set(true);
    this.cloverError.set(null);
    this.cloverNotice.set(null);
    const value = this.cloverForm.getRawValue();
    this.cloverClient.save(value.clientId, value.merchantId, value.clientSecret).subscribe({
      next: (saved) => {
        this.cloverSettings.update((current) => current && { ...current, ...saved });
        this.cloverForm.patchValue({ clientSecret: '' });
        this.cloverForm.markAsPristine();
        this.cloverAuthorization.set(null);
        this.cloverSaving.set(false);
        this.cloverNotice.set('Configuración sandbox guardada. El secreto no se vuelve a mostrar.');
      },
      error: (response: HttpErrorResponse) => {
        this.cloverSaving.set(false);
        this.cloverError.set(response.error?.error?.code === 'MERCHANT_CONFLICT'
          ? 'Ese comercio ya está asignado a otra empresa.'
          : 'No se pudo guardar Clover. Revisá App ID, Merchant ID y App Secret.');
      },
    });
  }

  protected authorizeClover(): void {
    if (this.cloverAuthorizing()) return;
    this.cloverAuthorizing.set(true);
    this.cloverError.set(null);
    this.cloverNotice.set(null);
    this.cloverAuthorization.set(null);
    this.cloverClient.authorize().subscribe({
      next: (authorization) => {
        this.cloverAuthorization.set(authorization);
        this.cloverAuthorizing.set(false);
        this.cloverNotice.set('Abrí el enlace en tu navegador e iniciá sesión con el comercio de prueba.');
      },
      error: () => {
        this.cloverAuthorizing.set(false);
        this.cloverError.set('No se pudo iniciar OAuth. Guardá primero el App ID, Merchant ID y App Secret.');
      },
    });
  }

  protected probeClover(): void {
    if (this.cloverProbing() || !this.cloverSettings()?.isConnected || this.cloverForm.dirty) return;
    this.cloverProbing.set(true);
    this.cloverProbeResult.set(null);
    this.cloverError.set(null);
    this.cloverClient.checkConnection().subscribe({
      next: () => {
        this.cloverProbing.set(false);
        this.cloverProbeResult.set('Comercio verificado contra Clover sandbox. Esto no prueba un cobro.');
      },
      error: (response: HttpErrorResponse) => {
        this.cloverProbing.set(false);
        const code = response.error?.error?.code;
        this.cloverError.set(code === 'CLOVER_TOKEN_EXPIRED' || code === 'CLOVER_TOKEN_REJECTED'
          ? 'El token ya no permite consultar Clover. Volvé a conectar el comercio.'
          : code === 'CLOVER_MERCHANT_MISMATCH'
            ? 'Clover devolvió otro comercio. Revisá el Merchant ID antes de cobrar.'
            : 'No se pudo verificar el comercio en Clover sandbox. Reintentá más tarde.');
      },
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
