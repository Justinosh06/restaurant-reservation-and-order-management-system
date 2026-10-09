(() => {
    const ORDER_STATUSES = ["Pending", "Preparing", "Served"];
    const NEXT_ACTION = {
        Pending: { label: "Start preparing", icon: "fa-solid fa-fire-burner" },
        Preparing: { label: "Mark served", icon: "fa-solid fa-bell-concierge" }
    };
    const COLUMN_COUNT = 8;

    let orders = [];
    let activeFilter = "All";
    let searchTerm = "";

    const filterEl = document.getElementById("status-filter");
    const bodyEl = document.getElementById("orders-body");

    const renderFilters = () => {
        const counts = { All: orders.length };
        ORDER_STATUSES.forEach(s => counts[s] = orders.filter(o => o.status === s).length);

        filterEl.innerHTML = ["All", ...ORDER_STATUSES].map(s => `
            <button type="button" class="btn ${s === activeFilter ? "btn-primary" : "btn-outline-primary"}" data-filter="${s}" aria-pressed="${s === activeFilter}">
                ${s} <span class="badge text-bg-secondary">${counts[s]}</span>
            </button>`).join("");
    };

    const renderOrders = () => {
        if (orders.length === 0) {
            bodyEl.innerHTML = AdminUI.tableMessageRow(COLUMN_COUNT, "No active orders right now. New orders will appear here.");
            return;
        }

        const term = searchTerm.toLowerCase();
        const visible = orders.filter(o =>
            (activeFilter === "All" || o.status === activeFilter) &&
            (!term || [o.displayId, o.table, o.customer].some(v => (v ?? "").toLowerCase().includes(term))));

        if (visible.length === 0) {
            bodyEl.innerHTML = AdminUI.tableMessageRow(COLUMN_COUNT, "No orders match this status or search. Try another status or clear the search box.");
            return;
        }

        bodyEl.innerHTML = visible.map(o => {
            const next = NEXT_ACTION[o.status];
            const action = next
                ? `<button type="button" class="btn btn-sm btn-primary w-100" data-advance="${o.id}"><i class="${next.icon} me-1"></i>${next.label}</button>`
                : `<span class="d-block text-center text-muted small"><i class="fa-solid fa-check me-1"></i>Done</span>`;

            return `
                <tr>
                    <td class="fw-medium text-truncate">${AdminUI.escapeHtml(o.displayId)}</td>
                    <td>${AdminUI.escapeHtml(o.table ?? "-")}</td>
                    <td class="text-truncate" title="${AdminUI.escapeHtml(o.customer)}">${AdminUI.escapeHtml(o.customer)}</td>
                    <td class="small text-muted">${o.items.map(AdminUI.escapeHtml).join("<br>") || "-"}</td>
                    <td class="text-nowrap">${AdminUI.formatTime(o.createdAt)}</td>
                    <td class="text-nowrap">${AdminUI.formatCurrency(o.total)}</td>
                    <td>${AdminUI.statusBadge(o.status)}</td>
                    <td>${action}</td>
                </tr>`;
        }).join("");
    };

    const render = () => { renderFilters(); renderOrders(); };

    const load = () => {
        bodyEl.innerHTML = AdminUI.tableMessageRow(COLUMN_COUNT, "Loading...");
        AdminUI.get("List")
            .then(data => { orders = data; render(); })
            .catch(error => {
                bodyEl.innerHTML = AdminUI.tableMessageRow(COLUMN_COUNT, error.message, "text-danger");
                AdminUI.showError(error);
            });
    };

    filterEl.addEventListener("click", (e) => {
        const btn = e.target.closest("[data-filter]");
        if (!btn) return;
        activeFilter = btn.dataset.filter;
        render();
    });

    bodyEl.addEventListener("click", async (e) => {
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
        renderOrders();
    });

    renderFilters();
    load();
})();
