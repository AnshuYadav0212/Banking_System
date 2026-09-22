// Light/dark theme toggle. Applied as early as possible (see the inline
// script in App.razor's <head>) to avoid a flash of the wrong theme, and
// wired up here with event delegation so it keeps working across Blazor
// enhanced navigation.
(function () {
    const STORAGE_KEY = "theme";

    function apply(theme) {
        document.documentElement.setAttribute("data-theme", theme);
        try {
            localStorage.setItem(STORAGE_KEY, theme);
        } catch {
            // Private browsing / storage disabled: the toggle still works for this page view.
        }
    }

    document.addEventListener("click", function (event) {
        const button = event.target instanceof Element ? event.target.closest("[data-theme-toggle]") : null;
        if (!button) return;

        const current = document.documentElement.getAttribute("data-theme") === "dark" ? "dark" : "light";
        apply(current === "dark" ? "light" : "dark");
    });
})();
