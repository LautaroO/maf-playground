declare module 'monaco-editor/language/json/monaco.contribution.js' {
  export const jsonDefaults: {
    setDiagnosticsOptions(options: {
      validate: boolean;
      enableSchemaRequest: boolean;
      schemas: { uri: string; fileMatch: string[]; schema: unknown }[];
    }): void;
  };
}
