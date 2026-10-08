// Icon choices map to Announcement.Icon (a Font Awesome class string).
const ANNOUNCEMENT_ICONS = [
    { value: "fa-solid fa-circle-info", label: "General" },
    { value: "fa-solid fa-door-closed", label: "Closure" },
    { value: "fa-solid fa-clock", label: "Hours change" },
    { value: "fa-solid fa-screwdriver-wrench", label: "Maintenance" },
    { value: "fa-solid fa-utensils", label: "New menu" },
    { value: "fa-solid fa-champagne-glasses", label: "Event" },
    { value: "fa-solid fa-gift", label: "Festive" },
    { value: "fa-solid fa-triangle-exclamation", label: "Urgent" }
];

// Mock data - mirrors the Announcement model. Replace with API calls later.
const announcements = [
    {
        id: "ANN-0003", title: "Closed for Deepavali", icon: "fa-solid fa-door-closed", isActive: true,
        description: "We will be closed on 1 November to celebrate Deepavali with our families. See you on the 2nd!",
        createdAt: "2026-10-05T09:00:00", expiresAt: "2026-11-02"
    },
    {
        id: "ANN-0002", title: "New autumn pasta menu", icon: "fa-solid fa-utensils", isActive: true,
        description: "Try our new pumpkin ravioli and mushroom truffle tagliatelle, available from this week.",
        createdAt: "2026-10-01T10:30:00", expiresAt: null
    },
    {
        id: "ANN-0001", title: "Kitchen maintenance", icon: "fa-solid fa-screwdriver-wrench", isActive: false,
        description: "Some dishes were unavailable on 20 September due to scheduled kitchen maintenance.",
        createdAt: "2026-09-18T08:15:00", expiresAt: "2026-09-21"
    }
];

(() => {
    const form = document.getElementById("announcement-form");
    const titleInput = document.getElementById("ann-title");
    const descInput = document.getElementById("ann-description");
    const expiresInput = document.getElementById("ann-expires");
    const activeInput = document.getElementById("ann-active");
    const charCount = document.getElementById("ann-char-count");
    const listEl = document.getElementById("announcement-list");
    const emptyEl = document.getElementById("ann-empty");

    document.getElementById("icon-picker").innerHTML = ANNOUNCEMENT_ICONS.map((icon, i) => `
        <input type="radio" name="ann-icon" id="ann-icon-${i}" value="${icon.value}" ${i === 0 ? "checked" : ""} />
        <label for="ann-icon-${i}"><i class="${icon.value}"></i>${icon.label}</label>`).join("");

    const render = () => {
        const activeCount = announcements.filter(a => a.isActive).length;
        document.getElementById("ann-count").textContent = `${announcements.length} total, ${activeCount} active`;
        emptyEl.classList.toggle("d-none", announcements.length > 0);

        listEl.innerHTML = announcements.map(a => `
            <div class="announcement-item">
                <div class="announcement-icon"><i class="${AdminUI.escapeHtml(a.icon)}"></i></div>
                <div class="announcement-body">
                    <div class="d-flex align-items-center gap-2 flex-wrap">
                        <h6 class="mb-0">${AdminUI.escapeHtml(a.title)}</h6>
                        <span class="status-pill ${a.isActive ? "served" : "inactive"}">${a.isActive ? "Active" : "Hidden"}</span>
                    </div>
                    <p class="mt-1">${AdminUI.escapeHtml(a.description)}</p>
                    <small>Posted ${AdminUI.formatDate(a.createdAt)}${a.expiresAt ? ` &middot; Expires ${AdminUI.formatDate(a.expiresAt)}` : ""}</small>
                </div>
                <div class="d-flex flex-column gap-1">
                    <button type="button" class="btn-icon" data-toggle="${a.id}" aria-label="${a.isActive ? "Hide" : "Publish"} ${AdminUI.escapeHtml(a.title)}" title="${a.isActive ? "Hide" : "Publish"}">
                        <i class="fa-regular ${a.isActive ? "fa-eye-slash" : "fa-eye"}"></i>
                    </button>
                    <button type="button" class="btn-icon danger" data-delete="${a.id}" aria-label="Delete ${AdminUI.escapeHtml(a.title)}" title="Delete">
                        <i class="fa-solid fa-trash"></i>
                    </button>
                </div>
            </div>`).join("");
    };

    descInput.addEventListener("input", () => {
        charCount.textContent = `${descInput.value.length} / 1000`;
    });

    form.addEventListener("submit", (e) => {
        e.preventDefault();
        form.classList.add("was-validated");
        if (!form.checkValidity()) return;

        announcements.unshift({
            id: AdminUI.generateId("ANN"),
            title: titleInput.value.trim(),
            description: descInput.value.trim(),
            icon: form.querySelector("input[name='ann-icon']:checked").value,
            isActive: activeInput.checked,
            createdAt: new Date().toISOString(),
            expiresAt: expiresInput.value || null
        });

        form.reset();
        form.classList.remove("was-validated");
        charCount.textContent = "0 / 1000";
        AdminUI.showToast("Announcement posted");
        render();
    });

    listEl.addEventListener("click", (e) => {
        const toggleBtn = e.target.closest("[data-toggle]");
        if (toggleBtn) {
            const ann = announcements.find(a => a.id === toggleBtn.dataset.toggle);
            ann.isActive = !ann.isActive;
            AdminUI.showToast(ann.isActive ? "Announcement published" : "Announcement hidden");
            render();
            return;
        }

        const deleteBtn = e.target.closest("[data-delete]");
        if (deleteBtn && confirm("Delete this announcement?")) {
            announcements.splice(announcements.findIndex(a => a.id === deleteBtn.dataset.delete), 1);
            AdminUI.showToast("Announcement deleted");
            render();
        }
    });

    render();
})();
