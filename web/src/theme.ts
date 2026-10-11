// Platform colour palette → CSS custom properties on <html>. The stylesheets use var(--zev-*) with :root defaults equal
// to the factory palette; shades (hover, muted text, input borders, tints) are var(--zev-x, <old value>) and are only
// set — derived from the palette — when the palette differs from the factory one, so the default look stays exact.
export type ColorKey = "primaryColor" | "secondaryColor" | "accentColor" | "backgroundColor" | "surfaceColor" | "textColor"
  | "headerBgColor" | "headerTextColor" | "menuBgColor" | "menuTextColor" | "menuActiveColor" | "borderColor";
export type ThemeColors = Record<ColorKey, string>;
export type ThemePreset = { name: string; label: string; colors: ThemeColors };
export type Theme = { activeThemeName: string; colors: ThemeColors; updatedAt: string | null; presets: ThemePreset[] };
type StyleTarget = { setProperty(name: string, value: string): void; removeProperty(name: string): unknown };

export const colorFields: { key: ColorKey; cssVar: string; label: string; hint: string }[] = [
  { key: "primaryColor", cssVar: "--zev-primary", label: "Primarna", hint: "Dugmad, linkovi, aktivni elementi" },
  { key: "secondaryColor", cssVar: "--zev-secondary", label: "Sekundarna", hint: "Sporedni tekst i oznake" },
  { key: "accentColor", cssVar: "--zev-accent", label: "Naglasak", hint: "Istaknute kartice i pozadine" },
  { key: "backgroundColor", cssVar: "--zev-bg", label: "Pozadina", hint: "Pozadina stranice" },
  { key: "surfaceColor", cssVar: "--zev-surface", label: "Površina", hint: "Paneli, tabele, polja" },
  { key: "textColor", cssVar: "--zev-text", label: "Tekst", hint: "Osnovni tekst" },
  { key: "headerBgColor", cssVar: "--zev-header-bg", label: "Zaglavlje — pozadina", hint: "Gornja traka" },
  { key: "headerTextColor", cssVar: "--zev-header-text", label: "Zaglavlje — tekst", hint: "Tekst gornje trake" },
  { key: "menuBgColor", cssVar: "--zev-menu-bg", label: "Meni — pozadina", hint: "Bočni meni" },
  { key: "menuTextColor", cssVar: "--zev-menu-text", label: "Meni — tekst", hint: "Stavke menija" },
  { key: "menuActiveColor", cssVar: "--zev-menu-active", label: "Meni — aktivna stavka", hint: "Odabrana stavka menija" },
  { key: "borderColor", cssVar: "--zev-border", label: "Rubovi", hint: "Okviri panela i linije" },
];

// Equal to SystemThemeSettings defaults on the backend and to the :root block in styles.css (checked by a test).
export const factoryColors: ThemeColors = {
  primaryColor: "#176d50", secondaryColor: "#4f6070", accentColor: "#e9f5ef", backgroundColor: "#f5f7f8",
  surfaceColor: "#ffffff", textColor: "#17233a", headerBgColor: "#ffffff", headerTextColor: "#17233a",
  menuBgColor: "#102e31", menuTextColor: "#e3eeeb", menuActiveColor: "#23524d", borderColor: "#e4e9ec",
};

export const customThemeName = "Custom";

export function isHexColor(value: unknown): value is string {
  return typeof value === "string" && /^#[0-9a-f]{6}$/i.test(value);
}

function normalizeColors(raw: unknown, fallback: ThemeColors): ThemeColors {
  const value = (typeof raw === "object" && raw !== null ? raw : {}) as Record<string, unknown>;
  return Object.fromEntries(colorFields.map(({ key }) =>
    [key, isHexColor(value[key]) ? (value[key] as string).toLowerCase() : fallback[key]])) as ThemeColors;
}

export function normalizeTheme(raw: unknown): Theme {
  const value = (typeof raw === "object" && raw !== null ? raw : {}) as Record<string, unknown>;
  const presets = Array.isArray(value.presets) ? value.presets.flatMap((item) => {
    const preset = item as Record<string, unknown>;
    return typeof preset?.name === "string" && typeof preset.label === "string"
      ? [{ name: preset.name, label: preset.label, colors: normalizeColors(preset.colors, factoryColors) }] : [];
  }) : [];
  return {
    activeThemeName: typeof value.activeThemeName === "string" ? value.activeThemeName : "Default",
    colors: normalizeColors(value, factoryColors),
    updatedAt: typeof value.updatedAt === "string" ? value.updatedAt : null,
    presets,
  };
}

