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

    // Picking a transaction updates the collapsed summary and closes the
    // picker, instead of leaving it open showing the old selection.
    document.addEventListener("change", function (event) {
        const radio = event.target;
        if (!(radio instanceof Element) || !radio.matches(".tx-pick-radio")) return;

        const details = radio.closest(".tx-picker");
        const row = radio.closest(".tx-pick-row");
        if (!details || !row) return;

        const summaryText = details.querySelector("[data-tx-summary-text]");
        if (summaryText) {
            summaryText.textContent = row.getAttribute("data-tx-preview") || "";
            summaryText.classList.remove("text-muted");
        }

        details.removeAttribute("open");
    });

    // Some categories require a related transaction; the hint next to the
    // field's label reflects the category actually selected, live, instead of
    // only after a submit-and-reload round trip.
    document.addEventListener("change", function (event) {
        const select = event.target;
        if (!(select instanceof Element) || select.id !== "category") return;

        const hint = document.querySelector("[data-tx-required-hint]");
        if (!hint) return;

        const requiresTx = select.options[select.selectedIndex]?.dataset.requiresTx === "true";

        hint.textContent = requiresTx ? "(required for this category)" : "(optional)";
        hint.classList.toggle("text-danger", requiresTx);
        hint.classList.toggle("text-muted", !requiresTx);
    });
})();
