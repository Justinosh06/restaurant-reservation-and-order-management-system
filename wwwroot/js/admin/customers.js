(() => {
    let customers = [];
    let page = 1;

    const rowsEl = document.getElementById("customer-rows");
    const countEl = document.getElementById("customer-count");
    const rangeEl = document.getElementById("customer-range");
    const pagesEl = document.getElementById("customer-pages");
    const pageSizeEl = document.getElementById("customer-page-size");
    const searchEl = document.getElementById("customer-search");
    const activityFilterEl = document.getElementById("customer-activity-filter");
    const sortEl = document.getElementById("customer-sort");
    const detailEl = document.getElementById("customer-detail");
    const detailTitleEl = document.getElementById("customer-modal-title");
    const customerModal = new bootstrap.Modal(document.getElementById("customer-modal"));

    const COLUMNS = 9;
    const AVATAR_COLORS = ["primary", "success", "info", "warning", "danger", "secondary"];
    const ACTIVITY_FILTERS = {
        booked: c => c.reservations > 0,
        ordered: c => c.orders > 0,
        inactive: c => c.reservations === 0 && c.orders === 0
    };
    const SORTS = {
        name: (a, b) => (a.name || a.email).localeCompare(b.name || b.email),
        spent: (a, b) => b.spent - a.spent,
        lastVisit: (a, b) => (b.lastVisit ?? "").localeCompare(a.lastVisit ?? "")
    };

    const notGiven = `<span class="text-body-secondary">Not given</span>`;

    // Initials in a coloured circle; the colour comes from the customer ID, so it stays the same on every visit.
    const avatar = (c) => {
        const initials = (c.name || c.email).split(/\s+/).filter(Boolean).slice(0, 2).map(word => word[0]).join("").toUpperCase();
        const hash = [...c.id].reduce((sum, ch) => sum + ch.charCodeAt(0), 0);
        const color = AVATAR_COLORS[hash % AVATAR_COLORS.length];
        return `<span class="customer-avatar bg-${color}-subtle text-${color}-emphasis" aria-hidden="true">${AdminUI.escapeHtml(initials)}</span>`;
    };

    const customerRow = (c, number) => {
        const email = AdminUI.escapeHtml(c.email);
        return `
            <tr>
                <td class="ps-3 text-body-secondary">${number}</td>
                <td>
                    <div class="d-flex align-items-center gap-2">
                        ${avatar(c)}
                        <div class="text-truncate">
                            <div class="fw-medium text-truncate">${c.name ? AdminUI.escapeHtml(c.name) : `<span class="text-body-secondary">No name</span>`}</div>
                            <div class="text-body-secondary text-truncate d-xl-none">${email}</div>
                        </div>
                    </div>
                </td>
                <td class="d-none d-xl-table-cell">${email}</td>
                <td class="d-none d-xxl-table-cell">${c.phone ? AdminUI.escapeHtml(c.phone) : notGiven}</td>
                <td class="text-end d-none d-xl-table-cell">${c.reservations}</td>
                <td class="text-end d-none d-xl-table-cell">${c.orders}</td>
                <td class="text-end text-nowrap">${AdminUI.formatCurrency(c.spent)}</td>
                <td class="d-none d-sm-table-cell text-nowrap">${c.lastVisit ? AdminUI.formatDate(c.lastVisit) : `<span class="text-body-secondary">Never</span>`}</td>
                <td class="text-end pe-3">
                    <button type="button" class="btn btn-sm btn-light border" data-view="${AdminUI.escapeHtml(c.id)}" aria-label="View ${email}" title="View details">
                        <i class="fa-regular fa-eye"></i>
                    </button>
                </td>
            </tr>`;
    };

    const pageButton = (label, target, { disabled = false, active = false, ariaLabel } = {}) => `
        <li class="page-item${disabled ? " disabled" : ""}${active ? " active" : ""}">
            <button type="button" class="page-link" data-page="${target}" ${disabled ? "disabled" : ""}
                    ${active ? `aria-current="page"` : ""} ${ariaLabel ? `aria-label="${ariaLabel}"` : ""}>${label}</button>
        </li>`;

    // Page numbers around the current page, at most five, so the bar fits on a phone.
    const renderPages = (pageCount) => {
        if (pageCount <= 1) {
            pagesEl.innerHTML = "";
            return;
        }
        const first = Math.max(1, Math.min(page - 2, pageCount - 4));
        const last = Math.min(pageCount, first + 4);
        const numbers = [];
        for (let n = first; n <= last; n++) {
            numbers.push(pageButton(n, n, { active: n === page }));
        }
        pagesEl.innerHTML = [
            pageButton(`<i class="fa-solid fa-angles-left"></i>`, 1, { disabled: page === 1, ariaLabel: "First page" }),
            pageButton(`<i class="fa-solid fa-angle-left"></i>`, page - 1, { disabled: page === 1, ariaLabel: "Previous page" }),
            ...numbers,
            pageButton(`<i class="fa-solid fa-angle-right"></i>`, page + 1, { disabled: page === pageCount, ariaLabel: "Next page" }),
            pageButton(`<i class="fa-solid fa-angles-right"></i>`, pageCount, { disabled: page === pageCount, ariaLabel: "Last page" })
        ].join("");
    };

    const render = () => {
        if (customers.length === 0) {
            countEl.textContent = "";
            rangeEl.textContent = "";
            pagesEl.innerHTML = "";
            rowsEl.innerHTML = AdminUI.tableMessageRow(COLUMNS, "No customers have signed up yet.");
            return;
        }

        const term = searchEl.value.trim().toLowerCase();
        const activity = ACTIVITY_FILTERS[activityFilterEl.value];
        const visible = customers
            .filter(c =>
                (!term || [c.name, c.email, c.phone].some(value => (value ?? "").toLowerCase().includes(term))) &&
                (!activity || activity(c)))
            .sort(SORTS[sortEl.value] ?? SORTS.name);

        countEl.textContent = `· ${customers.length} ${customers.length === 1 ? "customer" : "customers"}`;
        if (visible.length === 0) {
            rangeEl.textContent = "";
            pagesEl.innerHTML = "";
            rowsEl.innerHTML = AdminUI.tableMessageRow(COLUMNS, "No customers match these filters.");
            return;
        }

        const pageSize = Number(pageSizeEl.value);
        const pageCount = Math.ceil(visible.length / pageSize);
        page = Math.min(page, pageCount);
        const start = (page - 1) * pageSize;
        const shown = visible.slice(start, start + pageSize);

        rowsEl.innerHTML = shown.map((c, i) => customerRow(c, start + i + 1)).join("");
        rangeEl.textContent = `Showing ${start + 1} to ${start + shown.length} of ${visible.length} ${visible.length === 1 ? "customer" : "customers"}`;
        renderPages(pageCount);
    };

    // A new search, filter, sort or page size starts again from the first page.
    const restart = () => {
        page = 1;
        render();
    };

    const load = () => {
        rowsEl.innerHTML = AdminUI.tableMessageRow(COLUMNS, "Loading...");
        AdminUI.get("List")
            .then(data => { customers = data; render(); })
            .catch(error => {
                rowsEl.innerHTML = AdminUI.tableMessageRow(COLUMNS, error.message, "text-danger");
                AdminUI.showError(error);
            });
    };

    const historyTable = (headings, rows, emptyText) => rows.length === 0
        ? `<p class="small text-body-secondary mb-0">${emptyText}</p>`
        : `<div class="table-responsive">
               <table class="table align-middle small mb-0">
                   <thead><tr>${headings.map(([label, align]) => `<th class="text-body-secondary fw-medium ${align ?? ""}">${label}</th>`).join("")}</tr></thead>
                   <tbody>${rows.join("")}</tbody>
               </table>
           </div>`;

    const showDetail = (d) => {
        detailTitleEl.textContent = d.name || d.email;
        const field = (label, value) => `
            <div class="col-sm-6">
                <div class="small text-body-secondary">${label}</div>
                <div class="text-break">${value}</div>
            </div>`;

        const reservations = d.reservations.map(r => `
            <tr>
                <td>${AdminUI.escapeHtml(r.displayId)}</td>
                <td class="text-nowrap">${AdminUI.formatDate(r.date)}, ${AdminUI.escapeHtml(r.time)}</td>
                <td class="text-end">${r.pax}</td>
                <td>${AdminUI.statusBadge(r.status)}</td>
            </tr>`);
        const orders = d.orders.map(o => `
            <tr>
                <td>${AdminUI.escapeHtml(o.displayId)}</td>
                <td class="text-nowrap">${AdminUI.formatDate(o.createdAt)}, ${AdminUI.formatTime(o.createdAt)}</td>
                <td>${AdminUI.escapeHtml(o.table)}</td>
                <td class="text-end">${o.items}</td>
                <td>${AdminUI.statusBadge(o.status)}</td>
                <td class="text-end text-nowrap">${AdminUI.formatCurrency(o.total)}</td>
            </tr>`);

        detailEl.innerHTML = `
            <div class="row g-3 mb-4">
                ${field("Email", AdminUI.escapeHtml(d.email))}
                ${field("Phone", d.phone ? AdminUI.escapeHtml(d.phone) : notGiven)}
                ${field("Gender", d.gender ? AdminUI.escapeHtml(d.gender) : notGiven)}
                ${field("Date of birth", d.dateOfBirth ? AdminUI.formatDate(d.dateOfBirth) : notGiven)}
                ${field("Customer ID", AdminUI.escapeHtml(d.displayId))}
            </div>
            <h6 class="fw-semibold">Recent reservations</h6>
            <div class="mb-4">
                ${historyTable([["Booking"], ["Date"], ["Guests", "text-end"], ["Status"]], reservations, "No reservations yet.")}
            </div>
            <h6 class="fw-semibold">Recent orders</h6>
            ${historyTable([["Order"], ["Time"], ["Table"], ["Items", "text-end"], ["Status"], ["Total", "text-end"]], orders, "No orders yet.")}`;
    };

    const openDetail = (id) => {
        detailTitleEl.textContent = "Customer";
        detailEl.innerHTML = `<p class="text-body-secondary text-center py-4 mb-0">Loading...</p>`;
        customerModal.show();
        AdminUI.get("Detail", { id })
            .then(showDetail)
            .catch(error => {
                detailEl.innerHTML = `<p class="text-danger text-center py-4 mb-0">${AdminUI.escapeHtml(error.message)}</p>`;
            });
    };

    rowsEl.addEventListener("click", (e) => {
        const button = e.target.closest("[data-view]");
        if (button) openDetail(button.dataset.view);
    });

    pagesEl.addEventListener("click", (e) => {
        const button = e.target.closest("[data-page]");
        if (!button || button.disabled) return;
        page = Number(button.dataset.page);
        render();
    });

    searchEl.addEventListener("input", restart);
    for (const el of [activityFilterEl, sortEl, pageSizeEl]) {
        el.addEventListener("change", restart);
    }

    load();
})();
