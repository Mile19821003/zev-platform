// Palette of business icons (24×24 stroke paths in the Lucide style) for modules and categories.
// The keys must match ModuleIcons.IconKeys in src/Zev.Api/Domain/ModuleIconSetting.cs (checked by a web test).
export type PaletteIcon = { key: string; label: string; tags: string[]; path: string };

export const iconPalette: PaletteIcon[] = [
  { key: "home", label: "Početna", tags: ["kuća", "pregled", "meni"], path: "M3 11 12 4l9 7M5 10v10h5v-6h4v6h5V10" },
  { key: "cog", label: "Zupčanik", tags: ["postavke", "podešavanja", "sistem"], path: "M12 15a3 3 0 1 0 0-6 3 3 0 0 0 0 6zM19.4 15a1.7 1.7 0 0 0 .3 1.8l.1.1a2 2 0 1 1-2.8 2.8l-.1-.1a1.7 1.7 0 0 0-1.8-.3 1.7 1.7 0 0 0-1 1.5V21a2 2 0 1 1-4 0v-.1a1.7 1.7 0 0 0-1.1-1.5 1.7 1.7 0 0 0-1.8.3l-.1.1a2 2 0 1 1-2.8-2.8l.1-.1a1.7 1.7 0 0 0 .3-1.8 1.7 1.7 0 0 0-1.5-1H3a2 2 0 1 1 0-4h.1a1.7 1.7 0 0 0 1.5-1.1 1.7 1.7 0 0 0-.3-1.8l-.1-.1a2 2 0 1 1 2.8-2.8l.1.1a1.7 1.7 0 0 0 1.8.3H9a1.7 1.7 0 0 0 1-1.5V3a2 2 0 1 1 4 0v.1a1.7 1.7 0 0 0 1 1.5 1.7 1.7 0 0 0 1.8-.3l.1-.1a2 2 0 1 1 2.8 2.8l-.1.1a1.7 1.7 0 0 0-.3 1.8V9a1.7 1.7 0 0 0 1.5 1H21a2 2 0 1 1 0 4h-.1a1.7 1.7 0 0 0-1.5 1z" },
  { key: "sliders", label: "Klizači", tags: ["podešavanja", "regionalno", "prikaz"], path: "M4 6h10M18 6h2M4 12h4M12 12h8M4 18h12M20 18h0M14 4v4M8 10v4M16 16v4" },
  { key: "barcode", label: "Crtični kod", tags: ["šifarnik", "šifarnici", "šifre", "artikli"], path: "M3 5v14M6 5v14M9 5v10M12 5v14M15 5v10M18 5v14M21 5v14" },
  { key: "list", label: "Lista", tags: ["popis", "stavke", "šifarnik"], path: "M9 6h11M9 12h11M9 18h11M4 6h.01M4 12h.01M4 18h.01" },
  { key: "clipboard-check", label: "Kontrolna lista", tags: ["clipboard", "provjera", "zadaci"], path: "M9 4h6v3H9zM8 5H6v16h12V5h-2M9 14l2 2 4-4" },
  { key: "monitor", label: "Monitor", tags: ["oprema", "ekran", "računar"], path: "M3 4h18v12H3zM8 20h8M12 16v4" },
  { key: "package", label: "Paket", tags: ["roba", "zalihe", "isporuka"], path: "M12 3l8 4.5v9L12 21l-8-4.5v-9zM12 12l8-4.5M12 12 4 7.5M12 12v9M8 5.2l8 4.6" },
  { key: "user", label: "Osoba", tags: ["vlasnik", "korisnik", "radnik"], path: "M12 11a4 4 0 1 0 0-8 4 4 0 0 0 0 8zM4 21c0-4 3.6-7 8-7s8 3 8 7" },
  { key: "users", label: "Osobe", tags: ["korisnici", "zajednica", "tim"], path: "M9 11a3.5 3.5 0 1 0 0-7 3.5 3.5 0 0 0 0 7zM2 20c0-3.5 3-6 7-6s7 2.5 7 6M17 4.5a3.5 3.5 0 0 1 0 6.5M22 20c0-2.8-1.8-4.8-4.5-5.6" },
  { key: "hard-hat", label: "Radnik", tags: ["kaciga", "izvođač", "održavanje"], path: "M2 18h20M4 18v-3a8 8 0 0 1 16 0v3M10 7V5h4v2M9 10V7M15 10V7" },
  { key: "activity", label: "Puls", tags: ["monitoring", "aktivnost", "praćenje"], path: "M3 12h4l3-8 4 16 3-8h4" },
  { key: "shield-check", label: "Štit", tags: ["kontrola", "sigurnost", "administracija", "prava"], path: "M12 3l8 3v6c0 4.5-3.4 8-8 9-4.6-1-8-4.5-8-9V6zM9 12l2 2 4-4" },
  { key: "bot", label: "Robot", tags: ["automatizacija", "pravila"], path: "M12 4V2M6 8h12v11H6zM9 13h.01M15 13h.01M2 13h2M20 13h2M10 17h4" },
  { key: "file-lock", label: "Zaključan dokument", tags: ["dokumenti", "brava", "privatno"], path: "M14 3H6v18h12V7zM14 3v4h4M10 14v-1.5a2 2 0 0 1 4 0V14M9 14h6v4H9z" },
  { key: "file-text", label: "Dokument", tags: ["račun", "faktura", "obračun"], path: "M6 3h9l3 3v15H6zM9 9h6M9 13h6M9 17h4" },
  { key: "building", label: "Zgrada", tags: ["objekat", "ulaz", "registar"], path: "M5 21V4h10v17M15 9h4v12M8 8h2M8 12h2M8 16h2M3 21h18" },
  { key: "building-2", label: "Stambeni blok", tags: ["zev", "nekretnine", "registar"], path: "M4 21V6l8-3 8 3v15M9 21v-4h6v4M8 8h2M14 8h2M8 12h2M14 12h2" },
  { key: "door", label: "Vrata", tags: ["stan", "posebni dio", "ulaz"], path: "M6 21V4h12v17M3 21h18M14 12h.01" },
  { key: "key", label: "Ključ", tags: ["pristup", "uloge", "stan"], path: "M8 15a4 4 0 1 1 3.5-6H21v3h-2v3h-3v-3h-4.5A4 4 0 0 1 8 15z" },
  { key: "banknote", label: "Novčanica", tags: ["novac", "finansije", "gotovina"], path: "M3 7h18v10H3zM7 7v10M17 7v10M12 9.5a2.5 2.5 0 1 0 0 5 2.5 2.5 0 0 0 0-5" },
  { key: "wallet", label: "Novčanik", tags: ["portal", "saldo", "vlasnik"], path: "M4 7h15a1 1 0 0 1 1 1v11H4zM4 7l11-3v3M16 13h2" },
  { key: "credit-card", label: "Kartica", tags: ["plaćanje", "novac", "uplata"], path: "M3 6h18v12H3zM3 10h18M7 15h3" },
  { key: "landmark", label: "Banka", tags: ["izvodi", "uplate", "račun"], path: "M3 10h18L12 4zM5 10v8M9.5 10v8M14.5 10v8M19 10v8M3 20h18" },
  { key: "receipt", label: "Uplatnica", tags: ["račun", "rashodi", "priznanica"], path: "M6 3h12v18l-3-2-3 2-3-2-3 2zM9 8h6M9 12h6" },
  { key: "arrow-left-right", label: "Transfer", tags: ["transakcije", "prenos", "sravnjenje"], path: "M4 8h14l-3-3M20 16H6l3 3" },
  { key: "chart-bar", label: "Stubičasti grafikon", tags: ["izvještaji", "bi", "analitika"], path: "M4 20V4M4 20h16M8 16v-5M12 16V8M16 16v-3" },
  { key: "chart-pie", label: "Kružni grafikon", tags: ["izvještaji", "udjeli", "bi"], path: "M12 3a9 9 0 1 0 9 9h-9zM15 3.5A9 9 0 0 1 20.5 9H15z" },
  { key: "scale", label: "Vaga", tags: ["pravo", "pravni predmeti", "opomene"], path: "M12 4v16M7 20h10M5 7h14M7 7l-3 6h6zM17 7l-3 6h6z" },
  { key: "scroll", label: "Svitak", tags: ["vlasništvo", "ugovor", "isprava"], path: "M6 3h12v18H6zM9 8h6M9 12h6M14 17l1.5 1.5L18 16" },
  { key: "wrench", label: "Ključ za radove", tags: ["održavanje", "kvarovi", "popravke"], path: "M14.5 6.5a4 4 0 0 0 5 5L12 19a2 2 0 0 1-3-3l7.5-7.5a4 4 0 0 0-2-2zM5 21l2-2" },
  { key: "megaphone", label: "Megafon", tags: ["obavještenja", "oglasna ploča", "zajednica"], path: "M3 10v4h3l7 4V6L6 10zM17 9a4 4 0 0 1 0 6" },
  { key: "vote", label: "Glasanje", tags: ["glasačka kutija", "odluke", "skupština"], path: "M4 12h16v9H4zM8 12V4h8v8M10 8l1.5 1.5L14 7" },
  { key: "link", label: "Veza", tags: ["povezivanje", "nalozi", "lanac"], path: "M10 14a4 4 0 0 0 5.7 0l3-3a4 4 0 0 0-5.7-5.7l-1 1M14 10a4 4 0 0 0-5.7 0l-3 3a4 4 0 0 0 5.7 5.7l1-1" },
  { key: "workflow", label: "Tok rada", tags: ["hodogram", "proces", "raci"], path: "M4 4h6v5H4zM14 15h6v5h-6zM7 9v4h10v2" },
  { key: "network", label: "Organizacija", tags: ["struktura", "upravnici", "mreža"], path: "M9 3h6v5H9zM12 8v4M5 12h14M5 12v4M19 12v4M2 16h6v5H2zM16 16h6v5h-6z" },
  { key: "calendar", label: "Kalendar", tags: ["rokovi", "termini", "datum"], path: "M4 5h16v16H4zM4 10h16M8 3v4M16 3v4" },
  { key: "bell", label: "Zvono", tags: ["upozorenja", "notifikacije", "podsjetnik"], path: "M6 16V11a6 6 0 0 1 12 0v5l2 2H4zM10 21h4" },
  { key: "palette", label: "Paleta", tags: ["ikonice", "prikaz", "izgled"], path: "M12 3a9 9 0 1 0 0 18c1 0 1.5-.8 1.5-1.6 0-.5-.3-.9-.3-1.4 0-.8.6-1.5 1.5-1.5H17a4 4 0 0 0 4-4c0-5-4-9.5-9-9.5zM7.5 11h.01M10 7.5h.01M14.5 7.5h.01M17 11h.01" },
  { key: "droplet", label: "Kap", tags: ["voda", "vodovod", "komunalije"], path: "M12 3s-6 7-6 11a6 6 0 0 0 12 0c0-4-6-11-6-11z" },
];

