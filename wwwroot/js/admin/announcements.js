(() => {
    const STATUS_COLORS = { Active: "success", Hidden: "secondary", Expired: "danger" };
    const DAY_MS = 24 * 60 * 60 * 1000;

    let announcements = [];

    const listEl = document.getElementById("announcement-list");
    const statsEl = document.getElementById("ann-stats");
    const searchEl = document.getElementById("ann-search");
    const categoryFilterEl = document.getElementById("ann-category-filter");
    const statusFilterEl = document.getElementById("ann-status-filter");

    const form = document.getElementById("announcement-form");
    const formError = document.getElementById("ann-form-error");
    const submitBtn = document.getElementById("ann-submit");
    const idInput = document.getElementById("ann-id");
    const titleInput = document.getElementById("ann-title");
    const descInput = document.getElementById("ann-description");
    const expiresInput = document.getElementById("ann-expires");
    const activeInput = document.getElementById("ann-active");
    const charCount = document.getElementById("ann-char-count");
    const formModal = new bootstrap.Modal(document.getElementById("ann-modal"));
    const viewModal = new bootstrap.Modal(document.getElementById("ann-view-modal"));

    const statusOf = (a) => {
        if (!a.isActive) return "Hidden";
        if (a.isExpired) return "Expired";
        return "Active";
    };

    const formatDay = (isoDate) => AdminUI.formatDate(`${isoDate}T00:00:00`);

    const dateLine = (a) => {
        const end = a.lastDay ? `${a.isExpired ? "Ended" : "Shows until"} ${formatDay(a.lastDay)}` : "No end date";
        return `Posted ${AdminUI.formatDate(a.createdAt)} <span class="mx-1">|</span> ${end}`;
    };

    const statusBadge = (status) => {
        const color = STATUS_COLORS[status];
        return `<span class="badge rounded-pill bg-${color}-subtle text-${color}-emphasis">${status}</span>`;
    };

    const announcementRow = (a) => {
        const status = statusOf(a);
        const color = STATUS_COLORS[status];
        const title = AdminUI.escapeHtml(a.title);
        return `
            <div class="card bg-body border-0 rounded-4">
                <div class="card-body d-flex gap-3">
                    <span class="d-inline-flex align-items-center justify-content-center rounded-circle bg-${color}-subtle text-${color}-emphasis flex-shrink-0 align-self-start p-3">
                        <i class="${AdminUI.escapeHtml(a.icon)} fa-fw" aria-hidden="true"></i>
                    </span>
                    <div class="flex-grow-1 overflow-hidden">
                        <div class="d-flex flex-wrap justify-content-between align-items-start gap-2">
                            <h2 class="h6 fw-semibold mb-1">${title}</h2>
                            <div class="d-flex gap-1">
                                <span class="badge rounded-pill bg-body-tertiary text-body border">${AdminUI.escapeHtml(a.category)}</span>
                                ${statusBadge(status)}
                            </div>
                        </div>
                        <div class="small text-body-secondary">${dateLine(a)}</div>
                        <p class="small text-truncate mt-2 mb-0">${AdminUI.escapeHtml(a.description)}</p>
                        <div class="d-flex flex-wrap justify-content-end gap-2 mt-3">
                            <button type="button" class="btn btn-sm btn-light border" data-view="${a.id}">View</button>
                            <button type="button" class="btn btn-sm btn-light border" data-edit="${a.id}">Edit</button>
                            <button type="button" class="btn btn-sm btn-light border" data-toggle="${a.id}" aria-label="${a.isActive ? "Hide" : "Show"} ${title}">${a.isActive ? "Hide" : "Show"}</button>
                            <button type="button" class="btn btn-sm btn-light border text-danger" data-delete="${a.id}" aria-label="Delete ${title}">Delete</button>
                        </div>
                    </div>
                </div>
            </div>`;
    };

    const statRow = (label, note, count, color) => `
        <li class="list-group-item d-flex align-items-center gap-3 py-3">
            <i class="fa-solid fa-circle text-${color}" aria-hidden="true"></i>
            <div class="flex-grow-1">
                <div class="fw-medium">${label}</div>
                <div class="small text-body-secondary">${note}</div>
            </div>
            <span class="badge rounded-pill text-bg-dark">${count}</span>
        </li>`;

    const renderStats = () => {
        const now = Date.now();
        const count = (status) => announcements.filter(a => statusOf(a) === status).length;
        const endingSoon = announcements.filter(a => statusOf(a) === "Active" && a.lastDay
            && new Date(`${a.lastDay}T23:59:59`) - now <= 7 * DAY_MS).length;
        const postedThisWeek = announcements.filter(a => now - new Date(a.createdAt) <= 7 * DAY_MS).length;

        statsEl.innerHTML = [
            statRow("Active", "Customers can see these", count("Active"), "success"),
            statRow("Ending soon", "Active, ends within 7 days", endingSoon, "warning"),
            statRow("Hidden", "Turned off by staff", count("Hidden"), "secondary"),
            statRow("Expired", "Past their end date", count("Expired"), "danger"),
            statRow("Posted this week", "In the last 7 days", postedThisWeek, "info"),
            statRow("Total", "All announcements", announcements.length, "dark")
        ].join("");
        document.getElementById("ann-updated").textContent = `Last updated ${AdminUI.formatTime(new Date())}`;
    };

    const showListMessage = (text, tone = "text-body-secondary") => {
        listEl.innerHTML = `<div class="bg-body rounded-4 text-center ${tone} py-5">${AdminUI.escapeHtml(text)}</div>`;
    };

    const render = () => {
        renderStats();

        if (announcements.length === 0) {
            showListMessage("No announcements yet. Use \"New announcement\" to post the first one.");
            return;
        }

        const term = searchEl.value.trim().toLowerCase();
        const visible = announcements.filter(a =>
            (!term || a.title.toLowerCase().includes(term)) &&
            (!categoryFilterEl.value || a.category === categoryFilterEl.value) &&
            (!statusFilterEl.value || statusOf(a) === statusFilterEl.value));

        if (visible.length === 0) {
            showListMessage("No announcements match these filters.");
            return;
        }
        listEl.innerHTML = visible.map(announcementRow).join("");
    };

    const load = () => {
        showListMessage("Loading...");
        AdminUI.get("List")
            .then(data => { announcements = data; render(); })
            .catch(error => { showListMessage(error.message, "text-danger"); AdminUI.showError(error); });
    };

    const updateCharCount = () => {
        charCount.textContent = `${descInput.value.length} / ${descInput.maxLength}`;
    };

    // Opens the form empty for a new announcement, or filled in for editing.
    const openForm = (a) => {
        form.reset();
        form.classList.remove("was-validated");
        formError.classList.add("d-none");

        idInput.value = a?.id ?? "";
        titleInput.value = a?.title ?? "";
        descInput.value = a?.description ?? "";
        expiresInput.value = a?.lastDay ?? "";
        expiresInput.min = a ? "" : form.dataset.today;
        activeInput.checked = a?.isActive ?? true;
        const categoryInput = [...form.querySelectorAll("input[name='ann-category']")].find(input => input.value === a?.category);
        if (categoryInput) categoryInput.checked = true;
        updateCharCount();

        document.getElementById("ann-modal-title").textContent = a ? "Edit announcement" : "New announcement";
        submitBtn.textContent = a ? "Save changes" : "Post announcement";
        formModal.show();
    };

    const showDetails = (a) => {
        const status = statusOf(a);
        const color = STATUS_COLORS[status];
        document.getElementById("ann-view-title").textContent = a.title;
        document.getElementById("ann-view-body").innerHTML = `
            <div class="d-flex align-items-center gap-2 mb-3">
                <span class="rounded-circle bg-${color}-subtle text-${color}-emphasis p-2"><i class="${AdminUI.escapeHtml(a.icon)} fa-fw"></i></span>
                <span class="badge rounded-pill bg-body-tertiary text-body border">${AdminUI.escapeHtml(a.category)}</span>
                ${statusBadge(status)}
            </div>
            <p class="mb-3">${AdminUI.escapeHtml(a.description).replace(/\n/g, "<br>")}</p>
            <div class="small text-body-secondary">${dateLine(a)}</div>`;
        viewModal.show();
    };

    const replaceAnnouncement = (updated) => {
        const index = announcements.findIndex(a => a.id === updated.id);
        if (index >= 0) announcements[index] = updated; else announcements.unshift(updated);
    };

    document.getElementById("btn-new-announcement").addEventListener("click", () => openForm(null));
    descInput.addEventListener("input", updateCharCount);

    form.addEventListener("submit", async (e) => {
        e.preventDefault();
        formError.classList.add("d-none");
        form.classList.add("was-validated");
        if (!form.checkValidity()) return;

        const editingId = idInput.value;
        AdminUI.setBusy(submitBtn, true);
        try {
            const saved = await AdminUI.post(editingId ? "Update" : "Create", {
                id: editingId || null,
                title: titleInput.value.trim(),
                description: descInput.value.trim(),
                category: form.querySelector("input[name='ann-category']:checked").value,
                isActive: activeInput.checked,
                expiresAt: expiresInput.value || null
            });
            replaceAnnouncement(saved);
            formModal.hide();
            AdminUI.showToast(editingId ? "Announcement updated" : "Announcement posted");
            render();
        } catch (error) {
            formError.textContent = error.message;
            formError.classList.remove("d-none");
        } finally {
            AdminUI.setBusy(submitBtn, false);
        }
    });

    const toggleAnnouncement = async (button) => {
        AdminUI.setBusy(button, true);
        try {
            const updated = await AdminUI.post("Toggle", { id: button.dataset.toggle });
            replaceAnnouncement(updated);
            AdminUI.showToast(updated.isActive ? "Announcement is now visible" : "Announcement hidden");
            render();
        } catch (error) {
            AdminUI.setBusy(button, false);
            AdminUI.showError(error);
        }
    };

    const deleteAnnouncement = async (button) => {
        if (!confirm("Delete this announcement?")) return;

        AdminUI.setBusy(button, true);
        try {
            await AdminUI.post("Delete", { id: button.dataset.delete });
            announcements = announcements.filter(a => a.id !== button.dataset.delete);
            AdminUI.showToast("Announcement deleted");
            render();
        } catch (error) {
            AdminUI.setBusy(button, false);
            AdminUI.showError(error);
        }
    };

    listEl.addEventListener("click", (e) => {
        const button = e.target.closest("button");
        if (!button) return;

        const find = (id) => announcements.find(a => a.id === id);
        if (button.dataset.view) showDetails(find(button.dataset.view));
        else if (button.dataset.edit) openForm(find(button.dataset.edit));
        else if (button.dataset.toggle) toggleAnnouncement(button);
        else if (button.dataset.delete) deleteAnnouncement(button);
    });

    searchEl.addEventListener("input", render);
    categoryFilterEl.addEventListener("change", render);
    statusFilterEl.addEventListener("change", render);

    load();
})();
