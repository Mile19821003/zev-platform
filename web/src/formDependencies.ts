export function updateDependentForm(
  current: Record<string, string>, key: string, value: string, fields: { key: string; dependsOn?: string }[],
): Record<string, string> {
  const updated = { ...current, [key]: value };
  if (current[key] === value) return updated;
  const changed = [key];
  while (changed.length) {
    const parent = changed.shift();
    for (const field of fields) if (field.dependsOn === parent) {
      updated[field.key] = "";
      changed.push(field.key);
    }
  }
  return updated;
}
