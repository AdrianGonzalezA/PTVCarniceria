export interface EmulatorState {
  readonly weightKg: number;
  readonly stable: boolean;
}

export interface KretzQrPreview {
  readonly payload: string;
  readonly imageUrl: string;
  readonly companyName: string;
  readonly catalogCount: number;
  readonly items: readonly { plu: string; weightKg: number; productName: string }[];
}

export const enum EmulatorChannel {
  State = 'carnicerias-emulator:state',
  SetWeight = 'carnicerias-emulator:set-weight',
  RandomKretzQr = 'carnicerias-emulator:random-kretz-qr',
  SimulateWeight = 'carnicerias-emulator:simulate-weight',
  CopyQr = 'carnicerias-emulator:copy-qr',
}
