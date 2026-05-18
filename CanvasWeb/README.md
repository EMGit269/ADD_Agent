# CanvasWeb

`CanvasWeb` is the long-term React + TypeScript + Vite frontend for the ADDGH workbench canvas.

This implementation includes a runtime fallback in [`runtime/index.html`](./runtime/index.html) so the plugin can load the canvas even when a local npm toolchain is unavailable. When a full Node toolchain is present, `dist/` should be produced from the Vite project and will be preferred by the plugin host.
