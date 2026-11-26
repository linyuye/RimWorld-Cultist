const controls = document.querySelector("[data-filters]");
if (controls) {
  const buttons = [...controls.querySelectorAll("button")];
  const days = [...document.querySelectorAll("[data-day]")];
  const entries = [...document.querySelectorAll("[data-kind]")];
  const status = document.querySelector("[data-status]");

  function select(kind) {
    for (const button of buttons) {
      button.setAttribute("aria-pressed", String(button.dataset.filter === kind));
    }
    for (const entry of entries) {
      entry.hidden = kind !== "all" && entry.dataset.kind !== kind;
    }
    for (const day of days) {
      day.hidden = !day.querySelector("[data-kind]:not([hidden])");
    }
    status.textContent = `显示 ${entries.filter(entry => !entry.hidden).length} 条记录`;
  }

  controls.hidden = false;
  controls.addEventListener("click", event => {
    const button = event.target.closest("button[data-filter]");
    if (button) select(button.dataset.filter);
  });
  for (const link of document.querySelectorAll("[data-date-link]")) {
    link.addEventListener("click", () => select("all"));
  }
}
