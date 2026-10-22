(() => {
    let items = [];
    let category = "";
    let selectedImage = null;
    let previewUrl = null;
    let removeSavedImage = false;

    const gridEl = document.getElementById("menu-grid");
    const countEl = document.getElementById("menu-count");
    const searchEl = document.getElementById("menu-search");
    const availabilityFilterEl = document.getElementById("menu-availability-filter");
    const categoryFilterEl = document.getElementById("menu-category-filter");

    const form = document.getElementById("menu-form");
    const formError = document.getElementById("menu-form-error");
    const submitBtn = document.getElementById("menu-submit");
    const idInput = document.getElementById("menu-id");
    const nameInput = document.getElementById("menu-name");
    const categoryInput = document.getElementById("menu-category");
    const priceInput = document.getElementById("menu-price");
    const descInput = document.getElementById("menu-description");
    const charCount = document.getElementById("menu-char-count");
    const availableInput = document.getElementById("menu-available");
    const optionsEl = document.getElementById("menu-options");
    const addOptionBtn = document.getElementById("menu-add-option");
    const fileInput = document.getElementById("menu-image");
    const dropEl = document.getElementById("menu-image-drop");
    const imageError = document.getElementById("menu-image-error");
    const removeImageBtn = document.getElementById("menu-remove-image");
    const formModal = new bootstrap.Modal(document.getElementById("menu-modal"));

    const dropPlaceholder = dropEl.innerHTML;
    const maxImageBytes = Number(fileInput.dataset.maxBytes);
    const allowedTypes = fileInput.accept.split(",").map(type => type.trim());
    const maxOptions = Number(addOptionBtn.dataset.max);
    const optionNameMax = Number(optionsEl.dataset.nameMax);

    // ---- List ----

    const availabilityBadge = (m) => m.available
        ? `<span class="badge rounded-pill bg-success-subtle text-success-emphasis">Available</span>`
        : `<span class="badge rounded-pill bg-secondary-subtle text-secondary-emphasis">Unavailable</span>`;

    const photo = (m) => m.imageUrl
        ? `<img src="${AdminUI.escapeHtml(m.imageUrl)}" class="object-fit-cover" alt="" />`
        : `<div class="d-flex align-items-center justify-content-center bg-body-tertiary text-body-secondary"><i class="fa-solid fa-utensils fs-3"></i></div>`;

    const itemCard = (m) => {
        const name = AdminUI.escapeHtml(m.name);
        const optionSummary = m.options.length
            ? `<div class="small text-body-secondary text-truncate"><i class="fa-solid fa-sliders fa-fw me-1"></i>${m.options.map(o => AdminUI.escapeHtml(o.name)).join(", ")}</div>`
            : "";

        return `
            <div class="col-sm-6 col-xl-4 col-xxl-3">
                <div class="card h-100 bg-body border-0 rounded-4 overflow-hidden ${m.available ? "" : "opacity-75"}">
                    <div class="ratio ratio-4x3">${photo(m)}</div>
                    <div class="card-body d-flex flex-column">
                        <div class="d-flex justify-content-between align-items-start gap-2">
                            <h2 class="h6 fw-semibold mb-0 text-truncate" title="${name}">${name}</h2>
                            <span class="fw-semibold text-nowrap">${AdminUI.formatCurrency(m.price)}</span>
                        </div>
                        <div class="d-flex flex-wrap align-items-center gap-2 small text-body-secondary mt-1">
                            <span>${AdminUI.escapeHtml(m.category)}</span>
                            &middot;
                            ${availabilityBadge(m)}
                        </div>
                        <p class="small text-body-secondary mt-2 mb-2 menu-description">${AdminUI.escapeHtml(m.description) || "<em>No description</em>"}</p>
                        ${optionSummary}
                        <div class="small text-body-secondary"><i class="fa-solid fa-receipt fa-fw me-1"></i>Ordered ${m.timesOrdered} ${m.timesOrdered === 1 ? "time" : "times"}</div>

                        <div class="d-flex align-items-center gap-2 border-top pt-3 mt-auto">
                            <div class="form-check form-switch mb-0 me-auto" title="${m.available ? "Customers can order this" : "Hidden from ordering"}">
                                <input class="form-check-input" type="checkbox" role="switch" id="avail-${m.id}" data-toggle="${m.id}" ${m.available ? "checked" : ""} />
                                <label class="form-check-label small" for="avail-${m.id}">Available</label>
                            </div>
                            <button type="button" class="btn btn-sm btn-light border" data-edit="${m.id}">Edit</button>
                            <button type="button" class="btn btn-sm btn-light border text-danger" data-delete="${m.id}" aria-label="Delete ${name}"><i class="fa-solid fa-trash"></i></button>
                        </div>
                    </div>
                </div>
            </div>`;
    };

    const showGridMessage = (text, tone = "text-body-secondary") => {
        gridEl.innerHTML = `<div class="col-12"><div class="bg-body rounded-4 text-center ${tone} py-5">${AdminUI.escapeHtml(text)}</div></div>`;
    };

    const renderSummary = () => {
        document.getElementById("summary-total").textContent = items.length;
        document.getElementById("summary-available").textContent = items.filter(m => m.available).length;
        document.getElementById("summary-unavailable").textContent = items.filter(m => !m.available).length;
        document.getElementById("summary-no-image").textContent = items.filter(m => !m.imageUrl).length;
    };

    const render = () => {
        renderSummary();

        if (items.length === 0) {
            countEl.textContent = "";
            showGridMessage("The menu is empty. Use \"New menu item\" to add the first dish.");
            return;
        }

        const term = searchEl.value.trim().toLowerCase();
        const availability = availabilityFilterEl.value;
        const visible = items.filter(m =>
            (!term || m.name.toLowerCase().includes(term)) &&
            (!category || m.category === category) &&
            (!availability || (availability === "available") === m.available));

        countEl.textContent = `${visible.length} of ${items.length}`;
        if (visible.length === 0) {
            showGridMessage("No menu items match these filters.");
            return;
        }
        gridEl.innerHTML = visible.map(itemCard).join("");
    };

    const load = () => {
        showGridMessage("Loading...");
        AdminUI.get("List")
            .then(data => { items = data; render(); })
            .catch(error => { showGridMessage(error.message, "text-danger"); AdminUI.showError(error); });
    };

    const replaceItem = (saved) => {
        const index = items.findIndex(m => m.id === saved.id);
        if (index >= 0) items[index] = saved; else items.push(saved);
        items.sort((a, b) => a.category.localeCompare(b.category) || a.name.localeCompare(b.name));
    };

    // ---- Options editor ----

    const optionRow = (option) => {
        const row = document.createElement("div");
        row.className = "d-flex gap-2 align-items-start";
        row.innerHTML = `
            <input type="text" class="form-control form-control-sm" style="max-width: 11rem;" data-option-name
                   maxlength="${optionNameMax}" placeholder="Option, e.g. Size" aria-label="Option name" />
            <input type="text" class="form-control form-control-sm" data-option-choices
                   placeholder="Choices, e.g. Regular, Large" aria-label="Choices, separated by commas" />
            <button type="button" class="btn btn-sm btn-light border text-danger" data-remove-option aria-label="Remove option">
                <i class="fa-solid fa-xmark"></i>
            </button>`;
        row.querySelector("[data-option-name]").value = option?.name ?? "";
        row.querySelector("[data-option-choices]").value = (option?.choices ?? []).join(", ");
        return row;
    };

    const updateAddOption = () => {
        addOptionBtn.disabled = optionsEl.children.length >= maxOptions;
    };

    const readOptions = () => [...optionsEl.children].map(row => ({
        name: row.querySelector("[data-option-name]").value.trim(),
        choices: row.querySelector("[data-option-choices]").value.split(",").map(c => c.trim()).filter(Boolean)
    })).filter(o => o.name || o.choices.length);

    addOptionBtn.addEventListener("click", () => {
        const row = optionRow(null);
        optionsEl.appendChild(row);
        row.querySelector("[data-option-name]").focus();
        updateAddOption();
    });

    optionsEl.addEventListener("click", (e) => {
        const button = e.target.closest("[data-remove-option]");
        if (!button) return;
        button.closest(".d-flex").remove();
        updateAddOption();
    });

    // ---- Photo ----

    const showPreview = (url) => {
        dropEl.innerHTML = `<img src="${AdminUI.escapeHtml(url)}" class="object-fit-cover" alt="Menu item photo preview" />`;
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
        removeSavedImage = false;
        showPreview(previewUrl);
    };

    dropEl.addEventListener("click", () => fileInput.click());
    dropEl.addEventListener("keydown", (e) => {
        if (e.key === "Enter" || e.key === " ") { e.preventDefault(); fileInput.click(); }
    });
    fileInput.addEventListener("change", () => loadImage(fileInput.files[0]));
    removeImageBtn.addEventListener("click", () => {
        clearImage();
        removeSavedImage = true;
    });

    const highlightDropArea = (e) => { e.preventDefault(); dropEl.classList.add("border-dark"); };
    const unhighlightDropArea = (e) => { e.preventDefault(); dropEl.classList.remove("border-dark"); };
    dropEl.addEventListener("dragenter", highlightDropArea);
    dropEl.addEventListener("dragover", highlightDropArea);
    dropEl.addEventListener("dragleave", unhighlightDropArea);
    dropEl.addEventListener("drop", (e) => {
        unhighlightDropArea(e);
        loadImage(e.dataTransfer.files[0]);
    });

    // ---- Form ----

    const updateCharCount = () => {
        charCount.textContent = `${descInput.value.length} / ${descInput.maxLength}`;
    };
    descInput.addEventListener("input", updateCharCount);

    // Opens the form empty for a new dish, or filled in for editing.
    const openForm = (m) => {
        form.reset();
        form.classList.remove("was-validated");
        formError.classList.add("d-none");
        imageError.textContent = "";
        clearImage();
        removeSavedImage = false;

        idInput.value = m?.id ?? "";
        nameInput.value = m?.name ?? "";
        categoryInput.value = m?.category ?? (category || categoryInput.options[0].value);
        priceInput.value = m ? (m.price / 100).toFixed(2) : "";
        descInput.value = m?.description ?? "";
        availableInput.checked = m?.available ?? true;
        updateCharCount();
        if (m?.imageUrl) showPreview(m.imageUrl);

        optionsEl.replaceChildren(...(m?.options ?? []).map(optionRow));
        updateAddOption();

        document.getElementById("menu-modal-title").textContent = m ? "Edit menu item" : "New menu item";
        submitBtn.textContent = m ? "Save changes" : "Add to menu";
        formModal.show();
    };

    document.getElementById("btn-new-item").addEventListener("click", () => openForm(null));

    form.addEventListener("submit", async (e) => {
        e.preventDefault();
        formError.classList.add("d-none");
        form.classList.add("was-validated");
        if (!form.checkValidity()) return;

        const editingId = idInput.value;
        const data = new FormData();
        if (editingId) data.append("id", editingId);
        data.append("name", nameInput.value.trim());
        data.append("category", categoryInput.value);
        data.append("price", priceInput.value);
        data.append("description", descInput.value.trim());
        data.append("available", String(availableInput.checked));
        data.append("options", JSON.stringify(readOptions()));
        data.append("removeImage", String(removeSavedImage));
        if (selectedImage) data.append("image", selectedImage);

        AdminUI.setBusy(submitBtn, true);
        try {
            const saved = await AdminUI.postForm(editingId ? "Update" : "Create", data);
            replaceItem(saved);
            formModal.hide();
            AdminUI.showToast(editingId ? "Menu item updated" : "Added to the menu");
            render();
        } catch (error) {
            formError.textContent = error.message;
            formError.classList.remove("d-none");
        } finally {
            AdminUI.setBusy(submitBtn, false);
        }
    });

    // ---- Card actions ----

    const toggleAvailability = async (input) => {
        input.disabled = true;
        try {
            const saved = await AdminUI.post("Toggle", { id: input.dataset.toggle });
            replaceItem(saved);
            AdminUI.showToast(saved.available ? `${saved.name} is available again` : `${saved.name} is now unavailable`);
            render();
        } catch (error) {
            input.checked = !input.checked;
            input.disabled = false;
            AdminUI.showError(error);
        }
    };

    const deleteItem = async (button) => {
        const item = items.find(m => m.id === button.dataset.delete);
        if (item.timesOrdered > 0) {
            await AppDialog.alert({
                title: "Can't delete this dish",
                message: `"${item.name}" is part of past orders. Switch off "Available" to hide it from the menu instead.`,
                icon: "fa-circle-info"
            });
            return;
        }
        if (!(await AppDialog.confirm({ title: `Delete "${item.name}"?`, message: "It will be removed from the menu. This can't be undone.", confirmText: "Delete", cancelText: "Keep it", danger: true, icon: "fa-trash" }))) return;

        AdminUI.setBusy(button, true);
        try {
            await AdminUI.post("Delete", { id: item.id });
            items = items.filter(m => m.id !== item.id);
            AdminUI.showToast("Menu item deleted");
            render();
        } catch (error) {
            AdminUI.setBusy(button, false);
            AdminUI.showError(error);
        }
    };

    gridEl.addEventListener("change", (e) => {
        if (e.target.dataset.toggle) toggleAvailability(e.target);
    });

    gridEl.addEventListener("click", (e) => {
        const button = e.target.closest("button");
        if (!button) return;
        if (button.dataset.edit) openForm(items.find(m => m.id === button.dataset.edit));
        else if (button.dataset.delete) deleteItem(button);
    });

    categoryFilterEl.addEventListener("click", (e) => {
        const button = e.target.closest("[data-category]");
        if (!button) return;
        category = button.dataset.category;
        categoryFilterEl.querySelectorAll("[data-category]").forEach(b => {
            const active = b === button;
            b.classList.toggle("btn-dark", active);
            b.setAttribute("aria-pressed", String(active));
        });
        render();
    });

    searchEl.addEventListener("input", render);
    availabilityFilterEl.addEventListener("change", render);

    load();
})();
