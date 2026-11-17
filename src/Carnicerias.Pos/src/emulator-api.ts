export interface EmulatorState {
  readonly weightKg: number;
  readonly stable: boolean;
}

export interface KretzQrPreview {
  readonly payload: string;
  readonly imageUrl: string;
}

export const enum EmulatorChannel {
  State = 'carnicerias-emulator:state',
  SetWeight = 'carnicerias-emulator:set-weight',
  KretzQr = 'carnicerias-emulator:kretz-qr',
  CopyQr = 'carnicerias-emulator:copy-qr',
}
