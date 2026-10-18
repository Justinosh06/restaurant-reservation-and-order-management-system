(() => {
    const DAY_NAMES = ["Sunday", "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday"];
    const DEFAULT_OPEN = "11:00";
    const DEFAULT_CLOSE = "22:00";

    let saved = null;
    let hours = [];

    const form = document.getElementById("settings-form");
    const saveBtn = document.getElementById("save-settings");
    const errorEl = document.getElementById("settings-error");
    const firstTimeEl = document.getElementById("settings-first-time");
    const hoursList = document.getElementById("hours-list");
    const hoursError = document.getElementById("hours-error");
    const updatedEl = document.getElementById("settings-updated");
    const fields = {
        name: document.getElementById("set-name"),
        address: document.getElementById("set-address"),
        phoneNumber: document.getElementById("set-phone"),
        defaultCurrency: document.getElementById("set-currency")
    };

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

    const fillForm = () => {
        fields.name.value = saved.name;
        fields.address.value = saved.address;
        fields.phoneNumber.value = saved.phoneNumber ?? "";
        fields.defaultCurrency.value = saved.defaultCurrency;
        updatedEl.textContent = saved.updatedAt
            ? `Last updated ${AdminUI.formatDate(saved.updatedAt)}, ${AdminUI.formatTime(saved.updatedAt)}`
            : "Not saved yet";
        firstTimeEl.classList.toggle("d-none", saved.exists);
        hours = saved.businessHours.map(h => ({ ...h }));
        hoursError.textContent = "";
        errorEl.classList.add("d-none");
        form.classList.remove("was-validated");
        renderHours();
    };

    const load = () => {
        updatedEl.textContent = "Loading...";
        AdminUI.get("Settings")
            .then(data => { saved = data; fillForm(); })
            .catch(error => {
                updatedEl.textContent = "";
                errorEl.textContent = error.message;
                errorEl.classList.remove("d-none");
            });
    };

    hoursList.addEventListener("input", (e) => {
        const { index, field } = e.target.dataset;
        if (index === undefined) return;
        const day = hours[index];

        if (field === "isOpen") {
            day.isClosed = !e.target.checked;
            if (!day.isClosed && !day.openTime) {
                day.openTime = DEFAULT_OPEN;
                day.closeTime = DEFAULT_CLOSE;
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
        if (!saved) return;
        fillForm();
        AdminUI.showToast("Changes discarded");
    });

    form.addEventListener("submit", async (e) => {
        e.preventDefault();
        errorEl.classList.add("d-none");
        form.classList.add("was-validated");

        const invalidDays = hours.filter(h => !h.isClosed && (!h.openTime || !h.closeTime || h.closeTime <= h.openTime));
        hoursError.textContent = invalidDays.length
            ? `Closing time must be after opening time for: ${invalidDays.map(h => DAY_NAMES[h.dayOfWeek]).join(", ")}.`
            : "";

        if (!form.checkValidity() || invalidDays.length) return;

        AdminUI.setBusy(saveBtn, true);
        try {
            saved = await AdminUI.post("Save", {
                name: fields.name.value.trim(),
                address: fields.address.value.trim(),
                phoneNumber: fields.phoneNumber.value.trim() || null,
                defaultCurrency: fields.defaultCurrency.value,
                businessHours: hours
            });
            fillForm();
            AdminUI.showToast("Settings saved");
        } catch (error) {
            errorEl.textContent = error.message;
            errorEl.classList.remove("d-none");
        } finally {
            AdminUI.setBusy(saveBtn, false);
        }
    });

    load();
})();
