(() => {
    let promotions = [];
    let selectedImage = null;
    let previewUrl = null;

    const form = document.getElementById("promo-form");
    const formError = document.getElementById("promo-form-error");
    const submitBtn = document.getElementById("promo-submit");
    const titleInput = document.getElementById("promo-title");
    const descInput = document.getElementById("promo-description");
    const startInput = document.getElementById("promo-start");
    const endInput = document.getElementById("promo-end");
    const fileInput = document.getElementById("promo-image");
    const dropEl = document.getElementById("image-drop");
    const imageError = document.getElementById("image-error");
    const removeImageBtn = document.getElementById("remove-image");
    const listEl = document.getElementById("promo-list");
    const messageEl = document.getElementById("promo-message");

    const dropPlaceholder = dropEl.innerHTML;
    const maxImageBytes = Number(fileInput.dataset.maxBytes);
    const allowedTypes = fileInput.accept.split(",").map(type => type.trim());

    const showMessage = (text, tone = "text-muted") => {
        messageEl.className = `text-center ${tone} py-4`;
        messageEl.textContent = text;
    };

    const render = () => {
        document.getElementById("promo-count").textContent = `${promotions.length} promotion${promotions.length === 1 ? "" : "s"}`;

        if (promotions.length === 0) {
            listEl.innerHTML = "";
            showMessage("No promotions yet. Create one using the form.");
            return;
        }
        messageEl.classList.add("d-none");

        listEl.innerHTML = promotions.map(p => {
            const image = p.imageUrl
                ? `<img src="${AdminUI.escapeHtml(p.imageUrl)}" class="card-img-top promo-image" alt="${AdminUI.escapeHtml(p.title)}" />`
                : `<div class="card-img-top promo-image bg-body-tertiary d-flex align-items-center justify-content-center text-muted"><i class="fa-regular fa-image fs-2" aria-hidden="true"></i></div>`;

            return `
                <div class="col">
                    <div class="card h-100">
                        ${image}
                        <div class="card-body">
                            <div class="d-flex justify-content-between align-items-start gap-2">
                                <h6 class="card-title mb-1">${AdminUI.escapeHtml(p.title)}</h6>
                                ${AdminUI.statusBadge(p.status)}
                            </div>
                            <p class="card-text text-muted small">${AdminUI.escapeHtml(p.description)}</p>
                        </div>
                        <div class="card-footer d-flex justify-content-between align-items-center small text-muted">
                            <span><i class="fa-regular fa-calendar me-1"></i>${AdminUI.formatDate(`${p.startDate}T00:00:00`)} - ${AdminUI.formatDate(`${p.endDate}T00:00:00`)}</span>
                            <button type="button" class="btn btn-sm btn-outline-danger btn-icon" data-delete="${p.id}" aria-label="Delete ${AdminUI.escapeHtml(p.title)}"><i class="fa-solid fa-trash"></i></button>
                        </div>
                    </div>
                </div>`;
        }).join("");
    };

    const load = () => {
        showMessage("Loading...");
        AdminUI.get("List")
            .then(data => { promotions = data; render(); })
            .catch(error => { showMessage(error.message, "text-danger"); AdminUI.showError(error); });
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
        dropEl.innerHTML = `<img src="${previewUrl}" class="w-100 h-100 object-fit-cover" alt="Selected promotion image preview" />`;
        removeImageBtn.classList.remove("d-none");
    };

    dropEl.addEventListener("click", () => fileInput.click());
    dropEl.addEventListener("keydown", (e) => {
        if (e.key === "Enter" || e.key === " ") { e.preventDefault(); fileInput.click(); }
    });
    fileInput.addEventListener("change", () => loadImage(fileInput.files[0]));
    removeImageBtn.addEventListener("click", clearImage);

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

        const data = new FormData();
        data.append("title", titleInput.value.trim());
        data.append("description", descInput.value.trim());
        data.append("startDate", startInput.value);
        data.append("endDate", endInput.value);
        if (selectedImage) data.append("image", selectedImage);

        AdminUI.setBusy(submitBtn, true);
        try {
            const created = await AdminUI.postForm("Create", data);
            promotions.unshift(created);
            form.reset();
            form.classList.remove("was-validated");
            clearImage();
            AdminUI.showToast("Promotion created");
            render();
        } catch (error) {
            formError.textContent = error.message;
            formError.classList.remove("d-none");
        } finally {
            AdminUI.setBusy(submitBtn, false);
        }
    });

    listEl.addEventListener("click", async (e) => {
        const deleteBtn = e.target.closest("[data-delete]");
        if (!deleteBtn || !confirm("Delete this promotion?")) return;

        AdminUI.setBusy(deleteBtn, true);
        try {
            await AdminUI.post("Delete", { id: deleteBtn.dataset.delete });
            promotions = promotions.filter(p => p.id !== deleteBtn.dataset.delete);
            AdminUI.showToast("Promotion deleted");
            render();
        } catch (error) {
            AdminUI.setBusy(deleteBtn, false);
            AdminUI.showError(error);
        }
    });

    load();
})();
