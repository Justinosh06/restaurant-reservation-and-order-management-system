// Mock data - replace with API calls once the backend is ready.
const ORDER_STATUSES = ["Pending", "Preparing", "Served"];

const NEXT_ACTION = {
    Pending: { label: "Start preparing", icon: "fa-solid fa-fire-burner" },
    Preparing: { label: "Mark served", icon: "fa-solid fa-bell-concierge" }
};

const orders = [
    { id: "ORD-1064", table: "T05", customer: "Aisyah Rahman", items: ["Carbonara x2", "Iced Lemon Tea x2"], time: "19:42", total: 8650, status: "Pending" },
    { id: "ORD-1063", table: "T12", customer: "Jason Lim", items: ["Bolognese x3", "Garlic Bread x1"], time: "19:35", total: 12400, status: "Preparing" },
    { id: "ORD-1062", table: "T03", customer: "Priya Nair", items: ["Aglio Olio x1", "Latte x1"], time: "19:31", total: 5600, status: "Preparing" },
    { id: "ORD-1061", table: "T08", customer: "Wei Jie Tan", items: ["Pesto Penne x2", "Carbonara x2", "Mocha x2"], time: "19:20", total: 15890, status: "Served" },
    { id: "ORD-1060", table: "T01", customer: "Nurul Huda", items: ["Aglio Olio x1"], time: "19:12", total: 4300, status: "Served" },
    { id: "ORD-1059", table: "T07", customer: "Daniel Wong", items: ["Bolognese x1", "Iced Lemon Tea x1"], time: "19:05", total: 4900, status: "Pending" },
    { id: "ORD-1058", table: "T10", customer: "Siti Aminah", items: ["Carbonara x1", "Pesto Penne x1"], time: "18:58", total: 7800, status: "Served" }
];

(() => {
    let activeFilter = "All";
    let searchTerm = "";

    const filterEl = document.getElementById("status-filter");
    const bodyEl = document.getElementById("orders-body");
    const emptyEl = document.getElementById("orders-empty");

    const renderFilters = () => {
        const counts = { All: orders.length };
        ORDER_STATUSES.forEach(s => counts[s] = orders.filter(o => o.status === s).length);

        filterEl.innerHTML = ["All", ...ORDER_STATUSES].map(s => `
            <button type="button" role="tab" class="chip ${s === activeFilter ? "active" : ""}" data-filter="${s}" aria-selected="${s === activeFilter}">
                ${s}<span class="chip-count">${counts[s]}</span>
            </button>`).join("");
    };

    const renderProgress = (status) => {
        const reached = ORDER_STATUSES.indexOf(status);
        return `<div class="order-progress" title="${status}">` +
            ORDER_STATUSES.map((_, i) => `<span class="step ${i <= reached ? "done" : ""}"></span>`).join("") +
            `</div>`;
    };

    const renderOrders = () => {
        const term = searchTerm.toLowerCase();
        const visible = orders.filter(o =>
            (activeFilter === "All" || o.status === activeFilter) &&
            (!term || [o.id, o.table, o.customer].some(v => v.toLowerCase().includes(term))));

        emptyEl.classList.toggle("d-none", visible.length > 0);

        bodyEl.innerHTML = visible.map(o => {
            const next = NEXT_ACTION[o.status];
            const action = next
                ? `<button type="button" class="btn btn-sm btn-admin" data-advance="${o.id}"><i class="${next.icon} me-1"></i>${next.label}</button>`
                : `<span class="text-muted small"><i class="fa-solid fa-check me-1"></i>Done</span>`;

            return `
                <tr>
                    <td class="fw-medium">${AdminUI.escapeHtml(o.id)}</td>
                    <td>${AdminUI.escapeHtml(o.table)}</td>
                    <td>${AdminUI.escapeHtml(o.customer)}</td>
                    <td class="small text-muted">${o.items.map(AdminUI.escapeHtml).join("<br>")}</td>
                    <td>${AdminUI.escapeHtml(o.time)}</td>
                    <td class="text-nowrap">${AdminUI.formatCurrency(o.total)}</td>
                    <td>${renderProgress(o.status)}</td>
                    <td><span class="status-pill ${o.status.toLowerCase()}">${o.status}</span></td>
                    <td class="text-end text-nowrap">${action}</td>
                </tr>`;
        }).join("");
    };

    const render = () => { renderFilters(); renderOrders(); };

    filterEl.addEventListener("click", (e) => {
        const btn = e.target.closest("[data-filter]");
        if (!btn) return;
        activeFilter = btn.dataset.filter;
        render();
    });

    bodyEl.addEventListener("click", (e) => {
        const btn = e.target.closest("[data-advance]");
        if (!btn) return;
        const order = orders.find(o => o.id === btn.dataset.advance);
        const nextIndex = ORDER_STATUSES.indexOf(order.status) + 1;
        if (nextIndex >= ORDER_STATUSES.length) return;
        order.status = ORDER_STATUSES[nextIndex];
        AdminUI.showToast(`${order.id} moved to ${order.status}`);
        render();
    });

    document.getElementById("order-search").addEventListener("input", (e) => {
        searchTerm = e.target.value.trim();
        renderOrders();
    });

    render();
})();
