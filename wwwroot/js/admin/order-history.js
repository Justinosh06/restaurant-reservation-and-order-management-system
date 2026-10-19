(() => {
    const PAGE_SIZE = 12;
    const ITEMS_SHOWN = 4;

    let orders = [];
    let shownCount = PAGE_SIZE;

    const form = document.getElementById("history-form");
    const fromInput = document.getElementById("history-from");
    const toInput = document.getElementById("history-to");
    const searchInput = document.getElementById("history-search");
    const countEl = document.getElementById("history-count");
    const gridEl = document.getElementById("history-grid");
    const moreButton = document.getElementById("history-more");
    const orderModal = new bootstrap.Modal(document.getElementById("order-modal"));

    // Every order in the history has been served and paid for.
    const paidBadge = `<span class="badge rounded-pill border bg-success-subtle text-success-emphasis border-success-subtle">
        <i class="fa-solid fa-check me-1"></i>Paid</span>`;

    const itemRows = (items) => items.map(item => `
        <tr>
            <td class="ps-0">${AdminUI.escapeHtml(item.name)}</td>
            <td class="text-center">${item.quantity}</td>
            <td class="text-end pe-0">${AdminUI.formatCurrency(item.unitPrice * item.quantity)}</td>
        </tr>`).join("");

    const orderCard = (o) => {
        const more = o.items.length > ITEMS_SHOWN
            ? `<div class="small text-body-secondary text-center">+${o.items.length - ITEMS_SHOWN} more items</div>`
            : "";

        return `
            <div class="col-md-6 col-xxl-4">
                <div class="card h-100 bg-body border-0 rounded-4">
                    <div class="card-body d-flex flex-column">
                        <div class="d-flex align-items-start gap-3">
                            <div class="bg-success-subtle text-success-emphasis rounded-3 fw-semibold small text-center px-2 py-3">${AdminUI.escapeHtml(o.table ?? "-")}</div>
                            <div class="flex-grow-1 overflow-hidden">
                                <div class="fw-semibold text-truncate" title="${AdminUI.escapeHtml(o.customer)}">${AdminUI.escapeHtml(o.customer)}</div>
                                <div class="small text-body-secondary">Order #${AdminUI.escapeHtml(o.displayId)} &middot; Table ${AdminUI.escapeHtml(o.table ?? "-")}</div>
                            </div>
                            ${paidBadge}
                        </div>

                        <div class="d-flex justify-content-between small text-body-secondary border-bottom pb-2 my-3">
                            <span>${AdminUI.formatDate(o.createdAt)}</span>
                            <span>${AdminUI.formatTime(o.createdAt)}</span>
                        </div>

                        <table class="table table-sm table-borderless small mb-1">
                            <thead>
                                <tr class="text-body-secondary">
                                    <th class="ps-0 fw-normal text-body-secondary">Items</th>
                                    <th class="text-center fw-normal text-body-secondary">Qty</th>
                                    <th class="text-end pe-0 fw-normal text-body-secondary">Price</th>
                                </tr>
                            </thead>
                            <tbody>${itemRows(o.items.slice(0, ITEMS_SHOWN))}</tbody>
                        </table>
                        ${more}

                        <div class="d-flex justify-content-between fw-semibold border-top pt-2 mt-auto">
                            <span>Total</span>
                            <span>${AdminUI.formatCurrency(o.total)}</span>
                        </div>
                        <button type="button" class="btn btn-sm btn-light border mt-3" data-details="${o.id}">View details</button>
                    </div>
                </div>
            </div>`;
    };

    const showDetails = (o) => {
        document.getElementById("order-modal-title").textContent = `Order #${o.displayId}`;
        const remarks = o.items.filter(item => item.remarks);
        document.getElementById("order-modal-body").innerHTML = `
            <div class="d-flex justify-content-between align-items-center mb-3">
                ${paidBadge}
                <span class="small text-body-secondary">${AdminUI.formatDate(o.createdAt)}, ${AdminUI.formatTime(o.createdAt)}</span>
            </div>
            <dl class="row small mb-3">
                <dt class="col-4 fw-normal text-body-secondary">Customer</dt><dd class="col-8 text-break">${AdminUI.escapeHtml(o.customer)}</dd>
                <dt class="col-4 fw-normal text-body-secondary">Table</dt><dd class="col-8">${AdminUI.escapeHtml(o.table ?? "-")}</dd>
            </dl>
            <table class="table table-sm small mb-2">
                <thead><tr><th class="ps-0">Items</th><th class="text-center">Qty</th><th class="text-end pe-0">Price</th></tr></thead>
                <tbody>${itemRows(o.items)}</tbody>
                <tfoot><tr class="fw-semibold"><td class="ps-0" colspan="2">Total</td><td class="text-end pe-0">${AdminUI.formatCurrency(o.total)}</td></tr></tfoot>
            </table>
            ${remarks.length ? `
                <div class="small fw-semibold mb-1">Remarks</div>
                ${remarks.map(item => `<div class="bg-body-tertiary rounded-2 small px-2 py-1 mb-1">${AdminUI.escapeHtml(item.name)}: ${AdminUI.escapeHtml(item.remarks)}</div>`).join("")}` : ""}
            <p class="small text-body-secondary mb-0 mt-2">Prices shown are today's menu prices.</p>`;
        orderModal.show();
    };

    const showGridMessage = (text, tone = "text-body-secondary") => {
        gridEl.innerHTML = `<div class="col-12"><div class="bg-body rounded-4 text-center ${tone} py-5">${AdminUI.escapeHtml(text)}</div></div>`;
        moreButton.classList.add("d-none");
    };

    const matchesSearch = (o) => {
        const term = searchInput.value.trim().toLowerCase();
        return !term || [o.displayId, o.table, o.customer].some(v => (v ?? "").toLowerCase().includes(term));
    };

    const render = () => {
        const visible = orders.filter(matchesSearch);
        countEl.textContent = `· ${visible.length}`;

        if (visible.length === 0) {
            showGridMessage(orders.length ? "No orders match this search." : "No paid orders for these dates.");
            return;
        }

        gridEl.innerHTML = visible.slice(0, shownCount).map(orderCard).join("");
        moreButton.classList.toggle("d-none", visible.length <= shownCount);
    };

    const loadHistory = () => {
        if (toInput.value < fromInput.value) {
            AdminUI.showToast("The To date must be on or after the From date.", "danger");
            return;
        }

        countEl.textContent = "";
        shownCount = PAGE_SIZE;
        showGridMessage("Loading...");

        AdminUI.get("History", { from: fromInput.value, to: toInput.value })
            .then(data => { orders = data; render(); })
            .catch(error => {
                showGridMessage(error.message, "text-danger");
                AdminUI.showError(error);
            });
    };

    form.addEventListener("submit", (e) => {
        e.preventDefault();
        loadHistory();
    });

    searchInput.addEventListener("input", () => {
        shownCount = PAGE_SIZE;
        render();
    });

    moreButton.addEventListener("click", () => {
        shownCount += PAGE_SIZE;
        render();
    });

    gridEl.addEventListener("click", (e) => {
        const button = e.target.closest("[data-details]");
        if (button) showDetails(orders.find(o => o.id === button.dataset.details));
    });

    document.getElementById("history-tab").addEventListener("shown.bs.tab", loadHistory, { once: true });
})();
