import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';

export interface PosTerminal {
  readonly id: string;
  readonly name: string;
  readonly companyId: string;
  readonly branchId: string;
}

@Injectable({ providedIn: 'root' })
export class PosTerminalClient {
  private readonly http = inject(HttpClient);

  current() {
    return this.http.get<PosTerminal>('/api/pos-terminals/current', { withCredentials: true });
  }
}
