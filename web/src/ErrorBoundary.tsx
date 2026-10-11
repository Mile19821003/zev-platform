import { Component, ErrorInfo, ReactNode } from "react";
import { buildClientReport, reportClientError } from "./errorReporting.ts";

// Catches rendering errors anywhere in the app, reports them to the system error log and shows a recovery screen
// instead of a blank page.
export default class ErrorBoundary extends Component<{ children: ReactNode }, { failed: boolean }> {
  state = { failed: false };

  static getDerivedStateFromError() { return { failed: true }; }

  componentDidCatch(error: Error, info: ErrorInfo) {
    reportClientError(buildClientReport(error, `Greška prikaza: ${error.message}`, window.location.href, info.componentStack));
  }

  render() {
    if (!this.state.failed) return this.props.children;
    return <main className="loading" role="alert">
      <div className="empty-state">
        <h1>Došlo je do greške</h1>
        <p>Greška je automatski prijavljena administratoru. Pokušajte ponovo.</p>
        <button type="button" onClick={() => window.location.reload()}>Ponovo učitaj</button>
      </div>
    </main>;
  }
}