export function sameColors(left: ThemeColors, right: ThemeColors): boolean {
  return colorFields.every(({ key }) => left[key].toLowerCase() === right[key].toLowerCase());
}

// The preset whose colours are exactly these, otherwise "Custom".
export function matchingPreset(colors: ThemeColors, presets: ThemePreset[]): string {
  return presets.find((preset) => sameColors(preset.colors, colors))?.name ?? customThemeName;
}

function channels(hex: string): [number, number, number] {
  return [1, 3, 5].map((start) => parseInt(hex.slice(start, start + 2), 16)) as [number, number, number];
}

// Linear mix: weight 0 → a, weight 1 → b.
export function mixColors(a: string, b: string, weight: number): string {
  const [x, y] = [channels(a), channels(b)];
  return `#${x.map((value, index) => Math.round(value + (y[index] - value) * weight).toString(16).padStart(2, "0")).join("")}`;
}

// WCAG contrast ratio (1–21).
export function contrastRatio(a: string, b: string): number {
  const luminance = (hex: string) => {
    const [r, g, b2] = channels(hex).map((value) => {
      const c = value / 255;
      return c <= 0.03928 ? c / 12.92 : ((c + 0.055) / 1.055) ** 2.4;
    });
    return 0.2126 * r + 0.7152 * g + 0.0722 * b2;
  };
  const [light, dark] = [luminance(a), luminance(b)].sort((x, y) => y - x);
  return (light + 0.05) / (dark + 0.05);
}

// Pairs that must stay readable (WCAG AA for normal text: 4.5).
export function contrastWarnings(colors: ThemeColors): string[] {
  const pairs: [string, string, string][] = [
    ["Tekst na pozadini", colors.textColor, colors.backgroundColor],
    ["Tekst na površini", colors.textColor, colors.surfaceColor],
    ["Tekst zaglavlja", colors.headerTextColor, colors.headerBgColor],
    ["Tekst menija", colors.menuTextColor, colors.menuBgColor],
    ["Tekst aktivne stavke menija", colors.menuTextColor, colors.menuActiveColor],
    ["Bijeli tekst na primarnoj boji", "#ffffff", colors.primaryColor],
  ];
  return pairs.filter(([, a, b]) => contrastRatio(a, b) < 4.5)
    .map(([label, a, b]) => `${label}: kontrast ${contrastRatio(a, b).toFixed(1)}:1 (preporučeno najmanje 4.5:1)`);
}

const shadeVariables = ["--zev-primary-strong", "--zev-muted", "--zev-input-border", "--zev-surface-alt", "--zev-accent-soft",
  "--zev-secondary-bg", "--zev-menu-hover", "--zev-menu-muted", "--zev-menu-border"];

// CSS variables for a palette; shades are null (= keep the stylesheet's own values) for the factory palette.
export function themeVariables(colors: ThemeColors): Record<string, string | null> {
  const variables: Record<string, string | null> = Object.fromEntries(colorFields.map(({ key, cssVar }) => [cssVar, colors[key]]));
  if (sameColors(colors, factoryColors)) {
    for (const name of shadeVariables) variables[name] = null;
    return variables;
  }
  const c = colors;
  return {
    ...variables,
    "--zev-primary-strong": mixColors(c.primaryColor, "#000000", 0.2),
    "--zev-muted": mixColors(c.textColor, c.backgroundColor, 0.38),
    "--zev-input-border": mixColors(c.borderColor, c.textColor, 0.15),
    "--zev-surface-alt": mixColors(c.surfaceColor, c.textColor, 0.05),
    "--zev-accent-soft": mixColors(c.accentColor, c.surfaceColor, 0.45),
    "--zev-secondary-bg": mixColors(c.secondaryColor, c.surfaceColor, 0.85),
    "--zev-menu-hover": mixColors(c.menuBgColor, c.menuActiveColor, 0.5),
    "--zev-menu-muted": mixColors(c.menuTextColor, c.menuBgColor, 0.3),
    "--zev-menu-border": mixColors(c.menuBgColor, c.menuTextColor, 0.22),
  };
}

export function applyTheme(style: StyleTarget, colors: ThemeColors): void {
  for (const [name, value] of Object.entries(themeVariables(colors))) {
    if (value === null) style.removeProperty(name);
    else style.setProperty(name, value);
  }
}

// Inline style for a preview box (React style object with custom properties).
export function previewStyle(colors: ThemeColors): Record<string, string> {
  return Object.fromEntries(Object.entries(themeVariables(colors)).filter((entry): entry is [string, string] => entry[1] !== null));
}
