(function () {
    document.addEventListener("input", function (event) {
        const input = event.target;
        if (!(input instanceof Element) || !input.matches("[data-tx-search-input]")) return;

        const panel = input.closest(".tx-picker-panel");
        if (!panel) return;

        const term = input.value.trim().toLowerCase();
        const rows = panel.querySelectorAll(".tx-pick-row[data-tx-search]");
        let anyVisible = false;

        rows.forEach(function (row) {
            const matches = term === "" || row.getAttribute("data-tx-search").includes(term);
            row.hidden = !matches;
            if (matches) anyVisible = true;
        });

        const empty = panel.querySelector(".tx-pick-empty");
        if (empty) empty.hidden = anyVisible;
    });
})();
