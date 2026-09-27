// Sidebar collapse/expand, and closing the account dropdown (a <details>) when
// a menu item is chosen or the user clicks outside it. Event delegation
// throughout, so this keeps working across Blazor enhanced navigation - see
// wwwroot/js/theme.js for the same reasoning.
(function () {
    const STORAGE_KEY = "sidebarCollapsed";

    document.addEventListener("click", function (event) {
        const toggle = event.target instanceof Element ? event.target.closest("[data-sidebar-toggle]") : null;
        if (!toggle) return;

        const collapsed = document.documentElement.getAttribute("data-sidebar") === "collapsed";
        const next = collapsed ? "expanded" : "collapsed";

        document.documentElement.setAttribute("data-sidebar", next);
        try {
            localStorage.setItem(STORAGE_KEY, next);
        } catch {
            // Private browsing / storage disabled: the toggle still works for this page view.
        }
    });

    // Close the account menu after choosing an item inside it (link or the
    // logout button), so it doesn't stay open across the navigation.
    document.addEventListener("click", function (event) {
        const target = event.target instanceof Element ? event.target : null;
        if (!target) return;

        const menu = target.closest(".user-menu");
        const insidePanel = target.closest(".user-menu-panel");

        if (menu && insidePanel && (target.closest("a, button"))) {
            menu.removeAttribute("open");
            return;
        }

        // Clicked outside any open account menu: close it.
        document.querySelectorAll(".user-menu[open]").forEach(function (open) {
            if (!open.contains(target)) open.removeAttribute("open");
        });
    });
})();
