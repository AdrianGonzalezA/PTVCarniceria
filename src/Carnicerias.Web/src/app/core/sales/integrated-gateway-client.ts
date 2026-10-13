import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { MercadoPagoMode } from './mercado-pago-client';

export interface AvailableIntegratedGateway {
  readonly provider: 'mercadoPago';
  readonly modes: readonly MercadoPagoMode[];
}

export interface IntegratedGatewayAvailability {
  readonly gateways: readonly AvailableIntegratedGateway[];
}

@Injectable({ providedIn: 'root' })
export class IntegratedGatewayClient {
  private readonly http = inject(HttpClient);

  available() {
    return this.http.get<IntegratedGatewayAvailability>('/api/pos/payment-gateways',
      { withCredentials: true });
  }
}
