(() => {
    const STATUSES = ["Pending", "Preparing", "Served"];
    const NEXT_ACTION = {
        Pending: { label: "Start preparing", color: "btn-dark" },
        Preparing: { label: "Mark as served", color: "btn-success" }
    };
    const ITEMS_SHOWN = 3;
    const SERVED_SHOWN = 5;
    const LATE_AFTER_MINUTES = 15;

    let orders = [];
    let searchTerm = "";

    const boardEl = document.getElementById("order-board");
    const refreshButton = document.getElementById("order-refresh");
    const cashModalEl = document.getElementById("cash-modal");
    const cashModal = new bootstrap.Modal(cashModalEl);
    const cashForm = document.getElementById("cash-payment-form");
    const cashTenderedInput = document.getElementById("cash-tendered");
    const cashEmailInput = document.getElementById("cash-customer-email");
    const cashEmailOptions = document.getElementById("cash-customer-emails");
    const cashPaymentEntry = document.getElementById("cash-payment-entry");
    const cashPaymentResult = document.getElementById("cash-payment-result");
    const cashPaymentSubmit = document.getElementById("cash-payment-submit");
    const cashPaymentDone = document.getElementById("cash-payment-done");

    const renderCashOrders = (orders) => {
        document.getElementById("cash-order-breakdown").innerHTML = orders.map(order => `
            <section class="border rounded-3 p-2 mb-2">
                <div class="d-flex justify-content-between gap-2 fw-semibold small mb-2">
                    <span>Order #${AdminUI.escapeHtml(order.displayId)} · Due</span>
                    <span>${AdminUI.formatCurrency(order.amountDueCents)}</span>
                </div>
                <ul class="list-unstyled small mb-0">
                    ${order.items.map(item => `
                        <li class="d-flex justify-content-between gap-2">
                            <span>${item.quantity}&times; ${AdminUI.escapeHtml(item.itemName)}</span>
                            <span>${AdminUI.formatCurrency(item.unitPrice * item.quantity)}</span>
                        </li>`).join("")}
                </ul>
            </section>`).join("");
    };

    const updateCashChange = () => {
        const dueCents = Number(document.getElementById("cash-amount-due").dataset.cents);
        const tenderedCents = Math.round(Number(cashTenderedInput.value || 0) * 100);
        document.getElementById("cash-change-due").textContent =
            AdminUI.formatCurrency(Math.max(0, tenderedCents - dueCents));
    };

    cashTenderedInput.addEventListener("input", updateCashChange);

    const openCashPayment = async (sessionId, button) => {
        AdminUI.setBusy(button, true);
        try {
            const options = await AdminUI.get("CashOptions", { sessionId });
            cashPaymentEntry.classList.remove("d-none");
            cashPaymentResult.classList.add("d-none");
            cashPaymentResult.replaceChildren();
            cashPaymentSubmit.classList.remove("d-none");
            cashPaymentDone.classList.add("d-none");
            document.getElementById("cash-session-id").value = options.sessionId;
            document.getElementById("cash-modal-title").textContent = `Record cash · Table ${options.tableId}`;
            renderCashOrders(options.orders);

            const due = document.getElementById("cash-amount-due");
            due.dataset.cents = options.amountDueCents;
            due.textContent = AdminUI.formatCurrency(options.amountDueCents);

            cashTenderedInput.min = (options.amountDueCents / 100).toFixed(2);
            cashTenderedInput.value = (options.amountDueCents / 100).toFixed(2);
            cashEmailInput.value = options.customerEmail ?? "";
            cashEmailOptions.replaceChildren();
            options.customerEmails.forEach(email => {
                const option = document.createElement("option");
                option.value = email;
                cashEmailOptions.appendChild(option);
            });
            updateCashChange();
            cashModal.show();
        } catch (error) {
            AdminUI.showError(error);
        } finally {
            AdminUI.setBusy(button, false);
        }
    };

    cashForm.addEventListener("submit", async event => {
        event.preventDefault();
        const submitButton = document.getElementById("cash-payment-submit");
        AdminUI.setBusy(submitButton, true);
        try {
            const result = await AdminUI.post("RecordCash", {
                sessionId: document.getElementById("cash-session-id").value,
                cashTendered: Number(cashTenderedInput.value),
                customerEmail: cashEmailInput.value.trim()
            });
            cashPaymentEntry.classList.add("d-none");
            cashPaymentResult.innerHTML = `
                <h6 class="fw-semibold mb-3">Cash payment recorded</h6>
                <div class="d-flex justify-content-between border-top py-2">
                    <span>Paid amount</span><strong>${AdminUI.formatCurrency(result.amountDueCents)}</strong>
                </div>
                <div class="d-flex justify-content-between border-top py-2">
                    <span>Cash received</span><strong>${AdminUI.formatCurrency(result.cashTenderedCents)}</strong>
                </div>
                <div class="d-flex justify-content-between border-top border-bottom py-2">
                    <span>Change returned</span><strong>${AdminUI.formatCurrency(result.changeCents)}</strong>
                </div>`;
            cashPaymentResult.classList.remove("d-none");
            cashPaymentSubmit.classList.add("d-none");
            cashPaymentDone.classList.remove("d-none");
            AdminUI.showToast("Cash payment recorded.");
            load();
        } catch (error) {
            AdminUI.showError(error);
        } finally {
            AdminUI.setBusy(submitButton, false);
        }
    });

    const minutesSince = (date) => Math.max(0, Math.floor((Date.now() - new Date(date)) / 60000));

    const isOverdue = (o) => minutesSince(o.createdAt) >= LATE_AFTER_MINUTES;

    // Orders waiting longer than LATE_AFTER_MINUTES get a red badge.
    const waitBadge = (o) => {
        const tone = isOverdue(o) ? "bg-danger-subtle text-danger-emphasis" : "bg-body-tertiary text-body-secondary";
        return `<span class="badge rounded-pill fw-medium ${tone}" title="Placed at ${AdminUI.formatTime(o.createdAt)}">
            <i class="fa-regular fa-clock me-1"></i>Waiting ${AdminUI.formatDuration(minutesSince(o.createdAt))}</span>`;
    };

    const itemList = (o) => {
        const shown = o.items.slice(0, ITEMS_SHOWN)
            .map(item => `<li><span class="text-body-secondary me-1">${item.quantity}&times;</span>${AdminUI.escapeHtml(item.name)}</li>`)
            .join("");
        const more = o.items.length > ITEMS_SHOWN ? `<li class="text-body-secondary">+${o.items.length - ITEMS_SHOWN} more items</li>` : "";
        return `<ul class="list-unstyled small d-flex flex-column gap-1 mb-0">${shown}${more}</ul>`;
    };

    // Remarks such as "No chilli" are shown as small grey notes under the customer.
    const noteList = (o) => {
        const notes = o.items
            .filter(item => item.remarks)
            .map(item => `<div class="bg-warning-subtle text-warning-emphasis rounded-2 px-2 py-1"><i class="fa-regular fa-note-sticky me-1"></i>${AdminUI.escapeHtml(item.name)}: ${AdminUI.escapeHtml(item.remarks)}</div>`)
            .join("");
        return `<div class="small d-flex flex-column gap-1">${notes}</div>`;
    };

    const orderCard = (o) => {
        const next = NEXT_ACTION[o.status];
        return `
            <div class="card bg-body border-0 rounded-4">
                <div class="card-body">
                    <div class="d-flex justify-content-between align-items-start gap-2 mb-3">
                        <div class="overflow-hidden">
                            <div class="fw-semibold">Order #${AdminUI.escapeHtml(o.displayId)}</div>
                            <div class="small text-body-secondary text-truncate" title="${AdminUI.escapeHtml(o.customer ?? "Walk-in")}">${AdminUI.escapeHtml(o.customer ?? "Walk-in")}</div>
                        </div>
                        <span class="badge rounded-pill bg-body-tertiary text-body border fw-medium"><i class="fa-solid fa-chair me-1"></i>Table ${AdminUI.escapeHtml(o.table ?? "-")}</span>
                    </div>
                    <div class="row g-3">
                        <div class="col-sm-6">${itemList(o)}</div>
                        <div class="col-sm-6">${noteList(o)}</div>
                    </div>
                    <div class="d-flex flex-wrap align-items-center gap-2 border-top pt-3 mt-3">
                        ${waitBadge(o)}
                        <span class="small fw-semibold ms-auto">${AdminUI.formatCurrency(o.total)}</span>
                        <button type="button" class="btn btn-sm ${next.color} px-3" data-advance="${o.id}">${next.label}</button>
                    </div>
                </div>
            </div>`;
    };

    const servedRow = (o) => `
        <div class="card bg-body border-0 rounded-4">
            <div class="card-body d-flex align-items-center gap-2 py-2">
                <i class="fa-solid fa-circle-check text-success"></i>
                <span class="small fw-medium">Order #${AdminUI.escapeHtml(o.displayId)}</span>
                <span class="small text-body-secondary ms-auto">Table ${AdminUI.escapeHtml(o.table ?? "-")} &middot; ordered ${AdminUI.formatDuration(minutesSince(o.createdAt))} ago</span>
            </div>
        </div>`;

    const emptyMessage = (text, tone = "text-body-secondary") => `
        <div class="text-center ${tone} bg-body rounded-4 py-4 small">${AdminUI.escapeHtml(text)}</div>`;

    const matchesSearch = (o) => {
        const term = searchTerm.toLowerCase();
        return !term || [o.displayId, o.table, o.customer].some(v => (v ?? "").toLowerCase().includes(term));
    };

    const renderServed = (inColumn, visible) => {
        const columnEl = document.getElementById("column-Served");

        if (visible.length === 0) {
            columnEl.innerHTML = emptyMessage(searchTerm ? "No matching orders." : "No orders served yet.");
            return;
        }

        const cashSessions = new Map();
        visible.filter(order => order.cashPaymentPending).forEach(order => {
            if (!cashSessions.has(order.sessionId)) cashSessions.set(order.sessionId, order);
        });
        const cashActions = [...cashSessions.values()].map(order => `
            <div class="card border-success-subtle bg-success-subtle rounded-4">
                <div class="card-body d-flex flex-wrap align-items-center gap-2 py-2">
                    <span class="small fw-medium">Cash payment waiting · Table ${AdminUI.escapeHtml(order.table ?? "-")}</span>
                    <span class="small fw-semibold ms-auto">${AdminUI.formatCurrency(order.cashSessionAmountCents)}</span>
                    <button type="button" class="btn btn-sm btn-success" data-record-cash="${AdminUI.escapeHtml(order.sessionId)}">
                        Record cash received
                    </button>
                </div>
            </div>`).join("");
        const rows = visible.slice(0, SERVED_SHOWN).map(servedRow).join("");
        const more = visible.length > SERVED_SHOWN
            ? `<p class="small text-body-secondary text-center mb-0 py-1">and ${visible.length - SERVED_SHOWN} more</p>`
            : "";
        columnEl.innerHTML = cashActions + rows + more;
    };

    const render = () => {
        const waiting = orders.filter(o => o.status !== "Served");
        document.getElementById("count-active").textContent = waiting.length;
        document.getElementById("summary-overdue").textContent = waiting.filter(isOverdue).length;

        for (const status of STATUSES) {
            const inColumn = orders.filter(o => o.status === status);
            const visible = inColumn.filter(matchesSearch);
            document.getElementById(`count-${status}`).textContent = inColumn.length;
            document.getElementById(`summary-${status}`).textContent = inColumn.length;

            if (status === "Served") {
                renderServed(inColumn, visible);
                continue;
            }

            // Oldest first, so the order that has waited longest is at the top.
            const columnEl = document.getElementById(`column-${status}`);
            columnEl.innerHTML = visible.length
                ? [...visible].reverse().map(orderCard).join("")
                : emptyMessage(searchTerm ? "No matching orders." : "No orders.");
        }
    };

    const showColumnsMessage = (text, tone) => {
        for (const status of STATUSES) {
            document.getElementById(`column-${status}`).innerHTML = emptyMessage(text, tone);
        }
    };

    const load = () => {
        showColumnsMessage("Loading...");
        AdminUI.setBusy(refreshButton, true);
        AdminUI.get("List")
            .then(data => { orders = data; render(); })
            .catch(error => {
                showColumnsMessage(error.message, "text-danger");
                AdminUI.showError(error);
            })
            .finally(() => AdminUI.setBusy(refreshButton, false));
    };

    boardEl.addEventListener("click", async (e) => {
        const cashButton = e.target.closest("[data-record-cash]");
        if (cashButton) {
            await openCashPayment(cashButton.dataset.recordCash, cashButton);
            return;
        }

        const btn = e.target.closest("[data-advance]");
        if (!btn) return;

        AdminUI.setBusy(btn, true);
        try {
            const order = orders.find(o => o.id === btn.dataset.advance);
            const result = await AdminUI.post("Advance", { id: order.id, expectedStatus: order.status });
            order.status = result.status;
            AdminUI.showToast(`Order ${order.displayId} moved to ${order.status}`);
            render();
        } catch (error) {
            const order = error.status === 409 ? orders.find(o => o.id === error.data?.id) : null;
            if (order) {
                order.status = error.data.status;
                render();
            } else {
                AdminUI.setBusy(btn, false);
            }
            AdminUI.showError(error);
        }
    });

    document.getElementById("order-search").addEventListener("input", (e) => {
        searchTerm = e.target.value.trim();
        render();
    });

    refreshButton.addEventListener("click", load);

    // Bootstrap only moves the "active" class between tabs, so the dark pill follows the selected tab here.
    document.getElementById("order-tabs").addEventListener("shown.bs.tab", () => {
        for (const tab of document.querySelectorAll("#order-tabs [data-bs-toggle=tab]")) {
            tab.classList.toggle("btn-dark", tab.classList.contains("active"));
        }
    });

    load();
})();
