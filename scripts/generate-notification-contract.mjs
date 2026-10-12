import { readFile, writeFile, mkdir } from 'node:fs/promises';
import { fileURLToPath } from 'node:url';
import path from 'node:path';

// Deliberately constrained to this repository's three local payload schemas.
// No evaluation, external references, network, or arbitrary output paths.
const root = fileURLToPath(new URL('../', import.meta.url));
const source = JSON.parse(await readFile(path.join(root, 'contracts/notifications.openapi.json'), 'utf8'));
if (source.openapi !== '3.1.0') throw new Error('Unsupported contract version');
const schemas = source.components.schemas;
const names = Object.keys(schemas);
if (names.length !== 3 || names.some(name => !/^Notification[A-Za-z]+$/.test(name))) throw new Error('Unexpected schema');
const reference = value => {
  const prefix = '#/components/schemas/';
  if (!value.startsWith(prefix) || !names.includes(value.slice(prefix.length))) throw new Error('Only approved local references are supported');
  return value.slice(prefix.length);
};
function nonNullType(schema) {
  if (Array.isArray(schema.type)) {
    if (schema.type.length !== 2 || !schema.type.includes('null')) throw new Error('Unsupported nullable union');
    return schema.type.find(value => value !== 'null');
  }
  return schema.type;
}
function zod(schema) {
  let expression;
  if (schema.$ref) return `${reference(schema.$ref)}Schema`;
  switch (nonNullType(schema)) {
    case 'string':
      expression = schema.format === 'uuid' ? 'z.string().uuid()' : schema.format === 'date-time' ? 'z.iso.datetime({ offset: true })' : 'z.string()';
      for (const [keyword, method] of [['minLength', 'min'], ['maxLength', 'max']]) {
        if (schema[keyword] !== undefined) expression += `.${method}(${integer(schema[keyword])})`;
      }
      break;
    case 'integer':
      if (schema.enum) {
        if (schema.enum.length < 2) throw new Error('An enum needs multiple explicit values');
        expression = `z.union([${schema.enum.map(value => `z.literal(${integer(value)})`).join(', ')}])`;
      } else {
        expression = 'z.number().int()';
        for (const [keyword, method] of [['minimum', 'min'], ['maximum', 'max']]) {
          if (schema[keyword] !== undefined) expression += `.${method}(${integer(schema[keyword])})`;
        }
      }
      break;
    case 'array':
      expression = `z.array(${zod(schema.items)})`;
      if (schema.maxItems !== undefined) expression += `.max(${integer(schema.maxItems)})`;
      break;
    default: throw new Error('Unsupported payload type');
  }
  if (Array.isArray(schema.type)) expression += '.nullable()';
  return expression;
}
function integer(value) {
  if (!Number.isSafeInteger(value) || value < 0) throw new Error('Invalid numeric constraint');
  return value;
}
function csharp(schema) {
  if (schema.$ref) return `${reference(schema.$ref)}Dto`;
  const types = { string: schema.format === 'uuid' ? 'Guid' : schema.format === 'date-time' ? 'DateTimeOffset' : 'string', integer: 'int' };
  const type = nonNullType(schema);
  const value = type === 'array' ? `IReadOnlyList<${csharp(schema.items)}>` : types[type];
  if (!value) throw new Error('Unsupported C# payload type');
  return value + (Array.isArray(schema.type) ? '?' : '');
}
const ts = ["// Generated from contracts/notifications.openapi.json. Run the repository generator.", "import { z } from 'zod';", ''];
const cs = ['// Generated from contracts/notifications.openapi.json. Run the repository generator.', '#nullable enable', 'namespace BLL.Models.Notifications;', ''];
for (const name of names) {
  const schema = schemas[name];
  if (schema.type !== 'object' || schema.additionalProperties !== false) throw new Error('A closed object schema is required');
  const fields = Object.entries(schema.properties);
  if (schema.required.some(key => !Object.hasOwn(schema.properties, key))) throw new Error('Unknown required field');
  ts.push(`export const ${name}Schema = z.strictObject({`);
  const parameters = [];
  let optionalSeen = false;
  for (const [key, field] of fields) {
    if (!/^[a-z][A-Za-z0-9]*$/.test(key)) throw new Error('Unsupported property identifier');
    const required = schema.required.includes(key);
    if (!required && !Array.isArray(field.type)) throw new Error('Only nullable fields may be omitted');
    if (optionalSeen && required) throw new Error('Optional fields must follow required fields');
    optionalSeen ||= !required;
    ts.push(`  ${key}: ${zod(field)}${required ? '' : '.optional().default(null)'},`);
    parameters.push(`${csharp(field)} ${key[0].toUpperCase() + key.slice(1)}${required ? '' : ' = null'}`);
  }
  ts.push('});', `export type ${name} = z.infer<typeof ${name}Schema>;`, '');
  cs.push(`public sealed record ${name}Dto(`, `    ${parameters.join(',\n    ')});`, '');
}
const outputs = [
  ['frontend/src/lib/generated/notifications.ts', ts.join('\n')],
  ['BLL/Models/Notifications/NotificationDtos.cs', cs.join('\n')],
];
for (const [relative, contents] of outputs) {
  const target = path.resolve(root, relative);
  if (!target.startsWith(path.resolve(root) + path.sep)) throw new Error('Output escaped the repository');
  if (process.argv.includes('--check')) {
    const existing = await readFile(target, 'utf8').catch(() => '');
    if (existing.replaceAll('\r\n', '\n') !== contents) throw new Error(`Regenerate ${relative}`);
  } else {
    await mkdir(path.dirname(target), { recursive: true });
    await writeFile(target, contents, 'utf8');
  }
}
console.log(process.argv.includes('--check') ? 'Notification contract outputs are current.' : 'Generated C# and Zod notification payloads.');
