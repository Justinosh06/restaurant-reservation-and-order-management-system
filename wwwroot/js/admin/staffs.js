(() => {
    const ROLE_COLORS = { Administrator: "info", Staff: "success" };
    const GENDER_AVATARS = {
        Male: { icon: "fa-person", color: "primary" },
        Female: { icon: "fa-person-dress", color: "danger" }
    };
    const NO_GENDER_AVATAR = { icon: "fa-user", color: "secondary" };

    let staffs = [];
    let pendingDeleteId = null;

    const gridEl = document.getElementById("staff-grid");
    const countEl = document.getElementById("staff-count");
    const searchEl = document.getElementById("staff-search");
    const roleFilterEl = document.getElementById("staff-role-filter");
    const genderFilterEl = document.getElementById("staff-gender-filter");

    const form = document.getElementById("staff-form");
    const formError = document.getElementById("staff-form-error");
    const genderError = document.getElementById("staff-gender-error");
    const submitBtn = document.getElementById("staff-submit");
    const idInput = document.getElementById("staff-id");
    const emailInput = document.getElementById("staff-email");
    const passwordInput = document.getElementById("staff-password");
    const roleInput = document.getElementById("staff-role");
    const genderInputs = document.querySelectorAll("[name=staff-gender]");
    const confirmDeleteBtn = document.getElementById("confirm-delete");
    const staffModal = new bootstrap.Modal(document.getElementById("staff-modal"));
    const deleteModal = new bootstrap.Modal(document.getElementById("delete-modal"));

    const selectedGender = () => [...genderInputs].find(input => input.checked)?.value ?? null;

    const staffCard = (s) => {
        const color = ROLE_COLORS[s.role] ?? "secondary";
        const avatar = GENDER_AVATARS[s.gender] ?? NO_GENDER_AVATAR;
        const avatarImage = s.gender === "Female" ? "female" : "male";
        const email = AdminUI.escapeHtml(s.email);
        const gender = s.gender
            ? AdminUI.escapeHtml(s.gender)
            : `<span class="text-warning-emphasis">Gender not set</span>`;

        return `
            <div class="col-sm-6 col-lg-4 col-xxl-3">
                <div class="card h-100 bg-body border-0 rounded-4">
                    <div class="card-body">
                        <div class="d-flex justify-content-between align-items-center">
                            <span class="badge rounded-pill bg-${color}-subtle text-${color}-emphasis">
                                <i class="fa-solid fa-circle text-${color} me-1 small"></i>${AdminUI.escapeHtml(s.role)}
                            </span>
                            <div class="dropdown">
                                <button type="button" class="btn btn-sm btn-link text-body-secondary" data-bs-toggle="dropdown" aria-expanded="false" aria-label="Actions for ${email}">
                                    <i class="fa-solid fa-ellipsis"></i>
                                </button>
                                <ul class="dropdown-menu dropdown-menu-end">
                                    <li><button type="button" class="dropdown-item" data-edit="${s.id}"><i class="fa-solid fa-pen fa-fw me-2"></i>Edit</button></li>
                                    ${s.isCurrentUser ? "" : `<li><button type="button" class="dropdown-item text-danger" data-delete="${s.id}"><i class="fa-solid fa-trash fa-fw me-2"></i>Delete</button></li>`}
                                </ul>
                            </div>
                        </div>

                        <div class="text-center my-3">
                            <span class="d-inline-block rounded-circle bg-${avatar.color}-subtle text-${avatar.color}-emphasis fs-4 p-3">
                                <img class="avatar-img" style="width: 50px;" src="/images/avatar-${avatarImage}.png" alt="" />
                            </span>
                            ${s.name ? `<div class="fw-semibold mt-2 text-truncate">${AdminUI.escapeHtml(s.name)}</div>` : ""}
                            <div class="${s.name ? "small" : "fw-semibold mt-2"} text-truncate" title="${email}">${email}</div>
                            <div class="small text-body-secondary">${AdminUI.escapeHtml(s.role)}</div>
                        </div>

                        <div class="bg-body-tertiary rounded-3 p-3 small d-flex flex-column gap-2">
                            <div><i class="fa-solid fa-hashtag fa-fw text-body-secondary me-1"></i>${AdminUI.escapeHtml(s.displayId)}</div>
                            <div><i class="fa-solid ${avatar.icon} fa-fw text-body-secondary me-1"></i>${gender}</div>
                            <div><i class="fa-solid fa-user-tag fa-fw text-body-secondary me-1"></i>${s.role === "Administrator" ? "Full access" : "Orders, tables and reservations"}</div>
                        </div>
                    </div>
                    <div class="card-footer bg-body border-0 pt-0 pb-3">
                        <button type="button" class="btn btn-sm btn-light border w-100" data-edit="${s.id}">Edit account</button>
                    </div>
                </div>
            </div>`;
    };

    const showGridMessage = (text, tone = "text-body-secondary") => {
        gridEl.innerHTML = `<div class="col-12"><div class="bg-body rounded-4 text-center ${tone} py-5">${AdminUI.escapeHtml(text)}</div></div>`;
    };

    const render = () => {
        document.getElementById("summary-total").textContent = staffs.length;
        document.getElementById("summary-no-gender").textContent = staffs.filter(s => !s.gender).length;
        for (const role of Object.keys(ROLE_COLORS)) {
            document.getElementById(`count-${role}`).textContent = staffs.filter(s => s.role === role).length;
        }

        if (staffs.length === 0) {
            countEl.textContent = "";
            showGridMessage("No staff accounts yet. Use \"Add staff\" to create the first one.");
            return;
        }

        const term = searchEl.value.trim().toLowerCase();
        const visible = staffs.filter(s =>
            (!term || s.email.includes(term) || (s.name ?? "").toLowerCase().includes(term)) &&
            (!roleFilterEl.value || s.role === roleFilterEl.value) &&
            (!genderFilterEl.value || s.gender === genderFilterEl.value));

        countEl.textContent = visible.length === staffs.length ? "" : `· ${visible.length} of ${staffs.length} shown`;
        if (visible.length === 0) {
            showGridMessage("No staff accounts match these filters.");
            return;
        }

        gridEl.innerHTML = visible.map(staffCard).join("");
    };

    const load = () => {
        showGridMessage("Loading...");
        AdminUI.get("List")
            .then(data => { staffs = data; render(); })
            .catch(error => {
                showGridMessage(error.message, "text-danger");
                AdminUI.showError(error);
            });
    };

    const openForm = (staff) => {
        form.classList.remove("was-validated");
        formError.classList.add("d-none");
        idInput.value = staff?.id ?? "";
        emailInput.value = staff?.email ?? "";
        passwordInput.value = "";
        passwordInput.required = !staff;
        roleInput.value = staff?.role ?? "Staff";
        genderInputs.forEach(input => { input.checked = input.value === staff?.gender; });
        genderError.classList.add("d-none");

        document.getElementById("staff-modal-title").textContent = staff ? "Edit staff" : "Add staff";
        submitBtn.textContent = staff ? "Save changes" : "Create account";
        document.getElementById("password-help").textContent = staff
            ? "Leave blank to keep the current password."
            : `At least ${passwordInput.minLength} characters. Share it with the staff member so they can sign in.`;
        staffModal.show();
    };

    document.getElementById("btn-add-staff").addEventListener("click", () => openForm(null));

    document.getElementById("toggle-password").addEventListener("click", (e) => {
        const show = passwordInput.type === "password";
        passwordInput.type = show ? "text" : "password";
        e.currentTarget.innerHTML = `<i class="fa-regular ${show ? "fa-eye-slash" : "fa-eye"}"></i>`;
    });

    form.addEventListener("submit", async (e) => {
        e.preventDefault();
        formError.classList.add("d-none");
        form.classList.add("was-validated");
        genderError.classList.toggle("d-none", selectedGender() !== null);
        if (!form.checkValidity()) return;

        const editingId = idInput.value;
        const payload = {
            id: editingId || null,
            email: emailInput.value.trim(),
            password: passwordInput.value || null,
            role: roleInput.value,
            gender: selectedGender()
        };

        AdminUI.setBusy(submitBtn, true);
        try {
            const saved = await AdminUI.post(editingId ? "Update" : "Create", payload);
            const index = staffs.findIndex(s => s.id === saved.id);
            if (index >= 0) staffs[index] = saved; else staffs.push(saved);
            staffModal.hide();
            AdminUI.showToast(editingId ? "Staff account updated" : "Staff account created");
            render();
        } catch (error) {
            formError.textContent = error.message;
            formError.classList.remove("d-none");
        } finally {
            AdminUI.setBusy(submitBtn, false);
        }
    });

    gridEl.addEventListener("click", (e) => {
        const editBtn = e.target.closest("[data-edit]");
        if (editBtn) {
            openForm(staffs.find(s => s.id === editBtn.dataset.edit));
            return;
        }

        const deleteBtn = e.target.closest("[data-delete]");
        if (deleteBtn) {
            pendingDeleteId = deleteBtn.dataset.delete;
            document.getElementById("delete-email").textContent = staffs.find(s => s.id === pendingDeleteId).email;
            deleteModal.show();
        }
    });

    confirmDeleteBtn.addEventListener("click", async () => {
        AdminUI.setBusy(confirmDeleteBtn, true);
        try {
            await AdminUI.post("Delete", { id: pendingDeleteId });
            staffs = staffs.filter(s => s.id !== pendingDeleteId);
            AdminUI.showToast("Staff account deleted");
            render();
        } catch (error) {
            AdminUI.showError(error);
        } finally {
            pendingDeleteId = null;
            deleteModal.hide();
            AdminUI.setBusy(confirmDeleteBtn, false);
        }
    });

    searchEl.addEventListener("input", render);
    roleFilterEl.addEventListener("change", render);
    genderFilterEl.addEventListener("change", render);
    genderInputs.forEach(input => input.addEventListener("change", () => genderError.classList.add("d-none")));

    load();
})();
