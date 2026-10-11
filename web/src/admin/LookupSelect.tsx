import { useEffect } from "react";
import { defaultValue, LookupOption, LookupTypeCode, selectOptions, ValueBy } from "../lookupOptions.ts";
import { useLookup } from "../useLookup.ts";

// Drop-down filled from a code list. With `preselectDefault` an empty value takes the list's default item.
export default function LookupSelect({ typeCode, token, value, onChange, valueBy = "code", required, placeholder = "Odaberite",
  accept, preselectDefault, ariaLabel }: {
  typeCode: LookupTypeCode; token: string; value: string; onChange: (value: string) => void; valueBy?: ValueBy;
  required?: boolean; placeholder?: string; accept?: (item: LookupOption) => boolean; preselectDefault?: boolean;
  ariaLabel?: string;
}) {
  const { items } = useLookup(typeCode, token);
  const options = selectOptions(items, valueBy, value, accept);
  const fallback = preselectDefault ? defaultValue(items, valueBy, accept) : "";
  useEffect(() => { if (!value && fallback) onChange(fallback); }, [value, fallback]);
  return <select required={required} value={value} aria-label={ariaLabel} onChange={(event) => onChange(event.target.value)}>
    <option value="">{placeholder}</option>
    {options.map((option) => <option key={option.value} value={option.value}>{option.label}</option>)}
  </select>;
}

// Readable name of a stored code (for tables).
export function LookupLabel({ typeCode, token, value }: { typeCode: LookupTypeCode; token: string; value: string | null | undefined }) {
  const { items } = useLookup(typeCode, token);
  return <>{value ? items.find((item) => item.code === value)?.name ?? value : "—"}</>;
}

// Reason from REVERSAL_REASON plus an optional free note; the caller joins them with reasonText().
export function ReasonFields({ token, reason, note, onReason, onNote, label = "Razlog" }: { token: string; reason: string;
  note: string; onReason: (value: string) => void; onNote: (value: string) => void; label?: string }) {
  return <>
    <label>{label} *<LookupSelect typeCode="REVERSAL_REASON" token={token} valueBy="name" value={reason}
      onChange={onReason} required /></label>
    <label>Napomena<input maxLength={300} value={note} placeholder="Dodatno objašnjenje (opciono)"
      onChange={(event) => onNote(event.target.value)} /></label>
  </>;
}
