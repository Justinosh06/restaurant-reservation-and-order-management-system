// Mock data - mirrors RestaurantSettings + BusinessHours (DayOfWeek: Sunday = 0). Replace with API calls later.
const savedSettings = {
    name: "Resto",
    address: "1 Jalan BBN 12/1, Putra Nilai, 71800 Nilai, Negeri Sembilan",
    phoneNumber: "+60 6-798 2000",
    defaultCurrency: "MYR",
    updatedAt: "2026-10-01T09:00:00",
    businessHours: [
        { dayOfWeek: 1, openTime: "11:00", closeTime: "22:00", isClosed: false },
        { dayOfWeek: 2, openTime: "11:00", closeTime: "22:00", isClosed: false },
        { dayOfWeek: 3, openTime: "11:00", closeTime: "22:00", isClosed: false },
        { dayOfWeek: 4, openTime: "11:00", closeTime: "22:00", isClosed: false },
        { dayOfWeek: 5, openTime: "11:00", closeTime: "23:00", isClosed: false },
        { dayOfWeek: 6, openTime: "10:00", closeTime: "23:00", isClosed: false },
        { dayOfWeek: 0, openTime: null, closeTime: null, isClosed: true }
    ]
};

const DAY_NAMES = ["Sunday", "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday"];

(() => {
    const form = document.getElementById("settings-form");
    const hoursList = document.getElementById("hours-list");
    const hoursError = document.getElementById("hours-error");
    const fields = {
        name: document.getElementById("set-name"),
        address: document.getElementById("set-address"),
        phoneNumber: document.getElementById("set-phone"),
        defaultCurrency: document.getElementById("set-currency")
    };

    // Working copy so "Discard changes" can restore the saved state.
    let hours = [];

    const renderHours = () => {
        hoursList.innerHTML = hours.map((h, i) => `
            <div class="row g-2 align-items-center py-2 border-bottom">
                <div class="col-12 col-sm-3 fw-medium">${DAY_NAMES[h.dayOfWeek]}</div>
                <div class="col-5 col-sm-3">
                    <input type="time" class="form-control form-control-sm" data-index="${i}" data-field="openTime" value="${h.openTime ?? ""}" ${h.isClosed ? "disabled" : ""} aria-label="${DAY_NAMES[h.dayOfWeek]} opening time" />
                </div>
                <div class="col-5 col-sm-3">
                    <input type="time" class="form-control form-control-sm" data-index="${i}" data-field="closeTime" value="${h.closeTime ?? ""}" ${h.isClosed ? "disabled" : ""} aria-label="${DAY_NAMES[h.dayOfWeek]} closing time" />
                </div>
                <div class="col-2 col-sm-3">
                    <div class="form-check form-switch mb-0">
                        <input class="form-check-input" type="checkbox" role="switch" id="open-${i}" data-index="${i}" data-field="isOpen" ${h.isClosed ? "" : "checked"} />
                        <label class="form-check-label small d-none d-sm-inline" for="open-${i}">${h.isClosed ? "Closed" : "Open"}</label>
                    </div>
                </div>
            </div>`).join("");
    };

    const load = () => {
        fields.name.value = savedSettings.name;
        fields.address.value = savedSettings.address;
        fields.phoneNumber.value = savedSettings.phoneNumber ?? "";
        fields.defaultCurrency.value = savedSettings.defaultCurrency;
        document.getElementById("settings-updated").textContent =
            `Last updated ${AdminUI.formatDate(savedSettings.updatedAt)}, ${AdminUI.formatTime(savedSettings.updatedAt)}`;
        hours = savedSettings.businessHours.map(h => ({ ...h }));
        hoursError.textContent = "";
        form.classList.remove("was-validated");
        renderHours();
    };

    hoursList.addEventListener("input", (e) => {
        const { index, field } = e.target.dataset;
        if (index === undefined) return;
        const day = hours[index];

        if (field === "isOpen") {
            day.isClosed = !e.target.checked;
            if (!day.isClosed && !day.openTime) {
                day.openTime = "11:00";
                day.closeTime = "22:00";
            }
            renderHours();
        } else {
            day[field] = e.target.value || null;
        }
    });

    document.getElementById("copy-monday").addEventListener("click", () => {
        const monday = hours.find(h => h.dayOfWeek === 1);
        hours.forEach(h => {
            h.openTime = monday.openTime;
            h.closeTime = monday.closeTime;
            h.isClosed = monday.isClosed;
        });
        renderHours();
    });

    document.getElementById("reset-settings").addEventListener("click", () => {
        load();
        AdminUI.showToast("Changes discarded");
    });

    form.addEventListener("submit", (e) => {
        e.preventDefault();
        form.classList.add("was-validated");

        const invalidDays = hours.filter(h => !h.isClosed && (!h.openTime || !h.closeTime || h.closeTime <= h.openTime));
        hoursError.textContent = invalidDays.length
            ? `Closing time must be after opening time for: ${invalidDays.map(h => DAY_NAMES[h.dayOfWeek]).join(", ")}.`
            : "";

        if (!form.checkValidity() || invalidDays.length) return;

        savedSettings.name = fields.name.value.trim();
        savedSettings.address = fields.address.value.trim();
        savedSettings.phoneNumber = fields.phoneNumber.value.trim() || null;
        savedSettings.defaultCurrency = fields.defaultCurrency.value;
        savedSettings.businessHours = hours.map(h => h.isClosed
            ? { ...h, openTime: null, closeTime: null }
            : { ...h });
        savedSettings.updatedAt = new Date().toISOString();

        load();
        AdminUI.showToast("Settings saved");
    });

    load();
})();
