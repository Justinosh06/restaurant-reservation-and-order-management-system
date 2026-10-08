// Mock data - mirrors the Promotion model (Title, Description, ImageUrl, StartDate, EndDate).
// Images are kept as in-browser data URLs for now; the backend will upload them and store ImageUrl.
const promotions = [
    {
        id: "PRM-0002", title: "Pasta Tuesday 20% off", imageUrl: null,
        description: "Every Tuesday, enjoy 20% off all pasta dishes. Dine-in only.",
        startDate: "2026-10-01", endDate: "2026-12-31"
    },
    {
        id: "PRM-0001", title: "Free drink with any set meal", imageUrl: null,
        description: "Get a free iced lemon tea with any set meal ordered before 3pm.",
        startDate: "2026-09-01", endDate: "2026-09-30"
    }
];

const MAX_IMAGE_BYTES = 5 * 1024 * 1024;
const ALLOWED_TYPES = ["image/png", "image/jpeg", "image/webp"];

(() => {
    const form = document.getElementById("promo-form");
    const titleInput = document.getElementById("promo-title");
    const descInput = document.getElementById("promo-description");
    const startInput = document.getElementById("promo-start");
    const endInput = document.getElementById("promo-end");
    const fileInput = document.getElementById("promo-image");
    const dropEl = document.getElementById("image-drop");
    const imageError = document.getElementById("image-error");
    const removeImageBtn = document.getElementById("remove-image");
    const listEl = document.getElementById("promo-list");
    const emptyEl = document.getElementById("promo-empty");

    const dropPlaceholder = dropEl.innerHTML;
    let imageDataUrl = null;

    const todayIso = () => {
        const d = new Date();
        return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, "0")}-${String(d.getDate()).padStart(2, "0")}`;
    };

    const promoStatus = (p) => {
        const today = todayIso();
        if (p.endDate < today) return "Ended";
        if (p.startDate > today) return "Scheduled";
        return "Active";
    };

    const render = () => {
        document.getElementById("promo-count").textContent = `${promotions.length} promotion${promotions.length === 1 ? "" : "s"}`;
        emptyEl.classList.toggle("d-none", promotions.length > 0);

        listEl.innerHTML = promotions.map(p => {
            const status = promoStatus(p);
            const image = p.imageUrl
                ? `<img src="${p.imageUrl}" class="card-img-top promo-image" alt="${AdminUI.escapeHtml(p.title)}" />`
                : `<div class="card-img-top promo-image bg-light d-flex align-items-center justify-content-center text-muted"><i class="fa-regular fa-image fs-2" aria-hidden="true"></i></div>`;

            return `
                <div class="col">
                    <div class="card h-100">
                        ${image}
                        <div class="card-body">
                            <div class="d-flex justify-content-between align-items-start gap-2">
                                <h6 class="card-title mb-1">${AdminUI.escapeHtml(p.title)}</h6>
                                ${AdminUI.statusBadge(status)}
                            </div>
                            <p class="card-text text-muted small">${AdminUI.escapeHtml(p.description)}</p>
                        </div>
                        <div class="card-footer bg-white d-flex justify-content-between align-items-center small text-muted">
                            <span><i class="fa-regular fa-calendar me-1"></i>${AdminUI.formatDate(p.startDate)} - ${AdminUI.formatDate(p.endDate)}</span>
                            <button type="button" class="btn btn-sm btn-outline-danger" data-delete="${p.id}" aria-label="Delete ${AdminUI.escapeHtml(p.title)}"><i class="fa-solid fa-trash"></i></button>
                        </div>
                    </div>
                </div>`;
        }).join("");
    };

    const clearImage = () => {
        imageDataUrl = null;
        fileInput.value = "";
        dropEl.innerHTML = dropPlaceholder;
        removeImageBtn.classList.add("d-none");
    };

    const loadImage = (file) => {
        imageError.textContent = "";
        if (!file) return;
        if (!ALLOWED_TYPES.includes(file.type)) {
            imageError.textContent = "Please choose a JPG, PNG or WebP image.";
            return;
        }
        if (file.size > MAX_IMAGE_BYTES) {
            imageError.textContent = "Image must be 5 MB or smaller.";
            return;
        }

        const reader = new FileReader();
        reader.onload = () => {
            imageDataUrl = reader.result;
            dropEl.innerHTML = `<img src="${imageDataUrl}" class="w-100 h-100 object-fit-cover" alt="Selected promotion image preview" />`;
            removeImageBtn.classList.remove("d-none");
        };
        reader.readAsDataURL(file);
    };

    dropEl.addEventListener("click", () => fileInput.click());
    dropEl.addEventListener("keydown", (e) => {
        if (e.key === "Enter" || e.key === " ") { e.preventDefault(); fileInput.click(); }
    });
    fileInput.addEventListener("change", () => loadImage(fileInput.files[0]));
    removeImageBtn.addEventListener("click", clearImage);

    ["dragenter", "dragover"].forEach(evt => dropEl.addEventListener(evt, (e) => {
        e.preventDefault();
        dropEl.classList.add("border-dark");
    }));
    ["dragleave", "drop"].forEach(evt => dropEl.addEventListener(evt, (e) => {
        e.preventDefault();
        dropEl.classList.remove("border-dark");
    }));
    dropEl.addEventListener("drop", (e) => loadImage(e.dataTransfer.files[0]));

    startInput.addEventListener("change", () => { endInput.min = startInput.value; });

    form.addEventListener("submit", (e) => {
        e.preventDefault();
        const endBeforeStart = startInput.value && endInput.value && endInput.value < startInput.value;
        endInput.setCustomValidity(endBeforeStart ? "End date must be on or after the start date." : "");

        form.classList.add("was-validated");
        if (!form.checkValidity()) return;

        promotions.unshift({
            id: AdminUI.generateId("PRM"),
            title: titleInput.value.trim(),
            description: descInput.value.trim(),
            imageUrl: imageDataUrl,
            startDate: startInput.value,
            endDate: endInput.value
        });

        form.reset();
        form.classList.remove("was-validated");
        clearImage();
        AdminUI.showToast("Promotion created");
        render();
    });

    listEl.addEventListener("click", (e) => {
        const deleteBtn = e.target.closest("[data-delete]");
        if (deleteBtn && confirm("Delete this promotion?")) {
            promotions.splice(promotions.findIndex(p => p.id === deleteBtn.dataset.delete), 1);
            AdminUI.showToast("Promotion deleted");
            render();
        }
    });

    render();
})();
