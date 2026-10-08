export interface EmulatorState {
  readonly weightKg: number;
  readonly stable: boolean;
  readonly printed: readonly { readonly saleId: string; readonly total: number; readonly path: string }[];
}

export const enum EmulatorChannel {
  State = 'carnicerias-emulator:state',
  SetWeight = 'carnicerias-emulator:set-weight',
}
