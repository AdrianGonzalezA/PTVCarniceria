import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';

export interface HealthResponse {
  readonly status: 'healthy';
}

@Injectable({ providedIn: 'root' })
export class HealthClient {
  private readonly http = inject(HttpClient);

  check() {
    return this.http.get<HealthResponse>('/api/health');
  }
}
