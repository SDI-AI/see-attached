"use client";

import { useRef, useState } from "react";

const reports: Record<string, { title: string; src: string }> = {
  invoice: { title: "Invoice", src: "/invoice.html" },
  orders: { title: "Orders", src: "/orders.html" },
  barcode: { title: "Shipping label", src: "/barcode.html" },
};

export default function HomePage() {
  const frame = useRef<HTMLIFrameElement>(null);
  const [zoom, setZoom] = useState(100);
  const [report, setReport] = useState("invoice");
  const current = reports[report] ?? reports.invoice;

  return (
    <main className="viewer">
      <header>
        <strong>{current.title}</strong>
        <button type="button" onClick={() => setReport("invoice")}>
          Invoice
        </button>
        <button type="button" onClick={() => setReport("orders")}>
          Orders
        </button>
        <button type="button" onClick={() => setReport("barcode")}>
          Shipping label
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
        title={current.title}
        sandbox="allow-same-origin allow-modals"
        src={current.src}
        style={{ ["--zoom" as string]: String(zoom / 100) }}
      />
    </main>
  );
}
