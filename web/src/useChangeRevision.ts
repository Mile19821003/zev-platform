import { useEffect, useState } from "react";
import { subscribeChanges } from "./dataSync.ts";

export function useChangeRevision(...keys: string[]): number {
  const [revision, setRevision] = useState(0);
  const identity = keys.join("|");
  useEffect(() => subscribeChanges(identity.split("|").filter(Boolean), () => setRevision((value) => value + 1)), [identity]);
  return revision;
}
