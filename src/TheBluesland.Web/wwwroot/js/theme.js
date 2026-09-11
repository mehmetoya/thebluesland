// docs/specs/dark-light-mode-toggle.md section 2: FOUC-free theme detection. This file is
// intentionally NOT wrapped in an IIFE-after-DOMContentLoaded pattern for its first job below - the
// top-level code must run synchronously, before first paint, so `data-theme` is on <html> before
// app.css is ever applied. Loaded from App.razor with neither `defer` nor `async` and placed before
// the stylesheet <link> - see that file's comment for why (a deferred/async script here would defeat
// the whole point: it would run after parsing/paint, which is the exact flash this exists to avoid).
//
// Dark is the default for every visitor with no stored preference (Mehmet's explicit 2026-09-11
// decision) - system `prefers-color-scheme` is deliberately never read here.
if (localStorage.getItem("theme") === "light") {
  document.documentElement.setAttribute("data-theme", "light");
}

// Second job: wire up the header toggle button(s). Safe to register now even though the script runs
// before <body> exists yet - the listener itself only fires once parsing completes.
document.addEventListener("DOMContentLoaded", function () {
  document.querySelectorAll(".theme-toggle").forEach(function (button) {
    button.addEventListener("click", function () {
      var isLight = document.documentElement.getAttribute("data-theme") === "light";
      if (isLight) {
        document.documentElement.removeAttribute("data-theme");
        localStorage.setItem("theme", "dark");
      } else {
        document.documentElement.setAttribute("data-theme", "light");
        localStorage.setItem("theme", "light");
      }

      var nowLight = !isLight;
      button.setAttribute("aria-pressed", String(nowLight));
      button.setAttribute("aria-label", nowLight ? "Switch to dark theme" : "Switch to light theme");
    });
  });
});
