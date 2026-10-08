(() => {
    let announcements = [];

    const form = document.getElementById("announcement-form");
    const formError = document.getElementById("ann-form-error");
    const submitBtn = document.getElementById("ann-submit");
    const titleInput = document.getElementById("ann-title");
    const descInput = document.getElementById("ann-description");
    const expiresInput = document.getElementById("ann-expires");
    const activeInput = document.getElementById("ann-active");
    const charCount = document.getElementById("ann-char-count");
    const listEl = document.getElementById("announcement-list");
    const messageEl = document.getElementById("ann-message");

    const showMessage = (text, tone = "text-muted") => {
        messageEl.className = `text-center ${tone} py-4 mb-0`;
        messageEl.textContent = text;
    };

    const render = () => {
        const activeCount = announcements.filter(a => a.isActive && !a.isExpired).length;
        document.getElementById("ann-count").textContent = `${announcements.length} total, ${activeCount} active`;

        if (announcements.length === 0) {
            listEl.innerHTML = "";
            showMessage("No announcements yet. Post one using the form.");
            return;
        }
        messageEl.classList.add("d-none");

        listEl.innerHTML = announcements.map(a => `
            <li class="list-group-item d-flex gap-3 py-3">
                <i class="${AdminUI.escapeHtml(a.icon)} fs-4 text-secondary announcement-icon" aria-hidden="true"></i>
                <div class="flex-grow-1">
                    <div class="d-flex align-items-center gap-2 flex-wrap">
                        <h6 class="mb-0">${AdminUI.escapeHtml(a.title)}</h6>
                        ${AdminUI.statusBadge(!a.isActive ? "Hidden" : a.isExpired ? "Expired" : "Active")}
                    </div>
                    <p class="mb-1 text-muted announcement-text">${AdminUI.escapeHtml(a.description)}</p>
                    <small class="text-muted">Posted ${AdminUI.formatDate(a.createdAt)}${a.lastDay ? ` &middot; ${a.isExpired ? "Ended" : "Shows until"} ${AdminUI.formatDate(`${a.lastDay}T00:00:00`)}` : ""}</small>
                </div>
                <div class="d-flex flex-column gap-1">
                    <button type="button" class="btn btn-sm btn-outline-secondary btn-icon" data-toggle="${a.id}" title="${a.isActive ? "Hide" : "Publish"}" aria-label="${a.isActive ? "Hide" : "Publish"} ${AdminUI.escapeHtml(a.title)}">
                        <i class="fa-regular ${a.isActive ? "fa-eye-slash" : "fa-eye"}"></i>
                    </button>
                    <button type="button" class="btn btn-sm btn-outline-danger btn-icon" data-delete="${a.id}" title="Delete" aria-label="Delete ${AdminUI.escapeHtml(a.title)}">
                        <i class="fa-solid fa-trash"></i>
                    </button>
                </div>
            </li>`).join("");
    };

    const load = () => {
        showMessage("Loading...");
        AdminUI.get("List")
            .then(data => { announcements = data; render(); })
            .catch(error => { showMessage(error.message, "text-danger"); AdminUI.showError(error); });
    };

    descInput.addEventListener("input", () => {
        charCount.textContent = `${descInput.value.length} / ${descInput.maxLength}`;
    });

    form.addEventListener("submit", async (e) => {
        e.preventDefault();
        formError.classList.add("d-none");
        form.classList.add("was-validated");
        if (!form.checkValidity()) return;

        AdminUI.setBusy(submitBtn, true);
        try {
            const created = await AdminUI.post("Create", {
                title: titleInput.value.trim(),
                description: descInput.value.trim(),
                icon: form.querySelector("input[name='ann-icon']:checked").value,
                isActive: activeInput.checked,
                expiresAt: expiresInput.value || null
            });
            announcements.unshift(created);
            form.reset();
            form.classList.remove("was-validated");
            charCount.textContent = `0 / ${descInput.maxLength}`;
            AdminUI.showToast("Announcement posted");
            render();
        } catch (error) {
            formError.textContent = error.message;
            formError.classList.remove("d-none");
        } finally {
            AdminUI.setBusy(submitBtn, false);
        }
    });

    listEl.addEventListener("click", async (e) => {
        const toggleBtn = e.target.closest("[data-toggle]");
        const deleteBtn = e.target.closest("[data-delete]");
        const button = toggleBtn ?? deleteBtn;
        if (!button) return;
        if (deleteBtn && !confirm("Delete this announcement?")) return;

        AdminUI.setBusy(button, true);
        try {
            if (toggleBtn) {
                const updated = await AdminUI.post("Toggle", { id: toggleBtn.dataset.toggle });
                announcements = announcements.map(a => a.id === updated.id ? updated : a);
                AdminUI.showToast(updated.isActive ? "Announcement published" : "Announcement hidden");
            } else {
                await AdminUI.post("Delete", { id: deleteBtn.dataset.delete });
                announcements = announcements.filter(a => a.id !== deleteBtn.dataset.delete);
                AdminUI.showToast("Announcement deleted");
            }
            render();
        } catch (error) {
            AdminUI.setBusy(button, false);
            AdminUI.showError(error);
        }
    });

    load();
})();
