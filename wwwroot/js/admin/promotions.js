(() => {
    const STATUS_COLORS = { Active: "success", Scheduled: "warning", Ended: "secondary" };
    const DAY_MS = 24 * 60 * 60 * 1000;

    let promotions = [];
    let selectedImage = null;
    let previewUrl = null;
    let removeSavedImage = false;

    const listEl = document.getElementById("promo-list");
    const statsEl = document.getElementById("promo-stats");
    const searchEl = document.getElementById("promo-search");
    const statusFilterEl = document.getElementById("promo-status-filter");

    const form = document.getElementById("promo-form");
    const formError = document.getElementById("promo-form-error");
    const submitBtn = document.getElementById("promo-submit");
    const idInput = document.getElementById("promo-id");
    const titleInput = document.getElementById("promo-title");
    const descInput = document.getElementById("promo-description");
    const startInput = document.getElementById("promo-start");
    const endInput = document.getElementById("promo-end");
    const fileInput = document.getElementById("promo-image");
    const dropEl = document.getElementById("image-drop");
    const imageError = document.getElementById("image-error");
    const removeImageBtn = document.getElementById("remove-image");
    const formModal = new bootstrap.Modal(document.getElementById("promo-modal"));
    const viewModal = new bootstrap.Modal(document.getElementById("promo-view-modal"));

    const dropPlaceholder = dropEl.innerHTML;
    const maxImageBytes = Number(fileInput.dataset.maxBytes);
    const allowedTypes = fileInput.accept.split(",").map(type => type.trim());

    const formatDay = (isoDate) => AdminUI.formatDate(`${isoDate}T00:00:00`);
    const dateRange = (p) => `${formatDay(p.startDate)} - ${formatDay(p.endDate)}`;

    const statusBadge = (status) => {
        const color = STATUS_COLORS[status];
        return `<span class="badge rounded-pill bg-${color}-subtle text-${color}-emphasis">${status}</span>`;
    };

    const thumbnail = (p) => p.imageUrl
        ? `<img src="${AdminUI.escapeHtml(p.imageUrl)}" width="120" height="80" class="rounded-3 object-fit-cover flex-shrink-0" alt="" />`
        : `<div class="rounded-3 bg-body-tertiary text-body-secondary d-flex align-items-center justify-content-center flex-shrink-0 align-self-start px-4 py-3"><i class="fa-regular fa-image fs-4"></i></div>`;

    const promotionRow = (p) => {
        const title = AdminUI.escapeHtml(p.title);
        return `
            <div class="card border-0 shadow-sm rounded-4">
                <div class="card-body d-flex flex-wrap flex-sm-nowrap gap-3">
                    ${thumbnail(p)}
                    <div class="flex-grow-1 overflow-hidden">
                        <div class="d-flex flex-wrap justify-content-between align-items-start gap-2">
                            <h2 class="h6 fw-semibold mb-1">${title}</h2>
                            ${statusBadge(p.status)}
                        </div>
                        <div class="small text-body-secondary"><i class="fa-regular fa-calendar me-1"></i>${dateRange(p)}</div>
                        <p class="small text-truncate mt-2 mb-0">${AdminUI.escapeHtml(p.description)}</p>
                        <div class="d-flex flex-wrap justify-content-end gap-2 mt-3">
                            <button type="button" class="btn btn-sm btn-light border" data-view="${p.id}">View</button>
                            <button type="button" class="btn btn-sm btn-light border" data-edit="${p.id}">Edit</button>
                            <button type="button" class="btn btn-sm btn-light border text-danger" data-delete="${p.id}" aria-label="Delete ${title}">Delete</button>
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
        const count = (status) => promotions.filter(p => p.status === status).length;
        const endingSoon = promotions.filter(p => p.status === "Active"
            && new Date(`${p.endDate}T23:59:59`) - now <= 7 * DAY_MS).length;
        const withoutImage = promotions.filter(p => !p.imageUrl).length;

        statsEl.innerHTML = [
            statRow("Active", "Running today", count("Active"), "success"),
            statRow("Ending soon", "Active, ends within 7 days", endingSoon, "danger"),
            statRow("Scheduled", "Starts on a later date", count("Scheduled"), "warning"),
            statRow("Ended", "Past their end date", count("Ended"), "secondary"),
            statRow("Without image", "Add one so it stands out", withoutImage, "info"),
            statRow("Total", "All promotions", promotions.length, "dark")
        ].join("");
        document.getElementById("promo-updated").textContent = `Last updated ${AdminUI.formatTime(new Date())}`;
    };

    const showListMessage = (text, tone = "text-body-secondary") => {
        listEl.innerHTML = `<div class="bg-body-tertiary rounded-4 text-center ${tone} py-5">${AdminUI.escapeHtml(text)}</div>`;
    };

    const render = () => {
        renderStats();

        if (promotions.length === 0) {
            showListMessage("No promotions yet. Use \"New promotion\" to create the first one.");
            return;
        }

        const term = searchEl.value.trim().toLowerCase();
        const visible = promotions.filter(p =>
            (!term || p.title.toLowerCase().includes(term)) &&
            (!statusFilterEl.value || p.status === statusFilterEl.value));

        if (visible.length === 0) {
            showListMessage("No promotions match these filters.");
            return;
        }
        listEl.innerHTML = visible.map(promotionRow).join("");
    };

    const load = () => {
        showListMessage("Loading...");
        AdminUI.get("List")
            .then(data => { promotions = data; render(); })
            .catch(error => { showListMessage(error.message, "text-danger"); AdminUI.showError(error); });
    };

    const showPreview = (url) => {
        dropEl.innerHTML = `<img src="${AdminUI.escapeHtml(url)}" class="object-fit-cover" alt="Promotion image preview" />`;
        removeImageBtn.classList.remove("d-none");
    };

    const clearImage = () => {
        if (previewUrl) URL.revokeObjectURL(previewUrl);
        selectedImage = null;
        previewUrl = null;
        fileInput.value = "";
        dropEl.innerHTML = dropPlaceholder;
        removeImageBtn.classList.add("d-none");
    };

    const loadImage = (file) => {
        imageError.textContent = "";
        if (!file) return;
        if (!allowedTypes.includes(file.type)) {
            imageError.textContent = "Please choose a JPG, PNG or WebP image.";
            return;
        }
        if (file.size > maxImageBytes) {
            imageError.textContent = `Image must be ${maxImageBytes / (1024 * 1024)} MB or smaller.`;
            return;
        }

        clearImage();
        selectedImage = file;
        previewUrl = URL.createObjectURL(file);
        showPreview(previewUrl);
    };

    // Opens the form empty for a new promotion, or filled in for editing.
    const openForm = (p) => {
        form.reset();
        form.classList.remove("was-validated");
        formError.classList.add("d-none");
        imageError.textContent = "";
        clearImage();
        removeSavedImage = false;

        idInput.value = p?.id ?? "";
        titleInput.value = p?.title ?? "";
        descInput.value = p?.description ?? "";
        startInput.value = p?.startDate ?? "";
        endInput.value = p?.endDate ?? "";
        endInput.min = startInput.value;
        if (p?.imageUrl) showPreview(p.imageUrl);

        document.getElementById("promo-modal-title").textContent = p ? "Edit promotion" : "New promotion";
        submitBtn.textContent = p ? "Save changes" : "Create promotion";
        formModal.show();
    };

    const showDetails = (p) => {
        document.getElementById("promo-view-title").textContent = p.title;
        document.getElementById("promo-view-body").innerHTML = `
            ${p.imageUrl ? `<img src="${AdminUI.escapeHtml(p.imageUrl)}" class="img-fluid rounded-3 mb-3 w-100" alt="" />` : ""}
            <div class="d-flex justify-content-between align-items-center mb-3">
                ${statusBadge(p.status)}
                <span class="small text-body-secondary"><i class="fa-regular fa-calendar me-1"></i>${dateRange(p)}</span>
            </div>
            <p class="mb-0">${AdminUI.escapeHtml(p.description).replace(/\n/g, "<br>")}</p>`;
        viewModal.show();
    };

    document.getElementById("btn-new-promotion").addEventListener("click", () => openForm(null));

    dropEl.addEventListener("click", () => fileInput.click());
    dropEl.addEventListener("keydown", (e) => {
        if (e.key === "Enter" || e.key === " ") { e.preventDefault(); fileInput.click(); }
    });
    fileInput.addEventListener("change", () => loadImage(fileInput.files[0]));
    removeImageBtn.addEventListener("click", () => {
        clearImage();
        removeSavedImage = true;
    });

    const highlightDropArea = (e) => {
        e.preventDefault();
        dropEl.classList.add("border-primary");
    };

    const unhighlightDropArea = (e) => {
        e.preventDefault();
        dropEl.classList.remove("border-primary");
    };

    dropEl.addEventListener("dragenter", highlightDropArea);
    dropEl.addEventListener("dragover", highlightDropArea);
    dropEl.addEventListener("dragleave", unhighlightDropArea);
    dropEl.addEventListener("drop", (e) => {
        unhighlightDropArea(e);
        loadImage(e.dataTransfer.files[0]);
    });

    startInput.addEventListener("change", () => { endInput.min = startInput.value; });

    form.addEventListener("submit", async (e) => {
        e.preventDefault();
        formError.classList.add("d-none");
        const endBeforeStart = startInput.value && endInput.value && endInput.value < startInput.value;
        endInput.setCustomValidity(endBeforeStart ? "End date must be on or after the start date." : "");

        form.classList.add("was-validated");
        if (!form.checkValidity()) return;

        const editingId = idInput.value;
        const data = new FormData();
        if (editingId) data.append("id", editingId);
        data.append("title", titleInput.value.trim());
        data.append("description", descInput.value.trim());
        data.append("startDate", startInput.value);
        data.append("endDate", endInput.value);
        data.append("removeImage", String(removeSavedImage));
        if (selectedImage) data.append("image", selectedImage);

        AdminUI.setBusy(submitBtn, true);
        try {
            const saved = await AdminUI.postForm(editingId ? "Update" : "Create", data);
            const index = promotions.findIndex(p => p.id === saved.id);
            if (index >= 0) promotions[index] = saved; else promotions.unshift(saved);
            formModal.hide();
            AdminUI.showToast(editingId ? "Promotion updated" : "Promotion created");
            render();
        } catch (error) {
            formError.textContent = error.message;
            formError.classList.remove("d-none");
        } finally {
            AdminUI.setBusy(submitBtn, false);
        }
    });

    const deletePromotion = async (button) => {
        if (!confirm("Delete this promotion?")) return;

        AdminUI.setBusy(button, true);
        try {
            await AdminUI.post("Delete", { id: button.dataset.delete });
            promotions = promotions.filter(p => p.id !== button.dataset.delete);
            AdminUI.showToast("Promotion deleted");
            render();
        } catch (error) {
            AdminUI.setBusy(button, false);
            AdminUI.showError(error);
        }
    };

    listEl.addEventListener("click", (e) => {
        const button = e.target.closest("button");
        if (!button) return;

        const find = (id) => promotions.find(p => p.id === id);
        if (button.dataset.view) showDetails(find(button.dataset.view));
        else if (button.dataset.edit) openForm(find(button.dataset.edit));
        else if (button.dataset.delete) deletePromotion(button);
    });

    searchEl.addEventListener("input", render);
    statusFilterEl.addEventListener("change", render);

    load();
})();
