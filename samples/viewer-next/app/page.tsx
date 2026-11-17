"use client";

import { useRef, useState } from "react";

export default function HomePage() {
  const frame = useRef<HTMLIFrameElement>(null);
  const [zoom, setZoom] = useState(100);
  const [report, setReport] = useState("invoice");

  return (
    <main className="viewer">
      <header>
        <strong>{report === "orders" ? "Orders" : "Invoice"}</strong>
        <button type="button" onClick={() => setReport("invoice")}>
          Invoice
        </button>
        <button type="button" onClick={() => setReport("orders")}>
          Orders
        </button>
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
        title={report === "orders" ? "Orders" : "Invoice"}
        sandbox="allow-same-origin allow-modals"
        src={report === "orders" ? "/orders.html" : "/invoice.html"}
        style={{ ["--zoom" as string]: String(zoom / 100) }}
      />
    </main>
  );
}
