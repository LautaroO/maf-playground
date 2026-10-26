import { getLanguageService, type JSONSchema } from 'vscode-json-languageservice';
import { TextDocument } from 'vscode-languageserver-textdocument';
export interface InputDiagnostic {
  message: string;
  line: number;
  column: number;
}
export const INPUT_LIMIT = 2 * 1024 * 1024;
export async function validateJson(text: string, schema: unknown): Promise<InputDiagnostic[]> {
  if (text.length > INPUT_LIMIT || JSON.stringify(schema).length > INPUT_LIMIT)
    return [
      { message: 'Input or schema exceeds the local 2 MiB editor limit.', line: 1, column: 1 },
    ];
  // The catalog supplies schemas; external references must never trigger network access.
  const service = getLanguageService({
    schemaRequestService: async () => {
      throw new Error('External schema references are unavailable.');
    },
  });
  const document = TextDocument.create('studio://input.json', 'json', 1, text);
  const diagnostics = await service.doValidation(
    document,
    service.parseJSONDocument(document),
    { schemaValidation: 'error', schemaRequest: 'error' },
    schema as JSONSchema,
  );
  return diagnostics.map((item) => ({
    message: String(item.message),
    line: item.range.start.line + 1,
    column: item.range.start.character + 1,
  }));
}
