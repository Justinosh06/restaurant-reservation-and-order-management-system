(() => {
    const COLUMN_COUNT = 7;

    const form = document.getElementById("history-form");
    const fromInput = document.getElementById("history-from");
    const toInput = document.getElementById("history-to");
    const statusInput = document.getElementById("history-status");
    const countEl = document.getElementById("history-count");
    const bodyEl = document.getElementById("history-body");

    const showOrders = (orders) => {
        countEl.textContent = `${orders.length} order${orders.length === 1 ? "" : "s"}`;

        if (orders.length === 0) {
            bodyEl.innerHTML = AdminUI.tableMessageRow(COLUMN_COUNT, "No orders found for these dates.");
            return;
        }

        bodyEl.innerHTML = orders.map(o => `
            <tr>
                <td class="fw-medium">${AdminUI.escapeHtml(o.displayId)}</td>
                <td class="text-nowrap">${AdminUI.formatDate(o.createdAt)}, ${AdminUI.formatTime(o.createdAt)}</td>
                <td>${AdminUI.escapeHtml(o.table ?? "-")}</td>
                <td>${AdminUI.escapeHtml(o.customer)}</td>
                <td class="small text-muted">${o.items.map(AdminUI.escapeHtml).join("<br>") || "-"}</td>
                <td class="text-nowrap">${AdminUI.formatCurrency(o.total)}</td>
                <td>${AdminUI.statusBadge(o.status)}</td>
            </tr>`).join("");
    };

    const loadHistory = () => {
        if (toInput.value < fromInput.value) {
            AdminUI.showToast("The To date must be on or after the From date.", "danger");
            return;
        }

        countEl.textContent = "";
        bodyEl.innerHTML = AdminUI.tableMessageRow(COLUMN_COUNT, "Loading...");

        AdminUI.get("History", { from: fromInput.value, to: toInput.value, status: statusInput.value })
            .then(showOrders)
            .catch(error => {
                bodyEl.innerHTML = AdminUI.tableMessageRow(COLUMN_COUNT, error.message, "text-danger");
                AdminUI.showError(error);
            });
    };

    form.addEventListener("submit", (e) => {
        e.preventDefault();
        loadHistory();
    });

    document.getElementById("history-tab").addEventListener("shown.bs.tab", loadHistory, { once: true });
})();
