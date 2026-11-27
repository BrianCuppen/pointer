// Small progressive enhancements. Every page works without this file.
(function () {
  "use strict";

  // Submit buttons show a spinner and cannot be pressed twice.
  document.addEventListener("submit", function (e) {
    var form = e.target;
    if (!(form instanceof HTMLFormElement) || e.defaultPrevented) return;
    var button = e.submitter || form.querySelector("button[type=submit], button:not([type])");
    if (!button || button.hasAttribute("data-no-busy")) return;
    // Let the browser read the button's name/value first, then mark it busy.
    setTimeout(function () {
      button.classList.add("is-busy");
      button.setAttribute("aria-busy", "true");
    }, 0);
  });

  // Undo the busy state when a page is restored from the back/forward cache.
  window.addEventListener("pageshow", function () {
    document.querySelectorAll(".is-busy").forEach(function (b) {
      b.classList.remove("is-busy");
      b.removeAttribute("aria-busy");
    });
  });

  // Day picker: keep the selected day highlighted when htmx loads its times.
  document.addEventListener("click", function (e) {
    var day = e.target.closest && e.target.closest("[data-day]");
    if (!day) return;
    document.querySelectorAll("[data-day][aria-current]").forEach(function (d) { d.removeAttribute("aria-current"); });
    day.setAttribute("aria-current", "date");
  });

  // Copy buttons: <button data-copy="#id">.
  document.addEventListener("click", function (e) {
    var btn = e.target.closest && e.target.closest("[data-copy]");
    if (!btn) return;
    var source = document.querySelector(btn.getAttribute("data-copy"));
    if (!source || !navigator.clipboard) return;
    navigator.clipboard.writeText(source.textContent.trim()).then(function () {
      var label = btn.querySelector("[data-label]");
      if (!label) return;
      var original = label.textContent;
      label.textContent = btn.getAttribute("data-copied") || "OK";
      setTimeout(function () { label.textContent = original; }, 1800);
    });
  });

  // Toasts remove themselves once their animation ends.
  document.querySelectorAll(".toast").forEach(function (t) {
    t.addEventListener("animationend", function () { t.remove(); });
  });

  // Mobile admin menu: close when a link inside it is chosen or on Escape.
  document.addEventListener("keydown", function (e) {
    if (e.key !== "Escape") return;
    document.querySelectorAll("details[open].mobile-nav").forEach(function (d) { d.removeAttribute("open"); });
  });
})();
