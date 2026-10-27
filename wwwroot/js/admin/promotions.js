(() => {
    const STATUS_COLORS = { Active: "success", Scheduled: "warning", Ended: "secondary" };
    const DAY_MS = 24 * 60 * 60 * 1000;

    let promotions = [];
    let selectedImage = null;
    let previewUrl = null;
    let removeSavedImage = false;

    const listEl = document.getElementById("promo-list");
    const currencySymbol = document.getElementById("promotions-admin").dataset.currencySymbol;
    const emailsEnabled = document.getElementById("promotions-admin").dataset.emailsEnabled === "true";

    // Customers are emailed once, when a promotion starts (PromotionEmailWorker on the server).
    const emailNote = (p) => {
        if (!emailsEnabled) return "";
        if (p.status === "Active") return " and emailed to customers";
        if (p.status === "Scheduled") return `. Customers will be emailed on ${AdminUI.formatDate(p.startDate)}`;
        return "";
    };
    const statsEl = document.getElementById("promo-stats");
    const searchEl = document.getElementById("promo-search");
    const statusFilterEl = document.getElementById("promo-status-filter");

    const form = document.getElementById("promo-form");
    const formError = document.getElementById("promo-form-error");
    const submitBtn = document.getElementById("promo-submit");
    const idInput = document.getElementById("promo-id");
    const titleInput = document.getElementById("promo-title");
    const descInput = document.getElementById("promo-description");
    const codeInput = document.getElementById("promo-code");
    const discountTypeInput = document.getElementById("promo-discount-type");
    const discountValueInput = document.getElementById("promo-discount-value");
    const minSpendInput = document.getElementById("promo-min-spend");
    const audienceInput = document.getElementById("promo-audience");
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

    const formatCurrency = (amount) => `${currencySymbol} ${Number(amount).toFixed(2)}`;

    const audienceText = { Members: "members only", Birthday: "birthday month only" };

    // e.g. "20% off, min. spend $ 100.00, members only"; null for promotions made before discounts existed.
    const discountText = (p) => {
        if (!p.discountType) return null;
        const parts = [p.discountType === "Percentage" ? `${p.discountValue}% off` : `${formatCurrency(p.discountValue)} off`];
        if (p.minimumSpend > 0) parts.push(`min. spend ${formatCurrency(p.minimumSpend)}`);
        if (audienceText[p.audience]) parts.push(audienceText[p.audience]);
        return parts.join(", ");
    };

    const discountLine = (p) => {
        const text = discountText(p);
        return text
            ? `<span class="small fw-medium"><i class="fa-solid fa-tag me-1 text-body-secondary" aria-hidden="true"></i>${AdminUI.escapeHtml(text)}</span>`
            : `<span class="small text-warning-emphasis"><i class="fa-solid fa-triangle-exclamation me-1" aria-hidden="true"></i>No discount set: edit to make the code usable</span>`;
    };

    // Percent takes whole numbers up to 100; a fixed discount uses the restaurant currency.
    const syncDiscountInputs = () => {
        const percent = discountTypeInput.value === "Percentage";
        document.getElementById("promo-discount-value-label").textContent = percent ? "Percent" : "Amount";
        document.getElementById("promo-discount-prefix").classList.toggle("d-none", percent);
        document.getElementById("promo-discount-suffix").classList.toggle("d-none", !percent);
        document.getElementById("promo-discount-error").textContent = percent
            ? "Enter a whole number from 1 to 100."
            : `Enter an amount of at least ${currencySymbol} 0.01.`;
        discountValueInput.min = percent ? "1" : "0.01";
        discountValueInput.step = percent ? "1" : "0.01";
        if (percent) discountValueInput.max = "100"; else discountValueInput.removeAttribute("max");
    };

    const codeBadge = (p) => p.code
        ? `<span class="badge rounded-pill text-bg-light border font-monospace fw-semibold"><i class="fa-solid fa-ticket me-1" aria-hidden="true"></i>${AdminUI.escapeHtml(p.code)}</span>`
        : `<span class="badge rounded-pill text-bg-light border text-body-secondary fw-normal">No code yet</span>`;

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
            <div class="card bg-body border-0 rounded-4">
                <div class="card-body d-flex flex-wrap flex-sm-nowrap gap-3">
                    ${thumbnail(p)}
                    <div class="flex-grow-1 overflow-hidden">
                        <div class="d-flex flex-wrap justify-content-between align-items-start gap-2">
                            <h2 class="h6 fw-semibold mb-1">${title}</h2>
                            ${statusBadge(p.status)}
                        </div>
                        <div class="d-flex flex-wrap align-items-center gap-2">
                            ${codeBadge(p)}
                            <span class="small text-body-secondary"><i class="fa-regular fa-calendar me-1"></i>${dateRange(p)}</span>
                        </div>
                        <div class="mt-1">${discountLine(p)}</div>
                        <p class="small text-truncate mt-2 mb-0">${AdminUI.escapeHtml(p.description)}</p>
                        <div class="d-flex flex-wrap justify-content-end gap-2 mt-3">
                            <button type="button" class="btn btn-sm btn-light border" data-view="${p.id}">View</button>
                            ${AdminUI.isAdministrator ? `
                            <button type="button" class="btn btn-sm btn-light border" data-edit="${p.id}">Edit</button>
                            <button type="button" class="btn btn-sm btn-light border text-danger" data-delete="${p.id}" aria-label="Delete ${title}">Delete</button>` : ""}
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

        statsEl.innerHTML = [
            statRow("Active", "Running today", count("Active"), "success"),
            statRow("Ending soon", "Active, ends within 7 days", endingSoon, "danger"),
            statRow("Scheduled", "Starts on a later date", count("Scheduled"), "warning"),
            statRow("Ended", "Past their end date", count("Ended"), "secondary"),
            statRow("Total", "All promotions", promotions.length, "dark")
        ].join("");
        document.getElementById("promo-updated").textContent = `Last updated ${AdminUI.formatTime(new Date())}`;
    };

    const showListMessage = (text, tone = "text-body-secondary") => {
        listEl.innerHTML = `<div class="bg-body rounded-4 text-center ${tone} py-5">${AdminUI.escapeHtml(text)}</div>`;
    };

    const render = () => {
        renderStats();

        if (promotions.length === 0) {
            showListMessage("No promotions yet. Use \"New promotion\" to create the first one.");
            return;
        }

        const term = searchEl.value.trim().toLowerCase();
        const visible = promotions.filter(p =>
            (!term || p.title.toLowerCase().includes(term) || (p.code ?? "").toLowerCase().includes(term)) &&
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
        codeInput.value = p?.code ?? "";
        discountTypeInput.value = p?.discountType ?? "Percentage";
        discountValueInput.value = p?.discountType ? p.discountValue : "";
        minSpendInput.value = p?.minimumSpend > 0 ? p.minimumSpend : "";
        audienceInput.value = p?.audience ?? "Everyone";
        syncDiscountInputs();
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
                <div class="d-flex flex-wrap gap-2">${statusBadge(p.status)} ${codeBadge(p)}</div>
                <span class="small text-body-secondary"><i class="fa-regular fa-calendar me-1"></i>${dateRange(p)}</span>
            </div>
            <div class="mb-3">${discountLine(p)}</div>
            <p class="mb-0">${AdminUI.escapeHtml(p.description).replace(/\n/g, "<br>")}</p>`;
        viewModal.show();
    };

    // Not on the page for Staff, who can only view.
    document.getElementById("btn-new-promotion")?.addEventListener("click", () => openForm(null));

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
        dropEl.classList.add("border-dark");
    };

    const unhighlightDropArea = (e) => {
        e.preventDefault();
        dropEl.classList.remove("border-dark");
    };

    dropEl.addEventListener("dragenter", highlightDropArea);
    dropEl.addEventListener("dragover", highlightDropArea);
    dropEl.addEventListener("dragleave", unhighlightDropArea);
    dropEl.addEventListener("drop", (e) => {
        unhighlightDropArea(e);
        loadImage(e.dataTransfer.files[0]);
    });

    startInput.addEventListener("change", () => { endInput.min = startInput.value; });
    discountTypeInput.addEventListener("change", syncDiscountInputs);

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
        data.append("code", codeInput.value.trim());
        data.append("discountType", discountTypeInput.value);
        data.append("discountValue", discountValueInput.value);
        data.append("minimumSpend", minSpendInput.value);
        data.append("audience", audienceInput.value);
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
            AdminUI.showToast(editingId ? "Promotion updated" : `Promotion created${emailNote(saved)}`);
            render();
        } catch (error) {
            formError.textContent = error.message;
            formError.classList.remove("d-none");
        } finally {
            AdminUI.setBusy(submitBtn, false);
        }
    });

    const deletePromotion = async (button) => {
        if (!(await AppDialog.confirm({ title: "Delete this promotion?", message: "This can't be undone.", confirmText: "Delete", cancelText: "Keep it", danger: true, icon: "fa-trash" }))) return;

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
