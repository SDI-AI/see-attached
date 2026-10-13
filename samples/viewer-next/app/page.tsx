"use client";

import { useRef, useState } from "react";

export default function HomePage() {
  const frame = useRef<HTMLIFrameElement>(null);
  const [zoom, setZoom] = useState(100);

  return (
    <main className="viewer">
      <header>
        <strong>Invoice</strong>
        <button type="button" onClick={() => setZoom((value) => Math.max(50, value - 10))} disabled={zoom <= 50}>
          Smaller
        </button>
        <span>{zoom}%</span>
        <button type="button" onClick={() => setZoom((value) => Math.min(200, value + 10))} disabled={zoom >= 200}>
          Larger
        </button>
        <button type="button" onClick={() => frame.current?.contentWindow?.print()}>
          Print
        </button>
      </header>
      <iframe
        ref={frame}
        title="Invoice"
        sandbox="allow-same-origin allow-modals"
        src="/invoice.html"
        style={{ ["--zoom" as string]: String(zoom / 100) }}
      />
    </main>
  );
}
