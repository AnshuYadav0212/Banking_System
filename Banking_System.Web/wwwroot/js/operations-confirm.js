// Banking Operations: fills in the shared "freeze/unfreeze account"
// confirmation modal from the row button that opened it, so each row needs
// only a trigger button - not its own form - and the confirmation looks
// like the rest of the app instead of a native confirm() dialog. Wired with
// event delegation so it keeps working across Blazor enhanced navigation.
(function () {
    document.addEventListener("show.bs.modal", function (event) {
        const modal = event.target;
        const button = event.relatedTarget;
        if (!button) return;

        if (modal.id === "accountStatusModal") {
            fillAccountModal(modal, button);
        }
    });

    function fillAccountModal(modal, button) {
        const accountId = button.dataset.accountId;
        const targetActive = button.dataset.targetActive === "true";
        const name = button.dataset.customerName;
        const number = button.dataset.accountNumber;

        const form = modal.querySelector("#accountStatusForm");
        form.action = `/employee/operations/accounts/${accountId}/status`;
        modal.querySelector("#accountStatusActive").value = targetActive ? "true" : "false";

        modal.querySelector("#accountStatusTitle").textContent = targetActive ? "Unfreeze account" : "Freeze account";
        modal.querySelector("#accountStatusBody").innerHTML = targetActive
            ? `<p>Unfreeze account <strong>${escapeHtml(number)}</strong> for <strong>${escapeHtml(name)}</strong>?</p>
               <p class="text-muted small mb-0">Transfers on this account will work again immediately.</p>`
            : `<p>Freeze account <strong>${escapeHtml(number)}</strong> for <strong>${escapeHtml(name)}</strong>?</p>
               <p class="text-muted small mb-0">Transfers on this account will be blocked immediately.</p>`;

        const confirmButton = modal.querySelector("#accountStatusConfirm");
        confirmButton.textContent = targetActive ? "Unfreeze" : "Freeze";
        confirmButton.className = `btn ${targetActive ? "btn-success" : "btn-danger"}`;
    }

    function escapeHtml(value) {
        const div = document.createElement("div");
        div.textContent = value ?? "";
        return div.innerHTML;
    }
})();
