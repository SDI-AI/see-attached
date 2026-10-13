export function printFrame(id) {
  const frame = document.getElementById(id);
  frame?.contentWindow?.print();
}

export function downloadPdf(base64, fileName) {
  const link = document.createElement("a");
  link.href = "data:application/pdf;base64," + base64;
  link.download = fileName || "report.pdf";
  link.click();
}
