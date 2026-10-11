import { useCallback, useEffect, useRef, useState } from "react";

export type ToastMessage = { id: number; text: string; tone: "success" | "error" };

// Short confirmation in the bottom right corner that disappears by itself (role=status for screen readers).
export function useToast(duration = 4000) {
  const [toasts, setToasts] = useState<ToastMessage[]>([]);
  const next = useRef(1);
  const timers = useRef<number[]>([]);
  useEffect(() => () => timers.current.forEach((timer) => window.clearTimeout(timer)), []);
  const show = useCallback((text: string, tone: ToastMessage["tone"] = "success") => {
    const id = next.current++;
    setToasts((current) => [...current, { id, text, tone }]);
    timers.current.push(window.setTimeout(() => setToasts((current) => current.filter((item) => item.id !== id)), duration));
  }, [duration]);
  const dismiss = useCallback((id: number) => setToasts((current) => current.filter((item) => item.id !== id)), []);
  return { toasts, show, dismiss };
}

export function ToastStack({ toasts, onDismiss }: { toasts: ToastMessage[]; onDismiss: (id: number) => void }) {
  return <div className="toast-stack" role="status" aria-live="polite">
    {toasts.map((toast) => <div key={toast.id} className={`toast ${toast.tone}`}>
      <span>{toast.text}</span>
      <button type="button" aria-label="Zatvori obavijest" onClick={() => onDismiss(toast.id)}>×</button>
    </div>)}
  </div>;
}
