import ReactDOM from "react-dom/client";
import App from "./App";
import ErrorBoundary from "./ErrorBoundary";
import { installGlobalErrorReporting } from "./errorReporting.ts";
import { applyCachedTheme } from "./useTheme.ts";
import "./styles.css";
import "./admin.css";

installGlobalErrorReporting();
applyCachedTheme();
ReactDOM.createRoot(document.getElementById("root")!).render(
  <ErrorBoundary><App /></ErrorBoundary>,
);
