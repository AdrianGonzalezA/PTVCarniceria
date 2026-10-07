export interface NativeDiagnostic {
  readonly electronVersion: string;
  readonly platform: NodeJS.Platform;
  readonly status: 'ready';
}

export interface CarniceriasNativeApi {
  readonly getDiagnostic: () => Promise<NativeDiagnostic>;
}

export const diagnosticChannel = 'carnicerias:diagnostic';