const byKey = new Map(iconPalette.map((icon) => [icon.key, icon]));
const fallback = byKey.get("list")!;

export function isIconKey(key: string | null | undefined): boolean { return Boolean(key && byKey.has(key)); }
export function paletteIcon(key: string | null | undefined): PaletteIcon { return (key && byKey.get(key)) || fallback; }

const plain = (text: string) => text.toLowerCase().replace(/đ/g, "d").normalize("NFD").replace(/[̀-ͯ]/g, "");

// Picker search over key, label and tags, ignoring case and diacritics ("sifarnik" finds the barcode).
export function searchIcons(query: string): PaletteIcon[] {
  const needle = plain(query.trim());
  if (!needle) return iconPalette;
  return iconPalette.filter((icon) => [icon.key, icon.label, ...icon.tags].some((text) => plain(text).includes(needle)));
}

// Response of GET /api/system/module-icons → icon map, keeping only keys that exist in the palette.
export function normalizeModuleIcons(raw: unknown): Record<string, string> {
  const icons = typeof raw === "object" && raw !== null ? (raw as { icons?: unknown }).icons : null;
  if (typeof icons !== "object" || icons === null) return {};
  return Object.fromEntries(Object.entries(icons as Record<string, unknown>)
    .filter((entry): entry is [string, string] => typeof entry[1] === "string" && isIconKey(entry[1])));
}
