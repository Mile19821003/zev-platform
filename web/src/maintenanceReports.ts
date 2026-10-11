// Reporting centre of the maintenance module: chart geometry and table helpers (GET .../maintenance/reports/summary).
export type ReportRow = { source: string; title: string; category: string; plannedOn: string | null; completedOn: string | null;
  status: string; contractor: string | null; invoiceNumber: string | null; cost: number | null; fund: string | null; projected: boolean };
export type CategoryCost = { category: string; cost: number; jobs: number };
export type ReportMonth = { month: number; planned: number; realized: number; cost: number };
export type GanttMark = { date: string; status: string; projected: boolean };
export type GanttPlan = { planId: string; title: string; category: string; frequencyDays: number; isActive: boolean; marks: GanttMark[] };
export type MaintenanceReport = { year: number; rows: ReportRow[]; costByCategory: CategoryCost[]; months: ReportMonth[];
  gantt: GanttPlan[]; planned: number; realized: number; totalCost: number };

export type ReportView = "table" | "charts" | "timeline";
export const reportViews: { value: ReportView; label: string }[] = [
  { value: "table", label: "📋 Analitička tabela" }, { value: "charts", label: "📊 Grafikoni" },
  { value: "timeline", label: "📅 Gantogram" }];

export const monthNames = ["Jan", "Feb", "Mar", "Apr", "Maj", "Jun", "Jul", "Avg", "Sep", "Okt", "Nov", "Dec"];
export const sourceLabels: Record<string, string> = { Plan: "Plan", Prijava: "Prijava kvara", Nalog: "Radni nalog" };

// Categorical colours of the charts (fixed so a category keeps its colour between years).
export const chartColors = ["#176d50", "#2563eb", "#bf7b11", "#a22b2b", "#5b3d93", "#0e7490", "#65a30d", "#db2777"];

export type PieSlice = { category: string; cost: number; share: number; path: string; color: string };

// SVG paths of a pie of radius r centred at (r, r); a single category is a full circle.
export function pieSlices(costs: CategoryCost[], r = 90): PieSlice[] {
  const total = costs.reduce((sum, item) => sum + item.cost, 0);
  if (total <= 0) return [];
  let angle = -Math.PI / 2;
  return costs.map((item, index) => {
    const share = item.cost / total;
    const color = chartColors[index % chartColors.length];
    if (share >= 0.9999) return { category: item.category, cost: item.cost, share: 1, color,
      path: `M ${r} 0 A ${r} ${r} 0 1 1 ${r - 0.01} 0 Z` };
    const start = angle;
    angle += share * 2 * Math.PI;
    const point = (value: number) => `${(r + r * Math.cos(value)).toFixed(2)} ${(r + r * Math.sin(value)).toFixed(2)}`;
    return { category: item.category, cost: item.cost, share, color,
      path: `M ${r} ${r} L ${point(start)} A ${r} ${r} 0 ${share > 0.5 ? 1 : 0} 1 ${point(angle)} Z` };
  });
}

export function percent(share: number): string {
  return `${Math.round(share * 100)} %`;
}

// Bar heights (0–1) of planned vs realized per month on a common scale.
export function monthBars(months: ReportMonth[]): { month: number; planned: number; realized: number; max: number }[] {
  const max = Math.max(1, ...months.map((item) => Math.max(item.planned, item.realized)));
  return months.map((item) => ({ month: item.month, planned: item.planned / max, realized: item.realized / max, max }));
}

// Position of a date on the 12-month timeline, as a percentage of the year.
export function timelinePosition(date: string, year: number): number {
  const start = Date.UTC(year, 0, 1);
  const end = Date.UTC(year + 1, 0, 1);
  const [y, m, d] = date.split("-").map(Number);
  return Math.min(100, Math.max(0, ((Date.UTC(y, m - 1, d) - start) / (end - start)) * 100));
}

export function frequencyText(days: number): string {
  if (days <= 0) return "";
  if (days === 7) return "sedmično";
  if (days >= 28 && days <= 31) return "mjesečno";
  if (days >= 89 && days <= 92) return "kvartalno";
  if (days >= 180 && days <= 184) return "polugodišnje";
  if (days >= 364 && days <= 366) return "godišnje";
  return `svakih ${days} dana`;
}

// Table filter by source and a text search over title, category and contractor.
export function filterRows(rows: ReportRow[], source: string, search: string): ReportRow[] {
  const text = search.trim().toLowerCase();
  return rows.filter((row) => (!source || row.source === source)
    && (!text || [row.title, row.category, row.contractor ?? ""].some((value) => value.toLowerCase().includes(text))));
}

export const reportExportPath = (zevId: string, year: number, format: "pdf" | "xlsx") =>
  `/api/zevs/${zevId}/maintenance/reports/${year}.${format}`;
