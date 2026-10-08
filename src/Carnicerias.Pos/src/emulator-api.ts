export interface EmulatorState {
  readonly weightKg: number;
  readonly stable: boolean;
}

export const enum EmulatorChannel {
  State = 'carnicerias-emulator:state',
  SetWeight = 'carnicerias-emulator:set-weight',
}
