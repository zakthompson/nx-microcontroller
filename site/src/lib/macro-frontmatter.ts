import { parse as parseYaml } from 'yaml';

export interface MacroNumberOption {
  id: string;
  type: 'number';
  label: string;
  default: number;
  min: number;
  max: number;
}

export interface MacroDropdownOption {
  id: string;
  type: 'dropdown';
  label: string;
  options: Array<{ value: string; label: string }>;
}

export type MacroConfigOption = MacroNumberOption | MacroDropdownOption;

export interface ParsedMacro {
  config: MacroConfigOption[];
  body: string;
}

function validateNumberOption(
  raw: Record<string, unknown>,
  index: number
): MacroNumberOption {
  const { id, label, default: defaultVal, min, max } = raw;

  if (typeof id !== 'string' || !id) {
    throw new Error(
      `Config option ${index}: 'id' is required and must be a string`
    );
  }
  if (typeof label !== 'string' || !label) {
    throw new Error(
      `Config option '${id}': 'label' is required and must be a string`
    );
  }
  if (typeof defaultVal !== 'number') {
    throw new Error(
      `Config option '${id}': 'default' is required and must be a number`
    );
  }
  if (typeof min !== 'number') {
    throw new Error(
      `Config option '${id}': 'min' is required and must be a number`
    );
  }
  if (typeof max !== 'number') {
    throw new Error(
      `Config option '${id}': 'max' is required and must be a number`
    );
  }

  return { id, type: 'number', label, default: defaultVal, min, max };
}

function validateDropdownOption(
  raw: Record<string, unknown>,
  index: number
): MacroDropdownOption {
  const { id, label, options } = raw;

  if (typeof id !== 'string' || !id) {
    throw new Error(
      `Config option ${index}: 'id' is required and must be a string`
    );
  }
  if (typeof label !== 'string' || !label) {
    throw new Error(
      `Config option '${id}': 'label' is required and must be a string`
    );
  }
  if (!Array.isArray(options) || options.length === 0) {
    throw new Error(
      `Config option '${id}': 'options' must be a non-empty array`
    );
  }

  const validated = options.map((opt: unknown, i: number) => {
    if (typeof opt !== 'object' || opt === null) {
      throw new Error(`Config option '${id}', option ${i}: must be an object`);
    }
    const { value, label: optLabel } = opt as Record<string, unknown>;
    if (typeof value !== 'string' || !value) {
      throw new Error(
        `Config option '${id}', option ${i}: 'value' is required`
      );
    }
    if (typeof optLabel !== 'string' || !optLabel) {
      throw new Error(
        `Config option '${id}', option ${i}: 'label' is required`
      );
    }
    return { value, label: optLabel };
  });

  return { id, type: 'dropdown', label, options: validated };
}

export function parseMacroFrontmatter(content: string): ParsedMacro {
  const trimmed = content.trimStart();

  if (!trimmed.startsWith('---')) {
    return { config: [], body: content };
  }

  // Find the closing --- delimiter (must be on its own line)
  const afterOpener = trimmed.indexOf('\n');
  if (afterOpener === -1) {
    return { config: [], body: content };
  }

  const rest = trimmed.slice(afterOpener + 1);
  const closingIndex = rest.search(/^---\s*$/m);
  if (closingIndex === -1) {
    return { config: [], body: content };
  }

  const yamlContent = rest.slice(0, closingIndex);
  const body = rest.slice(
    closingIndex + rest.slice(closingIndex).indexOf('\n') + 1
  );

  const parsed = parseYaml(yamlContent);
  if (!parsed || typeof parsed !== 'object' || !Array.isArray(parsed.config)) {
    return { config: [], body };
  }

  const config: MacroConfigOption[] = parsed.config.map(
    (raw: Record<string, unknown>, index: number) => {
      if (typeof raw !== 'object' || raw === null) {
        throw new Error(`Config option ${index}: must be an object`);
      }

      const { type } = raw;
      if (type === 'number') {
        return validateNumberOption(raw, index);
      } else if (type === 'dropdown') {
        return validateDropdownOption(raw, index);
      } else {
        throw new Error(
          `Config option ${index}: unknown type '${String(type)}'`
        );
      }
    }
  );

  return { config, body };
}

export function applyMacroConfig(
  body: string,
  config: MacroConfigOption[],
  values: Record<string, string | number>
): string {
  // Build a lookup of config options by id
  const configById = new Map(config.map((opt) => [opt.id, opt]));

  // Resolve defaults for missing values
  const resolved: Record<string, string | number> = {};
  for (const opt of config) {
    if (values[opt.id] !== undefined) {
      resolved[opt.id] = values[opt.id];
    } else if (opt.type === 'number') {
      resolved[opt.id] = opt.default;
    } else if (opt.type === 'dropdown') {
      resolved[opt.id] = opt.options[0].value;
    }
  }

  // Validate values
  for (const opt of config) {
    const val = resolved[opt.id];

    if (opt.type === 'number') {
      const num = typeof val === 'string' ? Number(val) : val;
      if (!Number.isFinite(num)) {
        throw new Error(`Config '${opt.id}': value must be a number`);
      }
      if (num < opt.min || num > opt.max) {
        throw new Error(
          `Config '${opt.id}': value ${num} is out of range [${opt.min}, ${opt.max}]`
        );
      }
      resolved[opt.id] = num;
    } else if (opt.type === 'dropdown') {
      const str = String(val);
      if (!opt.options.some((o) => o.value === str)) {
        throw new Error(
          `Config '${opt.id}': value '${str}' is not a valid option`
        );
      }
      resolved[opt.id] = str;
    }
  }

  // Perform substitution
  return body.replace(/\$\{([a-zA-Z0-9_]+)\}/g, (match, id: string) => {
    if (!configById.has(id)) {
      throw new Error(`Unresolved config reference: \${${id}}`);
    }
    return String(resolved[id]);
  });
}
