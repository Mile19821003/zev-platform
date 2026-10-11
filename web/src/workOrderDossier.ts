// Work order dossier: attachment kinds and rules, completion request.
export type AttachmentType = "Invoice" | "PhotoBefore" | "PhotoAfter" | "WorkReport";
export const attachmentTypes: { value: AttachmentType; label: string }[] = [
  { value: "Invoice", label: "Račun / faktura (PDF)" }, { value: "PhotoBefore", label: "Slika prije" },
  { value: "PhotoAfter", label: "Slika poslije" }, { value: "WorkReport", label: "Izvještaj o radu" }];
export const attachmentMaxBytes = 10_000_000;
export const attachmentAccept = ".pdf,.jpg,.jpeg,.png";
const extensions: Record<string, string[]> = { "application/pdf": [".pdf"], "image/jpeg": [".jpg", ".jpeg"], "image/png": [".png"] };

export function attachmentLabel(type: string): string {
  return attachmentTypes.find((item) => item.value === type)?.label ?? type;
}

export function validateAttachment(file: { name: string; type: string; size: number }): string | null {
  const extension = file.name.slice(file.name.lastIndexOf(".")).toLowerCase();
  if (!extensions[file.type]?.includes(extension)) return "Dozvoljeni su PDF, JPG i PNG.";
  if (file.size > attachmentMaxBytes) return "Datoteka je veća od 10 MB.";
  if (file.size === 0) return "Datoteka je prazna.";
  return null;
}

export const isImage = (contentType: string) => contentType === "image/jpeg" || contentType === "image/png";

export type CompletionDraft = { completedDate: string; workDescription: string; invoiceNumber: string; costAmount: string;
  expenseFund: "" | "Regular" | "Reserve"; supplierId: string; contractorName: string; contractorPhone: string; recordExpense: boolean };

// What blocks "Završi nalog" before the request is sent (the API checks the same).
export function completionProblem(draft: CompletionDraft): string | null {
  if (!draft.completedDate) return "Unesite datum završetka.";
  if (!draft.workDescription.trim()) return "Opišite izvršeni posao.";
  if (draft.costAmount && !(Number(draft.costAmount) >= 0)) return "Iznos mora biti broj.";
  if (draft.recordExpense && (!draft.supplierId || !draft.invoiceNumber.trim() || !(Number(draft.costAmount) > 0) || !draft.expenseFund))
    return "Za upis u rashode potrebni su dobavljač, broj računa, iznos i fond.";
  return null;
}

export function completionBody(draft: CompletionDraft, rowVersion: string) {
  return {
    completedDate: draft.completedDate, workDescription: draft.workDescription.trim(),
    invoiceNumber: draft.invoiceNumber.trim() || null, costAmount: draft.costAmount ? Number(draft.costAmount) : null,
    expenseFund: draft.expenseFund || null, supplierId: draft.supplierId || null,
    contractorName: draft.contractorName.trim() || null, contractorPhone: draft.contractorPhone.trim() || null,
    recordExpense: draft.recordExpense, rowVersion,
  };
}

export function fileSizeLabel(bytes: number): string {
  if (bytes < 1_000_000) return `${Math.max(1, Math.round(bytes / 1000))} KB`;
  return `${(bytes / 1_000_000).toFixed(1).replace(".", ",")} MB`;
}
