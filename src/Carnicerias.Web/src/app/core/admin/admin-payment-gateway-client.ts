import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';

export interface MercadoPagoRegister {
  readonly id: string;
  readonly name: string;
  readonly branchId: string;
  readonly branchName: string;
  readonly isActive: boolean;
  readonly qrExternalPosId: string | null;
  readonly pointTerminalId: string | null;
}

export interface MercadoPagoSettings {
  readonly provider: 'mercadoPago';
  readonly environment: 'test';
  readonly sellerUserId: string;
  readonly hasAccessToken: boolean;
  readonly hasWebhookSecret: boolean;
  readonly registers: readonly MercadoPagoRegister[];
}

@Injectable({ providedIn: 'root' })
export class AdminPaymentGatewayClient {
  private readonly http = inject(HttpClient);
  private readonly endpoint = '/api/admin/payment-gateways/mercado-pago';

  get() {
    return this.http.get<MercadoPagoSettings>(this.endpoint, { withCredentials: true });
  }

  save(sellerUserId: string, accessToken: string, webhookSecret: string) {
    return this.http.put<Pick<MercadoPagoSettings, 'sellerUserId' | 'hasAccessToken' | 'hasWebhookSecret'>>(
      this.endpoint, { sellerUserId, accessToken: accessToken || null,
        webhookSecret: webhookSecret || null }, { withCredentials: true });
  }

  saveRegister(terminalId: string, qrExternalPosId: string, pointTerminalId: string) {
    return this.http.put<Pick<MercadoPagoRegister, 'qrExternalPosId' | 'pointTerminalId'>>(
      `${this.endpoint}/registers/${terminalId}`,
      { qrExternalPosId: qrExternalPosId || null, pointTerminalId: pointTerminalId || null },
      { withCredentials: true });
  }
}
