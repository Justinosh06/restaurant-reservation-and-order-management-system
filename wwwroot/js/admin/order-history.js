(() => {
    const PAGE_SIZE = 12;
    const ITEMS_SHOWN = 4;

    // Each entry is one visit (dining session) at a table, with all of its orders combined.
    let visits = [];
    let shownCount = PAGE_SIZE;

    const form = document.getElementById("history-form");
    const fromInput = document.getElementById("history-from");
    const toInput = document.getElementById("history-to");
    const searchInput = document.getElementById("history-search");
    const countEl = document.getElementById("history-count");
    const gridEl = document.getElementById("history-grid");
    const moreButton = document.getElementById("history-more");
    const orderModal = new bootstrap.Modal(document.getElementById("order-modal"));
    const paymentModal = new bootstrap.Modal(document.getElementById("payment-modal"));
    const paymentModalBody = document.getElementById("payment-modal-body");

    // Every order in the history has been served and paid for.
    const paidBadge = `<span class="badge rounded-pill border bg-success-subtle text-success-emphasis border-success-subtle">
        <i class="fa-solid fa-check me-1"></i>Paid</span>`;

    const itemRows = (items) => items.map(item => `
        <tr>
            <td class="ps-0">${AdminUI.escapeHtml(item.name)}</td>
            <td class="text-center">${item.quantity}</td>
            <td class="text-end pe-0">${AdminUI.formatCurrency(item.unitPrice * item.quantity)}</td>
        </tr>`).join("");

    const orderLabel = (o) => o.orderIds.length === 1
        ? `Order #${AdminUI.escapeHtml(o.orderIds[0])}`
        : `${o.orderIds.length} orders`;

    const timeRange = (o) => o.startedAt === o.lastOrderAt
        ? AdminUI.formatTime(o.startedAt)
        : `${AdminUI.formatTime(o.startedAt)} - ${AdminUI.formatTime(o.lastOrderAt)}`;

    const priceBreakdown = (o) => {
        const hasDiscount = o.promoDiscount > 0;
        const hasDepositCredit = o.reservationDepositCredit > 0;
        const amountDue = Math.max(0, o.total - o.promoDiscount - o.reservationDepositCredit);

        if (!hasDiscount && !hasDepositCredit) {
            return `<div class="d-flex justify-content-between fw-semibold border-top pt-2 mt-auto">
                <span>Total</span><span>${AdminUI.formatCurrency(o.total)}</span>
            </div>`;
        }

        return `<div class="border-top pt-2 mt-auto small">
            <div class="d-flex justify-content-between">
                <span>Orders total</span><span>${AdminUI.formatCurrency(o.total)}</span>
            </div>
            ${hasDiscount ? `<div class="d-flex justify-content-between text-success">
                <span>Promo code${o.promotionCode ? ` ${AdminUI.escapeHtml(o.promotionCode)}` : ""}</span>
                <span>-${AdminUI.formatCurrency(o.promoDiscount)}</span>
            </div>` : ""}
            ${hasDepositCredit ? `<div class="d-flex justify-content-between text-body-secondary">
                <span>Reservation deposit</span><span>-${AdminUI.formatCurrency(o.reservationDepositCredit)}</span>
            </div>` : ""}
            <div class="d-flex justify-content-between fw-semibold border-top pt-2 mt-2">
                <span>Amount due</span><span>${AdminUI.formatCurrency(amountDue)}</span>
            </div>
        </div>`;
    };

    const priceBreakdownTableRows = (o) => {
        const hasDiscount = o.promoDiscount > 0;
        const hasDepositCredit = o.reservationDepositCredit > 0;
        if (!hasDiscount && !hasDepositCredit) {
            return `<tr class="fw-semibold"><td class="ps-0" colspan="2">Total</td><td class="text-end pe-0">${AdminUI.formatCurrency(o.total)}</td></tr>`;
        }

        return `<tr><td class="ps-0" colspan="2">Orders total</td><td class="text-end pe-0">${AdminUI.formatCurrency(o.total)}</td></tr>
            ${hasDiscount ? `<tr class="text-success"><td class="ps-0" colspan="2">Promo code${o.promotionCode ? ` ${AdminUI.escapeHtml(o.promotionCode)}` : ""}</td><td class="text-end pe-0">-${AdminUI.formatCurrency(o.promoDiscount)}</td></tr>` : ""}
            ${hasDepositCredit ? `<tr class="text-body-secondary"><td class="ps-0" colspan="2">Reservation deposit</td><td class="text-end pe-0">-${AdminUI.formatCurrency(o.reservationDepositCredit)}</td></tr>` : ""}
            <tr class="fw-semibold"><td class="ps-0" colspan="2">Amount due</td><td class="text-end pe-0">${AdminUI.formatCurrency(Math.max(0, o.total - o.promoDiscount - o.reservationDepositCredit))}</td></tr>`;
    };

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
                                <div class="small text-body-secondary">${orderLabel(o)} &middot; Table ${AdminUI.escapeHtml(o.table ?? "-")}</div>
                            </div>
                            ${paidBadge}
                        </div>

                        <div class="d-flex justify-content-between small text-body-secondary border-bottom pb-2 my-3">
                            <span>${AdminUI.formatDate(o.startedAt)}</span>
                            <span>${timeRange(o)}</span>
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

                        ${priceBreakdown(o)}
                        <div class="d-flex gap-2 mt-3">
                            <button type="button" class="btn btn-sm btn-light border flex-fill" data-details="${o.id}">View orders</button>
                            <button type="button" class="btn btn-sm btn-outline-secondary flex-fill" data-payment-details="${o.id}">
                                <i class="fa-solid fa-credit-card me-1" aria-hidden="true"></i>Payment details
                            </button>
                        </div>
                    </div>
                </div>
            </div>`;
    };

    const showDetails = (o) => {
        document.getElementById("order-modal-title").textContent = `Table ${o.table ?? "-"}`;
        const remarks = o.items.filter(item => item.remarks);
        document.getElementById("order-modal-body").innerHTML = `
            <div class="d-flex justify-content-between align-items-center mb-3">
                ${paidBadge}
                <span class="small text-body-secondary">${AdminUI.formatDate(o.startedAt)}, ${timeRange(o)}</span>
            </div>
            <dl class="row small mb-3">
                <dt class="col-4 fw-normal text-body-secondary">Customer</dt><dd class="col-8 text-break">${AdminUI.escapeHtml(o.customer)}</dd>
                <dt class="col-4 fw-normal text-body-secondary">Table</dt><dd class="col-8">${AdminUI.escapeHtml(o.table ?? "-")}</dd>
                <dt class="col-4 fw-normal text-body-secondary">${o.orderIds.length === 1 ? "Order" : "Orders"}</dt><dd class="col-8">${o.orderIds.map(id => `#${AdminUI.escapeHtml(id)}`).join(", ")}</dd>
            </dl>
            <table class="table table-sm small mb-2">
                <thead><tr><th class="ps-0">Items</th><th class="text-center">Qty</th><th class="text-end pe-0">Price</th></tr></thead>
                <tbody>${itemRows(o.items)}</tbody>
                <tfoot>${priceBreakdownTableRows(o)}</tfoot>
            </table>
            ${remarks.length ? `
                <div class="small fw-semibold mb-1">Remarks</div>
                ${remarks.map(item => `<div class="bg-body-tertiary rounded-2 small px-2 py-1 mb-1">${AdminUI.escapeHtml(item.name)}: ${AdminUI.escapeHtml(item.remarks)}</div>`).join("")}` : ""}`;
        orderModal.show();
    };

    const paymentRows = (payment) => {
        const rows = [
            ["fa-solid fa-credit-card", "Payment method", payment.methodLabel],
            ["fa-regular fa-user", "Customer", payment.customerName],
            payment.customerEmail && ["fa-regular fa-envelope", "Email", payment.customerEmail],
            payment.checkoutSessionId && ["fa-solid fa-receipt", "Stripe Checkout ID", payment.checkoutSessionId],
            payment.paymentIntentId && ["fa-solid fa-credit-card", "Payment intent", payment.paymentIntentId],
            payment.paidAtUtc && ["fa-regular fa-calendar", "Paid on", new Date(payment.paidAtUtc).toLocaleString("en-MY")],
            payment.refundedAtUtc && ["fa-solid fa-arrow-rotate-left", "Refunded on", new Date(payment.refundedAtUtc).toLocaleString("en-MY")]
        ].filter(Boolean);

        return rows.map(([icon, label, value]) => `
            <div class="d-flex gap-2 justify-content-between border-top py-2 small">
                <span class="text-body-secondary"><i class="${AdminUI.escapeHtml(icon)} me-1" aria-hidden="true"></i>${AdminUI.escapeHtml(label)}</span>
                <span class="text-end text-break">${AdminUI.escapeHtml(value)}</span>
            </div>`).join("");
    };

    const renderPayments = (payments) => {
        if (payments.length === 0) {
            paymentModalBody.innerHTML = `<p class="text-body-secondary mb-0">No payment records were found for this dining session.</p>`;
            return;
        }

        paymentModalBody.innerHTML = payments.map(payment => {
            const isRefunded = payment.state === "Refunded";
            let receiptLink = "";
            if (typeof payment.receiptUrl === "string" && payment.receiptUrl.startsWith("https://")) {
                receiptLink = `<a class="btn btn-sm btn-light border w-100 mt-3" href="${AdminUI.escapeHtml(payment.receiptUrl)}" target="_blank" rel="noopener noreferrer">
                    View receipt <i class="fa-solid fa-arrow-up-right-from-square ms-1" aria-hidden="true"></i>
                </a>`;
            }

            return `<section class="border rounded-4 p-3 mb-3">
                <div class="d-flex justify-content-between align-items-center gap-2 mb-2">
                    <span class="small text-body-secondary fw-medium">Payment</span>
                    <span class="badge rounded-pill ${isRefunded ? "text-bg-secondary" : "bg-success-subtle text-success-emphasis"}">${AdminUI.escapeHtml(payment.state)}</span>
                </div>
                <div class="fs-4 fw-semibold mb-3">${AdminUI.escapeHtml(payment.currencySymbol)}${(payment.amountCents / 100).toLocaleString("en-MY", { minimumFractionDigits: 2, maximumFractionDigits: 2 })}</div>
                ${paymentRows(payment)}
                ${receiptLink}
            </section>`;
        }).join("");
    };

    const showPaymentDetails = async (sessionId, button) => {
        paymentModalBody.innerHTML = `<div class="text-center text-body-secondary py-4"><span class="spinner-border spinner-border-sm me-2" aria-hidden="true"></span>Loading payment details...</div>`;
        paymentModal.show();
        AdminUI.setBusy(button, true);

        try {
            const payments = await AdminUI.get("PaymentDetails", { id: sessionId });
            renderPayments(payments);
        } catch (error) {
            paymentModalBody.innerHTML = `<p class="text-danger mb-0">${AdminUI.escapeHtml(error.message)}</p>`;
            AdminUI.showError(error);
        } finally {
            AdminUI.setBusy(button, false);
        }
    };

    const showGridMessage = (text, tone = "text-body-secondary") => {
        gridEl.innerHTML = `<div class="col-12"><div class="bg-body rounded-4 text-center ${tone} py-5">${AdminUI.escapeHtml(text)}</div></div>`;
        moreButton.classList.add("d-none");
    };

    const matchesSearch = (o) => {
        const term = searchInput.value.trim().toLowerCase();
        return !term || [...o.orderIds, o.table, o.customer].some(v => (v ?? "").toLowerCase().includes(term));
    };

    const render = () => {
        const visible = visits.filter(matchesSearch);
        countEl.textContent = `· ${visible.length}`;

        if (visible.length === 0) {
            showGridMessage(visits.length ? "No orders match this search." : "No paid orders for these dates.");
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
            .then(data => { visits = data; render(); })
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
        const paymentButton = e.target.closest("[data-payment-details]");
        if (paymentButton) {
            showPaymentDetails(paymentButton.dataset.paymentDetails, paymentButton);
            return;
        }

        const button = e.target.closest("[data-details]");
        if (button) showDetails(visits.find(o => o.id === button.dataset.details));
    });

    document.getElementById("history-tab").addEventListener("shown.bs.tab", loadHistory, { once: true });
})();
